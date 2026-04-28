using ISIDA.Psychic.Automatism;
using ISIDA.Psychic.Thinking;
using ISIDA.Psychic.Thinking.Strategies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic.Memory.Episodic
{
  /// <summary>
  /// Выборка «модели понимания» из эпизодической памяти: единая точка для циклов мышления и оценки правил по значимости.
  /// </summary>
  public static class EpisodicUnderstandingModelService
  {
    /// <summary>Цепочка с позитивным исходом или одно лучшее правило по стимулу (typeRule: 1 прямые, 2 учитель, 3 все).</summary>
    public static EpisodicRule ResolveBestRuleForStimulus(EpisodicMemorySystem episodic, int typeRule, int triggerId)
    {
      if (episodic == null || !EpisodicMemorySystem.IsInitialized || triggerId <= 0)
        return null;

      var chain = episodic.GetTargetChain(triggerId);
      if (chain != null && chain.Count > 0)
        return chain[0];
      return episodic.GetSingleBestRule(typeRule, triggerId);
    }

    /// <summary>
    /// Образ объекта высокой значимости как триггер — лучший ответ с неотрицательной валентностью при релаксации контекста.
    /// </summary>
    public static EpisodicRule TryBestAnswerForExtremeObjectTrigger(EpisodicMemorySystem episodic, int objectAsTriggerId)
    {
      if (episodic == null || !EpisodicMemorySystem.IsInitialized || objectAsTriggerId <= 0)
        return null;

      for (int lev = 0; lev <= 3; lev++)
      {
        var rules = EpisodicMemorySearch.GetEpisodesFromConditions(episodic, 3, lev, objectAsTriggerId, 0);
        if (rules == null || rules.Count == 0) continue;
        var pos = rules.Where(r => EpisodicMemoryRules.SignedValence(r) >= 0).ToList();
        if (pos.Count == 0) continue;
        return EpisodicMemoryRules.FindBestRule(pos).Rule;
      }
      return null;
    }

    /// <summary>Правила с ненулевой значимостью (StimulsEffect) под веткой; лучшее по Importence*Count.</summary>
    public static EpisodicRule GetBestRuleFromImportantsByBranch(
        EpisodicMemorySystem episodic,
        int baseId,
        int emotionId,
        int understandingNodeId,
        int problemNodeId)
    {
      if (episodic == null || !EpisodicMemorySystem.IsInitialized)
        return null;

      var rules = CollectRulesWithStimulsImportance(episodic.Tree, 0, new[] { baseId, emotionId, understandingNodeId, problemNodeId });
      if (rules == null || rules.Count == 0)
        return null;

      EpisodicRule best = null;
      int maxScore = 0;
      foreach (var r in rules)
      {
        if (r.Importence <= 0) continue;
        int score = r.Importence * r.Count;
        if (score > maxScore)
        {
          maxScore = score;
          best = r;
        }
      }
      return best;
    }

    private static bool IsBranchConditionMatch(int level, EpisodicMemoryNode node, int[] cond)
    {
      if (cond == null || level >= cond.Length) return false;
      int nodeVal;
      if (level == 0) nodeVal = node.BaseID;
      else if (level == 1) nodeVal = node.EmotionID;
      else if (level == 2) nodeVal = node.UnderstandingNodeId;
      else if (level == 3) nodeVal = node.NodePID;
      else nodeVal = 0;
      return nodeVal == cond[level];
    }

    private static List<EpisodicRule> CollectRulesWithStimulsImportance(
        EpisodicMemoryNode node,
        int level,
        int[] cond)
    {
      var list = new List<EpisodicRule>();
      if (node == null || cond == null || level > 3) return list;

      foreach (var child in node.Children ?? Enumerable.Empty<EpisodicMemoryNode>())
      {
        if (!IsBranchConditionMatch(level, child, cond))
          continue;
        if (level == 3)
          CollectRulesWithStimulsImportanceRecursive(child, list);
        else
          list.AddRange(CollectRulesWithStimulsImportance(child, level + 1, cond));
      }
      return list;
    }

    private static void CollectRulesWithStimulsImportanceRecursive(EpisodicMemoryNode node, List<EpisodicRule> list)
    {
      if (node == null) return;
      if (node.Params != null && Math.Abs(node.Params.StimulsEffect) > 0)
        list.Add(new EpisodicRule
        {
          TriggerId = node.TriggerId,
          ActionId = node.ActionId,
          Effect = node.Params.Effect,
          Count = node.Params.Count,
          Importence = node.Params.StimulsEffect,
          IsTeacher = node.Params.IsTeacher
        });
      foreach (var child in node.Children ?? Enumerable.Empty<EpisodicMemoryNode>())
        CollectRulesWithStimulsImportanceRecursive(child, list);
    }

    /// <summary>Собрать решение по правилу: готовый моторный автоматизм или образ для материализации.</summary>
    public static ThinkingDecision BuildDecisionFromRule(ThinkingStrategyContext ctx, EpisodicRule rule, string debugPrefix)
    {
      if (ctx?.Cycle == null || rule == null || rule.ActionId <= 0)
        return ThinkingDecision.None("no_rule");

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
            DebugNote = $"{debugPrefix} existing_atmz actionImg={rule.ActionId}"
          };
        }
      }

      return new ThinkingDecision
      {
        ActionsImageIdToAutomatize = rule.ActionId,
        DebugNote = $"{debugPrefix} rule_actionImg={rule.ActionId}"
      };
    }
  }
}
