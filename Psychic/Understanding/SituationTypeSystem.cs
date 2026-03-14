using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Справочник типов ситуаций: связь ID с MoodId (настроение) и InfluenceId (воздействие).
  /// Обязательные значения 1–5 нельзя удалять. Редактируется на пульте.
  /// </summary>
  public sealed class SituationTypeSystem : IDisposable
  {
    /// <summary>ID обязательных записей по умолчанию (нельзя удалять)</summary>
    public static readonly int[] DefaultRequiredIds = { 1, 2, 3, 4, 5 };

    private readonly string _dataPath;
    private readonly Dictionary<int, SituationTypeRecord> _byId = new Dictionary<int, SituationTypeRecord>();
    private readonly Dictionary<int, int> _byMoodId = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _byInfluenceId = new Dictionary<int, int>();
    private int _nextMoodId = 11;
    private int _nextInfluenceId = 21;
    private bool _disposed;

    #region Инициализация

    private static SituationTypeSystem _instance;

    /// <summary>Глобальный экземпляр</summary>
    public static SituationTypeSystem Instance => _instance ??
        throw new InvalidOperationException("SituationTypeSystem не инициализирован.");

    /// <summary>Признак инициализации</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Инициализировать экземпляр</summary>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("SituationTypeSystem уже инициализирован.");
      _instance = new SituationTypeSystem(psychicDataPath);
    }

    private SituationTypeSystem(string psychicDataPath)
    {
      _dataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Understanding")
          : Path.Combine(psychicDataPath, "Understanding");
      EnsureDirectory();
      Load();
      EnsureDefaultTypes();
      RebuildIndexes();
    }

    #endregion

    #region Доступ

    /// <summary>Получить тип по ID</summary>
    public SituationTypeRecord GetById(int id)
    {
      return _byId.TryGetValue(id, out var r) ? r : null;
    }

    /// <summary>ID типа ситуации по MoodId (настроение). 0 если не найдено.</summary>
    public int GetIdByMoodId(int moodId)
    {
      return moodId <= 0 ? 0 : (_byMoodId.TryGetValue(moodId, out int id) ? id : 0);
    }

    /// <summary>ID типа ситуации по InfluenceId (воздействие). 0 если не найдено.</summary>
    public int GetIdByInfluenceId(int influenceId)
    {
      return influenceId <= 0 ? 0 : (_byInfluenceId.TryGetValue(influenceId, out int id) ? id : 0);
    }

    /// <summary>Все типы</summary>
    public IReadOnlyList<SituationTypeRecord> GetAll()
    {
      return _byId.Values.OrderBy(r => r.Id).ToList();
    }

    /// <summary>Существует ли тип с таким ID</summary>
    public bool Exists(int id)
    {
      return _byId.ContainsKey(id);
    }

    /// <summary>Обязательная запись по умолчанию (1–5) — удалять нельзя</summary>
    public static bool IsRequiredDefault(int id)
    {
      return Array.IndexOf(DefaultRequiredIds, id) >= 0;
    }

    #endregion

    #region Создание и удаление

    /// <summary>Создать запись по MoodId (ID 11–20). Дубликаты не создаются.</summary>
    public (int Id, string Error) AddByMoodId(int moodId, string description)
    {
      if (moodId <= 0) return (0, "MoodId должен быть > 0");
      if (FindByMoodId(moodId) != null) return (0, "Запись с таким MoodId уже есть");
      if (_nextMoodId > 20) return (0, "Превышен лимит ID для настроения (11–20)");
      int id = _nextMoodId++;
      var rec = new SituationTypeRecord { Id = id, MoodId = moodId, InfluenceId = 0, Description = description ?? "" };
      _byId[id] = rec;
      _byMoodId[moodId] = id;
      return (id, null);
    }

    /// <summary>Создать запись по InfluenceId (ID 21+). Дубликаты не создаются.</summary>
    public (int Id, string Error) AddByInfluenceId(int influenceId, string description)
    {
      if (influenceId <= 0) return (0, "InfluenceId должен быть > 0");
      if (FindByInfluenceId(influenceId) != null) return (0, "Запись с таким InfluenceId уже есть");
      int id = _nextInfluenceId++;
      var rec = new SituationTypeRecord { Id = id, MoodId = 0, InfluenceId = influenceId, Description = description ?? "" };
      _byId[id] = rec;
      _byInfluenceId[influenceId] = id;
      return (id, null);
    }

    /// <summary>Удалить запись. Для ID 1–5 возвращает ошибку.</summary>
    public (bool Success, string Error) Remove(int id)
    {
      if (IsRequiredDefault(id)) return (false, $"Запись ID={id} обязательна, удаление запрещено");
      if (!_byId.TryGetValue(id, out var rec)) return (false, "Запись не найдена");
      _byId.Remove(id);
      if (rec.MoodId > 0) _byMoodId.Remove(rec.MoodId);
      if (rec.InfluenceId > 0) _byInfluenceId.Remove(rec.InfluenceId);
      return (true, null);
    }

    private SituationTypeRecord FindByMoodId(int moodId) =>
        _byId.Values.FirstOrDefault(r => r.MoodId == moodId);

    private SituationTypeRecord FindByInfluenceId(int influenceId) =>
        _byId.Values.FirstOrDefault(r => r.InfluenceId == influenceId);

    private void RebuildIndexes()
    {
      _byMoodId.Clear();
      _byInfluenceId.Clear();
      foreach (var r in _byId.Values)
      {
        if (r.MoodId > 0) _byMoodId[r.MoodId] = r.Id;
        if (r.InfluenceId > 0) _byInfluenceId[r.InfluenceId] = r.Id;
      }
      _nextMoodId = _byId.Keys.Where(k => k >= 11 && k <= 20).DefaultIfEmpty(10).Max() + 1;
      _nextInfluenceId = _byId.Keys.Where(k => k >= 21).DefaultIfEmpty(20).Max() + 1;
    }

    #endregion

    #region Load / Save

    private const string FileName = "situation_types.dat";

    private void EnsureDirectory()
    {
      if (!string.IsNullOrEmpty(_dataPath) && !Directory.Exists(_dataPath))
        Directory.CreateDirectory(_dataPath);
    }

    private void Load()
    {
      var path = Path.Combine(_dataPath, FileName);
      _byId.Clear();
      if (!File.Exists(path) || !FileValidator.IsValidSituationTypeFile(path))
        return;

      foreach (var line in File.ReadLines(path))
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 4) continue;
        if (!int.TryParse(p[0], out int id) || id <= 0) continue;
        if (!int.TryParse(p[1], out int moodId)) moodId = 0;
        if (!int.TryParse(p[2], out int influenceId)) influenceId = 0;
        var desc = p.Length > 3 ? p[3] : "";
        _byId[id] = new SituationTypeRecord { Id = id, MoodId = moodId, InfluenceId = influenceId, Description = desc };
      }
    }

    /// <summary>Сохранить справочник</summary>
    public (bool Success, string Error) Save()
    {
      try
      {
        EnsureDirectory();
        var path = Path.Combine(_dataPath, FileName);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.SituationTypesFormat,
          FileValidator.FileHeaders.SituationTypesDesc
        };
        foreach (var r in _byId.Values.OrderBy(x => x.Id))
          lines.Add($"{r.Id}|{r.MoodId}|{r.InfluenceId}|{r.Description ?? ""}");

        var result = FileValidator.SafeSaveFile(
            path,
            lines,
            p => FileValidator.IsValidSituationTypeFile(p),
            minLinesCount: 2,
            fileDescription: "справочника типов ситуаций");

        return result.Success ? (true, null) : (false, result.ErrorMessage);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    private void EnsureDefaultTypes()
    {
      var defaults = new[]
      {
        (1, 0, 0, "ResponseAction"),
        (2, 0, 0, "AutomatizmRun"),
        (3, 0, 0, "NeedThinking"),
        (4, 0, 0, "Experiment"),
        (5, 0, 0, "OperatorIgnore")
      };
      foreach (var d in defaults)
      {
        if (!_byId.ContainsKey(d.Item1))
          _byId[d.Item1] = new SituationTypeRecord { Id = d.Item1, MoodId = d.Item2, InfluenceId = d.Item3, Description = d.Item4 };
      }
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы, сохраняет справочник на диск</summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        var (ok, err) = Save();
        if (!ok && !string.IsNullOrEmpty(err)) Logger.Warning($"Ошибка сохранения SituationTypeSystem: {err}");
      }
      catch { }
      _disposed = true;
    }

    #endregion
  }
}
