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
      bool isTeacher = node.Params.Effect == EpisodicMemoryRulesService.TeacherRuleEffect;
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

    /// <summary>Найти узел по частичным условиям (level 0-2)</summary>
    private static EpisodicMemoryNode FindNodeByPartialConditions(
        EpisodicMemoryNode root,
        int baseId,
        int emotionId,
        int nodePid,
        int level)
    {
      if (root == null) return null;
      int[] cond = level == 0 ? new[] { baseId, emotionId, nodePid, 0, 0 } :
                   level == 1 ? new[] { baseId, emotionId, 0, 0, 0 } :
                   new[] { baseId, 0, 0, 0, 0 };
      var (id, lev) = new EpisodicMemoryTree().FindBranch(0, cond, root);
      if (id <= 0) return null;
      return new EpisodicMemoryTree().FindNodeById(root, id);
    }

    /// <summary>Преобразовать ID узлов в EpisodicRule (kind: 0 — все, 1 — только позитивные, 2 — только негативные)</summary>
    private static List<EpisodicRule> NodesToRules(
        EpisodicMemoryNode root,
        IReadOnlyList<int> nodeIds,
        int kind,
        EpisodicMemoryTree tree)
    {
      var result = new List<EpisodicRule>();
      if (tree == null || root == null) return result;
      foreach (var nid in nodeIds ?? Array.Empty<int>())
      {
        var node = tree.FindNodeById(root, nid);
        if (node?.Params == null) continue;
        int effect = node.Params.Effect == EpisodicMemoryRulesService.TeacherRuleEffect ? 1 : node.Params.Effect;
        if (kind == 1 && effect < 0) continue;
        if (kind == 2 && effect >= 0) continue;
        result.Add(new EpisodicRule
        {
          TriggerId = node.TriggerId,
          ActionId = node.ActionId,
          Effect = node.Params.Effect,
          Count = node.Params.Count,
          Importence = node.Params.StimulsEffect
        });
      }
      return result;
    }

    /// <summary>Найти узлы по условиям. level: 0 — BaseID+EmotionID+NodePID, 1 — BaseID+EmotionID, 2 — BaseID</summary>
    public static List<int> GetEpisodeNodeIdsFromConditions(
        EpisodicMemoryNode root,
        int baseId,
        int emotionId,
        int nodePid,
        int typeRule,
        int level,
        int triggerId,
        int actionId)
    {
      if (root == null || triggerId == 0) return new List<int>();
      var list = new List<int>();
      EpisodicMemoryNode startNode = null;
      var tree = new EpisodicMemoryTree();

      switch (level)
      {
        case 0:
          if (nodePid == 0) return list;
          startNode = FindNodeByPartialConditions(root, baseId, emotionId, nodePid, 0);
          if (startNode == null) return list;
          if (startNode.BaseID != baseId || startNode.EmotionID != emotionId || startNode.NodePID != nodePid)
            return list;
          break;
        case 1:
          startNode = FindNodeByPartialConditions(root, baseId, emotionId, 0, 1);
          if (startNode == null) return list;
          if (startNode.BaseID != baseId || startNode.EmotionID != emotionId) return list;
          break;
        case 2:
          startNode = FindNodeByPartialConditions(root, baseId, 0, 0, 2);
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
      var (baseId, emotionId, nodePid) = system.GetCurrentConditions(false);
      var nodeIds = GetEpisodeNodeIdsFromConditions(
          system.Tree, baseId, emotionId, nodePid, typeRule, level, triggerId, actionId);
      var tree = system.TreeLogic;
      return NodesToRules(system.Tree, nodeIds, 0, tree);
    }

    private static Dictionary<int, EpisodicMemoryNode> GetNodesById(EpisodicMemoryNode root)
    {
      var d = new Dictionary<int, EpisodicMemoryNode>();
      CollectNodes(root, d);
      return d;
    }

    private static void CollectNodes(EpisodicMemoryNode node, Dictionary<int, EpisodicMemoryNode> dict)
    {
      if (node == null) return;
      dict[node.ID] = node;
      foreach (var c in node.Children)
        CollectNodes(c, dict);
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

        var chain = BuildChainFromHistory(entries, i, limit, tree, root, typeRule);

        if (chain == null || chain.Count == 0) continue;

        // Конечное звено цепочки должно быть положительным по эффекту
        int lastEffect = chain[chain.Count - 1].Effect == EpisodicMemoryRulesService.TeacherRuleEffect ? 1 : chain[chain.Count - 1].Effect;
        if (lastEffect < 0) continue;
        // Суммарный эффект всех звеньев тоже должен быть положительным
        int effectSum = chain.Sum(r => EpisodicMemoryRules.GetWpower(
            r.Effect == EpisodicMemoryRulesService.TeacherRuleEffect ? 1 : r.Effect, r.Count));
        if (effectSum > 0)
        {
          chains.Add(chain);
          break;
        }
      }
      if (chains.Count == 0) return null;

      var best = chains.OrderByDescending(c => c.Count > 0 ? EpisodicMemoryRules.GetWpower(
          c[c.Count - 1].Effect == EpisodicMemoryRulesService.TeacherRuleEffect ? 1 : c[c.Count - 1].Effect,
          c[c.Count - 1].Count) : 0).FirstOrDefault();

      return best;
    }

    private static List<EpisodicRule> BuildChainFromHistory(
        IReadOnlyList<EpisodicHistoryEntry> entries,
        int startIdx,
        int limit,
        EpisodicMemoryTree tree,
        EpisodicMemoryNode root,
        int typeRule)
    {
      var chain = new List<EpisodicRule>();
      if (tree == null || root == null) return chain;
      for (int n = startIdx; n < entries.Count && (n - startIdx) < limit; n++)
      {
        if (entries[n].NodeId == -1) break;
        var node = tree.FindNodeById(root, entries[n].NodeId);
        if (node?.Params == null) continue;
        if (!MatchTypeRule(node, typeRule)) continue;
        chain.Add(new EpisodicRule
        {
          TriggerId = node.TriggerId,
          ActionId = node.ActionId,
          Effect = node.Params.Effect,
          Count = node.Params.Count,
          Importence = node.Params.StimulsEffect
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

      for (int lev = 0; lev <= 2; lev++)
      {
        var (baseId, emotionId, nodePid) = system.GetCurrentConditions(false);
        nodeIds = GetEpisodeNodeIdsFromConditions(system.Tree, baseId, emotionId, nodePid, 1, lev, triggerId, 0);
        if (nodeIds != null && nodeIds.Count > 0) break;
      }

      if (nodeIds == null || nodeIds.Count == 0) return null;
      var chain = GetPositiveChainsFromHistory(system, nodeIds, 1, limit);

      if (chain != null && chain.Count > 0) return chain;
      var rules = NodesToRules(system.Tree, nodeIds, 1, system.TreeLogic);

      return rules.Count > 0 ? new List<EpisodicRule> { EpisodicMemoryRules.FindBestRule(rules).Rule } : null;
    }

    /// <summary>Лучшее правило по условиям</summary>
    public static EpisodicRule GetSingleBestRule(EpisodicMemorySystem system, int typeRule, int triggerId)
    {
      if (system == null || !EpisodicMemorySystem.IsInitialized || AppGlobalState.EvolutionStage < 4)
        return null;
      List<EpisodicRule> rules = null;
      for (int lev = 0; lev <= 2; lev++)
      {
        rules = GetEpisodesFromConditions(system, typeRule, lev, triggerId, 0);
        if (rules != null && rules.Count > 0) break;
      }
      if (rules == null || rules.Count == 0) return null;
      var posRules = rules.Where(r => (r.Effect == EpisodicMemoryRulesService.TeacherRuleEffect ? 1 : r.Effect) >= 0).ToList();
      if (posRules.Count == 0) return null;
      return EpisodicMemoryRules.FindBestRule(posRules).Rule;
    }
  }
}
