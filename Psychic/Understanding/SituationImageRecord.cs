namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Образ текущей ситуации для дерева Understanding.
  /// Сочетание узла дерева автоматизмов и типа ситуации.
  /// </summary>
  public class SituationImageRecord
  {
    /// <summary>Уникальный ID образа ситуации</summary>
    public int Id { get; set; }

    /// <summary>ID узла дерева моторных автоматизмов (конечный узел активной ветки)</summary>
    public int AutomatizmTreeNodeId { get; set; }

    /// <summary>ID типа ситуации из справочника SituationType</summary>
    public int SituationTypeId { get; set; }
  }
}
