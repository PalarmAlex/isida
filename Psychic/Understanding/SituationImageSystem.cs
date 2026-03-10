using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Система образов ситуаций (SituationImage).
  /// Связь: узел дерева автоматизмов + тип ситуации.
  /// </summary>
  public sealed class SituationImageSystem : IDisposable
  {
    private readonly string _dataPath;
    private readonly Dictionary<int, SituationImageRecord> _byId = new Dictionary<int, SituationImageRecord>();
    private readonly Dictionary<(int NodeId, int TypeId), int> _unicumKeyToId = new Dictionary<(int, int), int>();
    private int _lastId;
    private bool _disposed;

    #region Инициализация

    private static SituationImageSystem _instance;

    /// <summary>Глобальный экземпляр</summary>
    public static SituationImageSystem Instance => _instance ??
        throw new InvalidOperationException("SituationImageSystem не инициализирован.");

    /// <summary>Признак инициализации</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Инициализировать экземпляр</summary>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("SituationImageSystem уже инициализирован.");
      _instance = new SituationImageSystem(psychicDataPath);
    }

    private SituationImageSystem(string psychicDataPath)
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

    /// <summary>Создать или получить образ ситуации по (automatizmNodeId, situationTypeId)</summary>
    public (int Id, SituationImageRecord Record) CreateOrGet(int automatizmTreeNodeId, int situationTypeId, bool checkUnicum = true)
    {
      if (situationTypeId <= 0)
        return (0, null);

      var key = (automatizmTreeNodeId, situationTypeId);
      if (checkUnicum && _unicumKeyToId.TryGetValue(key, out int existingId))
      {
        return (existingId, _byId.TryGetValue(existingId, out var r) ? r : null);
      }

      _lastId++;
      var rec = new SituationImageRecord
      {
        Id = _lastId,
        AutomatizmTreeNodeId = automatizmTreeNodeId,
        SituationTypeId = situationTypeId
      };
      _byId[_lastId] = rec;
      _unicumKeyToId[key] = _lastId;
      return (_lastId, rec);
    }

    /// <summary>Получить образ по ID</summary>
    public SituationImageRecord GetById(int id)
    {
      return _byId.TryGetValue(id, out var r) ? r : null;
    }

    /// <summary>ID текущей ситуации для активации Understanding (упрощённая логика).</summary>
    /// <remarks>
    /// Минимальная реализация: при automatizmNodeId > 0 возвращает образ с типом 4 (Experiment).
    /// WaitingPeriodForActionsVal, curActiveActions (mood, кнопки).
    /// </remarks>
    public int GetCurSituationImageId(int automatizmTreeNodeId)
    {
      if (automatizmTreeNodeId < 0) return 0;
      if (!SituationTypeSystem.IsInitialized) return 0;

      var type4 = SituationTypeSystem.Instance.GetByCode("Experiment")
          ?? SituationTypeSystem.Instance.GetById(4);
      int typeId = type4?.Id ?? 4;
      if (!SituationTypeSystem.Instance.Exists(typeId))
        return 0;

      var (id, _) = CreateOrGet(automatizmTreeNodeId, typeId, true);
      return id;
    }

    #endregion

    #region Load / Save

    private const string FileName = "situation_images.dat";

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
      if (!File.Exists(path) || !FileValidator.IsValidSituationImageFile(path))
        return;

      foreach (var line in File.ReadLines(path))
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 3) continue;
        if (!int.TryParse(p[0], out int id) || id <= 0) continue;
        if (!int.TryParse(p[1], out int nodeId)) continue;
        if (!int.TryParse(p[2], out int typeId)) continue;

        var rec = new SituationImageRecord
        {
          Id = id,
          AutomatizmTreeNodeId = nodeId,
          SituationTypeId = typeId
        };
        _byId[id] = rec;
        _unicumKeyToId[(nodeId, typeId)] = id;
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
          FileValidator.FileHeaders.SituationImagesFormat,
          FileValidator.FileHeaders.SituationImagesDesc
        };
        foreach (var r in _byId.Values.OrderBy(x => x.Id))
          lines.Add($"{r.Id}|{r.AutomatizmTreeNodeId}|{r.SituationTypeId}");

        var result = FileValidator.SafeSaveFile(
            path,
            lines,
            p => FileValidator.IsValidSituationImageFile(p),
            minLinesCount: 2,
            fileDescription: "образов ситуаций");

        return result.Success ? (true, null) : (false, result.ErrorMessage);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы, сохраняет образы ситуаций на диск</summary>
    public void Dispose()
    {
      if (_disposed) return;
      var (ok, err) = Save();
      if (!ok && !string.IsNullOrEmpty(err))
        Logger.Warning($"Ошибка сохранения SituationImage: {err}");
      _disposed = true;
    }

    #endregion
  }
}
