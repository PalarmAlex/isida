using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ISIDA.Psychic.Memory.Episodic
{
  /// <summary>
  /// Сохранение и загрузка дерева и истории эпизодической памяти
  /// </summary>
  public static class EpisodicMemoryStorage
  {
    private const string EpisodicTreeFileName = "episodic_tree";
    private const string EpisodicHistoryFileName = "episodic_history";

    /// <summary>Путь к файлу дерева эпизодов</summary>
    public static string GetTreeFilePath(string basePath)
    {
      return Path.Combine(basePath, $"{EpisodicTreeFileName}.dat");
    }

    /// <summary>Путь к файлу истории эпизодов</summary>
    public static string GetHistoryFilePath(string basePath)
    {
      return Path.Combine(basePath, $"{EpisodicHistoryFileName}.dat");
    }

    /// <summary>Создать директорию при отсутствии</summary>
    public static void EnsureDirectory(string path)
    {
      if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
        Directory.CreateDirectory(path);
    }

    /// <summary>Загрузить дерево эпизодов из файла</summary>
    public static void LoadEpisodicTree(string basePath, EpisodicMemoryNode root, Dictionary<int, EpisodicMemoryNode> nodesById, ref int lastNodeId)
    {
      var filePath = GetTreeFilePath(basePath);
      if (!File.Exists(filePath) || !FileValidator.IsValidEpisodicTreeFile(filePath))
      {
        root.Children.Clear();
        nodesById.Clear();
        lastNodeId = 1;
        return;
      }

      root.Children.Clear();
      nodesById.Clear();
      nodesById[0] = root;
      lastNodeId = 1;

      int lineNum = 0;
      foreach (var line in File.ReadLines(filePath))
      {
        lineNum++;
        if (lineNum <= 11) continue;
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;

        var sharp = t.Split('#');
        var p = sharp[0].Split('|');
        if (p.Length < 7) continue;

        if (!int.TryParse(p[0], out int id)) continue;
        if (!int.TryParse(p[1], out int parentId)) continue;
        if (!int.TryParse(p[2], out int baseId)) continue;
        if (!int.TryParse(p[3], out int emotionId)) continue;
        if (!int.TryParse(p[4], out int nodePid)) continue;
        if (!int.TryParse(p[5], out int triggerId)) continue;
        if (!int.TryParse(p[6], out int actionId)) continue;

        EpisodicParams pars = null;
        if (actionId > 0 && sharp.Length > 1)
        {
          var parr = sharp[1].Split('|');
          if (parr.Length >= 3 &&
              int.TryParse(parr[0], out int eff) &&
              int.TryParse(parr[1], out int count) &&
              int.TryParse(parr[2], out int stimEff))
          {
            pars = new EpisodicParams { Effect = eff, Count = count, StimulsEffect = stimEff };
          }
        }

        // Родитель: из файла или при «плоском» файле (ParentID=0 у всех) — восстановить по иерархии условий
        EpisodicMemoryNode parent = null;
        if (parentId == 0)
        {
          if (IsRootLevelEpisodicNode(baseId, emotionId, nodePid, triggerId, actionId))
            parent = root;
          else
            parent = FindEpisodicParentByCondition(nodesById, baseId, emotionId, nodePid, triggerId, actionId);
        }
        else if (nodesById.TryGetValue(parentId, out var pn))
        {
          parent = pn;
        }
        if (parent == null)
          continue;

        var node = new EpisodicMemoryNode
        {
          ID = id,
          ParentID = parentId,
          ParentNode = parent,
          BaseID = baseId,
          EmotionID = emotionId,
          NodePID = nodePid,
          TriggerId = triggerId,
          ActionId = actionId,
          Params = pars
        };
        parent.Children.Add(node);
        nodesById[id] = node;
        if (id > lastNodeId) lastNodeId = id;
      }
    }

    /// <summary>Загрузить историю эпизодов из файла</summary>
    public static void LoadEpisodicHistory(string basePath, EpisodicMemoryHistory history)
    {
      var filePath = GetHistoryFilePath(basePath);
      if (!File.Exists(filePath))
      {
        history.Clear();
        return;
      }

      try
      {
        var lines = File.ReadAllLines(filePath);
        var entries = new List<EpisodicHistoryEntry>();
        foreach (var line in lines)
        {
          var t = line?.Trim();
          if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
          var parts = t.Split('|');
          foreach (var part in parts)
          {
            var s = part?.Trim();
            if (string.IsNullOrWhiteSpace(s)) continue;
            var p = s.Split(',');
            if (p.Length < 2) continue;
            if (!int.TryParse(p[0], out int nodeId)) continue;
            if (!int.TryParse(p[1], out int lifeTime)) continue;
            entries.Add(new EpisodicHistoryEntry { NodeId = nodeId, LifeTime = lifeTime });
          }
        }
        history.LoadFromEntries(entries);
      }
      catch (Exception ex)
      {
        Logger.Warning($"Ошибка загрузки истории эпизодов: {ex.Message}");
        history.Clear();
      }
    }

    /// <summary>Сохранить дерево эпизодов в файл</summary>
    public static (bool Success, string Error) SaveEpisodicTree(string basePath, EpisodicMemoryNode root)
    {
      try
      {
        EnsureDirectory(basePath);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.EpisodicTreeFormat,
          FileValidator.FileHeaders.EpisodicTreeId,
          FileValidator.FileHeaders.EpisodicTreeParentId,
          FileValidator.FileHeaders.EpisodicTreeBaseId,
          FileValidator.FileHeaders.EpisodicTreeEmotionId,
          FileValidator.FileHeaders.EpisodicTreeNodePid,
          FileValidator.FileHeaders.EpisodicTreeTriggerId,
          FileValidator.FileHeaders.EpisodicTreeActionId,
          FileValidator.FileHeaders.EpisodicTreeEffect,
          FileValidator.FileHeaders.EpisodicTreeCount,
          FileValidator.FileHeaders.EpisodicTreeStimulsEffect
        };
        CollectTreeLines(root, lines);

        var path = GetTreeFilePath(basePath);
        var result = FileValidator.SafeSaveFile(
            path,
            lines,
            FileValidator.IsValidEpisodicTreeFile,
            minLinesCount: 11,
            fileDescription: "дерева эпизодической памяти");

        return (result.Success, result.ErrorMessage);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    private static void CollectTreeLines(EpisodicMemoryNode n, List<string> lines)
    {
      foreach (var c in n.Children)
      {
        var line = $"{c.ID}|{c.ParentID}|{c.BaseID}|{c.EmotionID}|{c.NodePID}|{c.TriggerId}|{c.ActionId}";
        if (c.Params != null && c.ActionId > 0)
          line += $"#{c.Params.Effect}|{c.Params.Count}|{c.Params.StimulsEffect}";
        lines.Add(line);
        CollectTreeLines(c, lines);
      }
    }

    /// <summary>Узел корневого уровня: задан только BaseID (остальные 0)</summary>
    private static bool IsRootLevelEpisodicNode(int baseId, int emotionId, int nodePid, int triggerId, int actionId)
    {
      return emotionId == 0 && nodePid == 0 && triggerId == 0 && actionId == 0;
    }

    /// <summary>Родительский узел по условиям (на один уровень иерархии выше)</summary>
    private static EpisodicMemoryNode FindEpisodicParentByCondition(
        Dictionary<int, EpisodicMemoryNode> nodesById,
        int baseId, int emotionId, int nodePid, int triggerId, int actionId)
    {
      var (pBase, pEmo, pPid, pTrig, pAct) = GetEpisodicParentCondition(baseId, emotionId, nodePid, triggerId, actionId);
      foreach (var node in nodesById.Values)
      {
        if (node.BaseID == pBase && node.EmotionID == pEmo && node.NodePID == pPid && node.TriggerId == pTrig && node.ActionId == pAct)
          return node;
      }
      return null;
    }

    private static (int BaseID, int EmotionID, int NodePID, int TriggerId, int ActionId) GetEpisodicParentCondition(
        int baseId, int emotionId, int nodePid, int triggerId, int actionId)
    {
      if (actionId != 0) return (baseId, emotionId, nodePid, triggerId, 0);
      if (triggerId != 0) return (baseId, emotionId, nodePid, 0, 0);
      if (nodePid != 0) return (baseId, emotionId, 0, 0, 0);
      if (emotionId != 0) return (baseId, 0, 0, 0, 0);
      return (baseId, 0, 0, 0, 0);
    }

    /// <summary>Сохранить историю эпизодов в файл</summary>
    public static (bool Success, string Error) SaveEpisodicHistory(string basePath, EpisodicMemoryHistory history)
    {
      try
      {
        EnsureDirectory(basePath);
        var path = GetHistoryFilePath(basePath);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.EpisodicHistoryFormat
        };

        var sb = new System.Text.StringBuilder();
        foreach (var e in history.Entries)
        {
          if (sb.Length > 0) sb.Append('|');
          sb.Append(e.NodeId).Append(',').Append(e.LifeTime);
        }

        if (sb.Length > 0)
          lines.Add(sb.ToString());

        var result = FileValidator.SafeSaveFile(
            path,
            lines,
            FileValidator.IsValidEpisodicHistoryFile,
            minLinesCount: 1,
            fileDescription: "истории эпизодической памяти");

        return (result.Success, result.ErrorMessage);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }
  }
}
