using ISIDA.Common;
using static ISIDA.Common.ResearchLogger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Система образов тем мышления (ThemeImage).
  /// </summary>
  public sealed class ThemeImageSystem : IDisposable
  {
    private readonly string _dataPath;
    private readonly Dictionary<int, ThemeImageRecord> _byId = new Dictionary<int, ThemeImageRecord>();
    private readonly Dictionary<(int Weight, int Type, int PulsCount), int> _unicumKeyToId = new Dictionary<(int, int, int), int>();
    private int _lastId;
    private bool _disposed;

    #region Инициализация

    private static ThemeImageSystem _instance;

    /// <summary>Глобальный экземпляр</summary>
    public static ThemeImageSystem Instance => _instance ??
        throw new InvalidOperationException("ThemeImageSystem не инициализирован.");

    /// <summary>Признак инициализации</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Инициализировать экземпляр</summary>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("ThemeImageSystem уже инициализирован.");
      _instance = new ThemeImageSystem(psychicDataPath);
    }

    private ThemeImageSystem(string psychicDataPath)
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

    /// <summary>Создать или получить образ темы</summary>
    public (int Id, ThemeImageRecord Record) CreateOrGet(int weight, int type, int pulsCount, bool checkUnicum = true)
    {
      if (weight < 1) weight = 2;
      if (weight > 10) weight = 10;

      var key = (weight, type, pulsCount);
      if (checkUnicum && _unicumKeyToId.TryGetValue(key, out int existingId))
      {
        return (existingId, _byId.TryGetValue(existingId, out var r) ? r : null);
      }

      _lastId++;
      var rec = new ThemeImageRecord
      {
        Id = _lastId,
        Weight = weight,
        Type = type,
        PulsCount = pulsCount
      };
      _byId[_lastId] = rec;
      _unicumKeyToId[key] = _lastId;
      return (_lastId, rec);
    }

    /// <summary>Получить образ по ID</summary>
    public ThemeImageRecord GetById(int id)
    {
      return _byId.TryGetValue(id, out var r) ? r : null;
    }

    #endregion

    #region Load / Save

    private const string FileName = "theme_images.dat";

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
      if (!File.Exists(path) || !FileValidator.IsValidThemeImagesFile(path))
        return;

      foreach (var line in File.ReadLines(path))
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 4) continue;
        if (!int.TryParse(p[0], out int id) || id <= 0) continue;
        if (!int.TryParse(p[1], out int weight)) continue;
        if (!int.TryParse(p[2], out int type)) continue;
        if (!int.TryParse(p[3], out int pulsCount)) continue;

        var rec = new ThemeImageRecord { Id = id, Weight = weight, Type = type, PulsCount = pulsCount };
        _byId[id] = rec;
        _unicumKeyToId[(weight, type, pulsCount)] = id;
        if (id > _lastId) _lastId = id;
      }
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
          FileValidator.FileHeaders.ThemeImagesFormat,
          FileValidator.FileHeaders.ThemeImagesDesc
        };
        foreach (var r in _byId.Values.OrderBy(x => x.Id))
          lines.Add($"{r.Id}|{r.Weight}|{r.Type}|{r.PulsCount}");

        var result = FileValidator.SafeSaveFile(
            path,
            lines,
            p => FileValidator.IsValidThemeImagesFile(p),
            minLinesCount: 2,
            fileDescription: "образов тем");

        return result.Success ? (true, null) : (false, result.ErrorMessage);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы, сохраняет образы тем на диск</summary>
    public void Dispose()
    {
      if (_disposed) return;
      var (ok, err) = Save();
      if (!ok && !string.IsNullOrEmpty(err))
        Logger.Warning($"Ошибка сохранения ThemeImage: {err}");
      _disposed = true;
    }

    #endregion
  }
}
