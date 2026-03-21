using ISIDA.Common;
using ISIDA.Psychic.Memory.Episodic;
using System;
using System.Linq;

namespace ISIDA.Psychic.Thinking.Strategies
{
  /// <summary>
  /// Единый класс инфо-функций. Вызов через switch по Id.
  /// </summary>
  internal sealed class InfoFunctionsStrategy : IThinkingStrategy
  {
    private readonly ThinkingExperienceMemory _experienceMemory;
    private readonly Random _rng = new Random();

    /// <summary>Id стратегии для регистрации в диспетчере</summary>
    public string Id => "infoFunc";

    public InfoFunctionsStrategy(ThinkingExperienceMemory experienceMemory)
    {
      _experienceMemory = experienceMemory ?? throw new ArgumentNullException(nameof(experienceMemory));
    }

    /// <summary>Выполнить инфо-функцию по Id. Возвращает решение или None.</summary>
    public ThinkingDecision Execute(int infoFuncId, ThinkingStrategyContext ctx)
    {
      switch (infoFuncId)
      {
        case 14: return ExecuteEpisodicRule(ctx);
        case 17: return ExecuteExperienceRecommendation(ctx);
        case 25: return ExecuteRandomBranchAutomatizm(ctx);
        case 31: return ExecuteAskOperator(ctx);
        default: return ThinkingDecision.None("unknown_infoFunc");
      }
    }

    /// <summary>TryStep вызывается диспетчером с OptionalInfoFuncId в контексте</summary>
    public ThinkingDecision TryStep(ThinkingStrategyContext ctx)
    {
      if (ctx?.OptionalInfoFuncId == null || ctx.OptionalInfoFuncId <= 0)
        return ThinkingDecision.None("no_infoFunc_id");
      return Execute(ctx.OptionalInfoFuncId.Value, ctx);
    }

    private ThinkingDecision ExecuteEpisodicRule(ThinkingStrategyContext ctx)
    {
      if (ctx?.Cycle == null) return ThinkingDecision.None("no_ctx");
      if (ctx.EpisodicMemorySystem == null) return ThinkingDecision.None("no_episodic");

      var triggerId = ctx.Cycle.UnresolvedActionsImageId;
      if (triggerId <= 0) return ThinkingDecision.None("no_trigger");

      var chain = ctx.EpisodicMemorySystem.GetTargetChain(triggerId);
      var rule = (chain != null && chain.Count > 0) ? chain[0] : ctx.EpisodicMemorySystem.GetSingleBestRule(3, triggerId);
      if (rule == null || rule.ActionId <= 0) return ThinkingDecision.None("no_rule");

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

    private ThinkingDecision ExecuteExperienceRecommendation(ThinkingStrategyContext ctx)
    {
      if (_experienceMemory == null || ctx?.Cycle == null) return ThinkingDecision.None("no_memory");

      var c = ctx.Cycle;
      var rec = _experienceMemory.TryGetRecommendedAction(c.ProblemNodeId, c.ThemeId, c.PurposeId);
      if (rec <= 0) return ThinkingDecision.None("no_recommendation");

      if (c.UnresolvedActionsImageId > 0 && rec == c.UnresolvedActionsImageId)
        return ThinkingDecision.None("recommendation_equals_stimulus");

      Logger.Info($"Опыт циклов: рекомендован образ действий={rec}, проблема={c.ProblemNodeId}, тема={c.ThemeId}, цель={c.PurposeId}");
      return new ThinkingDecision
      {
        ActionsImageIdToAutomatize = rec,
        DebugNote = $"recommend_actionImg={rec}"
      };
    }

    private ThinkingDecision ExecuteRandomBranchAutomatizm(ThinkingStrategyContext ctx)
    {
      if (ctx?.Cycle == null || ctx.AutomatizmSystem == null) return ThinkingDecision.None("no_ctx");
      if (ctx.Cycle.UnresolvedNodeId <= 0) return ThinkingDecision.None("no_branch");

      var list = ctx.AutomatizmSystem.GetMotorsAutomatizmListFromTreeId(ctx.Cycle.UnresolvedNodeId)
        ?.Where(a => a != null && a.Usefulness >= 0)
        .ToList();
      if (list == null || list.Count == 0) return ThinkingDecision.None("no_candidates");

      var idx = _rng.Next(list.Count);
      var picked = list[idx];
      if (picked == null) return ThinkingDecision.None("picked_null");

      return new ThinkingDecision
      {
        AutomatizmToExecute = picked,
        DebugNote = $"random_atmz={picked.ID} actionImg={picked.ActionsImageID}"
      };
    }

    private ThinkingDecision ExecuteAskOperator(ThinkingStrategyContext ctx)
    {
      if (ctx?.InformationEnvironmentSystem?.CurrentInformationEnvironment == null) return ThinkingDecision.None("no_ie");
      var env = ctx.InformationEnvironmentSystem.CurrentInformationEnvironment;

      if (env.Danger || env.VeryActualSituation)
      {
        return new ThinkingDecision
        {
          RequestParrotFromOperator = true,
          DebugNote = "request_operator_help"
        };
      }

      return ThinkingDecision.None("not_urgent");
    }
  }
}
