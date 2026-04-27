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
  /// Значимость формируется в эпиз. памяти (StimulsEffect), в контексте ветки: BaseID, EmotionID, UnderstandingNodeId, NodePID.
  /// </summary>
  public static class ObjectImportanceService
  {
    /// <summary>Порог значимости: объект считается достаточно значимым при |ExtremVal| > 2</summary>
    public const int MinSignificantImportance = 2;

    /// <summary>Порог высокой значимости (для смены темы и т.п.) при |ExtremVal| > 3</summary>
    public const int HighImportanceThreshold = 3;

    /// <summary>
    /// Найти значимость объекта (ActionsImage ID) для заданных условий в дереве эпизодов.
    /// Ищется узел, где Trigger==objectId (прямое правило) или Action==objectId (учительское, IsTeacher), возвращается StimulsEffect.
    /// </summary>
    public static (ExtremImportance Obj, int Value) GetObjectImportanceValue(
      EpisodicMemoryNode root,
      EpisodicMemoryTree treeLogic,
      int objectId,
      int baseId,
      int emotionId,
      int understandingNodeId,
      int problemNodeId)
    {
      if (objectId == 0 || root == null || treeLogic == null)
        return (null, 0);

      var cond = new[] { baseId, emotionId, understandingNodeId, problemNodeId };
      var found = FindExtremImportanceInBranch(root, treeLogic, 0, cond, objectId);
      if (found != null)
        return (found, found.ExtremVal);
      return (null, 0);
    }

    private static ExtremImportance FindExtremImportanceInBranch(
      EpisodicMemoryNode node,
      EpisodicMemoryTree treeLogic,
      int level,
      int[] cond,
      int objectId)
    {
      if (node == null || cond == null || level > 3)
        return null;

      foreach (var child in node.Children ?? Enumerable.Empty<EpisodicMemoryNode>())
      {
        if (!IsBranchConditionMatch(level, child, cond))
          continue;
        if (level == 3)
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
      }
      return null;
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
    /// </summary>
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

      var (baseId, emotionId, understandingNodeId, nodePid) = episodic.GetCurrentConditions(false);
      var (obj, _) = GetObjectImportanceValue(
        episodic.Tree,
        episodic.TreeLogic,
        actionsImageId,
        baseId,
        emotionId,
        understandingNodeId,
        nodePid);

      if (obj != null && Math.Abs(obj.ExtremVal) > MinSignificantImportance)
      {
        infoEnv.CurrentInformationEnvironment.ExtremImportanceObjectID = obj.ObjId;
        if (Math.Abs(obj.ExtremVal) > HighImportanceThreshold && understandingTreeSystem != null)
          understandingTreeSystem.UpdateThemeByTrigger(AgentEventsCatalog.Codes.HighObjectImportance);
      }
      else
        infoEnv.CurrentInformationEnvironment.ExtremImportanceObjectID = 0;
    }

    /// <summary>Знаком ли образ actID для данных условий (есть ли запись в эпизодической памяти). Новизна.</summary>
    public static bool IsUnknownActionsImage(
      EpisodicMemorySystem episodic,
      int actId,
      int baseId,
      int emotionId,
      int understandingNodeId,
      int problemNodeId)
    {
      if (actId == 0 || episodic == null) return true;
      var (obj, _) = GetObjectImportanceValue(episodic.Tree, episodic.TreeLogic, actId, baseId, emotionId, understandingNodeId, problemNodeId);
      return obj == null;
    }

    /// <summary>
    /// Собрать правила с ненулевой значимостью по ветке и выбрать лучшее по Importence*Count (делегирует в EpisodicUnderstandingModelService).
    /// </summary>
    public static EpisodicRule GetBestRuleFromImportants(
      EpisodicMemorySystem episodic,
      int baseId,
      int emotionId,
      int understandingNodeId,
      int problemNodeId)
    {
      return EpisodicUnderstandingModelService.GetBestRuleFromImportantsByBranch(
          episodic, baseId, emotionId, understandingNodeId, problemNodeId);
    }

    /// <summary>Найти действие в учительских правилах с наивысшей значимостью в данных условиях (StimulsEffect*Count).</summary>
    public static int FindBestPositiveAction(
      EpisodicMemorySystem episodic,
      int baseId,
      int emotionId,
      int understandingNodeId,
      int problemNodeId)
    {
      if (episodic == null) return 0;

      var candidates = CollectPositiveTeacherActions(episodic.Tree, 0, new[] { baseId, emotionId, understandingNodeId, problemNodeId });
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

    private static List<ExtremImportance> CollectPositiveTeacherActions(
      EpisodicMemoryNode node,
      int level,
      int[] cond)
    {
      var list = new List<ExtremImportance>();
      if (node == null || cond == null || level > 3) return list;

      foreach (var child in node.Children ?? Enumerable.Empty<EpisodicMemoryNode>())
      {
        if (!IsBranchConditionMatch(level, child, cond))
          continue;
        if (level == 3)
          CollectTeacherActionsRecursive(child, list);
        else
          list.AddRange(CollectPositiveTeacherActions(child, level + 1, cond));
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
