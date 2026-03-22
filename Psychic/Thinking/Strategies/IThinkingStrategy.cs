using ISIDA.Psychic.Automatism;

namespace ISIDA.Psychic.Thinking.Strategies
{
  /// <summary>Результат одного шага стратегии 3-го уровня: что выполнить или запросить.</summary>
  public sealed class ThinkingDecision
  {
    /// <summary>Готовый автоматизм к выполнению (если найден).</summary>
    public Automatizm AutomatizmToExecute { get; set; }

    /// <summary>Идентификатор образа действий для создания/привязки автоматизма.</summary>
    public int ActionsImageIdToAutomatize { get; set; }

    /// <summary>Запросить подсказку у оператора (попугайство).</summary>
    public bool RequestParrotFromOperator { get; set; }

    /// <summary>Отладочная заметка о решении.</summary>
    public string DebugNote { get; set; }

    /// <summary>Если true — после шага цикл удаляется из диспетчера (решение найдено).</summary>
    public bool CloseCycleImmediately { get; set; }

    /// <summary>Создаёт решение «ничего не делать» с опциональной заметкой.</summary>
    /// <param name="note">Отладочная заметка (может быть null).</param>
    /// <returns>Решение без действия.</returns>
    public static ThinkingDecision None(string note = null) => new ThinkingDecision { DebugNote = note };
  }

  /// <summary>Стратегия одного шага цикла мышления (3-й уровень).</summary>
  public interface IThinkingStrategy
  {
    /// <summary>Уникальный идентификатор стратегии.</summary>
    string Id { get; }

    /// <summary>
    /// Выполнить один шаг стратегии. Стратегия НЕ должна напрямую выполнять автоматизм.
    /// Она возвращает решение для цикла (какой автоматизм выполнить/создать или что запросить).
    /// </summary>
    ThinkingDecision TryStep(ThinkingStrategyContext ctx);
  }
}

