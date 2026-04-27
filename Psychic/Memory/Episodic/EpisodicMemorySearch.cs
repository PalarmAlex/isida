using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic.Memory.Episodic
{
  /// <summary>
  /// Поиск и выбор правил из эпизодической памяти (GetEpisodesFromConditions, GetTargetChain, GetSingleBestRule)
  /// </summary>
  public static class EpisodicMemorySearch
  {
    /// <summary>Лимит кадров для GPT-цепочки</summary>
    private static int GetLimitCount()
    {
      if (AppGlobalState.EvolutionStage < 4) return 2;
      return AppGlobalState.EvolutionStage == 4 ? 5 : 20;
    }

    /// <summary>typeRule: 1 — только прямые, 2 — только учительские, 3 — все</summary>
    private static bool MatchTypeRule(EpisodicMemoryNode node, int typeRule)
    {
      if (node?.Params == null) return false;
      bool isTeacher = node.Params.IsTeacher;
      if (typeRule == 1 && isTeacher) return false;
      if (typeRule == 2 && !isTeacher) return false;
      return true;
    }

    /// <summary>Собрать ID листовых узлов с Params, начиная с node</summary>
    private static void GetIdsFromNode(
        EpisodicMemoryNode node,
        int typeRule,
        int triggerId,
        int actionId,
        ICollection<int> result)
    {
      if (node == null || result == null) return;
      foreach (var child in node.Children)
      {
        if (child.Params != null)
        {
          if (triggerId > 0 && child.TriggerId != triggerId) continue;
          if (actionId > 0 && child.ActionId != actionId) continue;
          if (!MatchTypeRule(child, typeRule)) continue;
          if (child.ID == -1) continue;
          result.Add(child.ID);
        }
        GetIdsFromNode(child, typeRule, triggerId, actionId, result);
      }
    }

    /// <summary>Найти узел по частичным условиям ветки. partialTier: 0 — Base+Emotion+Understanding+Problem, 1 — без Problem, 2 — Base+Emotion, 3 — только Base.</summary>
    private static EpisodicMemoryNode FindNodeByPartialConditions(
        EpisodicMemoryNode root,
        int baseId,
        int emotionId,
        int understandingNodeId,
        int nodePid,
        int partialTier)
    {
      if (root == null) return null;
      int[] cond;
      if (partialTier == 0)
        cond = new[] { baseId, emotionId, understandingNodeId, nodePid };
      else if (partialTier == 1)
        cond = new[] { baseId, emotionId, understandingNodeId };
      else if (partialTier == 2)
        cond = new[] { baseId, emotionId };
      else
        cond = new[] { baseId };
      var (id, _) = new EpisodicMemoryTree().FindBranch(0, cond, root);
      if (id <= 0) return null;
      return new EpisodicMemoryTree().FindNodeById(root, id);
    }

    /// <summary>Преобразовать ID узлов в EpisodicRule (kind: 0 — все, 1 — только позитивные, 2 — только негативные). При system != null используется O(1) GetNodeById.</summary>
    private static List<EpisodicRule> NodesToRules(
        EpisodicMemoryNode root,
        IReadOnlyList<int> nodeIds,
        int kind,
        EpisodicMemoryTree tree,
        EpisodicMemorySystem system = null)
    {
      var result = new List<EpisodicRule>();
      if (tree == null || root == null) return result;
      foreach (var nid in nodeIds ?? Array.Empty<int>())
      {
        var node = system != null ? system.GetNodeById(nid) : tree.FindNodeById(root, nid);
        if (node?.Params == null) continue;
        int signedVal = node.Params.IsTeacher ? node.Params.StimulsEffect : node.Params.Effect;
        if (kind == 1 && signedVal < 0) continue;
        if (kind == 2 && signedVal >= 0) continue;
        result.Add(new EpisodicRule
        {
          TriggerId = node.TriggerId,
          ActionId = node.ActionId,
          Effect = node.Params.Effect,
          Count = node.Params.Count,
          Importence = node.Params.StimulsEffect,
          IsTeacher = node.Params.IsTeacher
        });
      }
      return result;
    }

    /// <summary>Найти узлы по условиям. level: 0 — полный контекст ветки (4 ключа), далее релаксация по одному ключу с конца.</summary>
    public static List<int> GetEpisodeNodeIdsFromConditions(
        EpisodicMemoryNode root,
        int baseId,
        int emotionId,
        int understandingNodeId,
        int nodePid,
        int typeRule,
        int level,
        int triggerId,
        int actionId)
    {
      if (root == null || triggerId == 0) return new List<int>();
      var list = new List<int>();
      EpisodicMemoryNode startNode = null;

      switch (level)
      {
        case 0:
          if (nodePid == 0) return list;
          startNode = FindNodeByPartialConditions(root, baseId, emotionId, understandingNodeId, nodePid, 0);
          if (startNode == null) return list;
          if (startNode.BaseID != baseId || startNode.EmotionID != emotionId ||
              startNode.UnderstandingNodeId != understandingNodeId || startNode.NodePID != nodePid)
            return list;
          break;
        case 1:
          startNode = FindNodeByPartialConditions(root, baseId, emotionId, understandingNodeId, 0, 1);
          if (startNode == null) return list;
          if (startNode.BaseID != baseId || startNode.EmotionID != emotionId || startNode.UnderstandingNodeId != understandingNodeId)
            return list;
          break;
        case 2:
          startNode = FindNodeByPartialConditions(root, baseId, emotionId, 0, 0, 2);
          if (startNode == null) return list;
          if (startNode.BaseID != baseId || startNode.EmotionID != emotionId) return list;
          break;
        case 3:
          startNode = FindNodeByPartialConditions(root, baseId, 0, 0, 0, 3);
          if (startNode == null) return list;
          if (startNode.BaseID != baseId) startNode = root;
          break;
        default:
          return list;
      }

      GetIdsFromNode(startNode ?? root, typeRule, triggerId, actionId, list);
      return list;
    }

    /// <summary>Получить правила по условиям</summary>
    public static List<EpisodicRule> GetEpisodesFromConditions(
        EpisodicMemorySystem system,
        int typeRule,
        int level,
        int triggerId,
        int actionId)
    {
      if (system == null || !EpisodicMemorySystem.IsInitialized || AppGlobalState.EvolutionStage < 4)
        return new List<EpisodicRule>();
      var (baseId, emotionId, understandingNodeId, nodePid) = system.GetCurrentConditions(false);
      var nodeIds = GetEpisodeNodeIdsFromConditions(
          system.Tree, baseId, emotionId, understandingNodeId, nodePid, typeRule, level, triggerId, actionId);
      var tree = system.TreeLogic;
      return NodesToRules(system.Tree, nodeIds, 0, tree, system);
    }

    /// <summary>Найти цепочки в истории, начинающиеся с nodeIds, заканчивающиеся позитивом</summary>
    private static List<EpisodicRule> GetPositiveChainsFromHistory(
        EpisodicMemorySystem system,
        List<int> startNodeIds,
        int typeRule,
        int limit)
    {
      if (system?.History == null || startNodeIds == null || startNodeIds.Count == 0)
        return null;

      var entries = system.History.Entries;
      if (entries.Count == 0) return null;

      var tree = system.TreeLogic;
      var root = system.Tree;
      var chains = new List<List<EpisodicRule>>();

      for (int i = entries.Count - 1; i >= 0; i--)
      {
        if (entries[i].NodeId == -1) continue;
        if (!startNodeIds.Contains(entries[i].NodeId)) continue;

        var chain = BuildChainFromHistory(entries, i, limit, tree, root, typeRule, system);

        if (chain == null || chain.Count == 0) continue;

        // Конечное звено цепочки должно быть положительным по валентности
        var lastRule = chain[chain.Count - 1];
        if (EpisodicMemoryRules.SignedValence(lastRule) < 0) continue;
        // Суммарная полезность всех звеньев тоже должна быть положительной
        int effectSum = chain.Sum(r => EpisodicMemoryRules.RuleUtility(r));
        if (effectSum > 0)
        {
          chains.Add(chain);
          break;
        }
      }
      if (chains.Count == 0) return null;

      var best = chains.OrderByDescending(c => c.Count > 0
          ? EpisodicMemoryRules.RuleUtility(c[c.Count - 1])
          : 0).FirstOrDefault();

      return best;
    }

    private static List<EpisodicRule> BuildChainFromHistory(
        IReadOnlyList<EpisodicHistoryEntry> entries,
        int startIdx,
        int limit,
        EpisodicMemoryTree tree,
        EpisodicMemoryNode root,
        int typeRule,
        EpisodicMemorySystem system = null)
    {
      var chain = new List<EpisodicRule>();
      if (tree == null || root == null) return chain;
      for (int n = startIdx; n < entries.Count && (n - startIdx) < limit; n++)
      {
        if (entries[n].NodeId == -1) break;
        var node = system != null ? system.GetNodeById(entries[n].NodeId) : tree.FindNodeById(root, entries[n].NodeId);
        if (node?.Params == null) continue;
        if (!MatchTypeRule(node, typeRule)) continue;
        chain.Add(new EpisodicRule
        {
          TriggerId = node.TriggerId,
          ActionId = node.ActionId,
          Effect = node.Params.Effect,
          Count = node.Params.Count,
          Importence = node.Params.StimulsEffect,
          IsTeacher = node.Params.IsTeacher
        });
      }
      return chain;
    }

    /// <summary>GPT-цепочка: цепочка правил с конечным позитивом</summary>
    public static List<EpisodicRule> GetTargetChain(EpisodicMemorySystem system, int triggerId, int limit = 0)
    {
      if (system == null || !EpisodicMemorySystem.IsInitialized || AppGlobalState.EvolutionStage < 4)
        return null;

      if (limit <= 0) limit = GetLimitCount();
      List<int> nodeIds = null;

      for (int lev = 0; lev <= 3; lev++)
      {
        var (baseId, emotionId, understandingNodeId, nodePid) = system.GetCurrentConditions(false);
        nodeIds = GetEpisodeNodeIdsFromConditions(system.Tree, baseId, emotionId, understandingNodeId, nodePid, 1, lev, triggerId, 0);
        if (nodeIds != null && nodeIds.Count > 0) break;
      }

      if (nodeIds == null || nodeIds.Count == 0) return null;
      var chain = GetPositiveChainsFromHistory(system, nodeIds, 1, limit);

      if (chain != null && chain.Count > 0) return chain;
      var rules = NodesToRules(system.Tree, nodeIds, 1, system.TreeLogic, system);

      return rules.Count > 0 ? new List<EpisodicRule> { EpisodicMemoryRules.FindBestRule(rules).Rule } : null;
    }

    /// <summary>Лучшее правило по условиям</summary>
    public static EpisodicRule GetSingleBestRule(EpisodicMemorySystem system, int typeRule, int triggerId)
    {
      if (system == null || !EpisodicMemorySystem.IsInitialized || AppGlobalState.EvolutionStage < 4)
        return null;
      List<EpisodicRule> rules = null;
      for (int lev = 0; lev <= 3; lev++)
      {
        rules = GetEpisodesFromConditions(system, typeRule, lev, triggerId, 0);
        if (rules != null && rules.Count > 0) break;
      }
      if (rules == null || rules.Count == 0) return null;
      var posRules = rules.Where(r => EpisodicMemoryRules.SignedValence(r) >= 0).ToList();
      if (posRules.Count == 0) return null;
      return EpisodicMemoryRules.FindBestRule(posRules).Rule;
    }

    /// <summary>
    /// Прогноз последствий выполнения планируемого ответа после данного стимула: только прямые правила с совпадением Trigger и Action.
    /// Возвращает (0,0) при отсутствии данных; иначе accuracy 1..4 (уровень совпадения контекста ветки) и суммарную оценку эффекта.
    /// </summary>
    public static (int accuracy, int effect) GetAutomatizmActionPrognosis(
      EpisodicMemorySystem system,
      int stimulusActionsImageId,
      int plannedActionImageId)
    {
      if (system == null || !EpisodicMemorySystem.IsInitialized || AppGlobalState.EvolutionStage < 4)
        return (0, 0);
      if (stimulusActionsImageId <= 0 || plannedActionImageId <= 0)
        return (0, 0);

      var (acc, eff) = GetPrognoseFromAutomatizmActionPair(system, stimulusActionsImageId, plannedActionImageId);
      if (acc == 1 && eff < 0)
      {
        int bestPos = MaxPositiveDirectValence(system, stimulusActionsImageId, plannedActionImageId);
        if (bestPos > 0 && bestPos > -eff)
          return (acc, bestPos);
      }
      if (acc == 1 && eff > 0)
      {
        int worstNeg = MinNegativeDirectValence(system, stimulusActionsImageId, plannedActionImageId);
        if (worstNeg < 0 && -worstNeg > eff)
          return (acc, worstNeg);
      }
      return (acc, eff);
    }

    /// <summary>Сводка по набору прямых правил: сравнение лучшего неотрицательного и худшего отрицательного эффекта (как при свёртке кадров).</summary>
    private static int FinalCommonResultFromRules(IReadOnlyList<EpisodicRule> rules)
    {
      if (rules == null || rules.Count == 0)
        return 0;
      var neg = rules.Where(r => EpisodicMemoryRules.SignedValence(r) < 0).ToList();
      var pos = rules.Where(r => EpisodicMemoryRules.SignedValence(r) >= 0).ToList();
      var (_, worstNeg) = neg.Count > 0 ? EpisodicMemoryRules.FindWorseRule(neg) : (-1, (EpisodicRule)null);
      var (_, bestPos) = pos.Count > 0 ? EpisodicMemoryRules.FindBestRule(pos) : (-1, (EpisodicRule)null);
      int ne = worstNeg != null ? EpisodicMemoryRules.SignedValence(worstNeg) : int.MinValue;
      int pe = bestPos != null ? EpisodicMemoryRules.SignedValence(bestPos) : int.MinValue;
      if (pe == int.MinValue)
        return ne == int.MinValue ? 0 : ne;
      if (ne == int.MinValue)
        return pe;
      return pe > ne ? pe : ne;
    }

    private static (int accuracy, int effect) GetPrognoseFromAutomatizmActionPair(
      EpisodicMemorySystem system,
      int triggerId,
      int actionId)
    {
      const int directOnly = 1;
      for (int lev = 0; lev <= 3; lev++)
      {
        var rules = GetEpisodesFromConditions(system, directOnly, lev, triggerId, actionId);
        if (rules != null && rules.Count > 0)
          return (lev + 1, FinalCommonResultFromRules(rules));
      }
      return (0, 0);
    }

    private static int MaxPositiveDirectValence(EpisodicMemorySystem system, int triggerId, int actionId)
    {
      const int directOnly = 1;
      int best = 0;
      for (int lev = 0; lev <= 3; lev++)
      {
        var rules = GetEpisodesFromConditions(system, directOnly, lev, triggerId, actionId);
        if (rules == null || rules.Count == 0) continue;
        var pos = rules.Where(rule => EpisodicMemoryRules.SignedValence(rule) > 0).ToList();
        if (pos.Count == 0) continue;
        var (_, bestRule) = EpisodicMemoryRules.FindBestRule(pos);
        if (bestRule != null)
        {
          int v = EpisodicMemoryRules.SignedValence(bestRule);
          if (v > best) best = v;
        }
      }
      return best;
    }

    private static int MinNegativeDirectValence(EpisodicMemorySystem system, int triggerId, int actionId)
    {
      const int directOnly = 1;
      int worst = 0;
      bool any = false;
      for (int lev = 0; lev <= 3; lev++)
      {
        var rules = GetEpisodesFromConditions(system, directOnly, lev, triggerId, actionId);
        if (rules == null || rules.Count == 0) continue;
        var neg = rules.Where(rule => EpisodicMemoryRules.SignedValence(rule) < 0).ToList();
        if (neg.Count == 0) continue;
        var (_, worstRule) = EpisodicMemoryRules.FindWorseRule(neg);
        if (worstRule != null)
        {
          int v = EpisodicMemoryRules.SignedValence(worstRule);
          if (!any || v < worst)
          {
            worst = v;
            any = true;
          }
        }
      }
      return any ? worst : 0;
    }
  }
}
