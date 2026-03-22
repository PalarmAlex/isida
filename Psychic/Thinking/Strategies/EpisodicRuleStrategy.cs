using ISIDA.Psychic.Memory.Episodic;
using System.Linq;

namespace ISIDA.Psychic.Thinking.Strategies
{
  /// <summary>
  /// Повторный поиск решения по эпизодической памяти (включая GPT-цепочку) и выдача действия как ActionsImage.
  /// Уровень 2 уже это делает один раз; на 3-м уровне стратегия может запускаться повторно в другом контексте шага.
  /// </summary>
  public sealed class EpisodicRuleStrategy : IThinkingStrategy
  {
    /// <summary>
    /// Инфо-функция: поиск следующего действия по эпизодической памяти (правила/цепочки).
    /// Похоже по роли на GPT-подобный выбор: возвращает лучшее правило под триггер/контекст.
    /// </summary>
    public string Id => "infoFunc_28";

    /// <summary>
    /// Один шаг инфо-функции: берёт UnresolvedActionsImageId как триггер,
    /// ищет подходящее правило (включая target chain),
    /// и возвращает решение через ActionsImageIdToAutomatize или существующий Automatizm в ветке.
    /// </summary>
    /// <param name="ctx">Контекст текущего шага.</param>
    /// <returns>ThinkingDecision с выбором ActionsImageIdToAutomatize или AutomatizmToExecute.</returns>
    public ThinkingDecision TryStep(ThinkingStrategyContext ctx)
    {
      if (ctx?.Cycle == null) return ThinkingDecision.None("no_ctx");
      if (ctx.EpisodicMemorySystem == null) return ThinkingDecision.None("no_episodic");

      var triggerId = ctx.Cycle.UnresolvedActionsImageId;
      if (triggerId <= 0) return ThinkingDecision.None("no_trigger");

      // 1) GPT-цепочка (если есть позитивное продолжение)
      var chain = ctx.EpisodicMemorySystem.GetTargetChain(triggerId);
      var rule = (chain != null && chain.Count > 0) ? chain[0] : ctx.EpisodicMemorySystem.GetSingleBestRule(3, triggerId);
      if (rule == null || rule.ActionId <= 0) return ThinkingDecision.None("no_rule");

      // Попробовать сразу вернуть готовый автоматизм из ветки, если он уже есть
      if (ctx.Cycle.UnresolvedNodeId > 0 && ctx.AutomatizmSystem != null)
      {
        var existing = ctx.AutomatizmSystem
          .GetMotorsAutomatizmListFromTreeId(ctx.Cycle.UnresolvedNodeId)
          .FirstOrDefault(a => a != null && a.ActionsImageID == rule.ActionId && a.Usefulness >= 0);
        if (existing != null)
        {
          return new ThinkingDecision
          {
            AutomatizmToExecute = existing,
            DebugNote = $"existing_atmz_by_rule actionImg={rule.ActionId}"
          };
        }
      }

      return new ThinkingDecision
      {
        ActionsImageIdToAutomatize = rule.ActionId,
        DebugNote = $"rule_actionImg={rule.ActionId}"
      };
    }
  }
}

