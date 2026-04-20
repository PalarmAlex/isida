using ISIDA.Common;
using ISIDA.Psychic.Memory.Episodic;
using ISIDA.Psychic.Understanding;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic.Importance
{
  /// <summary>
  /// Сервис определения значимости объектов восприятия по эпизодической памяти.
  /// Значимость формируется в эпиз. памяти (StimulsEffect), определяется в контексте условий (BaseID, EmotionID, NodePID).
  /// </summary>
  public static class ObjectImportanceService
  {
    /// <summary>Порог значимости: объект считается достаточно значимым при |ExtremVal| > 2</summary>
    public const int MinSignificantImportance = 2;

    /// <summary>Порог высокой значимости (для смены темы и т.п.) при |ExtremVal| > 3</summary>
    public const int HighImportanceThreshold = 3;

    /// <summary>
    /// Найти значимость объекта (ActionsImage ID) для заданных условий в дереве эпизодов.
    /// Условия: BaseID, EmotionID, ProblemID (NodePID). Ищется узел, где Trigger==objectId (прямое правило)
    /// или Action==objectId (учительское, IsTeacher), возвращается StimulsEffect.
    /// </summary>
    /// <param name="root">Корень дерева эпизодической памяти</param>
    /// <param name="treeLogic">Логика дерева (для обхода)</param>
    /// <param name="objectId">ID образа (ActionsImage)</param>
    /// <param name="baseId">Базовое состояние (-1/0/1)</param>
    /// <param name="emotionId">ID эмоции</param>
    /// <param name="problemNodeId">ID узла проблемы (NodePID)</param>
    /// <returns>Объект значимости и значение, или (null, 0) если не найдено</returns>
    public static (ExtremImportance Obj, int Value) GetObjectImportanceValue(
      EpisodicMemoryNode root,
      EpisodicMemoryTree treeLogic,
      int objectId,
      int baseId,
      int emotionId,
      int problemNodeId)
    {
      if (objectId == 0 || root == null || treeLogic == null)
        return (null, 0);

      var cond = new[] { baseId, emotionId, problemNodeId };
      var found = FindExtremImportanceInBranch(root, treeLogic, 0, cond, objectId);
      if (found != null)
        return (found, found.ExtremVal);
      return (null, 0);
    }

    /// <summary>
    /// Рекурсивный поиск по ветке дерева (условия только по BaseID, EmotionID, NodePID).
    /// В узлах с Params проверяется: (TriggerId==objectId и Effect!=100) или (ActionId==objectId и Effect==100).
    /// </summary>
    private static ExtremImportance FindExtremImportanceInBranch(
      EpisodicMemoryNode node,
      EpisodicMemoryTree treeLogic,
      int level,
      int[] cond,
      int objectId)
    {
      if (node == null || cond == null || level > 2)
        return null;

      if (level < 3)
      {
        foreach (var child in node.Children ?? Enumerable.Empty<EpisodicMemoryNode>())
        {
          if (!IsConditionMatch(level, child, cond))
            continue;
          if (level == 2)
          {
            var found = ScanNodeAndDescendantsForImportance(child, objectId);
            if (found != null)
              return found;
          }
          else
          {
            var found = FindExtremImportanceInBranch(child, treeLogic, level + 1, cond, objectId);
            if (found != null)
              return found;
          }
          return null;
        }
        return null;
      }

      return null;
    }

    private static bool IsConditionMatch(int level, EpisodicMemoryNode node, int[] cond)
    {
      if (cond == null || level >= cond.Length) return false;
      int nodeVal = level == 0 ? node.BaseID : level == 1 ? node.EmotionID : node.NodePID;
      return nodeVal == cond[level];
    }

    /// <summary>Сканировать узел и потомков: ищем узел с Params и совпадением TriggerId/ActionId с objectId.</summary>
    private static ExtremImportance ScanNodeAndDescendantsForImportance(EpisodicMemoryNode node, int objectId)
    {
      if (node == null) return null;
      if (node.Params != null)
      {
        bool isDirect = node.TriggerId == objectId && !node.Params.IsTeacher;
        bool isTeacher = node.ActionId == objectId && node.Params.IsTeacher;
        if (isDirect || isTeacher)
          return new ExtremImportance(objectId, node.Params.StimulsEffect);
      }
      foreach (var child in node.Children ?? Enumerable.Empty<EpisodicMemoryNode>())
      {
        var found = ScanNodeAndDescendantsForImportance(child, objectId);
        if (found != null)
          return found;
      }
      return null;
    }

    /// <summary>
    /// Определить текущий объект максимальной значимости для стимула (curActiveActions) и записать в информационную среду.
    /// Вызывать при каждом новом стимуле (после установки ActionsImageID).
    /// </summary>
    /// <param name="episodic">Система эпизодической памяти.</param>
    /// <param name="infoEnv">Система информационной среды.</param>
    /// <param name="actionsImageId">ID образа действий.</param>
    /// <param name="understandingTreeSystem">Дерево понимания для триггера темы «объект высокой значимости» (8); передаётся вызывающим кодом, без использования Instance.</param>
    public static void UpdateExtremImportanceObject(
      EpisodicMemorySystem episodic,
      InformationEnvironmentSystem infoEnv,
      int actionsImageId,
      UnderstandingTreeSystem understandingTreeSystem = null)
    {
      if (episodic == null || infoEnv == null || !EpisodicMemorySystem.IsInitialized)
        return;
      if (AppGlobalState.EvolutionStage < 4)
        return;

      if (actionsImageId == 0)
      {
        infoEnv.CurrentInformationEnvironment.ExtremImportanceObjectID = 0;
        return;
      }

      var (baseId, emotionId, nodePid) = episodic.GetCurrentConditions(false);
      var (obj, value) = GetObjectImportanceValue(
        episodic.Tree,
        episodic.TreeLogic,
        actionsImageId,
        baseId,
        emotionId,
        nodePid);

      if (obj != null && Math.Abs(obj.ExtremVal) > MinSignificantImportance)
      {
        infoEnv.CurrentInformationEnvironment.ExtremImportanceObjectID = obj.ObjId;
        // Триггер «Высокая значимость объекта» — активировать тему мышления (через переданную ссылку, без Instance)
        if (Math.Abs(obj.ExtremVal) > HighImportanceThreshold && understandingTreeSystem != null)
          understandingTreeSystem.UpdateThemeByTrigger(AgentEventsCatalog.Codes.HighObjectImportance);
      }
      else
        infoEnv.CurrentInformationEnvironment.ExtremImportanceObjectID = 0;
    }

    /// <summary>
    /// Знаком ли образ actID для данных условий (есть ли запись в эпизодической памяти). Новизна.
    /// </summary>
    public static bool IsUnknownActionsImage(
      EpisodicMemorySystem episodic,
      int actId,
      int baseId,
      int emotionId,
      int problemNodeId)
    {
      if (actId == 0 || episodic == null) return true;
      var (obj, _) = GetObjectImportanceValue(episodic.Tree, episodic.TreeLogic, actId, baseId, emotionId, problemNodeId);
      return obj == null;
    }

    /// <summary>
    /// Собрать правила с ненулевой значимостью по текущим условиям и выбрать лучшее по Importence*Count
    /// Условия: BaseID, EmotionID, NodePID (в isida нет отдельного Understanding, используем Problem).
    /// </summary>
    public static EpisodicRule GetBestRuleFromImportants(
      EpisodicMemorySystem episodic,
      int baseId,
      int emotionId,
      int problemNodeId)
    {
      if (episodic == null || !EpisodicMemorySystem.IsInitialized)
        return null;

      var rules = CollectRulesWithImportance(episodic.Tree, episodic.TreeLogic, 0, new[] { baseId, emotionId, problemNodeId });
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

    private static List<EpisodicRule> CollectRulesWithImportance(
      EpisodicMemoryNode node,
      EpisodicMemoryTree treeLogic,
      int level,
      int[] cond)
    {
      var list = new List<EpisodicRule>();
      if (node == null || cond == null || level > 2) return list;

      foreach (var child in node.Children ?? Enumerable.Empty<EpisodicMemoryNode>())
      {
        if (!IsConditionMatch(level, child, cond))
          continue;
        if (level == 2)
          CollectRulesWithImportanceRecursive(child, list);
        else
          list.AddRange(CollectRulesWithImportance(child, treeLogic, level + 1, cond));
        if (level < 2)
          return list;
      }
      return list;
    }

    private static void CollectRulesWithImportanceRecursive(EpisodicMemoryNode node, List<EpisodicRule> list)
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
        CollectRulesWithImportanceRecursive(child, list);
    }

    /// <summary>
    /// Найти действие в учительских правилах с наивысшей значимостью в данных условиях (StimulsEffect*Count).
    /// </summary>
    public static int FindBestPositiveAction(
      EpisodicMemorySystem episodic,
      int baseId,
      int emotionId,
      int problemNodeId)
    {
      if (episodic == null) return 0;

      var candidates = CollectPositiveTeacherActions(episodic.Tree, episodic.TreeLogic, 0, new[] { baseId, emotionId, problemNodeId });
      if (candidates == null || candidates.Count == 0)
        return 0;

      int maxVal = 0;
      int bestActionId = 0;
      foreach (var c in candidates)
      {
        if (c.ExtremVal > maxVal)
        {
          maxVal = c.ExtremVal;
          bestActionId = c.ObjId;
        }
      }
      return bestActionId;
    }

    /// <summary>Учительские правила (IsTeacher), усреднённая оценка > 2; значение = StimulsEffect * Count.</summary>
    private static List<ExtremImportance> CollectPositiveTeacherActions(
      EpisodicMemoryNode node,
      EpisodicMemoryTree treeLogic,
      int level,
      int[] cond)
    {
      var list = new List<ExtremImportance>();
      if (node == null || cond == null || level > 2) return list;

      foreach (var child in node.Children ?? Enumerable.Empty<EpisodicMemoryNode>())
      {
        if (!IsConditionMatch(level, child, cond))
          continue;
        if (level == 2)
          CollectTeacherActionsRecursive(child, list);
        else
          list.AddRange(CollectPositiveTeacherActions(child, treeLogic, level + 1, cond));
        if (level < 2)
          return list;
      }
      return list;
    }

    private static void CollectTeacherActionsRecursive(EpisodicMemoryNode node, List<ExtremImportance> list)
    {
      if (node == null) return;
      if (node.Params != null &&
          node.Params.IsTeacher &&
          node.Params.StimulsEffect > 2)
        list.Add(new ExtremImportance(node.ActionId, node.Params.StimulsEffect * node.Params.Count));
      foreach (var child in node.Children ?? Enumerable.Empty<EpisodicMemoryNode>())
        CollectTeacherActionsRecursive(child, list);
    }
  }
}
