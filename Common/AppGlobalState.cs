using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Глобальные переменные с thread-safe доступом через ReaderWriterLockSlim
/// </summary>
public static class AppGlobalState
{
  // Глобальная блокировка для синхронизации доступа ко всем свойствам
  private static readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

  #region Автоматизмы

  private static int _currentActiveAutomatizmId = 0;
  private static int _lastAutomatizmPulse = 0;
  private static int _automatizmNodeId = 0;
  private static int _currentFindAtmzStepCount = 0;
  private static int _lastRunAutomatizmPulsCount = 0;
  private static int _lastTriggerStimulusID = 0;
  private static bool _isAutomatizmChainActive = false;

  #endregion

  #region Рефлексы

  private static int _detectedReflexNodeId = 0;
  private static int _lastOrientationReflexType = 0;
  private static int _lastOrientationReflexPulse = 0;
  private static int _lastThinkingLevel = 0;       // 0 = не активирован, 1 = УМ1, 2 = УМ2
  private static bool _lastThinkingLevelSuccess = false;

  /// <summary>Снимок главного цикла мышления (для ResearchLogger / UI).</summary>
  private static int _mainThinkingCycleId = 0;
  private static int _mainThinkingCycleWeight = 0;
  private static int _mainThinkingCycleProblemNodeId = 0;
  private static int _mainThinkingCycleThemeId = 0;
  private static int _mainThinkingCyclePurposeId = 0;
  private static string _mainThinkingCycleLastStrategyId = null;
  private static bool _mainThinkingCycleAwaitingEvaluation = false;
  private static int _mainThinkingCyclePendingSolutionAutomatizmId = 0;

  /// <summary>Фоновые циклы мышления (снимок после шага диспетчера), упорядочены по убыванию веса.</summary>
  private static PublishedBackgroundThinkingCycleSnapshot[] _publishedBackgroundThinkingCycles =
      Array.Empty<PublishedBackgroundThinkingCycleSnapshot>();

  private static bool _flgConditionReflexes = false;
  private static bool _isReflexChainActive = false;
  private static List<int> _geneticReflexesActions = new List<int>();
  private static List<int> _conditionReflexesActions = new List<int>();
  private static int _lastDetectedReflexNodeId = 0; // Для валидации изменений
  private static int _currentGeneticReflexID = 0; // ID безусловного рефлекса, чьи действия в GeneticReflexesActions

  #endregion

  #region Состояние агента

  private static HomeostasisState _currentOverallState = HomeostasisState.Normal;
  private static int _dominantParam = 0;
  private static bool _isDead = false;
  private static bool _isSleeping = false;
  private static bool _isNewConditions = false;
  private static List<GomeostasSystem.BehaviorStyle> _activeStyles = new List<GomeostasSystem.BehaviorStyle>();
  private static List<AdaptiveActionsSystem.AdaptiveAction> _activeAdaptiveActions = new List<AdaptiveActionsSystem.AdaptiveAction>();

  #endregion

  #region Эволюция и время жизни

  private static int _defaultAdaptiveActionIdage = 0;
  private static int _evolutionStage = 0;
  private static int _lifetime = 0;

  #endregion

  #region Пульт — режим наблюдения

  private static bool _observationMode = false;

  #endregion

  #region Оценка оператора

  private static int _waitingPeriodCountdown = 0;
  private static bool _waitingForOperatorEvaluation = false;
  /// <summary>ID автоматизма, запущенного перед окном ожидания (для отложенной оценки, если поля психики обнулились).</summary>
  private static int _automatizmIdWaitingForOperatorEvaluation = 0;
  private static int _lastAutomatizmEvaluationTime = 0;
  private static int _waitingPeriodForActionsVal = 0;
  private static int _noOperatorStimulusSilencePulses = 30;
  private static HomeostasisState _stateBeforeOperatorImpact = HomeostasisState.Normal;

  /// <summary>Снимок значений параметров гомеостаза на момент старта отслеживания автоматизма (до ответа оператора).</summary>
  private static Dictionary<int, float> _operatorEvalParameterValuesBefore;

  /// <summary>ID параметра, выбранного как фокус оценки (доминант на момент старта отслеживания).</summary>
  private static int _operatorEvalFocusParameterId;

  /// <summary>Есть ли валидный снимок для оценки по дельте параметров.</summary>
  private static bool _operatorEvalParameterSnapshotValid;

  /// <summary>
  /// До применения конфига движком период ожидания в пульсах может быть 0 — тогда окно ответа считалось закрытым,
  /// TryScheduleDeferredOperatorEvaluationOnStimulus сбрасывал зеркало и сдвиги не создавались. Дефолт 30 пульсов, как у IsidaEngine.
  /// </summary>
  private static int GetEffectiveWaitingPeriodForActionsVal()
  {
    return _waitingPeriodForActionsVal > 0 ? _waitingPeriodForActionsVal : 30;
  }

