using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Ментальная эпизодическая память: дерево контекстов (проблема / тема / цель) с несколькими листьями-цепочками
  /// инфо-функций и усреднённым эффектом. История кадров — отдельный файл.
  /// </summary>
  public sealed class MentalEpisodicTreeSystem : IDisposable
  {
    private const int RootParentId = 0;
    private const string TreeFileName = "mental_episodic_tree.dat";
    private const string HistoryFileName = "mental_episodic_history.dat";

    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly string _dataPath;
    private readonly Dictionary<int, MentalEpisodicNode> _nodes = new Dictionary<int, MentalEpisodicNode>();
    private readonly List<int> _rootChildIds = new List<int>();
    private int _nextId;
    private bool _disposed;

    private static MentalEpisodicTreeSystem _instance;

    /// <summary>Признак инициализации синглтона.</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Глобальный экземпляр.</summary>
    public static MentalEpisodicTreeSystem Instance => _instance ??
        throw new InvalidOperationException("MentalEpisodicTreeSystem не инициализирован.");

    /// <summary>Инициализирует систему загрузкой из каталога данных психики.</summary>
    /// <param name="psychicDataFolder">Корень данных психики (родитель каталога Understanding).</param>
    public static void InitializeInstance(string psychicDataFolder)
    {
      if (_instance != null)
        throw new InvalidOperationException("MentalEpisodicTreeSystem уже инициализирован.");
      _instance = new MentalEpisodicTreeSystem(psychicDataFolder);
    }

    private MentalEpisodicTreeSystem(string psychicDataFolder)
    {
      _dataPath = string.IsNullOrWhiteSpace(psychicDataFolder)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Understanding")
          : Path.Combine(psychicDataFolder, "Understanding");
      EnsureDirectory();
      Load();
      if (_nodes.Count == 0)
        EnsureEmptyRoot();
    }

    /// <summary>
    /// Сохраняет или обновляет эпизод: контекст (NodePID, Theme, Purpose) и цепочка инфо-функций;
    /// при совпадении цепочки с существующим листом — усреднение эффекта.
    /// </summary>
    /// <param name="nodePid">Узел дерева проблем.</param>
    /// <param name="themeId">Образ темы.</param>
    /// <param name="purposeId">Образ цели.</param>
    /// <param name="infoChain">Цепочка инфо-функций.</param>
    /// <param name="effect">Оценка эффекта.</param>
    /// <param name="lastMotorEpisodicNodeId">Последний узел моторной эпизодики (0 — нет связи).</param>
    public void SaveOrUpdate(int nodePid, int themeId, int purposeId, IReadOnlyList<int> infoChain, int effect,
        int lastMotorEpisodicNodeId = 0)
    {
      if (AppGlobalState.EvolutionStage < 4) return;

      var chain = DeduplicateChain(infoChain);
      if ((chain == null || chain.Count == 0) && effect == 0)
        return;

      var lifeTime = Math.Max(1, AppGlobalState.Lifetime);

      _lock.EnterWriteLock();
      try
      {
        var ctxId = GetOrCreateContextNodeLocked(nodePid, themeId, purposeId);
        var existingRuleId = FindRuleChildWithSameChainLocked(ctxId, chain);
        if (existingRuleId > 0 && _nodes.TryGetValue(existingRuleId, out var ruleNode))
        {
          AverageMentalEffect(ruleNode, AddUtils.Clamp(effect, -10, 10));
          AppendHistoryLocked(ruleNode.Id, lifeTime, lastMotorEpisodicNodeId);
          PersistToDiskLocked();
          return;
        }

        _nextId++;
        var newId = _nextId;
        var newRule = new MentalEpisodicNode
        {
          Id = newId,
          ParentId = ctxId,
          IsContextFolder = false,
          NodePid = nodePid,
          ThemeId = themeId,
          PurposeId = purposeId,
          InfoArr = chain != null ? new List<int>(chain) : new List<int>(),
          Effect = AddUtils.Clamp(effect, -10, 10),
          Count = 1
        };
        _nodes[newId] = newRule;
        if (_nodes.TryGetValue(ctxId, out var ctx))
          ctx.ChildIds.Add(newId);

        AppendHistoryLocked(newId, lifeTime, lastMotorEpisodicNodeId);
        PersistToDiskLocked();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Подбор следующей инфо-функции по опыту (GPT-префикс и релаксация контекста).</summary>
    public int? TryResolveNextInfoFunc(int nodePid, int themeId, int purposeId, IReadOnlyList<int> executedPrefix, bool exactOnly)
    {
      if (!IsInitialized || AppGlobalState.EvolutionStage < 4)
        return null;

      _lock.EnterReadLock();
      try
      {
        var gpt = GetNextGptStepLocked(nodePid, themeId, purposeId, executedPrefix);
        if (gpt.HasValue)
          return gpt;
        if (exactOnly)
          return null;
        return GetFavoriteFirstExecutableLocked(nodePid, themeId, purposeId);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Снимок дерева для UI: папки контекста (узел проблемы, тема, цель) и дочерние правила с цепочками ИФ.
    /// Пусто, если система не инициализирована или стадия эволюции ниже 4.
    /// </summary>
    public IReadOnlyList<MentalEpisodicContextSnapshot> GetDisplaySnapshot()
    {
      if (!IsInitialized || AppGlobalState.EvolutionStage < 4)
        return Array.Empty<MentalEpisodicContextSnapshot>();

      _lock.EnterReadLock();
      try
      {
        var result = new List<MentalEpisodicContextSnapshot>();
        foreach (var cid in _rootChildIds)
        {
          if (!_nodes.TryGetValue(cid, out var ctx) || !ctx.IsContextFolder) continue;
          var rules = new List<MentalEpisodicRuleSnapshot>();
          foreach (var rid in ctx.ChildIds)
          {
            if (!_nodes.TryGetValue(rid, out var r) || r.IsContextFolder) continue;
            var ids = r.InfoArr == null || r.InfoArr.Count == 0
              ? (IReadOnlyList<int>)Array.Empty<int>()
              : r.InfoArr.ToList();
            rules.Add(new MentalEpisodicRuleSnapshot(r.Id, ctx.Id, ids, r.Effect, r.Count));
          }
          rules.Sort((a, b) => a.Id.CompareTo(b.Id));
          result.Add(new MentalEpisodicContextSnapshot(ctx.Id, ctx.NodePid, ctx.ThemeId, ctx.PurposeId, rules));
        }
        result.Sort((a, b) =>
        {
          int c = a.NodePid.CompareTo(b.NodePid);
          if (c != 0) return c;
          c = a.ThemeId.CompareTo(b.ThemeId);
          if (c != 0) return c;
          return a.PurposeId.CompareTo(b.PurposeId);
        });
        return result;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Сохраняет данные на диск.</summary>
    public (bool Success, string Error) Save()
    {
      _lock.EnterReadLock();
      try
      {
        return PersistToDiskLocked();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Освобождает ресурсы и сохраняет файл.</summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        var (ok, err) = Save();
        if (!ok && !string.IsNullOrEmpty(err))
          Logger.Warning($"Ошибка сохранения ментальной эпизодики: {err}");
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
      finally
      {
        _disposed = true;
        _lock?.Dispose();
      }
    }

    private void EnsureEmptyRoot()
    {
      if (_rootChildIds.Count > 0 || _nodes.Count > 0)
        return;
      _nextId = 1;
    }

    private void EnsureDirectory()
    {
      if (!string.IsNullOrEmpty(_dataPath) && !Directory.Exists(_dataPath))
        Directory.CreateDirectory(_dataPath);
    }

    private void Load()
    {
      _nodes.Clear();
      _rootChildIds.Clear();
      _nextId = 0;

      var treePath = Path.Combine(_dataPath, TreeFileName);
      if (!File.Exists(treePath) || !FileValidator.IsValidMentalEpisodicTreeFile(treePath))
      {
        LoadHistory();
        return;
      }

      foreach (var raw in File.ReadAllLines(treePath))
      {
        var line = raw?.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
          continue;

        var hashIdx = line.IndexOf("#", StringComparison.Ordinal);
        if (hashIdx < 0) continue;
        var head = line.Substring(0, hashIdx);
        var tail = hashIdx + 1 < line.Length ? line.Substring(hashIdx + 1) : string.Empty;
        var hp = head.Split('|');
        if (hp.Length < 6) continue;
        if (!int.TryParse(hp[0].Trim(), out var id)) continue;
        if (!int.TryParse(hp[1].Trim(), out var parentId)) continue;
        if (!int.TryParse(hp[2].Trim(), out var nodePid)) continue;
        if (!int.TryParse(hp[3].Trim(), out var themeId)) continue;
        if (!int.TryParse(hp[4].Trim(), out var purposeId)) continue;

        var infoStr = hp[5].Trim();
        var infoArr = new List<int>();
        if (!string.IsNullOrEmpty(infoStr))
        {
          foreach (var s in infoStr.Split(','))
          {
            if (int.TryParse(s.Trim(), out var iid) && iid > 0)
              infoArr.Add(iid);
          }
        }

        int effect = 0;
        int count = 0;
        if (!string.IsNullOrEmpty(tail))
        {
          var tp = tail.Split('|');
          if (tp.Length >= 1) int.TryParse(tp[0].Trim(), out effect);
          if (tp.Length >= 2) int.TryParse(tp[1].Trim(), out count);
        }

        var isFolder = infoArr.Count == 0 && count == 0 && effect == 0;
        var node = new MentalEpisodicNode
        {
          Id = id,
          ParentId = parentId,
          IsContextFolder = isFolder,
          NodePid = nodePid,
          ThemeId = themeId,
          PurposeId = purposeId,
          InfoArr = infoArr,
          Effect = effect,
          Count = count
        };
        _nodes[id] = node;
        if (id > _nextId) _nextId = id;

        if (parentId == RootParentId)
          _rootChildIds.Add(id);
      }

      // Восстановить дочерние связи у не-корневых
      foreach (var kv in _nodes.ToList())
      {
        var n = kv.Value;
        if (n.ParentId == RootParentId) continue;
        if (_nodes.TryGetValue(n.ParentId, out var p) && !p.ChildIds.Contains(n.Id))
          p.ChildIds.Add(n.Id);
      }

      LoadHistory();
    }

    private readonly List<MentalHistoryEntry> _history = new List<MentalHistoryEntry>();

    private void LoadHistory()
    {
      _history.Clear();
      var path = Path.Combine(_dataPath, HistoryFileName);
      if (!File.Exists(path) || !FileValidator.IsValidMentalEpisodicHistoryFile(path))
        return;
      foreach (var raw in File.ReadAllLines(path))
      {
        var t = raw?.Trim();
        if (string.IsNullOrEmpty(t) || t.StartsWith("#", StringComparison.Ordinal)) continue;
        var p = t.Split('|');
        if (p.Length < 3) continue;
        if (!int.TryParse(p[0].Trim(), out var mentalId)) continue;
        if (!int.TryParse(p[1].Trim(), out var lifeTime)) continue;
        if (!int.TryParse(p[2].Trim(), out var motorId)) continue;
        _history.Add(new MentalHistoryEntry { MentalRuleNodeId = mentalId, LifeTime = lifeTime, LastEpisodicNodeId = motorId });
      }
    }

    private int GetOrCreateContextNodeLocked(int nodePid, int themeId, int purposeId)
    {
      foreach (var cid in _rootChildIds)
      {
        if (!_nodes.TryGetValue(cid, out var n)) continue;
        if (!n.IsContextFolder) continue;
        if (n.NodePid == nodePid && n.ThemeId == themeId && n.PurposeId == purposeId)
          return cid;
      }

      if (_nextId < 1) _nextId = 1;
      _nextId++;
      var newId = _nextId;
      var ctx = new MentalEpisodicNode
      {
        Id = newId,
        ParentId = RootParentId,
        IsContextFolder = true,
        NodePid = nodePid,
        ThemeId = themeId,
        PurposeId = purposeId,
        InfoArr = new List<int>(),
        Effect = 0,
        Count = 0
      };
      _nodes[newId] = ctx;
      _rootChildIds.Add(newId);
      return newId;
    }

    private int FindRuleChildWithSameChainLocked(int contextId, List<int> chain)
    {
      if (!_nodes.TryGetValue(contextId, out var ctx) || chain == null)
        return 0;
      foreach (var rid in ctx.ChildIds)
      {
        if (!_nodes.TryGetValue(rid, out var r) || r.IsContextFolder) continue;
        if (r.InfoArr != null && r.InfoArr.Count == chain.Count)
        {
          var ok = true;
          for (var i = 0; i < chain.Count; i++)
          {
            if (r.InfoArr[i] != chain[i]) { ok = false; break; }
          }
          if (ok) return rid;
        }
      }
      return 0;
    }

    private static List<int> DeduplicateChain(IReadOnlyList<int> chain)
    {
      var result = new List<int>();
      if (chain == null) return result;
      var seen = new HashSet<int>();
      foreach (var id in chain)
      {
        if (id <= 0) continue;
        if (seen.Add(id))
          result.Add(id);
      }
      return result;
    }

    private static void AverageMentalEffect(MentalEpisodicNode node, int effect)
    {
      var count = node.Count;
      if (count <= 0)
      {
        node.Effect = AddUtils.Clamp(effect, -10, 10);
        node.Count = 1;
        return;
      }

      var w = effect + (int)(effect / (count + 1));
      if (w > 10) w = 10;
      if (w < -10) w = -10;
      node.Effect = w;
      node.Count = count + 1;
    }

    private void AppendHistoryLocked(int mentalRuleId, int lifeTime, int lastMotorEpisodicNodeId)
    {
      _history.Add(new MentalHistoryEntry
      {
        MentalRuleNodeId = mentalRuleId,
        LifeTime = lifeTime,
        LastEpisodicNodeId = lastMotorEpisodicNodeId
      });
    }

    private int? GetNextGptStepLocked(int nodePid, int themeId, int purposeId, IReadOnlyList<int> executedPrefix)
    {
      var ctxId = FindContextNodeReadOnly(nodePid, themeId, purposeId);
      if (ctxId <= 0) return null;

      MentalEpisodicNode bestRule = null;
      var bestScore = -1;
      foreach (var rid in _nodes[ctxId].ChildIds)
      {
        if (!_nodes.TryGetValue(rid, out var r) || r.IsContextFolder) continue;
        if (r.Effect <= 0) continue;
        if (r.InfoArr == null || r.InfoArr.Count == 0) continue;
        if (!PrefixMatches(r.InfoArr, executedPrefix)) continue;
        var prefLen = executedPrefix?.Count ?? 0;
        if (prefLen > r.InfoArr.Count) continue;
        var score = r.InfoArr.Count * 1000 + r.Count * 10 + r.Effect;
        if (score > bestScore)
        {
          bestScore = score;
          bestRule = r;
        }
      }
      if (bestRule == null) return null;

      var arr = bestRule.InfoArr;
      if (executedPrefix == null || executedPrefix.Count == 0)
      {
        foreach (var id in arr)
        {
          if (!MentalInfoFuncIds.IsAuxiliary(id))
            return id;
        }
        return null;
      }

      if (executedPrefix.Count > arr.Count) return null;
      for (var i = 0; i < executedPrefix.Count; i++)
      {
        if (executedPrefix[i] != arr[i])
          return null;
      }

      if (executedPrefix.Count == arr.Count) return null;
      var next = arr[executedPrefix.Count];
      return MentalInfoFuncIds.IsAuxiliary(next) ? (int?)null : next;
    }

    private static bool PrefixMatches(IReadOnlyList<int> arr, IReadOnlyList<int> prefix)
    {
      if (prefix == null || prefix.Count == 0) return true;
      if (arr == null || arr.Count < prefix.Count) return false;
      for (var i = 0; i < prefix.Count; i++)
      {
        if (arr[i] != prefix[i]) return false;
      }
      return true;
    }

    private int FindContextNodeReadOnly(int nodePid, int themeId, int purposeId)
    {
      foreach (var cid in _rootChildIds)
      {
        if (!_nodes.TryGetValue(cid, out var n)) continue;
        if (!n.IsContextFolder) continue;
        if (n.NodePid == nodePid && n.ThemeId == themeId && n.PurposeId == purposeId)
          return cid;
      }
      return 0;
    }

    private int? GetFavoriteFirstExecutableLocked(int nodePid, int themeId, int purposeId)
    {
      foreach (var rule in BuildFavoriteRulesLocked(nodePid, themeId, purposeId))
      {
        if (rule.Count <= 1) continue;
        if (rule.InfoArr == null) continue;
        foreach (var id in rule.InfoArr)
        {
          if (!MentalInfoFuncIds.IsAuxiliary(id))
            return id;
        }
      }
      return null;
    }

    private List<MentalEpisodicNode> BuildFavoriteRulesLocked(int nodePid, int themeId, int purposeId)
    {
      bool Pos(MentalEpisodicNode n) => !n.IsContextFolder && n.Effect > 0;

      List<MentalEpisodicNode> CollectForContext(int np, int th, int pr)
      {
        var ctxId = FindContextNodeReadOnly(np, th, pr);
        var r = new List<MentalEpisodicNode>();
        if (ctxId <= 0 || !_nodes.TryGetValue(ctxId, out var ctxNode)) return r;
        foreach (var rid in ctxNode.ChildIds)
        {
          if (_nodes.TryGetValue(rid, out var node) && Pos(node))
            r.Add(node);
        }
        return r;
      }

      var a = CollectForContext(nodePid, themeId, purposeId);
      if (a.Count > 0) return a;
      var b = CollectForContext(nodePid, themeId, 0);
      if (b.Count > 0) return b;
      var cList = CollectForContext(nodePid, 0, 0);
      if (cList.Count > 0) return cList;
      if (purposeId > 0)
      {
        var relaxed = new List<MentalEpisodicNode>();
        foreach (var cid in _rootChildIds)
        {
          if (!_nodes.TryGetValue(cid, out var ctx) || !ctx.IsContextFolder) continue;
          if (ctx.PurposeId != purposeId) continue;
          foreach (var rid in ctx.ChildIds)
          {
            if (_nodes.TryGetValue(rid, out var node) && Pos(node))
              relaxed.Add(node);
          }
        }
        if (relaxed.Count > 0) return relaxed;
      }
      return new List<MentalEpisodicNode>();
    }

    private (bool Success, string Error) PersistToDiskLocked()
    {
      try
      {
        EnsureDirectory();
        var treePath = Path.Combine(_dataPath, TreeFileName);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.MentalEpisodicTreeFormat,
          FileValidator.FileHeaders.MentalEpisodicTreeDesc
        };
        foreach (var n in _nodes.Values.OrderBy(x => x.Id))
        {
          var infos = n.InfoArr == null || n.InfoArr.Count == 0
              ? string.Empty
              : string.Join(",", n.InfoArr);
          lines.Add($"{n.Id}|{n.ParentId}|{n.NodePid}|{n.ThemeId}|{n.PurposeId}|{infos}#{n.Effect}|{n.Count}");
        }

        var (ok, err) = FileValidator.SafeSaveFile(
            treePath,
            lines,
            p => FileValidator.IsValidMentalEpisodicTreeFile(p),
            minLinesCount: 2,
            fileDescription: "ментальной эпизодической памяти (дерево)");
        if (!ok) return (false, err);

        var hLines = new List<string>
        {
          FileValidator.FileHeaders.MentalEpisodicHistoryFormat,
          FileValidator.FileHeaders.MentalEpisodicHistoryDesc
        };
        foreach (var h in _history.OrderBy(x => x.LifeTime).ThenBy(x => x.MentalRuleNodeId))
          hLines.Add($"{h.MentalRuleNodeId}|{h.LifeTime}|{h.LastEpisodicNodeId}");

        var (ok2, err2) = FileValidator.SafeSaveFile(
            Path.Combine(_dataPath, HistoryFileName),
            hLines,
            p => FileValidator.IsValidMentalEpisodicHistoryFile(p),
            minLinesCount: 2,
            fileDescription: "ментальной эпизодической истории");
        return ok2 ? (true, null) : (false, err2);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    private sealed class MentalEpisodicNode
    {
      public int Id { get; set; }
      public int ParentId { get; set; }
      public bool IsContextFolder { get; set; }
      public int NodePid { get; set; }
      public int ThemeId { get; set; }
      public int PurposeId { get; set; }
      public List<int> ChildIds { get; } = new List<int>();
      public List<int> InfoArr { get; set; }
      public int Effect { get; set; }
      public int Count { get; set; }
    }

    private sealed class MentalHistoryEntry
    {
      public int MentalRuleNodeId { get; set; }
      public int LifeTime { get; set; }
      public int LastEpisodicNodeId { get; set; }
    }

    private static class MentalInfoFuncIds
    {
      public static bool IsAuxiliary(int id)
      {
        return id == 1 || id == 2 || id == 5 || id == 8 || id == 14 || id == 17 || id == 26;
      }
    }
  }

  /// <summary>Снимок сохранённого правила (листа дерева): цепочка инфо-функций и агрегированный эффект после оценок решения.</summary>
  public sealed class MentalEpisodicRuleSnapshot
  {
    /// <summary>Создаёт снимок правила для UI или сериализации.</summary>
    /// <param name="id">Идентификатор узла правила в файле mental_episodic_tree.dat.</param>
    /// <param name="parentContextId">Идентификатор родительской папки контекста.</param>
    /// <param name="infoFuncIds">Последовательность идентификаторов инфо-функций.</param>
    /// <param name="effect">Усреднённая оценка полезности (-10…10).</param>
    /// <param name="count">Число усреднений (накопленных совпадений цепочки).</param>
    public MentalEpisodicRuleSnapshot(int id, int parentContextId, IReadOnlyList<int> infoFuncIds, int effect, int count)
    {
      Id = id;
      ParentContextId = parentContextId;
      InfoFuncIds = infoFuncIds ?? Array.Empty<int>();
      Effect = effect;
      Count = count;
    }

    /// <summary>Идентификатор узла правила в дереве ментальной эпизодики.</summary>
    public int Id { get; }
    /// <summary>Идентификатор родительского узла-контекста (папка проблема/тема/цель).</summary>
    public int ParentContextId { get; }
    /// <summary>Цепочка вызванных инфо-функций (номера по справочнику).</summary>
    public IReadOnlyList<int> InfoFuncIds { get; }
    /// <summary>Агрегированная оценка эффекта решения цикла для этой цепочки.</summary>
    public int Effect { get; }
    /// <summary>Число записей, участвовавших в усреднении эффекта.</summary>
    public int Count { get; }
  }

  /// <summary>Снимок папки контекста (узел дерева проблем + тема + цель) и дочерних правил с цепочками ИФ.</summary>
  public sealed class MentalEpisodicContextSnapshot
  {
    /// <summary>Создаёт снимок контекста и списка правил.</summary>
    /// <param name="id">Идентификатор узла-контекста в файле.</param>
    /// <param name="nodePid">Узел дерева проблем (ProblemTree).</param>
    /// <param name="themeId">Идентификатор образа темы.</param>
    /// <param name="purposeId">Идентификатор образа цели.</param>
    /// <param name="rules">Дочерние правила (цепочки ИФ с эффектом).</param>
    public MentalEpisodicContextSnapshot(int id, int nodePid, int themeId, int purposeId, IReadOnlyList<MentalEpisodicRuleSnapshot> rules)
    {
      Id = id;
      NodePid = nodePid;
      ThemeId = themeId;
      PurposeId = purposeId;
      Rules = rules ?? Array.Empty<MentalEpisodicRuleSnapshot>();
    }

    /// <summary>Идентификатор узла-контекста в дереве ментальной эпизодики.</summary>
    public int Id { get; }
    /// <summary>Узел дерева проблем (NodePID в смысле контекста мышления).</summary>
    public int NodePid { get; }
    /// <summary>Идентификатор образа темы.</summary>
    public int ThemeId { get; }
    /// <summary>Идентификатор образа цели.</summary>
    public int PurposeId { get; }
    /// <summary>Правила, сохранённые под данным контекстом.</summary>
    public IReadOnlyList<MentalEpisodicRuleSnapshot> Rules { get; }
  }
}
