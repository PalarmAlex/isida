using ISIDA.Common;
using ISIDA.Psychic.Memory.Episodic;
using ISIDA.Psychic.Thinking;
using ISIDA.Psychic.Understanding;
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
    private int _episodicHistoryCursor = -1;

    /// <summary>Id стратегии для регистрации в диспетчере</summary>
    public string Id => "infoFunc";

    public InfoFunctionsStrategy(ThinkingExperienceMemory experienceMemory)
    {
      _experienceMemory = experienceMemory ?? throw new ArgumentNullException(nameof(experienceMemory));
    }

    /// <summary>Название инфо-функции по Id из фиксированного справочника (канон — <see cref="InfoFunctionsCatalog"/>).</summary>
    internal static string GetInfoFunctionDisplayName(int id)
    {
      if (id <= 0) return "";
      var e = InfoFunctionsCatalog.GetById(id);
      return e?.Name ?? "";
    }

    /// <summary>Выполнить инфо-функцию по Id. Возвращает решение или None.</summary>
    public ThinkingDecision Execute(int infoFuncId, ThinkingStrategyContext ctx)
    {
      switch (infoFuncId)
      {
        case 1: return ExecuteCycleContinuation(ctx);
        case 2: return ExecuteThinkingFocus(ctx);
        case 3: return ExecuteExperienceRecommendation(ctx);
        case 4: return ExecuteEmergencyMotor(ctx);
        case 5: return ExecuteExtremeObjectImportance(ctx);
        case 6: return ExecuteDoubtStaffAutomatizm(ctx);
        case 7: return ExecuteCreateMotorFromRule(ctx);
        case 8: return ExecuteMentalGoal(ctx);
        case 9: return ExecuteReinforceObjectImportance(ctx);
        case 10: return ExecuteAnchorSignificanceFrame(ctx);
        case 11: return ExecuteReactivateUnderstandingTree(ctx);
        case 12: return ExecuteEpisodicRule(ctx);
        case 13: return ExecuteRunStaffMotor(ctx);
        case 14: return ExecuteInformationEnvironment(ctx);
        case 15: return ExecuteHeuristicProbe(ctx);
        case 16: return ExecuteEpisodicHistoryStep(ctx, -1);
        case 17: return ExecuteEpisodicHistoryStep(ctx, 1);
        case 18: return ExecuteUrgentActionSearch(ctx);
        case 19:
        case 20:
        case 21:
        case 23:
        case 24: return ThinkingDecision.None("chain_not_supported");
        case 22: return ExecuteInitiative(ctx);
        case 25: return ExecuteFantasyDominant(ctx);
        case 26: return ExecuteRulesAnalysis(ctx);
        case 27: return ExecuteProvocationNoStimulus(ctx);
        case 28: return ExecuteEpisodicRule(ctx);
        case 29: return ExecuteExperienceRecommendation(ctx);
        case 30: return ExecuteRandomBranchAutomatizm(ctx);
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

    private static ThinkingDecision ExecuteCycleContinuation(ThinkingStrategyContext ctx)
    {
      if (ctx?.Cycle == null) return ThinkingDecision.None("no_ctx");
      return ThinkingDecision.None("cycle_continue");
    }

    private ThinkingDecision ExecuteThinkingFocus(ThinkingStrategyContext ctx)
    {
      var d = ExecuteExperienceRecommendation(ctx);
      if (d != null && (d.AutomatizmToExecute != null || d.ActionsImageIdToAutomatize > 0 || d.RequestParrotFromOperator))
        return d;
      return ExecuteEpisodicRule(ctx);
    }

    private ThinkingDecision ExecuteEmergencyMotor(ThinkingStrategyContext ctx)
    {
      var d = ExecuteEpisodicRule(ctx);
      if (d != null && (d.AutomatizmToExecute != null || d.ActionsImageIdToAutomatize > 0))
        return d;
      return ExecuteRandomBranchAutomatizm(ctx);
    }

    private ThinkingDecision ExecuteExtremeObjectImportance(ThinkingStrategyContext ctx)
    {
      return ApplyThemeByAgentCode(ctx, AgentEventsCatalog.Codes.HighObjectImportance);
    }

    private ThinkingDecision ExecuteReinforceObjectImportance(ThinkingStrategyContext ctx)
    {
      return ApplyThemeByAgentCode(ctx, AgentEventsCatalog.Codes.HighObjectImportance);
    }

    private ThinkingDecision ApplyThemeByAgentCode(ThinkingStrategyContext ctx, int agentEventCode)
    {
      if (ctx?.UnderstandingTreeSystem == null || ctx.ProblemTreeSystem == null || ctx.Cycle == null)
        return ThinkingDecision.None("no_understanding");
      ctx.UnderstandingTreeSystem.UpdateThemeByTriggerAndRefreshProblemTree(agentEventCode, ctx.ProblemTreeSystem);
      var tid = ctx.UnderstandingTreeSystem.ProblemTreeInfo.ThemeId;
      if (tid > 0) ctx.Cycle.ThemeId = tid;
      return ThinkingDecision.None("theme_refreshed");
    }

    private ThinkingDecision ExecuteDoubtStaffAutomatizm(ThinkingStrategyContext ctx)
    {
      if (ctx?.Cycle == null || ctx.AutomatizmSystem == null) return ThinkingDecision.None("no_ctx");
      if (ctx.Cycle.UnresolvedNodeId <= 0) return ThinkingDecision.None("no_branch");
      var staff = ctx.CurrentStaffAutomatizm;
      var list = ctx.AutomatizmSystem.GetMotorsAutomatizmListFromTreeId(ctx.Cycle.UnresolvedNodeId)
        ?.Where(a => a != null && a.Usefulness >= 0 && (staff == null || a.ID != staff.ID))
        .ToList();
      if (list == null || list.Count == 0) return ExecuteRandomBranchAutomatizm(ctx);
      var picked = list[_rng.Next(list.Count)];
      return new ThinkingDecision
      {
        AutomatizmToExecute = picked,
        DebugNote = $"non_staff_atmz={picked.ID}"
      };
    }

    private ThinkingDecision ExecuteCreateMotorFromRule(ThinkingStrategyContext ctx)
    {
      return ExecuteEpisodicRule(ctx);
    }

    private ThinkingDecision ExecuteMentalGoal(ThinkingStrategyContext ctx)
    {
      return ApplyThemeByAgentCode(ctx, AgentEventsCatalog.Codes.NeedThinking);
    }

    private ThinkingDecision ExecuteReactivateUnderstandingTree(ThinkingStrategyContext ctx)
    {
      return ApplyThemeByAgentCode(ctx, AgentEventsCatalog.Codes.NeedThinking);
    }

    private ThinkingDecision ExecuteAnchorSignificanceFrame(ThinkingStrategyContext ctx)
    {
      if (ctx?.EpisodicMemorySystem == null) return ThinkingDecision.None("no_episodic");
      var entries = ctx.EpisodicMemorySystem.History?.Entries;
      if (entries == null || entries.Count == 0) return ThinkingDecision.None("no_history");
      var tail = entries.Skip(Math.Max(0, entries.Count - 20)).ToList();
      int bestId = 0;
      int bestAbs = 0;
      foreach (var e in tail)
      {
        if (e == null || e.NodeId <= 0) continue;
        var node = ctx.EpisodicMemorySystem.GetNodeById(e.NodeId);
        if (node?.Params == null) continue;
        var abs = Math.Abs(node.Params.StimulsEffect);
        if (abs > bestAbs)
        {
          bestAbs = abs;
          bestId = node.ID;
        }
      }
      if (bestId <= 0) return ThinkingDecision.None("no_anchor");
      var bestNode = ctx.EpisodicMemorySystem.GetNodeById(bestId);
      if (bestNode == null || bestNode.ActionId <= 0) return ThinkingDecision.None("anchor_no_action");
      return new ThinkingDecision
      {
        ActionsImageIdToAutomatize = bestNode.ActionId,
        DebugNote = $"anchor_actionImg={bestNode.ActionId}"
      };
    }

    private ThinkingDecision ExecuteRunStaffMotor(ThinkingStrategyContext ctx)
    {
      var staff = ctx?.CurrentStaffAutomatizm;
      if (staff != null && staff.Usefulness >= 0)
      {
        return new ThinkingDecision
        {
          AutomatizmToExecute = staff,
          CloseCycleImmediately = staff.Usefulness >= 1,
          DebugNote = "staff_motor"
        };
      }
      return ExecuteRandomBranchAutomatizm(ctx);
    }

    private ThinkingDecision ExecuteInformationEnvironment(ThinkingStrategyContext ctx)
    {
      var ask = ExecuteAskOperator(ctx);
      if (ask.RequestParrotFromOperator) return ask;
      return ExecuteExperienceRecommendation(ctx);
    }

    private ThinkingDecision ExecuteHeuristicProbe(ThinkingStrategyContext ctx)
    {
      return ExecuteExperienceRecommendation(ctx);
    }

    private ThinkingDecision ExecuteEpisodicHistoryStep(ThinkingStrategyContext ctx, int delta)
    {
      if (ctx?.EpisodicMemorySystem?.History?.Entries == null) return ThinkingDecision.None("no_episodic");
      var entries = ctx.EpisodicMemorySystem.History.Entries;
      if (entries.Count == 0) return ThinkingDecision.None("no_history");
      if (_episodicHistoryCursor < 0) _episodicHistoryCursor = entries.Count - 1;
      else
      {
        _episodicHistoryCursor += delta;
        if (_episodicHistoryCursor < 0) _episodicHistoryCursor = 0;
        if (_episodicHistoryCursor >= entries.Count) _episodicHistoryCursor = entries.Count - 1;
      }
      var e = entries[_episodicHistoryCursor];
      if (e == null || e.NodeId <= 0) return ThinkingDecision.None("history_gap");
      var node = ctx.EpisodicMemorySystem.GetNodeById(e.NodeId);
      if (node == null || node.ActionId <= 0) return ThinkingDecision.None("history_no_action");
      return new ThinkingDecision
      {
        ActionsImageIdToAutomatize = node.ActionId,
        DebugNote = $"history_idx={_episodicHistoryCursor} actionImg={node.ActionId}"
      };
    }

    private ThinkingDecision ExecuteUrgentActionSearch(ThinkingStrategyContext ctx)
    {
      return ExecuteEmergencyMotor(ctx);
    }

    private ThinkingDecision ExecuteInitiative(ThinkingStrategyContext ctx)
    {
      if (ctx?.InformationEnvironmentSystem?.CurrentInformationEnvironment == null)
        return ThinkingDecision.None("no_ie");
      var env = ctx.InformationEnvironmentSystem.CurrentInformationEnvironment;
      if (env.Danger || env.VeryActualSituation || env.UnresolvedAtThinkingLevel2)
        return ExecuteRandomBranchAutomatizm(ctx);
      return ThinkingDecision.None("initiative_hold");
    }

    private ThinkingDecision ExecuteFantasyDominant(ThinkingStrategyContext ctx)
    {
      return ExecuteRandomBranchAutomatizm(ctx);
    }

    private ThinkingDecision ExecuteRulesAnalysis(ThinkingStrategyContext ctx)
    {
      return ExecuteEpisodicRule(ctx);
    }

    private ThinkingDecision ExecuteProvocationNoStimulus(ThinkingStrategyContext ctx)
    {
      if (ctx?.InformationEnvironmentSystem?.CurrentInformationEnvironment == null)
        return ThinkingDecision.None("no_ie");
      var env = ctx.InformationEnvironmentSystem.CurrentInformationEnvironment;
      if (env.Danger || env.VeryActualSituation)
        return ExecuteAskOperator(ctx);
      return ExecuteRandomBranchAutomatizm(ctx);
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
            CloseCycleImmediately = existing.Usefulness >= 1,
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
