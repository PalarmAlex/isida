namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Определение текущей ситуации (SituationImage / GetCurSituationImageID).
  /// Используется при активации дерева Understanding.
  /// </summary>
  /// <remarks>
  /// TODO ⚠️ BOT-INTEGRATION: Расширить логику по BOT understanding_situation_image.go.
  /// Сейчас: минимальная реализация (тип 4 — Experiment). Полная: Usefulness, LastRunAutomatizmPulsCount,
  /// WaitingPeriodForActionsVal, curActiveActions (mood, кнопки). 11–17 — настроение; 21–37 — кнопки.
  /// </remarks>
  public static class SituationImageService
  {
    /// <summary>
    /// Получить ID текущей ситуации для активации дерева Understanding.
    /// </summary>
    /// <param name="automatizmTreeNodeId">ID активного узла дерева автоматизмов (0 — нет)</param>
    /// <returns>SituationImage.Id или 0</returns>
    public static int GetCurSituationImageId(int automatizmTreeNodeId)
    {
      if (!SituationImageSystem.IsInitialized)
        return 0;
      return SituationImageSystem.Instance.GetCurSituationImageId(automatizmTreeNodeId);
    }
  }
}
