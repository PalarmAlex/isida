using ISIDA.Common;
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
    private Dictionary<int, string> _themeTypeStr = new Dictionary<int, string>();
    private int _lastId;
    private bool _disposed;

    /// <summary>ID типа темы по умолчанию. Задаётся из конфигурации.</summary>
    public int DefaultThemeTypeId { get; set; } = 0;

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
      LoadThemeTypes();
      if (_themeTypeStr.Count == 0)
      {
        CreateDefaultThemeTypesAndSave();
      }
    }

    #endregion

    #region Создание и поиск

    /// <summary>Создать или получить образ темы. Если type не задан (≤0), используется DefaultThemeTypeId.</summary>
    public (int Id, ThemeImageRecord Record) CreateThemeImageOrGet(int weight, int type, int pulsCount, bool checkUnicum = true)
    {
      if (weight < 1) weight = 2;
      if (weight > 10) weight = 10;
      if (type <= 0) type = DefaultThemeTypeId;

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

    /// <summary>Текстовое описание типа темы. При отсутствии в справочнике — пустая строка. Тип 0 всегда «Нет темы».</summary>
    public string GetThemeTypeDescription(int typeIndex)
    {
      if (typeIndex == 0) return "Нет темы";
      if (_themeTypeStr.TryGetValue(typeIndex, out string desc)) return desc;
      return "";
    }

    /// <summary>ID типов тем, зарезервированные в дефолтных типах ситуаций (SituationTypeSystem.EnsureDefaultTypes). Их нельзя удалять; новый ID темы не должен с ними совпадать.</summary>
    public static IReadOnlyList<int> GetThemeTypeIdsProtectedFromRemoval()
    {
      return SituationTypeSystem.GetThemeTypeIdsReservedInDefaultTypes();
    }

    /// <summary>Можно ли удалить тип темы из справочника (false, если он зарезервирован в дефолтных типах ситуаций).</summary>
    public static bool CanRemoveThemeType(int themeTypeId)
    {
      var protectedIds = GetThemeTypeIdsProtectedFromRemoval();
      return !protectedIds.Contains(themeTypeId);
    }

    /// <summary>Справочник типов тем: индекс → описание. При инициализации — из theme_types.dat; иначе — дефолтный список 1–17.</summary>
    public static IReadOnlyList<(int Id, string Description)> GetDefaultThemeTypesForSettings()
    {
      if (IsInitialized)
        return Instance.GetThemeTypesForSettings();
      return DefaultThemeTypesList;
    }

    /// <summary>Все типы тем из загруженного справочника (theme_types.dat) для выбора в привязках слотов 41–60.</summary>
    public IReadOnlyList<(int Id, string Description)> GetThemeTypesForSettings()
    {
      return _themeTypeStr
        .Where(kv => kv.Key >= 1)
        .OrderBy(kv => kv.Key)
        .Select(kv => (kv.Key, kv.Value ?? ""))
        .ToList();
    }

    /// <summary>Типы тем, доступные для редактирования в UI: только те, что есть в справочнике, исключая типы, зарезервированные в дефолтных типах ситуаций.</summary>
    public IReadOnlyList<(int Id, string Description)> GetEditableThemeTypes()
    {
      var protectedIds = GetThemeTypeIdsProtectedFromRemoval();
      return _themeTypeStr
        .Where(kv => kv.Key >= 1 && !protectedIds.Contains(kv.Key))
        .OrderBy(kv => kv.Key)
        .Select(kv => (kv.Key, kv.Value ?? ""))
        .ToList();
    }

    /// <summary>Обновить справочник типов тем из переданного списка (Id, Description) и сохранить theme_types.dat. Типы, отсутствующие в списке, удаляются (кроме зарезервированных в дефолтах).</summary>
    public (bool Success, string Error) UpdateThemeTypesFromEditable(IEnumerable<(int Id, string Description)> records)
    {
      if (records == null) return (false, "Нет данных для сохранения");
      var protectedIds = GetThemeTypeIdsProtectedFromRemoval();
      var idsInRecords = new HashSet<int>(records.Where(r => r.Id >= 1).Select(r => r.Id));

      foreach (var r in records)
      {
        if (r.Id < 1) continue;
        _themeTypeStr[r.Id] = r.Description ?? "";
      }

      foreach (var id in _themeTypeStr.Keys.ToList())
      {
        if (!idsInRecords.Contains(id) && !protectedIds.Contains(id))
          _themeTypeStr.Remove(id);
      }

      return SaveThemeTypes();
    }

    private static readonly IReadOnlyList<(int Id, string Description)> DefaultThemeTypesList = new List<(int, string)>
    {
      (1, "Негативный эффект моторного автоматизма"),
      (2, "Негативный эффект ментального автоматизма"),
      (3, "Состояние Плохо"),
      (4, "Стимул с Пульта"),
      (5, "Поисковый интерес"),
      (6, "Обучение с учителем"),
      (7, "Игнорирование оператором"),
      (8, "Игра"),
      (9, "Неудовлетворенность существующим"),
      (10, "Непонимание"),
      (11, "Действие оператора"),
      (12, "Сомнение в штатном автоматизме"),
      (13, "Защита"),
      (14, "Страх"),
      (15, "Агрессия"),
      (16, "Есть объект высокой значимости"),
      (17, "Улучшение настроения")
    };

    #endregion

    #region Load / Save

    private const string FileName = "theme_images.dat";
    private const string ThemeTypesFileName = "theme_types.dat";

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

    private void LoadThemeTypes()
    {
      var path = Path.Combine(_dataPath, ThemeTypesFileName);
      _themeTypeStr = new Dictionary<int, string>();
      if (!File.Exists(path) || !FileValidator.IsValidThemeTypesFile(path))
        return;
      foreach (var line in File.ReadLines(path))
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 2) continue;
        if (!int.TryParse(p[0], out int id) || id < 1) continue;
        _themeTypeStr[id] = p[1].Trim();
      }
    }

    private void CreateDefaultThemeTypesAndSave()
    {
      _themeTypeStr = new Dictionary<int, string>();
      foreach (var (id, desc) in DefaultThemeTypesList)
        _themeTypeStr[id] = desc;
      var (ok, _) = SaveThemeTypes();
      if (!ok)
        Logger.Warning("Не удалось сохранить справочник типов тем по умолчанию.");
    }

    /// <summary>Сохранить справочник типов тем на диск</summary>
    public (bool Success, string Error) SaveThemeTypes()
    {
      try
      {
        EnsureDirectory();
        var path = Path.Combine(_dataPath, ThemeTypesFileName);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.ThemeTypesFormat,
          FileValidator.FileHeaders.ThemeTypesDesc
        };
        foreach (var kv in _themeTypeStr.OrderBy(x => x.Key))
          if (kv.Key >= 1)
            lines.Add($"{kv.Key}|{kv.Value}");
        var result = FileValidator.SafeSaveFile(
            path,
            lines,
            p => FileValidator.IsValidThemeTypesFile(p),
            minLinesCount: 2,
            fileDescription: "справочник типов тем");
        return result.Success ? (true, null) : (false, result.ErrorMessage);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    /// <summary>Очистить все образы тем в памяти и в файле theme_images.dat (для перехода на младшую стадию). Справочник типов тем не трогает.</summary>
    public (bool Success, string Error) Clear()
    {
      _byId.Clear();
      _unicumKeyToId.Clear();
      _lastId = 0;
      return Save();
    }

    /// <summary>Сохранить на диск (образы тем и справочник типов)</summary>
    public (bool Success, string Error) Save()
    {
      try
      {
        EnsureDirectory();
        var themeTypesResult = SaveThemeTypes();
        if (!themeTypesResult.Success && !string.IsNullOrEmpty(themeTypesResult.Error))
          Logger.Warning($"Ошибка сохранения типов тем: {themeTypesResult.Error}");
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
