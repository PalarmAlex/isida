using ISIDA.Actions;
using ISIDA.Gomeostas;
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
  private static bool _isDead = false;
  private static bool _isSleeping = false;
  private static bool _flgConditionReflexes = false;
  private static List<GomeostasSystem.BehaviorStyle> _activeStyles = new List<GomeostasSystem.BehaviorStyle>();
  private static List<AdaptiveActionsSystem.AdaptiveAction> _activeAdaptiveActions = new List<AdaptiveActionsSystem.AdaptiveAction>();


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
