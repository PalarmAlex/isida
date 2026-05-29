using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Niche.Contour;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Niche
{
  /// <summary>
  /// Мост coupling Creature→Niche→Creature и тактовая обработка Niche (§6.7, этап 4).
  /// </summary>
  public sealed class CouplingBridge : IDisposable
  {
    private readonly GomeostasSystem _creatureGomeostas;
    private readonly AdaptiveActionsSystem _adaptiveActions;
    private readonly NicheEngine _nicheEngine = new NicheEngine();
    private readonly AoeEvaluationService _aoeEvaluation = new AoeEvaluationService();
    private readonly object _lock = new object();
    private TriadExperimentConfig _config;
    private ResearchLogger _researchLogger;
    private TriadInstanceState _triadInstanceState;
    private Dictionary<int, List<CouplingTarget>> _couplingByAction = new Dictionary<int, List<CouplingTarget>>();
    private Dictionary<int, List<OperatorNicheCouplingTarget>> _operatorCouplingByAction =
        new Dictionary<int, List<OperatorNicheCouplingTarget>>();
    private NicheInitSnapshot _initSnapshot;
    private Dictionary<int, float> _creatureInitialParams = new Dictionary<int, float>();
    private string _environmentFolder;
    private string _nicheDataFolder;
    private string _logsFolder;
    private string _experimentRunId;
    private int _lastSyncedEvolutionStage = -1;
    private bool _disposed;

    private INicheParameterState NicheState => _nicheEngine.State;

    /// <summary>Идентификатор текущего прогона эксперимента.</summary>
    public string ExperimentRunId => _experimentRunId;

    /// <summary>
    /// Эффективная фаза с учётом ограничений стадии (§4.1). Совпадает с <see cref="TriadExperimentConfig.Phase"/> после синхронизации.
    /// </summary>
    public TriadPhase EffectivePhase =>
        _config == null
            ? TriadPhase.A
            : TriadPhaseStagePolicy.ClampPhase(_config.Phase, AppGlobalState.EvolutionStage);

    /// <summary>True, если фаза C и прямое влияние на Creature заблокировано (§6.4).</summary>
    public bool IsOperatorCreatureInfluenceBlocked =>
        IsActive &&
        _config != null &&
        EffectivePhase >= TriadPhase.C &&
        AppGlobalState.EvolutionStage >= 4;

    /// <summary>
    /// Создаёт CouplingBridge и загружает конфигурацию из каталога Environment.
    /// </summary>
    /// <param name="creatureGomeostas">Гомеостаз Creature.</param>
    /// <param name="adaptiveActions">Система адаптивных действий Creature.</param>
    /// <param name="environmentFolder">Каталог конфигурации триады.</param>
    /// <param name="nicheDataFolder">Каталог Data/Niche (рефлексы, runtime).</param>
    /// <exception cref="ArgumentNullException">Если обязательные зависимости null.</exception>
    public CouplingBridge(
        GomeostasSystem creatureGomeostas,
        AdaptiveActionsSystem adaptiveActions,
        string environmentFolder,
        string nicheDataFolder = null)
    {
      _creatureGomeostas = creatureGomeostas ?? throw new ArgumentNullException(nameof(creatureGomeostas));
      _adaptiveActions = adaptiveActions ?? throw new ArgumentNullException(nameof(adaptiveActions));
      _environmentFolder = environmentFolder;
      _nicheDataFolder = nicheDataFolder;

      CouplingMappingLoader.EnsureTemplateFiles(environmentFolder);
      ReloadConfig(environmentFolder);
    }

    /// <summary>Движок Niche (гомеостаз + рефлексы).</summary>
    public NicheEngine NicheEngine => _nicheEngine;

    /// <summary>True, если загружена конфигурация coupling и Niche инициализирована.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Снимок инициализации Niche (§6.11).</summary>
    public NicheInitSnapshot InitSnapshot => _initSnapshot;

    /// <summary>Текущая конфигурация эксперимента.</summary>
    public TriadExperimentConfig Config => _config;

    /// <summary>True, если первичный AOE идёт от Niche (фаза B+).</summary>
    public bool IsNichePrimaryAoeActive => IsActive && _aoeEvaluation.IsNichePrimaryActive;

    /// <summary>Сервис первичного AOE по Niche.</summary>
    public AoeEvaluationService AoeEvaluation => _aoeEvaluation;

    /// <summary>
    /// Подключает логгер исследований для записи диады.
    /// </summary>
    /// <param name="researchLogger">Логгер или null.</param>
    public void SetResearchLogger(ResearchLogger researchLogger)
    {
      _researchLogger = researchLogger;
    }

    /// <summary>
    /// Устанавливает каталог логов для манифеста прогона (§6.5).
    /// </summary>
    /// <param name="logsFolder">Каталог логов проекта.</param>
    public void SetLogsFolder(string logsFolder)
    {
      _logsFolder = logsFolder;
      if (IsActive)
        WriteRunManifest("engine_start");
    }

    /// <summary>
    /// Подключает состояние экземпляра триады (§4.6).
    /// </summary>
    /// <param name="instanceState">Состояние или null.</param>
    public void SetTriadInstanceState(TriadInstanceState instanceState)
    {
      _triadInstanceState = instanceState;
      _aoeEvaluation.SetTriadInstanceState(instanceState);
    }

    /// <summary>
    /// Передаёт probe-key в контур Niche.
    /// </summary>
    /// <param name="probeKey">EnvironmentMetricProbeKey.</param>
    public void SetContourProbeKey(string probeKey)
    {
      _nicheEngine.SetContourProbeKey(probeKey);
    }

    /// <summary>
    /// Передаёт InputSnapshot контура напрямую (host API, §6.8).
    /// </summary>
    /// <param name="snapshot">Снимок входа.</param>
    /// <param name="probeKey">Опциональный probeKey для лога.</param>
    public void ApplyContourInputSnapshot(InputSnapshot snapshot, string probeKey = null)
    {
      _nicheEngine.ApplyContourInputSnapshot(snapshot, probeKey);
    }

    /// <summary>ProbeKey последнего применённого contour InputSnapshot.</summary>
    public string LastContourProbeKey { get; private set; } = string.Empty;

    /// <summary>Dim последнего InputSnapshot.</summary>
    public int LastContourInputDim { get; private set; }

    /// <summary>Дельты Niche от contour на последнем такте.</summary>
    public IReadOnlyDictionary<int, float> LastContourInputDelta { get; private set; }
        = new Dictionary<int, float>();

    /// <summary>
    /// Перенос на Niche₂ (§6.6).
    /// </summary>
    /// <param name="newInitialParams">Новые начальные параметры.</param>
    /// <returns>True, если применено.</returns>
    public bool ApplyNicheTransfer(IReadOnlyDictionary<int, float> newInitialParams)
    {
      if (!IsActive || newInitialParams == null || newInitialParams.Count == 0)
        return false;

      lock (_lock)
      {
        _nicheEngine.ApplyNicheTransfer(newInitialParams);
        var copied = new Dictionary<int, float>();
        foreach (var kvp in newInitialParams)
          copied[kvp.Key] = kvp.Value;
        _initSnapshot.InitialNicheParams = copied;
        Logger.Info($"CouplingBridge: перенос Niche₂, params={newInitialParams.Count}");
        return true;
      }
    }

    /// <summary>
    /// Перезагружает конфигурацию из каталога.
    /// </summary>
    /// <param name="environmentFolder">Каталог Environment.</param>
    public void ReloadConfig(string environmentFolder)
    {
      lock (_lock)
      {
        _environmentFolder = environmentFolder;
        _config = CouplingMappingLoader.LoadFromFolder(environmentFolder);
        RebuildCouplingIndex();
        _nicheEngine.Initialize(_config, _nicheDataFolder, _environmentFolder);
        _aoeEvaluation.Configure(_config);
        IsActive = _config.HasCouplingData && _nicheEngine.IsInitialized;

        if (_creatureInitialParams.Count == 0)
          _creatureInitialParams = SnapshotCreatureParams();

        if (string.IsNullOrWhiteSpace(_experimentRunId))
          _experimentRunId = Guid.NewGuid().ToString("N");

        _initSnapshot = BuildInitSnapshot();

        SyncPhaseWithEvolutionStage();

        if (!IsActive)
          Logger.Info("CouplingBridge: триада не активна — заполните файлы в каталоге Environment.");
        else
        {
          Logger.Info($"CouplingBridge: триада активна, phase={_config.Phase}, contour={_config.ContourId}, engine={_config.UseFullNicheEngine}, role={_nicheEngine.RoleProfile.ProfileId}, run={_experimentRunId}, {TriadPhaseStagePolicy.FormatAllowedRange(AppGlobalState.EvolutionStage)}");
          WriteRunManifest("config_reload");
        }
      }
    }

    /// <summary>
    /// Приводит фазу в triad_config к допустимому диапазону для текущей стадии (§4.1).
    /// При изменении записывает triad_config.dat и переконфигурирует AOE.
    /// </summary>
    /// <param name="persistToDisk">Сохранить скорректированную фазу в Environment.</param>
    /// <returns>True, если фаза была изменена.</returns>
    public bool SyncPhaseWithEvolutionStage(bool persistToDisk = true)
    {
      lock (_lock)
      {
        if (_config == null)
        {
          _lastSyncedEvolutionStage = AppGlobalState.EvolutionStage;
          return false;
        }

        int stage = AppGlobalState.EvolutionStage;
        TriadPhase before = _config.Phase;
        TriadPhase clamped = TriadPhaseStagePolicy.ClampPhase(before, stage);
        _lastSyncedEvolutionStage = stage;

        if (clamped == before)
          return false;

        _config.Phase = clamped;
        _aoeEvaluation.Configure(_config);

        Logger.Warning(
            $"TriadPhase: фаза {before} недопустима при {TriadPhaseStagePolicy.FormatAllowedRange(stage)} — установлена {clamped}.");

        if (persistToDisk && !string.IsNullOrWhiteSpace(_environmentFolder))
        {
          if (!CouplingMappingLoader.TrySaveTriadPhase(_environmentFolder, clamped, out string saveError))
            Logger.Warning($"TriadPhase: не удалось сохранить фазу в Environment: {saveError}");
        }

        WriteRunManifest("phase_stage_sync");
        return true;
      }
    }

    /// <summary>
    /// Фиксирует снимок инициализации Niche в файл и лог исследований (§6.11).
    /// </summary>
    public void RecordRunStart()
    {
      lock (_lock)
      {
        _initSnapshot = BuildInitSnapshot();
        NicheInitLogger.AppendSnapshot(_environmentFolder, _initSnapshot);
        _researchLogger?.LogNicheInitSnapshot(_initSnapshot);
        WriteRunManifest("run_start");
      }
    }

    /// <summary>
    /// Вызывается после успешного <see cref="AdaptiveActionsSystem.ApplyAction"/>.
    /// </summary>
    /// <param name="actionId">ID действия Creature.</param>
    /// <param name="pulse">Номер пульса.</param>
    public void NotifyCreatureActionApplied(int actionId, int pulse)
    {
      if (!IsActive || actionId <= 0)
        return;

      lock (_lock)
      {
        if (!_couplingByAction.TryGetValue(actionId, out List<CouplingTarget> targets) || targets.Count == 0)
          return;

        int vigor = _adaptiveActions.GetModifiedVigor(actionId);
        float vigorScale = vigor > 0 ? vigor / 5f : 1f;
        _nicheEngine.ApplyCreatureCoupling(actionId, pulse, vigorScale, targets);
      }
    }

    /// <summary>
    /// Обработка такта до <see cref="Gomeostas.GomeostasSystem.UpdateStateOnly"/> (§6.5).
    /// </summary>
    /// <param name="pulse">Глобальный номер пульса.</param>
    public void ProcessPulseBeforeGomeostasis(int pulse)
    {
      if (!IsActive)
        return;

      if (_lastSyncedEvolutionStage != AppGlobalState.EvolutionStage)
        SyncPhaseWithEvolutionStage();

      Dictionary<int, float> creatureBefore;
      Dictionary<int, float> nicheBefore;
      Dictionary<int, float> nicheAfter;
      Dictionary<int, float> creatureAfterMapping;
      Dictionary<int, float> spontaneous;
      Dictionary<int, float> response;
      int actionId;
      int reflexesApplied;
      StimulusOrigin origin;
      string contourProbeKey = string.Empty;
      int contourInputDim = 0;
      Dictionary<int, float> contourInputDelta = new Dictionary<int, float>();

      lock (_lock)
      {
        _nicheEngine.BeginPulse();
        _nicheEngine.ApplySpontaneousAndContour(pulse);

        var contourApp = _nicheEngine.LastContourApplication;
        if (contourApp != null)
        {
          contourProbeKey = contourApp.ProbeKey ?? string.Empty;
          contourInputDim = contourApp.Snapshot?.Dim ?? 0;
          if (contourApp.NicheDeltas != null)
          {
            foreach (var kv in contourApp.NicheDeltas)
              contourInputDelta[kv.Key] = kv.Value;
          }
        }

        creatureBefore = SnapshotCreatureParams();
        nicheBefore = NicheState.GetSnapshotBeforePulse();

        ApplyNicheToCreatureMapping(pulse);

        nicheAfter = _nicheEngine.EndPulse();
        creatureAfterMapping = SnapshotCreatureParams();
        origin = _creatureGomeostas.LastHostBatchUpdateOrigin;

        NicheState.ComputeDeltasForLog(nicheAfter, out spontaneous, out response);
        actionId = NicheState.LastCreatureActionPulse == pulse ? NicheState.LastCreatureActionId : 0;
        reflexesApplied = _config.UseFullNicheEngine ? _nicheEngine.LastReflexesApplied : 0;
      }

      LastContourProbeKey = contourProbeKey;
      LastContourInputDim = contourInputDim;
      LastContourInputDelta = contourInputDelta;

      var entry = new DyadPulseLogEntry
      {
        Pulse = pulse,
        CreatureActionId = actionId,
        NicheStateBefore = nicheBefore,
        NicheStateAfter = nicheAfter,
        CreatureGomeoBefore = creatureBefore,
        CreatureGomeoAfterMapping = creatureAfterMapping,
        LastCreatureUpdateOrigin = origin,
        NicheSpontaneousDelta = spontaneous,
        NicheResponseDelta = response,
        ContourId = _config.ContourId,
        NicheReflexesApplied = reflexesApplied,
        NicheRoleProfileId = _nicheEngine.RoleProfile.ProfileId,
        ContourProbeKey = contourProbeKey,
        ContourInputDim = contourInputDim,
        ContourInputDelta = contourInputDelta,
        ExperimentRunId = _experimentRunId,
        CouplingMappingVersion = _config?.CouplingMappingVersion ?? 0
      };

      _researchLogger?.LogDyadPulseEntry(entry);
      _aoeEvaluation.RecordDyadPulse(entry);
    }

    /// <summary>
    /// Открывает окно первичного AOE после успешного automatizm (фаза B+).
    /// </summary>
    /// <param name="automatizmId">ID automatizm.</param>
    /// <param name="actionPulse">Пульс выполнения.</param>
    /// <param name="creatureParamsBefore">Снимок параметров Creature.</param>
    /// <param name="overallStateBefore">Интегральное состояние до отклика Niche.</param>
    public void OnAutomatizmExecuted(
        int automatizmId,
        int actionPulse,
        IReadOnlyDictionary<int, float> creatureParamsBefore,
        AppGlobalState.HomeostasisState overallStateBefore)
    {
      if (!IsActive)
        return;

      _aoeEvaluation.OpenWindow(automatizmId, actionPulse, creatureParamsBefore, overallStateBefore);
    }

    /// <summary>
    /// Завершает отложенный первичный AOE после обновления гомеостаза.
    /// </summary>
    /// <param name="gomeostas">Гомеостаз Creature.</param>
    /// <param name="calculator">Калькулятор оценки.</param>
    /// <param name="result">Исход AOE.</param>
    /// <returns>True, если результат готов.</returns>
    public bool TryFinalizePrimaryNicheAoe(
        GomeostasSystem gomeostas,
        HomeostasisCalculator calculator,
        out NicheAoeResult result)
    {
      if (!_aoeEvaluation.TryFinalizePending(gomeostas, calculator, out result))
        return false;

      _researchLogger?.LogNicheAoeOutcome(result);
      return true;
    }

    /// <summary>
    /// Применяет coupling Operator→Niche (фаза C, §6.4).
    /// </summary>
    /// <param name="influenceActionId">ID воздействия с пульта.</param>
    /// <returns>True, если хотя бы одна дельта применена.</returns>
    public bool ApplyOperatorInfluenceToNiche(int influenceActionId)
    {
      if (!IsActive || influenceActionId <= 0)
        return false;

      lock (_lock)
      {
        if (!_operatorCouplingByAction.TryGetValue(influenceActionId, out List<OperatorNicheCouplingTarget> targets) ||
            targets.Count == 0)
        {
          Logger.Warning($"CouplingBridge: нет Operator→Niche mapping для influence ID={influenceActionId}");
          return false;
        }

        foreach (var t in targets)
          NicheState.ApplyCouplingDelta(t.NicheParamId, t.Delta * t.Scale);

        Logger.Info($"CouplingBridge: Operator→Niche influence ID={influenceActionId}, targets={targets.Count}");
        return true;
      }
    }

    /// <summary>
    /// Применяет coupling Operator→Niche для списка воздействий.
    /// </summary>
    /// <param name="influenceActionIds">ID воздействий с пульта.</param>
    /// <returns>Число успешно применённых mapping.</returns>
    public int ApplyOperatorInfluencesToNiche(IEnumerable<int> influenceActionIds)
    {
      if (influenceActionIds == null)
        return 0;

      int applied = 0;
      foreach (int id in influenceActionIds)
      {
        if (ApplyOperatorInfluenceToNiche(id))
          applied++;
      }

      return applied;
    }

    /// <summary>
    /// Выполняет сброс диады (§6.12).
    /// </summary>
    /// <param name="resetType">Тип сброса.</param>
    /// <returns>Результат операции.</returns>
    public DyadResetResult ResetDyad(DyadResetType resetType)
    {
      lock (_lock)
      {
        var result = new DyadResetResult
        {
          ResetType = resetType,
          ExperimentRunId = _experimentRunId,
          CouplingMappingVersion = _config?.CouplingMappingVersion ?? 0
        };

        switch (resetType)
        {
          case DyadResetType.NicheSoft:
            ResetNicheToInitSnapshot();
            result.Success = true;
            result.Message = "NicheSoft: параметры Niche восстановлены из NicheInitSnapshot";
            break;

          case DyadResetType.CreatureSoft:
            ResetCreatureSoftInternal();
            result.Success = true;
            result.Message = "CreatureSoft: гомеостаз Creature → норма, окна AOE сброшены";
            break;

          case DyadResetType.DyadHard:
            ResetNicheToInitSnapshot();
            ResetCreatureSoftInternal();
            _experimentRunId = Guid.NewGuid().ToString("N");
            _initSnapshot = BuildInitSnapshot();
            result.ExperimentRunId = _experimentRunId;
            result.Success = true;
            result.Message = "DyadHard: Niche + Creature → начальные снимки, новый experiment_run_id";
            break;

          case DyadResetType.Calibration:
            ResetNicheCalibration();
            result.Success = true;
            result.Message = "Calibration: параметры Niche → значения из niche_params.dat";
            break;

          default:
            result.Success = false;
            result.Message = "Неизвестный тип сброса";
            break;
        }

        if (result.Success)
        {
          Logger.Info($"CouplingBridge reset: {result.Message}");
          _researchLogger?.LogDyadReset(result);
          WriteRunManifest(resetType == DyadResetType.DyadHard ? "dyad_hard_reset" : "dyad_reset");
        }

        return result;
      }
    }

    /// <summary>
    /// Мягкий сброс Niche к начальным значениям (§6.12).
    /// </summary>
    public void ResetNicheSoft()
    {
      ResetDyad(DyadResetType.NicheSoft);
    }

    /// <inheritdoc />
    public void Dispose()
    {
      if (_disposed)
        return;
      _aoeEvaluation.Dispose();
      _nicheEngine.Dispose();
      _disposed = true;
    }

    private void RebuildCouplingIndex()
    {
      _couplingByAction = new Dictionary<int, List<CouplingTarget>>();
      foreach (var t in _config.ActionCoupling)
      {
        if (!_couplingByAction.TryGetValue(t.ActionId, out List<CouplingTarget> list))
        {
          list = new List<CouplingTarget>();
          _couplingByAction[t.ActionId] = list;
        }
        list.Add(t);
      }

      _operatorCouplingByAction = new Dictionary<int, List<OperatorNicheCouplingTarget>>();
      foreach (var t in _config.OperatorNicheCoupling)
      {
        if (!_operatorCouplingByAction.TryGetValue(t.InfluenceActionId, out List<OperatorNicheCouplingTarget> list))
        {
          list = new List<OperatorNicheCouplingTarget>();
          _operatorCouplingByAction[t.InfluenceActionId] = list;
        }
        list.Add(t);
      }
    }

    private NicheInitSnapshot BuildInitSnapshot()
    {
      return new NicheInitSnapshot
      {
        CapturedAtUtc = DateTime.UtcNow,
        ExperimentRunId = _experimentRunId,
        EnvironmentFolder = _environmentFolder,
        Config = _config,
        InitialNicheParams = NicheState.GetCurrentValues(),
        InitialCreatureParams = new Dictionary<int, float>(_creatureInitialParams)
      };
    }

    private void WriteRunManifest(string eventName)
    {
      if (string.IsNullOrWhiteSpace(_logsFolder) || _config == null)
        return;

      TriadRunManifestLogger.WriteCurrent(_logsFolder, new TriadRunManifest
      {
        ExperimentRunId = _experimentRunId ?? string.Empty,
        CouplingMappingVersion = _config.CouplingMappingVersion,
        Phase = _config.Phase.ToString(),
        ContourId = _config.ContourId ?? string.Empty,
        EnvironmentFolder = _environmentFolder ?? string.Empty,
        NicheRoleProfileId = _nicheEngine?.RoleProfile?.ProfileId ?? string.Empty,
        UseProbeContour = _config.UseProbeContour,
        Event = eventName ?? string.Empty
      });
    }

    private void ResetNicheToInitSnapshot()
    {
      if (_initSnapshot?.InitialNicheParams != null && _initSnapshot.InitialNicheParams.Count > 0)
        _nicheEngine.RestoreFromSnapshot(_initSnapshot.InitialNicheParams);
      else
        _nicheEngine.ResetToInitial();

      _aoeEvaluation.Reset();
    }

    private void ResetNicheCalibration()
    {
      _nicheEngine.ResetToInitial();
      _aoeEvaluation.Reset();
    }

    private void ResetCreatureSoftInternal()
    {
      _creatureGomeostas.RestoreParametersToNormForDyadReset();
      AppGlobalState.ForceStopWaitingForOperatorEvaluation();
      if (_triadInstanceState != null)
        _triadInstanceState.ResetWaitingForNicheResponse();
      else
        AppGlobalState.ResetWaitingForNicheResponse();
      _aoeEvaluation.Reset();
    }

    private void ApplyNicheToCreatureMapping(int pulse)
    {
      if (_config.NicheToCreature.Count == 0)
        return;

      var nicheValues = NicheState.GetCurrentValues();
      var updates = new Dictionary<int, float>();

      foreach (var map in _config.NicheToCreature)
      {
        if (map.LagPulses > 0)
          continue;

        if (!nicheValues.TryGetValue(map.NicheParamId, out float nicheVal))
          continue;

        float creatureVal = nicheVal * map.Scale;
        if (creatureVal < 0f) creatureVal = 0f;
        if (creatureVal > 100f) creatureVal = 100f;
        updates[map.CreatureParamId] = creatureVal;
      }

      if (updates.Count == 0)
        return;

      _creatureGomeostas.HostBatchUpdateParameterValues(updates, StimulusOrigin.Niche);
    }

    private Dictionary<int, float> SnapshotCreatureParams()
    {
      var list = _creatureGomeostas.GetAllParameters();
      return list.ToDictionary(p => p.Id, p => p.Value);
    }
  }
}
