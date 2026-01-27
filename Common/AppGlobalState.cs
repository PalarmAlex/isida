using ISIDA.Actions;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;

/// <summary>
/// Глобальные переменные
/// </summary>
public static class AppGlobalState
{
  private static int _defaultAdaptiveActionIdage = 0;
  private static int _evolutionStage = 0;
  private static int _lifetime = 0;
  private static int _dominantParam = 0;
  private static int automatizmNodeId = 0;
  private static int _curActiveVerbalId = 0;
  private static int _currentFindAtmzStepCount = 0;
  private static HomeostasisState _currentOverallState = HomeostasisState.Normal;
  private static bool _isDead = false;
  private static bool _isSleeping = false;
  private static bool _flgConditionReflexes = false;
  private static List<int> _geneticReflexesActions = new List<int>();
  private static List<int> _conditionReflexesActions = new List<int>();
  private static List<GomeostasSystem.BehaviorStyle> _activeStyles = new List<GomeostasSystem.BehaviorStyle>();
  private static List<AdaptiveActionsSystem.AdaptiveAction> _activeAdaptiveActions = new List<AdaptiveActionsSystem.AdaptiveAction>();

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
