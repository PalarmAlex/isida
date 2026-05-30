using ISIDA.Common;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Стартовая полезность автоматизмов при создании (эхо vs сдвиг/обычный).
  /// Изменение полезности по оценке оператора — линейное ±1 в <see cref="AutomatismResultTracker"/>.
  /// </summary>
  public static class AutomatizmConsolidationService
  {
    /// <summary>
    /// Роль при создании автоматизма (стартовая полезность).
    /// Belief=2 (штатный) задаётся отдельно через <see cref="AutomatizmSystem.SetAutomatizmBelief"/>.
    /// </summary>
    public enum AutomatizmCreationRole
    {
      /// <summary>Обычное создание: стадия 2 → 3, стадия 3 → 2.</summary>
      Default = 0,
      /// <summary>Зеркальный «сдвиг» Sₙ₋₁→Sₙ: стадия 2 → 3, стадия 3 → 2.</summary>
      Shift = 1,
      /// <summary>Зеркальное эхо S→S: полезность 0 на всех стадиях.</summary>
      Echo = 2
    }

    /// <summary>Начальная полезность по роли и стадии.</summary>
    public static int GetInitialUsefulness(AutomatizmCreationRole role, int evolutionStage)
    {
      if (role == AutomatizmCreationRole.Echo)
        return 0;
      if (evolutionStage == 2)
        return 3;
      if (evolutionStage == 3)
        return 2;
      return 0;
    }
  }
}
