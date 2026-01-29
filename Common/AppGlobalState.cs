using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;

/// <summary>
/// Глобальные переменные
/// </summary>
public static class AppGlobalState
{
  private static int _currentActiveAutomatizmId = 0;
  private static int _lastAutomatizmPulse = 0;
  private static int _lastOrientationReflexType = 0;
  private static int _lastOrientationReflexPulse = 0;
  private static int _defaultAdaptiveActionIdage = 0;
  private static int _evolutionStage = 0;
  private static int _lifetime = 0;
  private static int _dominantParam = 0;
  private static int automatizmNodeId = 0;
  private static int _curActiveVerbalId = 0;
  private static int _currentFindAtmzStepCount = 0;
  private static int _lastRunAutomatizmPulsCount = 0;
  private static int _waitingPeriodForActionsVal = 0;
  private static HomeostasisState _currentOverallState = HomeostasisState.Normal;
  private static bool _isDead = false;
  private static bool _isSleeping = false;
  private static bool _flgConditionReflexes = false;
  private static bool _waitingForOperatorEvaluation = false;
  private static int _lastEvaluatedAutomatizmId = 0;
  private static int _lastAutomatizmEvaluationTime = 0;
  private static List<int> _geneticReflexesActions = new List<int>();
  private static List<int> _conditionReflexesActions = new List<int>();
  private static List<GomeostasSystem.BehaviorStyle> _activeStyles = new List<GomeostasSystem.BehaviorStyle>();
  private static List<AdaptiveActionsSystem.AdaptiveAction> _activeAdaptiveActions = new List<AdaptiveActionsSystem.AdaptiveAction>();
  private static HomeostasisState _stateBeforeOperatorImpact = HomeostasisState.Normal;

  /// <summary>
  /// ID текущего активного автоматизма
  /// </summary>
  public static int CurrentActiveAutomatizmId
  {
    get => _currentActiveAutomatizmId;
    set => _currentActiveAutomatizmId = value;
  }

  /// <summary>
  /// Пульс, на котором был активирован последний автоматизм
  /// </summary>
  public static int LastAutomatizmPulse
  {
    get => _lastAutomatizmPulse;
    set => _lastAutomatizmPulse = value;
  }

  /// <summary>
  /// Обновить информацию об активации автоматизма
  /// </summary>
  public static void UpdateAutomatizmInfo(int automatizmId, int pulse)
  {
    CurrentActiveAutomatizmId = automatizmId;
    LastAutomatizmPulse = pulse;

    // Записываем в лог для отладки
    Logger.Info($"Автоматизм активирован: ID={automatizmId} на пульсе {pulse}");
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
  /// Флаг ожидания оценки от оператора (true - ждем, false - не ждем)
  /// </summary>
  public static bool WaitingForOperatorEvaluation
  {
    get => _waitingForOperatorEvaluation;
    set => _waitingForOperatorEvaluation = value;
  }

  /// <summary>
  /// ID последнего автоматизма, который ожидает оценки оператора
  /// </summary>
  public static int LastEvaluatedAutomatizmId
  {
    get => _lastEvaluatedAutomatizmId;
    set => _lastEvaluatedAutomatizmId = value;
  }

  /// <summary>
  /// Состояние агента перед воздействием оператора (для оценки)
  /// </summary>
  public static HomeostasisState StateBeforeOperatorImpact
  {
    get => _stateBeforeOperatorImpact;
    set => _stateBeforeOperatorImpact = value;
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
  /// Сохранить состояние перед воздействием оператора для оценки
  /// </summary>
  public static void SaveStateForEvaluation(HomeostasisState currentState)
  {
    StateBeforeOperatorImpact = currentState;
    Logger.Info($"Сохранено состояние для оценки: {currentState}");
  }

  /// <summary>
  /// Проверить, является ли текущий момент временем оценки предыдущего автоматизма
  /// </summary>
  public static bool IsEvaluationTime()
  {
    if (!_waitingForOperatorEvaluation ||
        LastRunAutomatizmPulsCount <= 0 ||
        WaitingPeriodForActionsVal <= 0)
      return false;

    int currentPulse = GlobalTimer.GlobalPulsCount;
    int timeSinceAutomatizm = currentPulse - LastRunAutomatizmPulsCount;

    // Только если мы активно ждем И время в пределах ожидания
    return timeSinceAutomatizm <= WaitingPeriodForActionsVal &&
           timeSinceAutomatizm > 0;
  }

  /// <summary>
  /// Пульс, на котором был запущен текущий автоматизм
  /// </summary>
  public static int LastRunAutomatizmPulsCount
  {
    get => _lastRunAutomatizmPulsCount;
    set => _lastRunAutomatizmPulsCount = value;
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
  /// Текущий шаг при поиске автоматизма в узлах ветки дерева автоматизмов
  /// </summary>
  public static int CurrentFindAtmzStepCount
  {
    get => _currentFindAtmzStepCount;
    set => _currentFindAtmzStepCount = value;
  }

  /// <summary>
  /// ID текущего активного вербального образа 
  /// </summary>
  public static int CurActiveVerbalId
  {
    get => _curActiveVerbalId;
    set => _curActiveVerbalId = value;
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
  /// Последний распознанный узел дерева автоматизмов
  /// </summary>
  public static int AutomatizmNodeId
  {
    get => automatizmNodeId;
    set => automatizmNodeId = value;
  }

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
  /// ID текущего доминирующего параметра гомеостаза
  /// </summary>
  public static int DominantParam
  {
    get => _dominantParam;
    set => _dominantParam = value;
  }

  /// <summary>
  /// Текущие активные безусловные рефлексы
  /// </summary>
  public static IReadOnlyList<int> GeneticReflexesActions
  {
    get => _geneticReflexesActions;
  }

  /// <summary>
  /// Обновить текущие активные безусловные рефлексы
  /// </summary>
  internal static void UpdateGlobalGeneticReflexesActions(List<int> actIdArr)
  {
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

  /// <summary>
  /// Текущие активные условные рефлексы
  /// </summary>
  public static IReadOnlyList<int> ConditionedReflexesActions
  {
    get => _conditionReflexesActions;
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

  /// <summary>
  /// Текущие активные стили поведения агента
  /// </summary>
  public static IReadOnlyList<GomeostasSystem.BehaviorStyle> ActiveStyles
  {
    get => _activeStyles.AsReadOnly();
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
  /// Текущие активные адаптивные действия агента
  /// </summary>
  public static IReadOnlyList<AdaptiveActionsSystem.AdaptiveAction> ActiveAdaptiveActions
  {
    get => _activeAdaptiveActions.AsReadOnly();
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
}
