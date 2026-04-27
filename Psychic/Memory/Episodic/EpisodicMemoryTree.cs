using ISIDA.Common;

namespace ISIDA.Psychic.Memory.Episodic
{
  /// <summary>
  /// Логика дерева эпизодической памяти: поиск, доращивание веток, усреднение.
  /// Ключ ветки: BaseID, EmotionID, UnderstandingNodeId, NodePID, TriggerId, ActionId (6 уровней; лист с Params на последнем).
  /// Правило проекта: Состояние (BaseID), Тон, Настроение (EmotionID) имеют рабочий код 0 — не считать 0 отсутствием параметра (см. .cursor/rules/isida-tree-zero-valid-params.mdc).
  /// </summary>
  public class EpisodicMemoryTree
  {
    /// <summary>Число полей условия в пути от корня к листу (без учёта Params)</summary>
    public const int BranchKeyLength = 6;

    /// <summary>Индекс уровня листа с Params (0-based)</summary>
    public const int LeafLevelIndex = BranchKeyLength - 1;

    private int _lastNodeId = 1;

    /// <summary>Проверить наличие ветки по условиям, вернуть (id, node) или (0, null)</summary>
    public (int Id, EpisodicMemoryNode Node) CheckBranchFromCondition(
        EpisodicMemoryNode root,
        int baseId,
        int emotionId,
        int understandingNodeId,
        int nodePid,
        int triggerId,
        int actionId)
    {
      var cond = new[] { baseId, emotionId, understandingNodeId, nodePid, triggerId, actionId };
      var (id, level) = FindBranch(0, cond, root);
      if (id <= 0) return (0, null);

      var lev0 = GetTrueLevel(cond);
      if (lev0 > level) return (0, null);

      var node = FindNodeById(root, id);
      return node != null ? (id, node) : (0, null);
    }

    /// <summary>Найти ветку по условиям</summary>
    public (int Id, int Level) FindBranch(int level, int[] cond, EpisodicMemoryNode root)
    {
      if (cond == null || cond.Length == 0 || level >= cond.Length)
        return (root?.ID ?? 0, cond?.Length ?? 0);

      foreach (var child in root.Children)
      {
        if (!IsEquivalentCondition(level, child, cond))
          continue;
        var (id, lev) = FindBranch(level + 1, cond, child);
        if (id == 0)
          return (child.ID, lev);
        return (id, lev);
      }
      return (0, level);
    }

    private static bool IsEquivalentCondition(int level, EpisodicMemoryNode node, int[] cond)
    {
      var arr = new[]
      {
        node.BaseID,
        node.EmotionID,
        node.UnderstandingNodeId,
        node.NodePID,
        node.TriggerId,
        node.ActionId
      };
      for (int i = 0; i < cond.Length && i <= level; i++)
      {
        if (cond[i] != arr[i])
          return false;
      }
      return true;
    }

    /// <summary>Индекс первого нуля в cond (для CheckBranchFromCondition). Учитывать: BaseID=0 и EmotionID=0 — рабочие коды (Норма/нейтральная эмоция), не «параметр отсутствует».</summary>
    private static int GetTrueLevel(int[] cond)
    {
      int lev = 0;
      for (int i = 0; i < cond.Length; i++)
      {
        if (cond[i] == 0) break;
        lev++;
      }
      return lev;
    }

    /// <summary>Найти среди детей родителя узел с заданным ключом уровня.</summary>
    private static EpisodicMemoryNode FindChildByLevelKey(EpisodicMemoryNode parent, int level, int[] condArr)
    {
      if (parent?.Children == null || level < 0 || level >= condArr.Length) return null;
      int key = level < condArr.Length ? condArr[level] : 0;
      foreach (var c in parent.Children)
      {
        int nodeKey;
        switch (level)
        {
          case 0: nodeKey = c.BaseID; break;
          case 1: nodeKey = c.EmotionID; break;
          case 2: nodeKey = c.UnderstandingNodeId; break;
          case 3: nodeKey = c.NodePID; break;
          case 4: nodeKey = c.TriggerId; break;
          case 5: nodeKey = c.ActionId; break;
          default: nodeKey = 0; break;
        }
        if (nodeKey == key) return c;
      }
      return null;
    }

