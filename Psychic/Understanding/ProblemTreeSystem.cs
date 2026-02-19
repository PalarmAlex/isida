using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Система дерева проблем — аналог BOT understanding_problem_tree
  /// </summary>
  /// <remarks>
  /// Активируется при активации дерева автоматизмов. DetectedActiveLastProblemNodeId
  /// используется в эпизодической памяти как NodePID. При отсутствии полного дерева
  /// понимания — используется упрощённая схема (AutTreeID = DetectedActiveLastNodeId).
  /// </remarks>
  public sealed class ProblemTreeSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed;
    private readonly string _psychicDataPath;

    #region Инициализация

    private static ProblemTreeSystem _instance;

    /// <summary>Глобальный экземпляр системы дерева проблем</summary>
    public static ProblemTreeSystem Instance => _instance ??
        throw new InvalidOperationException("ProblemTreeSystem не инициализирован.");

    /// <summary>Признак инициализации системы</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Инициализирует глобальный экземпляр системы дерева проблем</summary>
    /// <param name="psychicDataPath">Путь к данным психики или null для стандартного</param>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("ProblemTreeSystem уже инициализирован.");

      _instance = new ProblemTreeSystem(psychicDataPath);
    }

    private ProblemTreeSystem(string psychicDataPath = null)
    {
      _psychicDataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Understanding")
          : Path.Combine(psychicDataPath, "Understanding");

      try
      {
        EnsureDataDirectory();
        LoadProblemTree();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Поля

    private const string ProblemTreeFileName = "problem_tree";

    /// <summary>Корневой узел дерева проблем</summary>
    public ProblemTreeNode Tree { get; private set; } = new ProblemTreeNode { ID = 0 };
    private readonly Dictionary<int, ProblemTreeNode> _nodesById = new Dictionary<int, ProblemTreeNode>();
    private int _lastNodeId;

    /// <summary>ID последнего активного узла дерева проблем</summary>
    public int DetectedActiveLastProblemNodeId { get; set; }

    /// <summary>Предыдущее значение (для правил с usedOldCondition)</summary>
    public int OldDetectedActiveLastProblemNodeId { get; set; }

    #endregion

    #region Управление активной веткой

    /// <summary>
    /// Обновить активную ветку по ID узла дерева автоматизмов (упрощённый режим)
    /// </summary>
    public void UpdateActiveBranchFromAutomatizmTree(int automatizmTreeNodeId)
    {
      _lock.EnterWriteLock();
      try
      {
        OldDetectedActiveLastProblemNodeId = DetectedActiveLastProblemNodeId;

        if (automatizmTreeNodeId <= 0)
        {
          DetectedActiveLastProblemNodeId = 0;
          return;
        }

        var (id, _) = FindOrCreateByAutTreeId(automatizmTreeNodeId);
        DetectedActiveLastProblemNodeId = id;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Найти или создать узел по AutTreeID</summary>
    private (int Id, ProblemTreeNode Node) FindOrCreateByAutTreeId(int autTreeId)
    {
      foreach (var child in Tree.Children)
      {
        var found = FindByAutTreeIdRecursive(child, autTreeId);
        if (found.Node != null)
          return found;
      }

      _lastNodeId++;
      var node = new ProblemTreeNode
      {
        ID = _lastNodeId,
        ParentID = 0,
        ParentNode = Tree,
        AutTreeID = autTreeId
      };
      Tree.Children.Add(node);
      _nodesById[node.ID] = node;
      return (node.ID, node);
    }

    private (int Id, ProblemTreeNode Node) FindByAutTreeIdRecursive(ProblemTreeNode n, int autTreeId)
    {
      if (n.AutTreeID == autTreeId)
        return (n.ID, n);
      foreach (var c in n.Children)
      {
        var r = FindByAutTreeIdRecursive(c, autTreeId);
        if (r.Node != null)
          return r;
      }
      return (0, null);
    }

    /// <summary>Получить узел дерева по ID</summary>
    public ProblemTreeNode GetNodeById(int id)
    {
      _lock.EnterReadLock();
      try
      {
        return _nodesById.TryGetValue(id, out var n) ? n : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Load / Save

    private void EnsureDataDirectory()
    {
      if (!Directory.Exists(_psychicDataPath))
        Directory.CreateDirectory(_psychicDataPath);
    }

    private void LoadProblemTree()
    {
      var filePath = Path.Combine(_psychicDataPath, $"{ProblemTreeFileName}.dat");
      if (!File.Exists(filePath) || !FileValidator.IsValidProblemTreeFile(filePath))
      {
        Tree = new ProblemTreeNode { ID = 0 };
        _nodesById.Clear();
        _lastNodeId = 0;
        return;
      }

      Tree = new ProblemTreeNode { ID = 0 };
      _nodesById.Clear();
      _nodesById[0] = Tree;
      _lastNodeId = 0;

      var lineNum = 0;
      foreach (var line in File.ReadLines(filePath))
      {
        lineNum++;
        if (lineNum <= 6) continue;
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;

        var p = t.Split('|');
        if (p.Length < 5) continue;
        if (!int.TryParse(p[0], out int id)) continue;
        if (!int.TryParse(p[1], out int parentId)) continue;
        if (!int.TryParse(p[2], out int autTreeId)) continue;
        if (!int.TryParse(p[3], out int situationId)) continue;
        if (!int.TryParse(p[4], out int themeId)) continue;
        if (!int.TryParse(p.Length > 5 ? p[5] : "0", out int purposeId)) purposeId = 0;

        var parent = parentId == 0 ? Tree : (_nodesById.TryGetValue(parentId, out var pn) ? pn : null);
        if (parent == null) continue;

        var node = new ProblemTreeNode
        {
          ID = id,
          ParentID = parentId,
          ParentNode = parent,
          AutTreeID = autTreeId,
          SituationTreeID = situationId,
          ThemeID = themeId,
          PurposeID = purposeId
        };
        parent.Children.Add(node);
        _nodesById[id] = node;
        if (id > _lastNodeId) _lastNodeId = id;
      }
    }

    /// <summary>Сохранить дерево проблем на диск</summary>
    /// <returns>Успех и сообщение об ошибке при неудаче</returns>
    public (bool Success, string ErrorMessage) SaveProblemTree()
    {
      _lock.EnterReadLock();
      try
      {
        var path = Path.Combine(_psychicDataPath, $"{ProblemTreeFileName}.dat");
        EnsureDataDirectory();
        var lines = new List<string>
        {
          FileValidator.FileHeaders.ProblemTreeFormat,
          FileValidator.FileHeaders.ProblemTreeFields1,
          FileValidator.FileHeaders.ProblemTreeFields2
        };
        foreach (var node in Tree.Children)
          CollectLines(node, lines);

        if (!FileValidator.IsValidProblemTreeFile(lines))
        {
          return (false, "Валидация файла дерева проблем не пройдена");
        }

        File.WriteAllLines(path, lines);
        return (true, null);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    private void CollectLines(ProblemTreeNode n, List<string> lines)
    {
      lines.Add($"{n.ID}|{n.ParentID}|{n.AutTreeID}|{n.SituationTreeID}|{n.ThemeID}|{n.PurposeID}");
      foreach (var c in n.Children)
        CollectLines(c, lines);
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы, сохраняет дерево</summary>
    public void Dispose()
    {
      if (_disposed) return;
      _lock.EnterWriteLock();
      try
      {
        var (ok, err) = SaveProblemTree();
        if (!ok && !string.IsNullOrEmpty(err))
          Logger.Warning($"Ошибка сохранения дерева проблем: {err}");
        _disposed = true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion
  }
}
