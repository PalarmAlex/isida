using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;

/// <summary>
/// Глобальные переменные
/// </summary>
public static class AppGlobalState
{
  private static int _evolutionStage = 0;
  private static int _lifetime = 0;
  private static bool _isDead = false;
  private static bool _isSleeping = false;
  private static List<GomeostasSystem.BehaviorStyle> _activeStyles = new List<GomeostasSystem.BehaviorStyle>();

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
  /// Текущие активные стили поведения агента
  /// </summary>
  public static IReadOnlyList<GomeostasSystem.BehaviorStyle> ActiveStyles
  {
    get => _activeStyles.AsReadOnly();
  }

  /// <summary>
  /// Внутренний метод для обновления активных стилей (используется только GomeostasSystem)
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

}