    /// <summary>Добавить/дорастить ветку</summary>
    public int AddBranch(
        EpisodicMemoryNode fromNode,
        int level,
        int[] condArr,
        EpisodicParams newParams)
    {
      if (fromNode == null || level >= condArr.Length)
        return fromNode?.ID ?? 0;

      var vArr = new int[BranchKeyLength];
      for (int i = 0; i <= level && i < BranchKeyLength; i++)
        vArr[i] = condArr[i];

      var (idOld, nodeOld) = CheckBranchFromCondition(
          fromNode,
          vArr[0], vArr[1], vArr[2], vArr[3], vArr[4], vArr[5]);

      bool nodeMatchesLevel = nodeOld != null &&
          nodeOld.BaseID == vArr[0] && nodeOld.EmotionID == vArr[1] && nodeOld.UnderstandingNodeId == vArr[2] &&
          nodeOld.NodePID == vArr[3] && nodeOld.TriggerId == vArr[4] && nodeOld.ActionId == vArr[5];

      EpisodicMemoryNode node;
      if (idOld > 0 && nodeOld != null && nodeMatchesLevel)
      {
        node = nodeOld;
        if (level == LeafLevelIndex && newParams != null)
          AverageEffect(node, newParams.Effect, newParams.StimulsEffect, newParams.IsTeacher);
      }
      else
      {
        node = FindChildByLevelKey(fromNode, level, condArr);
        if (node == null)
        {
          EpisodicParams pars = level == LeafLevelIndex ? newParams : null;
          if (pars != null && !pars.IsTeacher)
            pars.Effect = AddUtils.Clamp(pars.Effect, -10, 10);

          _lastNodeId++;
          node = new EpisodicMemoryNode
          {
            ID = _lastNodeId,
            ParentID = fromNode.ID,
            ParentNode = fromNode,
            BaseID = vArr[0],
            EmotionID = vArr[1],
            UnderstandingNodeId = vArr[2],
            NodePID = vArr[3],
            TriggerId = vArr[4],
            ActionId = vArr[5],
            Params = pars
          };
          fromNode.Children.Add(node);
        }
        else if (level == LeafLevelIndex && newParams != null)
        {
          AverageEffect(node, newParams.Effect, newParams.StimulsEffect, newParams.IsTeacher);
        }
      }

      level++;
      return level >= condArr.Length ? node.ID : AddBranch(node, level, condArr, newParams);
    }

    /// <summary>Усреднить эффект при повторной записи</summary>
    public void AverageEffect(EpisodicMemoryNode node, int effect, int stimulsEffect, bool isTeacher)
    {
      if (node?.Params == null) return;
      var p = node.Params;
      int count = p.Count + 1;
      if (count == 0) return;

      if (isTeacher)
        p.IsTeacher = true;

      if (!p.IsTeacher)
      {
        int w = (p.Effect * (count - 1) + effect) / count;
        p.Effect = AddUtils.Clamp(w, -10, 10);
      }
      else
        p.Effect = 0;

      int sw = (p.StimulsEffect * (count - 1) + stimulsEffect) / count;
      p.StimulsEffect = AddUtils.Clamp(sw, -10, 10);
      p.Count = count;
    }

    /// <summary>Получить параметры узла по ID</summary>
    public EpisodicParams GetParams(EpisodicMemoryNode root, int nodeId)
    {
      var node = FindNodeById(root, nodeId);
      if (node?.ActionId == 0) return null;
      return node?.Params;
    }

    /// <summary>Найти узел по ID в дереве</summary>
    public EpisodicMemoryNode FindNodeById(EpisodicMemoryNode root, int id)
    {
      if (root == null) return null;
      if (root.ID == id) return root;
      foreach (var c in root.Children)
      {
        var found = FindNodeById(c, id);
        if (found != null) return found;
      }
      return null;
    }

    /// <summary>Установить последний использованный ID узла</summary>
    public void SetLastNodeId(int id)
    {
      if (id > _lastNodeId) _lastNodeId = id;
    }
  }
}