  #endregion

  #region Вербальные образы

  private static int _curActiveVerbalId = 0;

  #endregion

  #region Промпт свойств агента

  /// <summary>
  /// Базовая часть промпта (общая для всех генераций) — параметры агента без текстов вставки.
  /// Обновляется движком через GomeostasSystem.UpdateAgentPropertiesPromptContent().
  /// </summary>
  private static string _agentPropertiesPromptContent = string.Empty;

  /// <summary>
  /// Текстовая строка промпта для генерации
  /// </summary>
  public static string AgentPropertiesPromptContent
  {
    get
    {
      _lock.EnterReadLock();
      try { return _agentPropertiesPromptContent ?? string.Empty; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try { _agentPropertiesPromptContent = value ?? string.Empty; }
      finally { _lock.ExitWriteLock(); }
    }
  }

  #endregion

  #region Эпизодическая память

  /// <summary>Образ стимула (действий оператора) перед ответом Beast</summary>
  private static int _curStimulusImageId = 0;

  /// <summary>Текущая значимость стимула (−10…10) после последнего применения воздействий с пульта; для учительского правила в эпизодике.</summary>
  private static int _currentStimulsEffect = 0;

  /// <summary>Предыдущая значимость стимула до последнего сдвига при воздействии с пульта; для прямого правила в эпизодике.</summary>
  private static int _prevStimulsEffect = 0;

  /// <summary>Образ стимула (действий оператора) перед ответом Beast</summary>
  public static int CurStimulusImageId
  {
    get { _lock.EnterReadLock(); try { return _curStimulusImageId; } finally { _lock.ExitReadLock(); } }
    set { _lock.EnterWriteLock(); try { _curStimulusImageId = value; } finally { _lock.ExitWriteLock(); } }
  }

  /// <summary>Текущая значимость стимула (−10…10) после последнего применения воздействий с пульта.</summary>
  public static int CurrentStimulsEffect
  {
    get { _lock.EnterReadLock(); try { return _currentStimulsEffect; } finally { _lock.ExitReadLock(); } }
    set { _lock.EnterWriteLock(); try { _currentStimulsEffect = value; } finally { _lock.ExitWriteLock(); } }
  }

  /// <summary>Предыдущая значимость стимула (значение «текущей» до последнего сдвига при воздействии с пультом).</summary>
  public static int PrevStimulsEffect
  {
    get { _lock.EnterReadLock(); try { return _prevStimulsEffect; } finally { _lock.ExitReadLock(); } }
    set { _lock.EnterWriteLock(); try { _prevStimulsEffect = value; } finally { _lock.ExitWriteLock(); } }
  }

  /// <summary>
  /// Атомарно сдвигает пару значимости стимула: предыдущая ← бывшая текущая, текущая ← новое значение (уже с учётом обнуления для стимула без гомео-кнопок и усечения −10…10).
  /// </summary>
  /// <param name="newCurrentStimulsEffect">Новое значение текущей значимости.</param>
  public static void AdvanceStimulusEffectPair(int newCurrentStimulsEffect)
  {
    _lock.EnterWriteLock();
    try
    {
      _prevStimulsEffect = _currentStimulsEffect;
      _currentStimulsEffect = newCurrentStimulsEffect;
    }
    finally
    {
      _lock.ExitWriteLock();
    }
  }

  #endregion

  #region Автоматизмы - Свойства и методы

  /// <summary>
  /// ID триггера с пульта
  /// </summary>
  public static int LastTriggerStimulusID
  {
    get
    {
      _lock.EnterReadLock();
      try { return _lastTriggerStimulusID; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try { _lastTriggerStimulusID = value; }
      finally { _lock.ExitWriteLock(); }
    }
  }

  /// <summary>
  /// Флаг активности цепочки автоматизмов (thread-safe)
  /// </summary>
  public static bool IsAutomatizmChainActive
  {
    get
    {
      _lock.EnterReadLock();
      try { return _isAutomatizmChainActive; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try { _isAutomatizmChainActive = value; }
      finally { _lock.ExitWriteLock(); }
    }
  }

  /// <summary>
  /// ID текущего активного автоматизма (thread-safe)
  /// </summary>
  public static int CurrentActiveAutomatizmId
  {
    get
    {
      _lock.EnterReadLock();
      try { return _currentActiveAutomatizmId; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try { _currentActiveAutomatizmId = value; }
      finally { _lock.ExitWriteLock(); }
    }
  }

  /// <summary>
  /// Пульс, на котором был активирован последний автоматизм (thread-safe)
  /// </summary>
  public static int LastAutomatizmPulse
  {
    get
    {
      _lock.EnterReadLock();
      try { return _lastAutomatizmPulse; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try { _lastAutomatizmPulse = value; }
      finally { _lock.ExitWriteLock(); }
    }
  }

  /// <summary>
  /// Последний распознанный узел дерева автоматизмов (thread-safe)
  /// </summary>
  public static int AutomatizmNodeId
  {
    get
    {
      _lock.EnterReadLock();
      try { return _automatizmNodeId; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try { _automatizmNodeId = value; }
      finally { _lock.ExitWriteLock(); }
    }
  }

  /// <summary>
  /// Текущий шаг при поиске автоматизма в узлах ветки дерева автоматизмов (thread-safe)
  /// </summary>
  public static int CurrentFindAtmzStepCount
  {
    get
    {
      _lock.EnterReadLock();
      try { return _currentFindAtmzStepCount; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try { _currentFindAtmzStepCount = value; }
      finally { _lock.ExitWriteLock(); }
    }
  }

  /// <summary>
  /// Пульс, на котором был запущен текущий автоматизм (thread-safe)
  /// </summary>
  public static int LastRunAutomatizmPulsCount
  {
    get
    {
      _lock.EnterReadLock();
      try { return _lastRunAutomatizmPulsCount; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try { _lastRunAutomatizmPulsCount = value; }
      finally { _lock.ExitWriteLock(); }
    }
  }

  /// <summary>
  /// Обновить информацию об активации автоматизма
  /// </summary>
  public static void UpdateAutomatizmInfo(int automatizmId, int pulse)
  {
    CurrentActiveAutomatizmId = automatizmId;
    LastAutomatizmPulse = pulse;
  }

  /// <summary>
  /// Сбросить информацию об автоматизме
  /// </summary>
  public static void ResetAutomatizmInfo()
  {
    CurrentActiveAutomatizmId = 0;
    LastAutomatizmPulse = 0;
  }

  /// <summary>
  /// Получить информацию об активации автоматизма
  /// </summary>
  public static (int Id, int Pulse) GetAutomatizmInfo()
  {
    return (CurrentActiveAutomatizmId, LastAutomatizmPulse);
  }

  #endregion

  #region Рефлексы - Свойства и методы

  /// <summary>
  /// Флаг активности цепочки рефлексов (thread-safe)
  /// </summary>
  public static bool IsReflexChainActive
  {
    get
    {
      _lock.EnterReadLock();
      try { return _isReflexChainActive; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try { _isReflexChainActive = value; }
      finally { _lock.ExitWriteLock(); }
    }
  }

  /// <summary>
  /// Последний распознанный узел дерева рефлексов (thread-safe)
  /// </summary>
  public static int DetectedReflexNodeId
  {
    get
    {
      _lock.EnterReadLock();
      try { return _detectedReflexNodeId; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try
      {
        _lastDetectedReflexNodeId = _detectedReflexNodeId;
        _detectedReflexNodeId = value;
      }
      finally { _lock.ExitWriteLock(); }
    }
  }

  /// <summary>
  /// Тип последнего активированного ориентировочного рефлекса (0 = нет, 1 = ОР1, 2 = ОР2)
  /// </summary>
  public static int LastOrientationReflexType
  {
    get => _lastOrientationReflexType;
    internal set => _lastOrientationReflexType = value;
  }

  /// <summary>
  /// Пульс, на котором был активирован последний ориентировочный рефлекс
  /// </summary>
  public static int LastOrientationReflexPulse
  {
    get => _lastOrientationReflexPulse;
    internal set => _lastOrientationReflexPulse = value;
  }

  /// <summary>
  /// Флаг наличия условных рефлексов
  /// </summary>
  public static bool FlgConditionReflexes
  {
    get => _flgConditionReflexes;
    set => _flgConditionReflexes = value;
  }

  /// <summary>
  /// Текущие активные безусловные рефлексы
  /// </summary>
  public static IReadOnlyList<int> GeneticReflexesActions
  {
    get => _geneticReflexesActions;
  }

  /// <summary>
  /// Текущие активные условные рефлексы
  /// </summary>
  public static IReadOnlyList<int> ConditionedReflexesActions
  {
    get => _conditionReflexesActions;
  }

  /// <summary>
  /// Обновить информацию об активации ориентировочного рефлекса
  /// </summary>
  /// <param name="type">Тип рефлекса: 1 = ОР1, 2 = ОР2</param>
  /// <param name="pulse">Пульс активации</param>
  public static void UpdateOrientationReflexInfo(int type, int pulse)
  {
    LastOrientationReflexType = type;
    LastOrientationReflexPulse = pulse;
  }

  /// <summary>
  /// Получить информацию об активации ориентировочного рефлекса
  /// </summary>
  /// <returns>Кортеж (тип, пульс)</returns>
  public static (int Type, int Pulse) GetOrientationReflexInfo()
  {
    return (LastOrientationReflexType, LastOrientationReflexPulse);
  }

  /// <summary>
  /// Сбросить информацию об ориентировочном рефлексе
  /// </summary>
  public static void ResetOrientationReflexInfo()
  {
    LastOrientationReflexType = 0;
    LastOrientationReflexPulse = 0;
  }

  /// <summary>
  /// Обновить информацию об активации уровня мышления (УМ1 или УМ2)
  /// </summary>
  /// <param name="level">Уровень: 1 = УМ1 (штатный автоматизм), 2 = УМ2 (правила эпизодической памяти)</param>
  /// <param name="success">true — проблема решена на этом уровне, false — не решена</param>
  public static void UpdateThinkingLevelInfo(int level, bool success)
  {
    _lastThinkingLevel = level;
    _lastThinkingLevelSuccess = success;
  }

  /// <summary>
  /// Получить информацию об активации уровня мышления
  /// </summary>
  /// <returns>Кортеж (уровень 1/2 или 0, успех)</returns>
  public static (int Level, bool Success) GetThinkingLevelInfo()
  {
    return (_lastThinkingLevel, _lastThinkingLevelSuccess);
  }

  /// <summary>
  /// Сбросить информацию об уровне мышления
  /// </summary>
  public static void ResetThinkingLevelInfo()
  {
    _lastThinkingLevel = 0;
    _lastThinkingLevelSuccess = false;
  }

  /// <summary>Обновить снимок главного цикла мышления (вызывается из PsychicSystem после шага диспетчера).</summary>
  public static void UpdateMainThinkingCycleSnapshot(int cycleId, int weight, int problemNodeId, int themeId, int purposeId, string lastStrategyId,
      bool awaitingEvaluation = false, int pendingSolutionAutomatizmId = 0)
  {
    _mainThinkingCycleId = cycleId;
    _mainThinkingCycleWeight = weight;
    _mainThinkingCycleProblemNodeId = problemNodeId;
    _mainThinkingCycleThemeId = themeId;
    _mainThinkingCyclePurposeId = purposeId;
    _mainThinkingCycleLastStrategyId = lastStrategyId;
    _mainThinkingCycleAwaitingEvaluation = awaitingEvaluation;
    _mainThinkingCyclePendingSolutionAutomatizmId = pendingSolutionAutomatizmId;
  }

  /// <summary>Сбросить снимок главного цикла (нет активного главного цикла).</summary>
  public static void ClearMainThinkingCycleSnapshot()
  {
    _mainThinkingCycleId = 0;
    _mainThinkingCycleWeight = 0;
    _mainThinkingCycleProblemNodeId = 0;
    _mainThinkingCycleThemeId = 0;
    _mainThinkingCyclePurposeId = 0;
    _mainThinkingCycleLastStrategyId = null;
    _mainThinkingCycleAwaitingEvaluation = false;
    _mainThinkingCyclePendingSolutionAutomatizmId = 0;
    _publishedBackgroundThinkingCycles = Array.Empty<PublishedBackgroundThinkingCycleSnapshot>();
  }

  /// <summary>Публикует список фоновых циклов (после обновления главного).</summary>
  /// <param name="cycles">Снимки без главного цикла, обычно уже отсортированы по убыванию веса.</param>
  public static void UpdatePublishedBackgroundThinkingCycles(IReadOnlyList<PublishedBackgroundThinkingCycleSnapshot> cycles)
  {
    if (cycles == null || cycles.Count == 0)
    {
      _publishedBackgroundThinkingCycles = Array.Empty<PublishedBackgroundThinkingCycleSnapshot>();
      return;
    }
    var arr = new PublishedBackgroundThinkingCycleSnapshot[cycles.Count];
    for (int i = 0; i < cycles.Count; i++)
      arr[i] = cycles[i];
    _publishedBackgroundThinkingCycles = arr;
  }

  /// <summary>Копия снимка фоновых циклов для логирования.</summary>
  /// <returns>Массив снимков (может быть пустым).</returns>
  public static PublishedBackgroundThinkingCycleSnapshot[] GetPublishedBackgroundThinkingCycles()
  {
    var a = _publishedBackgroundThinkingCycles;
    if (a == null || a.Length == 0)
      return Array.Empty<PublishedBackgroundThinkingCycleSnapshot>();
    var copy = new PublishedBackgroundThinkingCycleSnapshot[a.Length];
    Array.Copy(a, copy, a.Length);
    return copy;
  }

  /// <summary>Текущий снимок главного цикла мышления для логирования.</summary>
  public static (int CycleId, int Weight, int ProblemNodeId, int ThemeId, int PurposeId, string LastStrategyId, bool AwaitingEvaluation, int PendingSolutionAutomatizmId) GetMainThinkingCycleSnapshot()
  {
    return (_mainThinkingCycleId, _mainThinkingCycleWeight, _mainThinkingCycleProblemNodeId, _mainThinkingCycleThemeId, _mainThinkingCyclePurposeId, _mainThinkingCycleLastStrategyId, _mainThinkingCycleAwaitingEvaluation, _mainThinkingCyclePendingSolutionAutomatizmId);
  }

  /// <summary>
  /// ID безусловного рефлекса, чьи действия сейчас в GeneticReflexesActions (для клонирования цепочек на стадии 2).
  /// </summary>
  public static int CurrentGeneticReflexID
  {
    get
    {
      _lock.EnterReadLock();
      try { return _currentGeneticReflexID; }
      finally { _lock.ExitReadLock(); }
    }
  }

  /// <summary>
  /// Обновить текущие активные безусловные рефлексы и ID рефлекса-источника
  /// </summary>
  /// <param name="actIdArr">Список ID действий</param>
  /// <param name="geneticReflexId">ID безусловного рефлекса (0 если не один рефлекс или нет)</param>
  internal static void UpdateGlobalGeneticReflexesActions(List<int> actIdArr, int geneticReflexId = 0)
  {
    _lock.EnterWriteLock();
    try
    {
      _currentGeneticReflexID = geneticReflexId;
      _geneticReflexesActions.Clear();

      if (actIdArr != null)
      {
        foreach (var act in actIdArr)
        {
          if (act != 0)
            _geneticReflexesActions.Add(act);
        }
      }
    }
    finally
    {
      _lock.ExitWriteLock();
    }
  }

  /// <summary>
  /// Обновить текущие активные условные рефлексы
  /// </summary>
  internal static void UpdateGlobalConditionedReflexesActions(List<int> actIdArr)
  {
    _conditionReflexesActions.Clear();

    if (actIdArr != null)
    {
      foreach (var acr in actIdArr)
      {
        if (acr != 0)
          _conditionReflexesActions.Add(acr);
      }
    }
  }

  #endregion

  #region Состояние агента - Свойства и методы

  /// <summary>
  /// Флаг изменения контекста условий
  /// </summary>
  public static bool IsNewConditions
  {
    get => _isNewConditions;
    set => _isNewConditions = value;
  }

  /// <summary>
  /// Состояние гомеостаза агента
  /// </summary>
  public enum HomeostasisState
  {
    /// <summary>
    /// Плохо
    /// </summary>
    Bad = -1,
    /// <summary>
    /// Норма
    /// </summary>
    Normal = 0,
    /// <summary>
    /// Хорошо
    /// </summary>
    Well = 1
  }

  /// <summary>
  /// Текущее интегральное состояние агента
  /// </summary>
  public static HomeostasisState CurrentOverallState
  {
    get => _currentOverallState;
    internal set => _currentOverallState = value;
  }

  /// <summary>
  /// ID текущего доминирующего параметра гомеостаза
  /// </summary>
  public static int DominantParam
  {
    get => _dominantParam;
    set => _dominantParam = value;
  }

  /// <summary>
  /// Флаг смерти агента
  /// </summary>
  public static bool IsDead
  {
    get => _isDead;
    set => _isDead = value;
  }

  /// <summary>
  /// Флаг сна агента
  /// </summary>
  public static bool IsSleeping
  {
    get => _isSleeping;
    set => _isSleeping = value;
  }

  /// <summary>
  /// Текущие активные стили поведения агента
  /// </summary>
  public static IReadOnlyList<GomeostasSystem.BehaviorStyle> ActiveStyles
  {
    get => _activeStyles.AsReadOnly();
  }

  /// <summary>
  /// Текущие активные адаптивные действия агента
  /// </summary>
  public static IReadOnlyList<AdaptiveActionsSystem.AdaptiveAction> ActiveAdaptiveActions
  {
    get => _activeAdaptiveActions.AsReadOnly();
  }

  /// <summary>
  /// Внутренний метод для обновления активных стилей
  /// </summary>
  internal static void UpdateActiveStyles(IEnumerable<GomeostasSystem.BehaviorStyle> styles)
  {
    _activeStyles.Clear();

    if (styles != null)
    {
      foreach (var style in styles)
      {
        if (style != null)
          _activeStyles.Add(style);
      }
    }
  }

  /// <summary>
  /// Внутренний метод для обновления активных адаптивных действий
  /// </summary>
  internal static void UpdateActiveAdaptiveActions(IEnumerable<AdaptiveActionsSystem.AdaptiveAction> actions)
  {
    _activeAdaptiveActions.Clear();

    if (actions != null)
    {
      foreach (var action in actions)
      {
        if (action != null)
          _activeAdaptiveActions.Add(action);
      }
    }
  }

  #endregion

  #region Эволюция и время жизни - Свойства и методы

  /// <summary>
  /// Адаптивное действие по умолчанию
  /// </summary>
  public static int DefaultAdaptiveActionId
  {
    get => _defaultAdaptiveActionIdage;
    set => _defaultAdaptiveActionIdage = value;
  }

  /// <summary>
  /// Текущая стадия эволюции агента
  /// </summary>
  public static int EvolutionStage
  {
    get => _evolutionStage;
    set => _evolutionStage = value;
  }

  /// <summary>
  /// Время жизни агента в пульсах
  /// </summary>
  public static int Lifetime
  {
    get => _lifetime;
    set => _lifetime = value;
  }

  #endregion

  #region Пульт — режим наблюдения (свойства)

  /// <summary>
  /// Режим наблюдения: при true воздействия с пульта не меняют параметры гомеостаза агента.
  /// </summary>
  public static bool ObservationMode
  {
    get
    {
      _lock.EnterReadLock();
      try { return _observationMode; }
      finally { _lock.ExitReadLock(); }
    }
    set
    {
      _lock.EnterWriteLock();
      try { _observationMode = value; }
      finally { _lock.ExitWriteLock(); }
    }
  }

  #endregion

  #region Оценка оператора - Свойства и методы

  /// <summary>
  /// Оставшееся время ожидания оценки (в пульсах)
  /// </summary>
  public static int WaitingPeriodCountdown
  {
    get => _waitingPeriodCountdown;
    private set => _waitingPeriodCountdown = value;
  }

  /// <summary>
  /// Флаг ожидания оценки от оператора (true - ждем, false - не ждем)
  /// </summary>
  public static bool WaitingForOperatorEvaluation
  {
    get => _waitingForOperatorEvaluation;
    set => _waitingForOperatorEvaluation = value;
  }

  /// <summary>Автоматизм, по которому ждём ответ оператора (снимок из <see cref="StartWaitingForOperatorEvaluation"/>).</summary>
  public static int AutomatizmIdWaitingForOperatorEvaluation => _automatizmIdWaitingForOperatorEvaluation;

  /// <summary>
  /// Состояние агента перед воздействием оператора (для оценки)
  /// </summary>
  public static HomeostasisState StateBeforeOperatorImpact
  {
    get => _stateBeforeOperatorImpact;
    set => _stateBeforeOperatorImpact = value;
  }

  /// <summary>
  /// Сохраняет снимок параметров для последующей оценки ответа оператора (вызывается при старте отслеживания автоматизма).
  /// </summary>
  public static void SetOperatorEvaluationParameterSnapshot(IReadOnlyDictionary<int, float> valuesByParameterId, int focusParameterId)
  {
    if (valuesByParameterId == null || valuesByParameterId.Count == 0)
    {
      _operatorEvalParameterSnapshotValid = false;
      _operatorEvalParameterValuesBefore = null;
      _operatorEvalFocusParameterId = 0;
      return;
    }

    _operatorEvalParameterValuesBefore = new Dictionary<int, float>(valuesByParameterId.Count);
    foreach (var kv in valuesByParameterId)
      _operatorEvalParameterValuesBefore[kv.Key] = kv.Value;
    _operatorEvalFocusParameterId = focusParameterId;
    _operatorEvalParameterSnapshotValid = true;
  }

  /// <summary>
  /// Снимок значений параметров «до ответа оператора» и фокус оценки; <paramref name="focusParameterId"/> — 0 если не задан.
  /// </summary>
  public static bool TryGetOperatorEvaluationParameterSnapshot(out IReadOnlyDictionary<int, float> valuesByParameterId, out int focusParameterId)
  {
    if (_operatorEvalParameterSnapshotValid && _operatorEvalParameterValuesBefore != null && _operatorEvalParameterValuesBefore.Count > 0)
    {
      valuesByParameterId = _operatorEvalParameterValuesBefore;
      focusParameterId = _operatorEvalFocusParameterId;
      return true;
    }

    valuesByParameterId = null;
    focusParameterId = 0;
    return false;
  }

  /// <summary>
  /// Время (пульс) последней оценки автоматизма оператором
  /// </summary>
  public static int LastAutomatizmEvaluationTime
  {
    get => _lastAutomatizmEvaluationTime;
    set => _lastAutomatizmEvaluationTime = value;
  }

  /// <summary>
  /// ЭТО НАСТРОЙКА, НЕ СБРАСЫВАТЬ! Период ожидания реакции оператора на действия автоматизма в пульсах
  /// </summary>
  public static int WaitingPeriodForActionsVal
  {
    get => _waitingPeriodForActionsVal;
    set => _waitingPeriodForActionsVal = value;
  }

  /// <summary>
  /// Порог тишины (пульсов без стимула с пульта) для события агента «долго без оператора» (резолвер типа темы мышления).
  /// </summary>
  public static int NoOperatorStimulusSilencePulses
  {
    get => _noOperatorStimulusSilencePulses;
    set => _noOperatorStimulusSilencePulses = value < 1 ? 1 : value;
  }

  /// <summary>
  /// Начать период ожидания оценки оператора
  /// </summary>
  public static void StartWaitingForOperatorEvaluation(int automatizmId)
  {
    WaitingForOperatorEvaluation = true;
    _automatizmIdWaitingForOperatorEvaluation = automatizmId > 0 ? automatizmId : 0;
    LastRunAutomatizmPulsCount = GlobalTimer.GlobalPulsCount;
    WaitingPeriodCountdown = GetEffectiveWaitingPeriodForActionsVal();

    Logger.Info($"Начат период ожидания оценки оператора для автоматизма ID={automatizmId}, " +
                $"длительность={WaitingPeriodCountdown} пульсов");
  }

  /// <summary>
  /// Обновить обратный отсчет периода ожидания
  /// </summary>
  public static void UpdateWaitingPeriodCountdown()
  {
    if (WaitingForOperatorEvaluation && WaitingPeriodCountdown > 0)
    {
      WaitingPeriodCountdown--;

      // Если время вышло, сбрасываем ожидание
      if (WaitingPeriodCountdown <= 0)
      {
        ResetWaitingForOperatorEvaluation();
        Logger.Info("Период ожидания оценки оператора истек");
      }
    }
  }

  /// <summary>
  /// Принудительно завершить период ожидания оценки оператора
  /// </summary>
  public static void ForceStopWaitingForOperatorEvaluation()
  {
    ResetWaitingForOperatorEvaluation();
  }

  /// <summary>
  /// Очищает индикаторы цепочек и автоматизмов при остановке пульсации.
  /// (Стили и параметры сбрасываются в GomeostasSystem.ClearPulseRuntimeIndicators.)
  /// </summary>
  public static void ClearPulseIndicators()
  {
    IsAutomatizmChainActive = false;
    IsReflexChainActive = false;
    ResetAutomatizmInfo();
  }

  /// <summary>
  /// Сбросить состояние ожидания оценки оператора
  /// </summary>
  public static void ResetWaitingForOperatorEvaluation()
  {
    WaitingForOperatorEvaluation = false;
    WaitingPeriodCountdown = 0;
    LastRunAutomatizmPulsCount = 0;
    _automatizmIdWaitingForOperatorEvaluation = 0;
  }

  /// <summary>
  /// Проверить, является ли текущий момент временем оценки предыдущего автоматизма
  /// </summary>
  public static bool IsEvaluationTime()
  {
    if (!_waitingForOperatorEvaluation || LastRunAutomatizmPulsCount <= 0)
      return false;

    int period = GetEffectiveWaitingPeriodForActionsVal();
    int currentPulse = GlobalTimer.GlobalPulsCount;
    int timeSinceAutomatizm = currentPulse - LastRunAutomatizmPulsCount;

    // Только если мы активно ждем и время в пределах ожидания
    return timeSinceAutomatizm <= period &&
           timeSinceAutomatizm > 0;
  }

  /// <summary>
  /// Окно для ответа оператора с пульта при активном ожидании оценки: те же границы пульсов, что и у
  /// <see cref="IsEvaluationTime"/>, но допускается <c>timeSince == 0</c> (второй стимул в том же глобальном
  /// пульсе после эхо — сценарий до <see cref="ISIDA.Psychic.PsychicSystem.ProcessPsychicPulse"/>).
  /// </summary>
  public static bool IsOperatorResponseWithinWaitingWindow()
  {
    if (!_waitingForOperatorEvaluation || LastRunAutomatizmPulsCount <= 0)
      return false;

    int period = GetEffectiveWaitingPeriodForActionsVal();
    int timeSince = GlobalTimer.GlobalPulsCount - LastRunAutomatizmPulsCount;
    return timeSince >= 0 && timeSince <= period;
  }

  #endregion

  #region Вербальные образы - Свойства и методы

  /// <summary>
  /// ID текущего активного вербального образа 
  /// </summary>
  public static int CurActiveVerbalId
  {
    get => _curActiveVerbalId;
    set => _curActiveVerbalId = value;
  }

  #endregion

  #region Контекст стимула с пульта (для ОР1 / эхо на 2-й стадии)

  private static int _currentStimulusActionsImageId = 0;
  private static List<int> _currentStimulusActionIdList = new List<int>();
  private static int _currentStimulusToneId = 0;
  private static int _currentStimulusMoodId = 0;

  /// <summary>ID образа действий текущего стимула с пульта (устанавливается перед вызовом ОР1)</summary>
  public static int CurrentStimulusActionsImageId { get => _currentStimulusActionsImageId; set => _currentStimulusActionsImageId = value; }

  /// <summary>Список ID действий текущего стимула с пульта</summary>
  public static List<int> CurrentStimulusActionIdList { get => _currentStimulusActionIdList; set => _currentStimulusActionIdList = value ?? new List<int>(); }

  /// <summary>Тон текущего стимула с пульта</summary>
  public static int CurrentStimulusToneId { get => _currentStimulusToneId; set => _currentStimulusToneId = value; }

  /// <summary>Настроение текущего стимула с пульта</summary>
  public static int CurrentStimulusMoodId { get => _currentStimulusMoodId; set => _currentStimulusMoodId = value; }

  #endregion

  #region Буферы стимулов для темы мышления (пульс)

  private static int _stimulusAgentEventPulse;
  private static int _stimulusAgentEventCode;
  private static int _stimulusInfluencePulse;
  private static readonly List<int> _stimulusInfluenceActionIds = new List<int>();
  private static int _resolvedThinkingThemeTypeId;

  /// <summary>Пульс последнего стимула с пульта (NotifyStimulus / SensorActivation). Для события «долго без оператора».</summary>
  private static int _lastPultStimulusGlobalPulse;

  /// <summary>Тип темы, выбранный в начале текущего пульса (по стимулам предыдущего пульса).</summary>
  public static int ResolvedThinkingThemeTypeId
  {
    get { _lock.EnterReadLock(); try { return _resolvedThinkingThemeTypeId; } finally { _lock.ExitReadLock(); } }
    set { _lock.EnterWriteLock(); try { _resolvedThinkingThemeTypeId = value; } finally { _lock.ExitWriteLock(); } }
  }

  /// <summary>Зафиксировать код события агента на текущем глобальном пульсе (последнее значение перезаписывает предыдущее).</summary>
  public static void RecordStimulusAgentEvent(int eventCode)
  {
    if (eventCode <= 0) return;
    _lock.EnterWriteLock();
    try
    {
      _stimulusAgentEventPulse = GlobalTimer.GlobalPulsCount;
      _stimulusAgentEventCode = eventCode;
    }
    finally { _lock.ExitWriteLock(); }
  }

  /// <summary>Записать событие агента, если на этом пульсе ещё не зафиксировано другое (не перезаписывает).</summary>
  public static bool TryRecordStimulusAgentEvent(int eventCode)
  {
    if (eventCode <= 0) return false;
    _lock.EnterWriteLock();
    try
    {
      int p = GlobalTimer.GlobalPulsCount;
      if (_stimulusAgentEventPulse == p && _stimulusAgentEventCode > 0)
        return false;
      _stimulusAgentEventPulse = p;
      _stimulusAgentEventCode = eventCode;
      return true;
    }
    finally { _lock.ExitWriteLock(); }
  }

  /// <summary>Глобальный пульс последнего стимула с пульта (0 — ещё не было).</summary>
  public static int LastPultStimulusGlobalPulse
  {
    get { _lock.EnterReadLock(); try { return _lastPultStimulusGlobalPulse; } finally { _lock.ExitReadLock(); } }
  }

  /// <summary>Обновить отметку последнего стимула с пульта (вызывать при реальном воздействии оператора).</summary>
  public static void UpdateLastPultStimulusPulse(int pulse)
  {
    if (pulse <= 0) return;
    _lock.EnterWriteLock();
    try { _lastPultStimulusGlobalPulse = pulse; }
    finally { _lock.ExitWriteLock(); }
  }

  /// <summary>Зафиксировать воздействия с пульта на текущем пульсе (список накапливается без дубликатов).</summary>
  public static void RecordStimulusInfluenceActions(IEnumerable<int> actionIds)
  {
    if (actionIds == null) return;
    int pulse = GlobalTimer.GlobalPulsCount;
    _lock.EnterWriteLock();
    try
    {
      if (_stimulusInfluencePulse != pulse)
      {
        _stimulusInfluencePulse = pulse;
        _stimulusInfluenceActionIds.Clear();
      }
      foreach (int id in actionIds)
      {
        if (id > 0 && !_stimulusInfluenceActionIds.Contains(id))
          _stimulusInfluenceActionIds.Add(id);
      }
    }
    finally { _lock.ExitWriteLock(); }
  }

  /// <summary>Снимок буферов стимулов для резолва темы в начале пульса.</summary>
  public static void TakeStimulusSnapshotForThemeResolution(
      out int eventPulse,
      out int eventCode,
      out int influencePulse,
      out IReadOnlyList<int> influenceIds)
  {
    _lock.EnterReadLock();
    try
    {
      eventPulse = _stimulusAgentEventPulse;
      eventCode = _stimulusAgentEventCode;
      influencePulse = _stimulusInfluencePulse;
      influenceIds = _stimulusInfluenceActionIds.Count > 0
        ? (IReadOnlyList<int>)_stimulusInfluenceActionIds.ToArray()
        : Array.Empty<int>();
    }
    finally { _lock.ExitReadLock(); }
  }

  /// <summary>Сброс буферов события/воздействий после выбора темы в начале пульса.</summary>
  public static void ClearStimulusBuffersAfterThemeResolution()
  {
    _lock.EnterWriteLock();
    try
    {
      _stimulusAgentEventPulse = 0;
      _stimulusAgentEventCode = 0;
      _stimulusInfluencePulse = 0;
      _stimulusInfluenceActionIds.Clear();
    }
    finally { _lock.ExitWriteLock(); }
  }

  #endregion
}