using System;

/// <summary>
/// Глобальные переменные
/// </summary>
public static class AppGlobalState
{
  private static int _evolutionStage = 0;

  /// <summary>
  /// Текущая стадия эволюции агента
  /// </summary>
  public static int EvolutionStage
  {
    get => _evolutionStage;
    set => _evolutionStage = value;
  }
}
