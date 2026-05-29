using ISIDA.Gomeostas;
using ISIDA.Niche.Contour;
using System;
using System.Collections.Generic;

namespace ISIDA.Niche
{
  /// <summary>
  /// Движок Niche: host-MVP или универсальный симбионт (Gomeostas + рефлексы), такт §6.5.
  /// </summary>
  public sealed class NicheEngine : IDisposable
  {
    private readonly NicheHostState _legacyState = new NicheHostState();
    private NicheGomeostasState _symbiontState;
    private INicheParameterState _activeState;
    private readonly NicheReflexLayer _reflexLayer;
    private readonly object _lock = new object();
    private RoleProfile _roleProfile = RoleProfile.NicheStage0;
    private IContourAdapter _contour;
    private TriadExperimentConfig _config;
    private string _nicheDataFolder;
    private string _environmentFolder;
    private int _lastReflexesApplied;
    private ContourInputApplication _lastContourApplication;
    private bool _useSymbiontGomeostas;
    private bool _disposed;

    /// <summary>
    /// Создаёт NicheEngine с профилем роли.
    /// </summary>
    /// <param name="roleProfile">Профиль Niche или null (niche_stage_0).</param>
    public NicheEngine(RoleProfile roleProfile = null)
    {
      _roleProfile = roleProfile ?? RoleProfile.NicheStage0;
      _reflexLayer = new NicheReflexLayer(_roleProfile);
      _contour = new StaticContourAdapter("static_mvp");
      _activeState = _legacyState;
    }

    /// <summary>Состояние параметров Niche (host или симбионт).</summary>
    public INicheParameterState State => _activeState;

    /// <summary>Гомеостаз Niche-симбионта; null в режиме host-MVP.</summary>
    public GomeostasSystem NicheGomeostas => _symbiontState?.Gomeostas;

    /// <summary>Контекст симбионта Niche (гомеостаз + рефлексы).</summary>
    public NicheSymbiontContext NicheSymbiont => _symbiontState?.Context;

    /// <summary>True, если Niche использует GomeostasSystem (UseFullNicheEngine).</summary>
    public bool UsesSymbiontGomeostas => _useSymbiontGomeostas;

    /// <summary>Активный профиль роли.</summary>
    public RoleProfile RoleProfile => _roleProfile;

    /// <summary>Число рефлексов, применённых на последнем такте.</summary>
    public int LastReflexesApplied => _lastReflexesApplied;

    /// <summary>True, если Niche инициализирована.</summary>
    public bool IsInitialized => _activeState != null && _activeState.IsInitialized;

    /// <summary>
    /// Инициализирует Niche из конфигурации триады и каталога Data/Niche.
    /// </summary>
    /// <param name="config">Конфигурация эксперимента.</param>
    /// <param name="nicheDataFolder">Каталог Data/Niche.</param>
    /// <param name="environmentFolder">Каталог Environment (fallback параметров).</param>
    public void Initialize(TriadExperimentConfig config, string nicheDataFolder, string environmentFolder)
    {
      lock (_lock)
      {
        _config = config ?? new TriadExperimentConfig();
        _nicheDataFolder = nicheDataFolder;
        _environmentFolder = environmentFolder;
        _useSymbiontGomeostas = _config.UseFullNicheEngine;

        if (!_useSymbiontGomeostas)
        {
          _roleProfile = new RoleProfile
          {
            ProfileId = "host_mvp",
            ActiveMask = SymbiontSubsystem.Gomeostasis
          };
          _symbiontState?.Dispose();
          _symbiontState = null;
          _activeState = _legacyState;
        }
        else
        {
          _roleProfile = RoleProfile.FromConfigName(_config.NicheRoleProfileId);
          if (_symbiontState == null)
            _symbiontState = new NicheGomeostasState();
          _activeState = _symbiontState;
        }

        _contour = CreateContourAdapter(_config, environmentFolder);

        if (_contour is ProbeContourAdapter probeAdapter)
          probeAdapter.ReloadProbes(environmentFolder);

        var parameters = _config.NicheParameters;
        if (parameters == null || parameters.Count == 0)
          parameters = CouplingMappingLoader.LoadFromFolder(environmentFolder ?? string.Empty).NicheParameters;

        if (_useSymbiontGomeostas)
        {
          _symbiontState.Initialize(_nicheDataFolder, _roleProfile, parameters);
        }
        else
        {
          _legacyState.Initialize(parameters);
        }

        string reflexFolder = _useSymbiontGomeostas
            ? NicheSymbiontBootstrap.GetReflexesFolder(_nicheDataFolder)
            : null;
        _reflexLayer.SetRoleProfile(_roleProfile);
        bool useLegacyReflexFile = _useSymbiontGomeostas &&
            (_symbiontState.Context?.GeneticReflexes?.GetAllGeneticReflexesList()?.Count ?? 0) == 0;
        _reflexLayer.LoadRules(
            _config.UseFullNicheEngine && (!_useSymbiontGomeostas || useLegacyReflexFile)
                ? reflexFolder ?? _nicheDataFolder
                : null);
      }
    }

