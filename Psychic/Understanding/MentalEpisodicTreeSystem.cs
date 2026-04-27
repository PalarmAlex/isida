using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Ментальная эпизодическая память: цепочки инфо-функций по контексту (узел проблемы, тема, цель) и усреднённый эффект (аналог дерева BOT <c>EpisodicMentalTree</c> в уплощённом виде одного листа на контекст).
  /// </summary>
  public sealed class MentalEpisodicTreeSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly string _dataPath;
    private readonly Dictionary<MentalTriple, MentalEpisodicLeaf> _leafByTriple = new Dictionary<MentalTriple, MentalEpisodicLeaf>();
    private readonly List<MentalEpisodicLeaf> _leaves = new List<MentalEpisodicLeaf>();
    private int _lastId;
    private bool _disposed;

    private const string FileName = "mental_episodic_tree.dat";

    private static MentalEpisodicTreeSystem _instance;

    /// <summary>Признак инициализации синглтона.</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Глобальный экземпляр.</summary>
    /// <returns>Система ментальной эпизодики.</returns>
    /// <exception cref="InvalidOperationException">Синглтон не создан.</exception>
    public static MentalEpisodicTreeSystem Instance => _instance ??
        throw new InvalidOperationException("MentalEpisodicTreeSystem не инициализирован.");

    /// <summary>Инициализирует систему загрузкой из каталога данных психики.</summary>
    /// <param name="psychicDataFolder">Корень данных психики (родитель каталога Understanding).</param>
    /// <exception cref="InvalidOperationException">Повторная инициализация.</exception>
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
    }

    /// <summary>
    /// Сохраняет или обновляет эпизод для полного контекста: усредняет эффект по тем же правилам, что <c>averageMentalEffect</c> в BOT.
    /// </summary>
    /// <param name="nodePid">Идентификатор узла дерева проблем (активная ветка).</param>
    /// <param name="themeId">Идентификатор образа темы мышления.</param>
    /// <param name="purposeId">Идентификатор образа цели.</param>
    /// <param name="infoChain">Цепочка номеров инфо-функций за эпизод.</param>
    /// <param name="effect">Оценка эффекта в диапазоне (как правило после оценки полезности автоматизма).</param>
    public void SaveOrUpdate(int nodePid, int themeId, int purposeId, IReadOnlyList<int> infoChain, int effect)
    {
      if (AppGlobalState.EvolutionStage < 4) return;

      var chain = DeduplicateChain(infoChain);
      if ((chain == null || chain.Count == 0) && effect == 0)
        return;

      _lock.EnterWriteLock();
      try
      {
        var key = new MentalTriple(nodePid, themeId, purposeId);
        if (!_leafByTriple.TryGetValue(key, out var leaf))
        {
          _lastId++;
          leaf = new MentalEpisodicLeaf
          {
            Id = _lastId,
            NodePid = nodePid,
            ThemeId = themeId,
            PurposeId = purposeId,
            InfoArr = new List<int>(),
            Effect = 0,
            Count = 0
          };
          _leafByTriple[key] = leaf;
          _leaves.Add(leaf);
        }

        if (leaf.InfoArr.Count == 0 && chain != null && chain.Count > 0)
          leaf.InfoArr = new List<int>(chain);

        AverageMentalEffect(leaf, AddUtils.Clamp(effect, -10, 10));
        PersistToDisk();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Подбор следующей инфо-функции по опыту: сначала точное продолжение префикса (позитивные эпизоды, тот же контекст), затем первая «привычная» цепочка с Count &gt; 1 (с релаксацией контекста как в BOT).
    /// </summary>
    /// <param name="nodePid">Узел дерева проблем.</param>
    /// <param name="themeId">Образ темы.</param>
    /// <param name="purposeId">Образ цели.</param>
    /// <param name="executedPrefix">Уже выполненный префикс цепочки в текущем эпизоде.</param>
    /// <param name="exactOnly">Если true — не использовать шаг «любимой» цепочки.</param>
    /// <returns>Следующий номер инфо-функции или null.</returns>
    public int? TryResolveNextInfoFunc(int nodePid, int themeId, int purposeId, IReadOnlyList<int> executedPrefix, bool exactOnly)
    {
      if (!IsInitialized || AppGlobalState.EvolutionStage < 4)
        return null;

      _lock.EnterReadLock();
      try
      {
        var gpt = GetNextGptStep(nodePid, themeId, purposeId, executedPrefix);
        if (gpt.HasValue)
          return gpt;
        if (exactOnly)
          return null;

        return GetFavoriteFirstExecutable(nodePid, themeId, purposeId);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Освобождает ресурсы и сохраняет файл.
    /// </summary>
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

    /// <summary>Сохраняет данные на диск.</summary>
    /// <returns>Успех и текст ошибки.</returns>
    public (bool Success, string Error) Save()
    {
      _lock.EnterReadLock();
      try
      {
        return PersistToDisk();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Запись файла без захвата блокировки (вызывать под уже удерживаемой блокировкой чтения или записи).</summary>
    /// <returns>Успех и текст ошибки.</returns>
    private (bool Success, string Error) PersistToDisk()
    {
      try
      {
        EnsureDirectory();
        var path = Path.Combine(_dataPath, FileName);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.MentalEpisodicTreeFormat,
          FileValidator.FileHeaders.MentalEpisodicTreeDesc
        };
        foreach (var leaf in _leaves.OrderBy(l => l.Id))
        {
          var infos = leaf.InfoArr == null || leaf.InfoArr.Count == 0
              ? string.Empty
              : string.Join(",", leaf.InfoArr);
          var line = $"{leaf.Id}|{leaf.NodePid}|{leaf.ThemeId}|{leaf.PurposeId}|{infos}#{leaf.Effect}|{leaf.Count}";
          lines.Add(line);
        }

        return FileValidator.SafeSaveFile(
            path,
            lines,
            p => FileValidator.IsValidMentalEpisodicTreeFile(p),
            minLinesCount: 2,
            fileDescription: "ментальной эпизодической памяти");
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    private void EnsureDirectory()
    {
      if (!string.IsNullOrEmpty(_dataPath) && !Directory.Exists(_dataPath))
        Directory.CreateDirectory(_dataPath);
    }

    private void Load()
    {
      var path = Path.Combine(_dataPath, FileName);
      _leafByTriple.Clear();
      _leaves.Clear();
      _lastId = 0;
      if (!File.Exists(path) || !FileValidator.IsValidMentalEpisodicTreeFile(path))
        return;

      foreach (var raw in File.ReadAllLines(path))
      {
        var line = raw?.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
          continue;

        var hashIdx = line.IndexOf("#", StringComparison.Ordinal);
        var head = hashIdx >= 0 ? line.Substring(0, hashIdx) : line;
        var tail = hashIdx >= 0 && hashIdx + 1 < line.Length ? line.Substring(hashIdx + 1) : string.Empty;

        var hp = head.Split('|');
        if (hp.Length < 5) continue;
        if (!int.TryParse(hp[0], out var id)) continue;
        if (!int.TryParse(hp[1], out var nodePid)) continue;
        if (!int.TryParse(hp[2], out var themeId)) continue;
        if (!int.TryParse(hp[3], out var purposeId)) continue;

        var infoArr = new List<int>();
        var infoStr = hp[4];
        if (!string.IsNullOrEmpty(infoStr))
        {
          foreach (var s in infoStr.Split(','))
          {
            if (string.IsNullOrWhiteSpace(s)) continue;
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

        var leaf = new MentalEpisodicLeaf
        {
          Id = id,
          NodePid = nodePid,
          ThemeId = themeId,
          PurposeId = purposeId,
          InfoArr = infoArr,
          Effect = effect,
          Count = count
        };
        _leaves.Add(leaf);
        _leafByTriple[new MentalTriple(nodePid, themeId, purposeId)] = leaf;
        if (id > _lastId) _lastId = id;
      }
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

    private static void AverageMentalEffect(MentalEpisodicLeaf node, int effect)
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

    private int? GetNextGptStep(int nodePid, int themeId, int purposeId, IReadOnlyList<int> executedPrefix)
    {
      MentalEpisodicLeaf leaf;
      if (!_leafByTriple.TryGetValue(new MentalTriple(nodePid, themeId, purposeId), out leaf))
        return null;
      if (leaf.InfoArr == null || leaf.InfoArr.Count == 0)
        return null;
      if (leaf.Effect <= 0)
        return null;

      var arr = leaf.InfoArr;
      if (executedPrefix == null || executedPrefix.Count == 0)
      {
        foreach (var id in arr)
        {
          if (!MentalInfoFuncIds.IsAuxiliary(id))
            return id;
        }
        return null;
      }

      if (executedPrefix.Count > arr.Count)
        return null;
      for (var i = 0; i < executedPrefix.Count; i++)
      {
        if (executedPrefix[i] != arr[i])
          return null;
      }

      if (executedPrefix.Count == arr.Count)
        return null;

      var next = arr[executedPrefix.Count];
      return MentalInfoFuncIds.IsAuxiliary(next) ? (int?)null : next;
    }

    private int? GetFavoriteFirstExecutable(int nodePid, int themeId, int purposeId)
    {
      foreach (var leaf in BuildFavoriteCandidates(nodePid, themeId, purposeId))
      {
        if (leaf.Count <= 1) continue;
        if (leaf.InfoArr == null) continue;
        foreach (var id in leaf.InfoArr)
        {
          if (!MentalInfoFuncIds.IsAuxiliary(id))
            return id;
        }
      }
      return null;
    }

    private IEnumerable<MentalEpisodicLeaf> BuildFavoriteCandidates(int nodePid, int themeId, int purposeId)
    {
      bool Pos(MentalEpisodicLeaf l) => l.Effect > 0;

      List<MentalEpisodicLeaf> Pick(Func<MentalEpisodicLeaf, bool> pred)
      {
        var r = new List<MentalEpisodicLeaf>();
        foreach (var leaf in _leaves)
        {
          if (Pos(leaf) && pred(leaf))
            r.Add(leaf);
        }
        return r;
      }

      var a = Pick(l => l.NodePid == nodePid && l.ThemeId == themeId && l.PurposeId == purposeId);
      if (a.Count > 0) return a;
      var b = Pick(l => l.NodePid == nodePid && l.ThemeId == themeId);
      if (b.Count > 0) return b;
      var c = Pick(l => l.NodePid == nodePid);
      if (c.Count > 0) return c;
      if (purposeId > 0)
      {
        var d = Pick(l => l.PurposeId == purposeId);
        if (d.Count > 0) return d;
      }
      return Enumerable.Empty<MentalEpisodicLeaf>();
    }

    private sealed class MentalEpisodicLeaf
    {
      public int Id { get; set; }
      public int NodePid { get; set; }
      public int ThemeId { get; set; }
      public int PurposeId { get; set; }
      public List<int> InfoArr { get; set; }
      public int Effect { get; set; }
      public int Count { get; set; }
    }

    private struct MentalTriple : IEquatable<MentalTriple>
    {
      public MentalTriple(int nodePid, int themeId, int purposeId)
      {
        NodePid = nodePid;
        ThemeId = themeId;
        PurposeId = purposeId;
      }

      public int NodePid { get; }
      public int ThemeId { get; }
      public int PurposeId { get; }

      public bool Equals(MentalTriple other)
      {
        return NodePid == other.NodePid && ThemeId == other.ThemeId && PurposeId == other.PurposeId;
      }

      public override bool Equals(object obj)
      {
        return obj is MentalTriple other && Equals(other);
      }

      public override int GetHashCode()
      {
        unchecked
        {
          var h = 17;
          h = h * 31 + NodePid;
          h = h * 31 + ThemeId;
          h = h * 31 + PurposeId;
          return h;
        }
      }
    }

    private static class MentalInfoFuncIds
    {
      public static bool IsAuxiliary(int id)
      {
        return id == 1 || id == 2 || id == 5 || id == 8 || id == 14 || id == 17 || id == 26;
      }
    }
  }
}
