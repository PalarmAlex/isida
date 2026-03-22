using System;
using System.Linq;

namespace ISIDA.Psychic.Thinking.Strategies
{
  /// <summary>
  /// Рискованная проба: выбрать случайный допустимый автоматизм в текущей ветке (кроме очевидно плохих).
  /// </summary>
  public sealed class RandomBranchAutomatizmStrategy : IThinkingStrategy
  {
    private readonly Random _rng = new Random();

    /// <summary>
    /// Инфо-функция: случайная проба моторного автоматизма из текущей ветки проблемы.
    /// Активация: стратегия вызывается диспетчером циклов, но execution ограничивается твоим allowed-list (SituationTypeSystem).
    /// </summary>
    public string Id => "infoFunc_30";

    /// <summary>
    /// Один шаг инфо-функции: выбирает случайный кандидат-автоматизм из списка моторов в UnresolvedNodeId
    /// и возвращает его как решение для выполнения/создания на уровне движка.
    /// </summary>
    /// <param name="ctx">Контекст текущего шага.</param>
    /// <returns>ThinkingDecision с выбранным AutomatizmToExecute или None.</returns>
    public ThinkingDecision TryStep(ThinkingStrategyContext ctx)
    {
      if (ctx?.Cycle == null || ctx.AutomatizmSystem == null) return ThinkingDecision.None("no_ctx");
      if (ctx.Cycle.UnresolvedNodeId <= 0) return ThinkingDecision.None("no_branch");

      var list = ctx.AutomatizmSystem.GetMotorsAutomatizmListFromTreeId(ctx.Cycle.UnresolvedNodeId)
        ?.Where(a => a != null && a.Usefulness >= 0)
        .ToList();
      if (list == null || list.Count == 0) return ThinkingDecision.None("no_candidates");

      // если есть штатный — всё равно можно попробовать другой
      var idx = _rng.Next(list.Count);
      var picked = list[idx];
      if (picked == null) return ThinkingDecision.None("picked_null");

      return new ThinkingDecision
      {
        AutomatizmToExecute = picked,
        DebugNote = $"random_atmz={picked.ID} actionImg={picked.ActionsImageID}"
      };
    }
  }
}

