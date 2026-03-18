using System;
using System.Collections.Generic;

namespace ISIDA.Psychic.Thinking
{
  /// <summary>
  /// Состояние одного цикла мышления (3-й уровень): идентификатор, порядок, флаги и контекст нерешённой проблемы.
  /// </summary>
  public sealed class ThinkingCycleInfo
  {
    /// <summary>Уникальный идентификатор цикла.</summary>
    public int Id { get; set; }

    /// <summary>Порядковый номер цикла (для приоритета при диспетчеризации).</summary>
    public int Order { get; set; }

    /// <summary>Является ли цикл главным (единственный, обновляющий инфо-картину).</summary>
    public bool IsMainCycle { get; set; }

    /// <summary>Пульс, на котором цикл был создан (или сброшен под новый стимул).</summary>
    public int CreatedPulse { get; set; }

    /// <summary>Число шагов, выполненных циклом.</summary>
    public int StepCount { get; set; }

    /// <summary>Если true, цикл крутится вхолостую и должен вызываться реже.</summary>
    public bool IsIdle { get; set; }

    /// <summary>Период ожидания оценки оператора (для главного цикла).</summary>
    public bool IsWaitingPeriod { get; set; }

    /// <summary>Пассивный режим размышления (dreaming / мечтания / сновидения).</summary>
    public bool Dreaming { get; set; }

    /// <summary>Вес цикла (значимость) для конкуренции параллельных циклов.</summary>
    public int Weight { get; set; }

    /// <summary>Контекст нерешённой проблемы на 2 уровне (узел дерева автоматизмов).</summary>
    public int UnresolvedNodeId { get; set; }

    /// <summary>Контекст нерешённой проблемы на 2 уровне (стимул в терминах ActionsImage).</summary>
    public int UnresolvedActionsImageId { get; set; }

    /// <summary>ID узла дерева проблем (ProblemTreeNode) на момент шага.</summary>
    public int ProblemNodeId { get; set; }

    /// <summary>ID активной темы (ThemeImage) на момент создания/обновления.</summary>
    public int ThemeId { get; set; }

    /// <summary>ID активной цели (PurposeImage) на момент создания/обновления.</summary>
    public int PurposeId { get; set; }

    /// <summary>Последняя запущенная стратегия, чтобы избежать дребезга.</summary>
    public string LastStrategyId { get; set; }

    /// <summary>Лог шагов (для диагностики/пульта).</summary>
    public List<string> Log { get; } = new List<string>();

    /// <summary>Время последнего обновления цикла (UTC).</summary>
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
  }
}

