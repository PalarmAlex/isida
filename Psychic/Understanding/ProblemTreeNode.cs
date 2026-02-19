using System.Collections.Generic;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Узел дерева проблем
  /// </summary>
  /// <remarks>
  /// Имеет 4 уровня: AutTreeID, SituationTreeID, ThemeID, PurposeID.
  /// Упрощённая версия: пока только AutTreeID (связь с деревом автоматизмов),
  /// остальные поля — для будущей ментальной памяти.
  /// </remarks>
  public class ProblemTreeNode
  {
    /// <summary>ID узла</summary>
    public int ID { get; set; }

    /// <summary>ID узла дерева автоматизмов</summary>
    public int AutTreeID { get; set; }

    /// <summary>ID узла дерева ситуации (0 пока нет)</summary>
    public int SituationTreeID { get; set; }

    /// <summary>ID темы (0 пока нет)</summary>
    public int ThemeID { get; set; }

    /// <summary>ID цели (0 пока нет)</summary>
    public int PurposeID { get; set; }

    /// <summary>Дочерние узлы</summary>
    public List<ProblemTreeNode> Children { get; set; } = new List<ProblemTreeNode>();

    /// <summary>ID родителя</summary>
    public int ParentID { get; set; }

    /// <summary>Ссылка на родителя</summary>
    public ProblemTreeNode ParentNode { get; set; }
  }
}
