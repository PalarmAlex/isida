namespace ISIDA.Psychic.Thinking
{
  /// <summary>
  /// Краткая запись о цикле мышления для UI (без лога).
  /// </summary>
  public sealed class ThinkingCycleListItem
  {
    /// <summary>Уникальный идентификатор цикла.</summary>
    public int Id { get; set; }

    /// <summary>Порядковый номер цикла.</summary>
    public int Order { get; set; }

    /// <summary>Вес (значимость).</summary>
    public int Weight { get; set; }

    /// <summary>Главный цикл.</summary>
    public bool IsMainCycle { get; set; }

    /// <summary>Пассивный режим.</summary>
    public bool IsIdle { get; set; }

    /// <summary>Режим мечтания.</summary>
    public bool Dreaming { get; set; }

    /// <summary>Ожидается оценка решения.</summary>
    public bool AwaitingEvaluation { get; set; }

    /// <summary>ID автоматизма, привязанного к ожиданию оценки.</summary>
    public int PendingSolutionAutomatizmId { get; set; }

    /// <summary>Число шагов.</summary>
    public int StepCount { get; set; }

    /// <summary>Тёмно-зелёная обводка «ожидание оценки» (приоритет над красной).</summary>
    public bool ShowAwaitingEvaluationBorder { get; set; }

    /// <summary>Красная обводка «решение ещё не найдено».</summary>
    public bool ShowNoSolutionBorder { get; set; }

    /// <summary>Образ темы (для подсказки в логе).</summary>
    public int ThemeId { get; set; }

    /// <summary>Образ цели.</summary>
    public int PurposeId { get; set; }

    /// <summary>Узел дерева проблем.</summary>
    public int ProblemNodeId { get; set; }

    /// <summary>Последняя стратегия (инфо-функция).</summary>
    public string LastStrategyId { get; set; }
  }
}
