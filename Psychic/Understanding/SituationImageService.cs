using ISIDA.Common;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Контекст для выбора типа ситуации.
  /// Передаётся при вызове GetCurSituationImageId, если доступны данные о наличии автоматизма в ветке и о действиях с пульта.
  /// </summary>
  public sealed class SituationImageContext
  {
    /// <summary>Есть ли в текущей ветке дерева автоматизм (тип 2 — AutomatizmRun)</summary>
    public bool HasAutomatismInBranch { get; set; }

    /// <summary>ID настроения с пульта (11–17). 0 — не задано.</summary>
    public int MoodId { get; set; }

    /// <summary>ID действий/кнопок с пульта (21–37). null — не задано.</summary>
    public int[] ActionIds { get; set; }
  }

  /// <summary>
  /// Определение текущей ситуации (SituationImage / GetCurSituationImageID).
  /// Используется при активации дерева Understanding.
  /// </summary>
  /// <remarks>
  /// Типы 1–5: ResponseAction, AutomatizmRun, NeedThinking, Experiment, OperatorIgnore.
  /// Типы 11–17 — настроение с пульта, 21–37 — кнопки (при наличии контекста с MoodId/ActionIds и соответствующих типов в справочнике).
  /// </remarks>
  public static class SituationImageService
  {
    /// <summary>
    /// Получить ID текущей ситуации для активации дерева Understanding (без контекста — используется только тип 4 при nodeId&gt;0, тип 3 при nodeId==0).
    /// </summary>
    /// <param name="situationImageSystem">Экземпляр системы образов ситуаций (передаётся вызывающим кодом)</param>
    /// <param name="automatizmTreeNodeId">ID активного узла дерева автоматизмов (0 — нет)</param>
    /// <returns>SituationImage.Id или 0</returns>
    public static int GetCurSituationImageId(SituationImageSystem situationImageSystem, int automatizmTreeNodeId)
    {
      return GetCurSituationImageId(situationImageSystem, automatizmTreeNodeId, null);
    }

    /// <summary>
    /// Получить ID текущей ситуации с учётом контекста (ожидание ответа, наличие автоматизма в ветке, настроение/кнопки с пульта).
    /// Порядок приоритетов: 3 при nodeId==0; затем 5 (истекло ожидание), 1 (ожидание ответа), 2 (есть автоматизм), по приоритету mood/actions, иначе 4.
    /// </summary>
    /// <param name="situationImageSystem">Экземпляр системы образов ситуаций (передаётся вызывающим кодом)</param>
    /// <param name="automatizmTreeNodeId">ID активного узла дерева автоматизмов</param>
    /// <param name="context">Контекст (hasAutomatismInBranch, moodId, actionIds) или null</param>
    /// <returns>SituationImage.Id или 0</returns>
    public static int GetCurSituationImageId(SituationImageSystem situationImageSystem, int automatizmTreeNodeId, SituationImageContext context)
    {
      if (situationImageSystem == null)
        return 0;
      return situationImageSystem.GetCurSituationImageId(automatizmTreeNodeId, context);
    }
  }
}
