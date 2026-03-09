using System.Collections.Generic;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Узел дерева понимания ситуации (Understanding tree).
  /// 3 уровня: Mood, EmotionID, SituationID.
  /// </summary>
  public class UnderstandingTreeNode
  {
    /// <summary>Уникальный ID узла</summary>
    public int Id { get; set; }

    /// <summary>Настроение: -1 Плохо, 0 Норма, 1 Хорошо</summary>
    public int Mood { get; set; }

    /// <summary>ID образа эмоций (EmotionsImage.Id)</summary>
    public int EmotionId { get; set; }

    /// <summary>ID образа ситуации (SituationImage.Id)</summary>
    public int SituationId { get; set; }

    /// <summary>Дочерние узлы</summary>
    public List<UnderstandingTreeNode> Children { get; set; } = new List<UnderstandingTreeNode>();

    /// <summary>ID родителя</summary>
    public int ParentId { get; set; }

    /// <summary>Ссылка на родителя</summary>
    public UnderstandingTreeNode ParentNode { get; set; }
  }
}
