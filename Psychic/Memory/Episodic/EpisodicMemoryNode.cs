using System.Collections.Generic;

namespace ISIDA.Psychic.Memory.Episodic
{
  /// <summary>
  /// Параметры узла эпизодической памяти (Effect, Count, StimulsEffect)
  /// </summary>
  public class EpisodicParams
  {
    /// <summary>Эффект правила (-10..10); для учительского узла всегда 0</summary>
    public int Effect { get; set; }
    /// <summary>Количество применений (для усреднения)</summary>
    public int Count { get; set; }
    /// <summary>Для прямого правила — значимость стимула; для учительского — подписанная оценка с пульта (-10..10)</summary>
    public int StimulsEffect { get; set; }
    /// <summary>Учительское правило (оценка в StimulsEffect, Effect = 0)</summary>
    public bool IsTeacher { get; set; }
  }

  /// <summary>
  /// Запись истории эпизодов (ID узла, время появления)
  /// </summary>
  public class EpisodicHistoryEntry
  {
    /// <summary>ID узла эпизода (-1 — пустой кадр)</summary>
    public int NodeId { get; set; }
    /// <summary>Пульс времени жизни при записи</summary>
    public int LifeTime { get; set; }
  }

  /// <summary>
  /// Правило для поиска (Trigger, Action, Effect, Count, Importence)
  /// </summary>
  public class EpisodicRule
  {
    /// <summary>ID стимула (триггера)</summary>
    public int TriggerId { get; set; }
    /// <summary>ID действия</summary>
    public int ActionId { get; set; }
    /// <summary>Эффект правила</summary>
    public int Effect { get; set; }
    /// <summary>Количество применений</summary>
    public int Count { get; set; }
    /// <summary>Важность</summary>
    public int Importence { get; set; }
    /// <summary>Учительское правило (валентность в Importence как у StimulsEffect в узле)</summary>
    public bool IsTeacher { get; set; }
  }

  /// <summary>
  /// Узел дерева эпизодической памяти
  /// </summary>
  public class EpisodicMemoryNode
  {
    /// <summary>Уникальный ID узла</summary>
    public int ID { get; set; }
    /// <summary>Базовое состояние (-1/0/1)</summary>
    public int BaseID { get; set; }
    /// <summary>ID эмоции</summary>
    public int EmotionID { get; set; }
    /// <summary>ID узла дерева проблем</summary>
    public int NodePID { get; set; }
    /// <summary>ID стимула</summary>
    public int TriggerId { get; set; }
    /// <summary>ID действия</summary>
    public int ActionId { get; set; }
    /// <summary>Параметры эффекта (для листовых узлов)</summary>
    public EpisodicParams Params { get; set; }

    /// <summary>Дочерние узлы</summary>
    public List<EpisodicMemoryNode> Children { get; set; } = new List<EpisodicMemoryNode>();
    /// <summary>ID родительского узла</summary>
    public int ParentID { get; set; }
    /// <summary>Ссылка на родительский узел</summary>
    public EpisodicMemoryNode ParentNode { get; set; }
  }
}
