using ISIDA.Common;
using static ISIDA.Common.ResearchLogger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Система образов целей мышления (PurposeImage).
  /// </summary>
  public sealed class PurposeImageSystem : IDisposable
  {
    private readonly string _dataPath;
    private readonly Dictionary<int, PurposeImageRecord> _byId = new Dictionary<int, PurposeImageRecord>();
    private readonly Dictionary<(int Target, int MoodId, int EmotionId, int SituationId), int> _unicumKeyToId =
        new Dictionary<(int, int, int, int), int>();
    private int _lastId;
    private bool _disposed;

    #region Инициализация

    private static PurposeImageSystem _instance;

    /// <summary>Глобальный экземпляр</summary>
    public static PurposeImageSystem Instance => _instance ??
        throw new InvalidOperationException("PurposeImageSystem не инициализирован.");

    /// <summary>Признак инициализации</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Инициализировать экземпляр</summary>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("PurposeImageSystem уже инициализирован.");
      _instance = new PurposeImageSystem(psychicDataPath);
    }

    private PurposeImageSystem(string psychicDataPath)
    {
      _dataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Understanding")
          : Path.Combine(psychicDataPath, "Understanding");
      EnsureDirectory();
      Load();
    }

    #endregion

    #region Создание и поиск

    /// <summary>Создать или получить образ цели</summary>
    public (int Id, PurposeImageRecord Record) CreatePurposeImageOrGet(int target, int moodId, int emotionId, int situationId, bool checkUnicum = true)
    {
      if (target < 1) target = 2;
      if (target > 2) target = 2;

      var key = (target, moodId, emotionId, situationId);
      if (checkUnicum && _unicumKeyToId.TryGetValue(key, out int existingId))
      {
        return (existingId, _byId.TryGetValue(existingId, out var r) ? r : null);
      }

      _lastId++;
      var rec = new PurposeImageRecord
      {
        Id = _lastId,
        Target = target,
        MoodId = moodId,
        EmotionId = emotionId,
        SituationId = situationId
      };
      _byId[_lastId] = rec;
      _unicumKeyToId[key] = _lastId;
      return (_lastId, rec);
    }

    /// <summary>Получить образ по ID</summary>
    public PurposeImageRecord GetById(int id)
    {
      return _byId.TryGetValue(id, out var r) ? r : null;
    }

    #endregion

    #region Load / Save

    private const string FileName = "purpose_images.dat";

    private void EnsureDirectory()
    {
      if (!string.IsNullOrEmpty(_dataPath) && !Directory.Exists(_dataPath))
        Directory.CreateDirectory(_dataPath);
    }

    private void Load()
    {
      var path = Path.Combine(_dataPath, FileName);
      _byId.Clear();
      _unicumKeyToId.Clear();
      _lastId = 0;
      if (!File.Exists(path) || !FileValidator.IsValidPurposeImagesFile(path))
        return;

      foreach (var line in File.ReadLines(path))
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 5) continue;
        if (!int.TryParse(p[0], out int id) || id <= 0) continue;
        if (!int.TryParse(p[1], out int target)) continue;
        if (!int.TryParse(p[2], out int moodId)) continue;
        if (!int.TryParse(p[3], out int emotionId)) continue;
        if (!int.TryParse(p[4], out int situationId)) continue;

        var rec = new PurposeImageRecord
        {
          Id = id,
          Target = target,
          MoodId = moodId,
          EmotionId = emotionId,
          SituationId = situationId
        };
        _byId[id] = rec;
        _unicumKeyToId[(target, moodId, emotionId, situationId)] = id;
        if (id > _lastId) _lastId = id;
      }
    }

    /// <summary>Очистить все образы целей в памяти (для перехода на младшую стадию). Запись на диск — при Dispose.</summary>
    public (bool Success, string Error) Clear()
    {
      _byId.Clear();
      _unicumKeyToId.Clear();
      _lastId = 0;
      return (true, null);
    }

    /// <summary>Сохранить на диск</summary>
    public (bool Success, string Error) Save()
    {
      try
      {
        EnsureDirectory();
        var path = Path.Combine(_dataPath, FileName);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.PurposeImagesFormat,
          FileValidator.FileHeaders.PurposeImagesDesc
        };
        foreach (var r in _byId.Values.OrderBy(x => x.Id))
          lines.Add($"{r.Id}|{r.Target}|{r.MoodId}|{r.EmotionId}|{r.SituationId}");

        var result = FileValidator.SafeSaveFile(
            path,
            lines,
            p => FileValidator.IsValidPurposeImagesFile(p),
            minLinesCount: 2,
            fileDescription: "образов целей");

        return result.Success ? (true, null) : (false, result.ErrorMessage);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы, сохраняет образы целей на диск</summary>
    public void Dispose()
    {
      if (_disposed) return;
      var (ok, err) = Save();
      if (!ok && !string.IsNullOrEmpty(err))
        Logger.Warning($"Ошибка сохранения PurposeImage: {err}");
      _disposed = true;
    }

    #endregion
  }
}