    /// <summary>
    /// Начало такта Niche.
    /// </summary>
    public void BeginPulse()
    {
      lock (_lock)
      {
        _lastReflexesApplied = 0;
        _activeState.BeginPulse();
      }
    }

    /// <summary>Последнее применение contour InputSnapshot.</summary>
    public ContourInputApplication LastContourApplication => _lastContourApplication;

    /// <summary>
    /// Спонтанный дрейф и contour-input.
    /// </summary>
    /// <param name="pulse">Глобальный пульс.</param>
    public void ApplySpontaneousAndContour(int pulse)
    {
      lock (_lock)
      {
        if (!_roleProfile.IsActive(SymbiontSubsystem.Gomeostasis))
          return;

        _lastContourApplication = null;
        bool drift = _config != null && _config.SpontaneousDriftEnabled;
        var contourDeltas = _contour?.GetNicheDeltasForPulse(pulse);
        if (_contour is ProbeContourAdapter probeAdapter)
          _lastContourApplication = probeAdapter.LastApplication;

        _activeState.ApplySpontaneousUpdate(drift, contourDeltas);
      }
    }

    /// <summary>
    /// Coupling Creature→Niche и реактивные рефлексы.
    /// </summary>
    /// <param name="actionId">ID действия Creature.</param>
    /// <param name="pulse">Пульс.</param>
    /// <param name="vigorScale">Множитель vigor.</param>
    /// <param name="targets">Цели coupling.</param>
    public void ApplyCreatureCoupling(int actionId, int pulse, float vigorScale, IReadOnlyList<CouplingTarget> targets)
    {
      lock (_lock)
      {
        if (actionId <= 0 || targets == null || targets.Count == 0)
          return;

        foreach (var t in targets)
        {
          float delta = t.Delta * t.Scale * vigorScale;
          _activeState.ApplyCouplingDelta(t.NicheParamId, delta);
        }

        _activeState.MarkCreatureAction(actionId, pulse);
        if (_useSymbiontGomeostas && _symbiontState != null)
          _lastReflexesApplied = _symbiontState.ApplySymbiontReflexes(actionId);
        else
          _lastReflexesApplied = _reflexLayer.ApplyReactiveReflexes(_activeState, actionId);
      }
    }

    /// <summary>
    /// Завершение такта: снимок «после».
    /// </summary>
    /// <returns>Состояние после такта.</returns>
    public Dictionary<int, float> EndPulse()
    {
      lock (_lock)
        return _activeState.EndPulse();
    }

    /// <summary>
    /// Перенос на Niche₂: новые начальные параметры при сохранении mapping (§6.6).
    /// </summary>
    /// <param name="newInitialParams">Новые значения paramId→value.</param>
    public void ApplyNicheTransfer(IReadOnlyDictionary<int, float> newInitialParams)
    {
      lock (_lock)
      {
        if (newInitialParams == null || newInitialParams.Count == 0)
          return;

        _activeState.RestoreFromSnapshot(newInitialParams);
      }
    }

    /// <summary>
    /// Устанавливает probe-key контура для следующего такта (§6.8, EnvironmentMetricProbeKey).
    /// </summary>
    /// <param name="probeKey">Ключ пробы или пусто.</param>
    public void SetContourProbeKey(string probeKey)
    {
      lock (_lock)
      {
        if (_contour is ProbeContourAdapter probeAdapter)
          probeAdapter.SetActiveProbeKey(probeKey);
      }
    }

    /// <summary>
    /// Передаёт InputSnapshot контура напрямую (host API).
    /// </summary>
    /// <param name="snapshot">Снимок входа.</param>
    /// <param name="probeKey">Опциональный probeKey для лога.</param>
    public void ApplyContourInputSnapshot(InputSnapshot snapshot, string probeKey = null)
    {
      lock (_lock)
      {
        if (_contour is ProbeContourAdapter probeAdapter)
          probeAdapter.ApplyInputSnapshot(snapshot, probeKey);
      }
    }

    /// <summary>
    /// Сброс Niche к начальным значениям из конфигурации.
    /// </summary>
    public void ResetToInitial()
    {
      lock (_lock)
      {
        if (_config?.NicheParameters != null)
          _activeState.ResetToInitial(_config.NicheParameters);
      }
    }

    /// <summary>
    /// Восстановление из снимка InitSnapshot.
    /// </summary>
    /// <param name="snapshot">Снимок параметров.</param>
    public void RestoreFromSnapshot(IReadOnlyDictionary<int, float> snapshot)
    {
      lock (_lock)
        _activeState.RestoreFromSnapshot(snapshot);
    }

    /// <inheritdoc />
    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;
      _symbiontState?.Dispose();
      _symbiontState = null;
    }

    private static IContourAdapter CreateContourAdapter(TriadExperimentConfig config, string environmentFolder)
    {
      string contourId = config?.ContourId ?? "static_mvp";
      if (config != null && config.UseProbeContour)
        return new ProbeContourAdapter(contourId, environmentFolder);

      return new StaticContourAdapter(contourId);
    }
  }
}
