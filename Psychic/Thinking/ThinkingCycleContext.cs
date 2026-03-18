namespace ISIDA.Psychic.Thinking
{
  /// <summary>
  /// Снимок контекста, из которого стартует цикл мышления.
  /// </summary>
  public sealed class ThinkingCycleContext
  {
    /// <summary>Номер пульса на момент создания контекста.</summary>
    public int PulseCount { get; set; }

    /// <summary>Базовый идентификатор цикла (для связи с родительским циклом).</summary>
    public int BaseId { get; set; }

    /// <summary>Идентификатор текущей эмоции.</summary>
    public int EmotionId { get; set; }

    /// <summary>Узел дерева автоматизмов, связанный со стимулом.</summary>
    public int AutomatizmNodeId { get; set; }

    /// <summary>Идентификатор образа действий стимула (ActionsImage).</summary>
    public int StimulusActionsImageId { get; set; }

    /// <summary>Идентификатор узла дерева проблем (ProblemTreeNode).</summary>
    public int ProblemNodeId { get; set; }

    /// <summary>Идентификатор активной темы (ThemeImage).</summary>
    public int ThemeId { get; set; }

    /// <summary>Идентификатор активной цели (PurposeImage).</summary>
    public int PurposeId { get; set; }

    /// <summary>Признак опасной ситуации.</summary>
    public bool Danger { get; set; }

    /// <summary>Признак очень актуальной ситуации.</summary>
    public bool VeryActualSituation { get; set; }

    /// <summary>Период ожидания оценки оператора.</summary>
    public bool IsWaitingPeriod { get; set; }
  }
}

