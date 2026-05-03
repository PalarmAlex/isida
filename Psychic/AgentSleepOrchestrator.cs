using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic.Memory.Episodic;
using ISIDA.Psychic.Thinking;
using System;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Фазы сна и ограничение моторики / циклов мышления по плану офлайн-переработки.
  /// Не дублирует обучение — задаёт режим работы существующих подсистем.
  /// </summary>
  public sealed class AgentSleepOrchestrator
  {
    /// <summary>Фаза одной сессии сна.</summary>
    public enum SleepPhase
    {
      /// <summary>Бодрствование (сессия сна не активна или уже завершена).</summary>
      Awake = 0,
      /// <summary>Вход в сон (минимум обработки).</summary>
      EnteringSleep = 1,
      /// <summary>Глубокий сон — без циклов мышления.</summary>
      DeepSleep = 2,
      /// <summary>Переработка — <see cref="ThinkingCyclesSystem.DispatchCycles(int, bool)"/> с <c>isSleeping: true</c>.</summary>
      Reprocessing = 3,
      /// <summary>Выход из сна.</summary>
      Waking = 4
    }

    /// <summary>Параметры длительности фаз (пульсы) и консолидации.</summary>
    public sealed class Settings
    {
      /// <summary>Пульсы фазы входа в сон.</summary>
      public int EnteringSleepPulses { get; set; } = 2;
      /// <summary>Пульсы глубокого сна без мышления.</summary>
      public int DeepSleepPulses { get; set; } = 5;
      /// <summary>Пульсы фазы переработки (сновидение / офлайн циклы).</summary>
      public int ReprocessingPulses { get; set; } = 8;
      /// <summary>Пульсы завершения перед пробуждением.</summary>
      public int WakingPulses { get; set; } = 1;
      /// <summary>Пробуждение при Danger или VeryActualSituation в ИС.</summary>
      public bool WakeOnDangerOrUrgent { get; set; } = true;
      /// <summary>Не выполнять моторные автоматизмы на пульт во время сна.</summary>
      public bool SuppressExternalMotorDuringSleep { get; set; } = true;
      /// <summary>Включить ослабление «шумовых» листьев эпизодики во фазе переработки.</summary>
      public bool EnableEpisodicWeakLeafPass { get; set; } = true;
      /// <summary>Максимум листьев дерева эпизодов за один проход консолидации.</summary>
      public int EpisodicWeakLeafScanBudget { get; set; } = 80;
      /// <summary>Сохранять эпизодику на диск после консолидации при изменениях.</summary>
      public bool SaveEpisodicAfterConsolidationChanges { get; set; } = true;
    }

    private static AgentSleepOrchestrator _instance;

    /// <summary>
    /// Единственный экземпляр оркестратора; требуется предварительный вызов <see cref="Initialize(InformationEnvironmentSystem)"/>.
    /// </summary>
    public static AgentSleepOrchestrator Instance => _instance ??
        throw new InvalidOperationException("AgentSleepOrchestrator не инициализирован.");

    /// <summary>Указывает, был ли выполнен <see cref="Initialize(InformationEnvironmentSystem)"/>.</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Сбрасывает синглтон после завершения <see cref="T:ISIDA.Common.IsidaEngine+IsidaContext"/> (повторный <see cref="IsidaEngine.Create"/> в том же AppDomain, например выгрузка плагина SolidWorks).
    /// </summary>
    public static void Reset()
    {
      _instance = null;
    }

    /// <summary>
    /// Создаёт синглтон оркестратора. Вызывать один раз при инициализации движка.
    /// </summary>
    /// <param name="informationEnvironment">Информационная среда (опасность / срочность — для досрочного пробуждения).</param>
    public static void Initialize(InformationEnvironmentSystem informationEnvironment)
    {
      if (_instance != null)
        throw new InvalidOperationException("AgentSleepOrchestrator уже инициализирован.");
      _instance = new AgentSleepOrchestrator(informationEnvironment ?? throw new ArgumentNullException(nameof(informationEnvironment)));
    }

    private readonly InformationEnvironmentSystem _informationEnvironment;
    private Settings _settings = new Settings();

    private AgentSleepOrchestrator(InformationEnvironmentSystem informationEnvironment)
    {
      _informationEnvironment = informationEnvironment;
    }

    /// <summary>Текущие настройки (можно менять до/во время работы).</summary>
    public Settings Config
    {
      get => _settings;
      set => _settings = value ?? new Settings();
    }

    /// <summary>Текущая фаза сна; при неактивной сессии — <see cref="SleepPhase.Awake"/>.</summary>
    public SleepPhase Phase { get; private set; } = SleepPhase.Awake;

    /// <summary>Активна ли сессия сна (фазы после пробуждения сбрасываются).</summary>
    public bool SessionActive { get; private set; }

    /// <summary>Пульсов в текущей фазе.</summary>
    public int PhasePulseIndex { get; private set; }

    /// <summary>Последний явный тип сна от таймера (1 — сон, 2 — с фазой сновидения при старте).</summary>
    public int LastExplicitSleepingType { get; private set; }

    private bool _preferDreamOnStart;

    /// <summary>Подавлять исполнение автоматизмов наружу (моторика).</summary>
    public bool SuppressExternalMotor =>
        SessionActive && (_settings.SuppressExternalMotorDuringSleep);

    /// <summary>Запускать DispatchCycles в режиме сна (RunDreamingStep).</summary>
    public bool ShouldRunSleepThinkingCycles =>
        SessionActive && (Phase == SleepPhase.Reprocessing || Phase == SleepPhase.Waking);

    /// <summary>Фаза сновидения для событий агента (бывший IsSleepingDream).</summary>
    public bool IsDreamReprocessingPhase => SessionActive && Phase == SleepPhase.Reprocessing;

    /// <summary>
    /// Вызывать в начале <see cref="PsychicSystem.ProcessPsychicPulse"/> после обновления стадии/пульса.
    /// </summary>
    /// <param name="pulseCount">Номер пульса.</param>
    /// <param name="timerSleepingType">0 — нет явного запроса; 1 — сон; 2 — сон с приоритетом переработки.</param>
    public void ResolvePulse(int pulseCount, int timerSleepingType)
    {
      LastExplicitSleepingType = timerSleepingType;

      if (_settings.WakeOnDangerOrUrgent && SessionActive && TryDangerWake())
        return;

      if (timerSleepingType > 0)
      {
        _preferDreamOnStart = timerSleepingType >= 2;
        if (!SessionActive)
          BeginSession(pulseCount);
        return;
      }

      // Сохранённый сон из свойств агента без явного типа от таймера
      if (!SessionActive && AppGlobalState.IsSleeping)
      {
        _preferDreamOnStart = false;
        BeginSession(pulseCount);
        return;
      }

      if (!SessionActive)
      {
        Phase = SleepPhase.Awake;
        PhasePulseIndex = 0;
        return;
      }

      AdvancePhaseOrComplete(pulseCount);
    }

    private bool TryDangerWake()
    {
      try
      {
        var env = _informationEnvironment?.CurrentInformationEnvironment;
        if (env == null)
          return false;
        if (env.Danger || env.VeryActualSituation)
        {
          Logger.Info("Сон прерван: Danger или VeryActualSituation в информационной среде.");
          CompleteSessionWake(wasDangerWake: true);
          return true;
        }
      }
      catch (Exception ex)
      {
        Logger.Warning($"AgentSleepOrchestrator.TryDangerWake: {ex.Message}");
      }
      return false;
    }

    private void BeginSession(int pulseCount)
    {
      SessionActive = true;
      PhasePulseIndex = 0;
      ApplySleepStateToAgent(true);

      if (_preferDreamOnStart && _settings.DeepSleepPulses <= 0 && _settings.EnteringSleepPulses <= 0)
        Phase = SleepPhase.Reprocessing;
      else if (_preferDreamOnStart && _settings.DeepSleepPulses <= 0)
        Phase = SleepPhase.Reprocessing;
      else
        Phase = _settings.EnteringSleepPulses > 0 ? SleepPhase.EnteringSleep : SleepPhase.DeepSleep;

      if (Phase == SleepPhase.DeepSleep && _settings.DeepSleepPulses <= 0)
        Phase = SleepPhase.Reprocessing;
      if (Phase == SleepPhase.Reprocessing && _settings.ReprocessingPulses <= 0)
        Phase = SleepPhase.Waking;

      Logger.Info($"Сон: начало сессии, фаза={Phase}, пульс={pulseCount}");
    }

    private void AdvancePhaseOrComplete(int pulseCount)
    {
      PhasePulseIndex++;

      switch (Phase)
      {
        case SleepPhase.EnteringSleep:
          if (PhasePulseIndex >= Math.Max(1, _settings.EnteringSleepPulses))
            EnterPhase(SleepPhase.DeepSleep);
          break;
        case SleepPhase.DeepSleep:
          if (PhasePulseIndex >= Math.Max(1, _settings.DeepSleepPulses))
            EnterPhase(SleepPhase.Reprocessing);
          break;
        case SleepPhase.Reprocessing:
          if (PhasePulseIndex >= Math.Max(1, _settings.ReprocessingPulses))
            EnterPhase(SleepPhase.Waking);
          break;
        case SleepPhase.Waking:
          if (PhasePulseIndex >= Math.Max(1, _settings.WakingPulses))
            CompleteSessionWake(wasDangerWake: false);
          break;
      }
    }

    private void EnterPhase(SleepPhase next)
    {
      Phase = next;
      PhasePulseIndex = 0;
      Logger.Info($"Сон: переход в фазу {Phase}");
    }

    private void CompleteSessionWake(bool wasDangerWake)
    {
      SessionActive = false;
      Phase = SleepPhase.Awake;
      PhasePulseIndex = 0;
      ApplySleepStateToAgent(false);

      if (wasDangerWake)
        Logger.Info("Сон: завершение после тревоги.");
      else
        Logger.Info("Сон: естественное пробуждение.");

      TrySaveEpisodicAfterSleep();
    }

    private void ApplySleepStateToAgent(bool sleeping)
    {
      try
      {
        if (GomeostasSystem.IsInitialized)
          GomeostasSystem.Instance.ApplySleepState(sleeping);
        else
        {
          AppGlobalState.IsSleeping = sleeping;
        }
      }
      catch (Exception ex)
      {
        Logger.Warning($"ApplySleepStateToAgent: {ex.Message}");
        AppGlobalState.IsSleeping = sleeping;
      }
    }

    private void TrySaveEpisodicAfterSleep()
    {
      if (!EpisodicMemorySystem.IsInitialized)
        return;
      try
      {
        EpisodicMemorySystem.Instance.FlushSleepConsolidationSaveIfNeeded(_settings.SaveEpisodicAfterConsolidationChanges);
      }
      catch (Exception ex)
      {
        Logger.Warning($"Episodic save after sleep: {ex.Message}");
      }
    }

    /// <summary>
    /// Консолидация эпизодики во фазе переработки (один проход за вызов).
    /// </summary>
    public void RunEpisodicConsolidationIfConfigured()
    {
      if (!SessionActive || Phase != SleepPhase.Reprocessing)
        return;
      if (!_settings.EnableEpisodicWeakLeafPass || !EpisodicMemorySystem.IsInitialized)
        return;
      try
      {
        var (visited, adjusted) = EpisodicMemorySystem.Instance.ApplySleepNoiseReductionToWeakLeaves(
            _settings.EpisodicWeakLeafScanBudget);
        if (visited > 0 && adjusted > 0)
          Logger.Info($"Сон (переработка): эпизодика — осмотрено листьев {visited}, скорректировано {adjusted}");
      }
      catch (Exception ex)
      {
        Logger.Warning($"RunEpisodicConsolidation: {ex.Message}");
      }
    }

    /// <summary>Сброс для тестов / полная очистка сессии.</summary>
    internal static void ResetForTests()
    {
      _instance = null;
    }
  }
}
