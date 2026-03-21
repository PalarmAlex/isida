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
    private Dictionary<int, ThemeTypeData> _themeTypes = new Dictionary<int, ThemeTypeData>();

    private struct ThemeTypeData
    {
      public string Description;
      public int DefaultWeight;
      public HashSet<int> AllowedInfoFuncIds;
    }
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
      var themeTypesPath = Path.Combine(_dataPath, ThemeTypesFileName);
      if (!File.Exists(themeTypesPath))
      {
        var (ok, err) = SaveThemeTypes();
        if (!ok && !string.IsNullOrEmpty(err))
          Logger.Warning($"Не удалось сохранить справочник типов тем по умолчанию: {err}");
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

    /// <summary>Текстовое описание типа темы. Для канонических Id — из кода (фиксированный список в ThemeImageSystem). Тип 0 всегда «Нет темы».</summary>
    public string GetThemeTypeDescription(int typeIndex)
    {
      if (typeIndex == 0) return "Нет темы";
      var canon = GetCanonicalThemeTypeDescription(typeIndex);
      if (!string.IsNullOrEmpty(canon)) return canon;
      if (_themeTypes.TryGetValue(typeIndex, out var rec)) return rec.Description ?? "";
      return "";
    }

    private static string GetCanonicalThemeTypeDescription(int id)
    {
      foreach (var t in DefaultThemeTypesList)
        if (t.Id == id) return t.Description ?? "";
      return "";
    }

    /// <summary>Вес по умолчанию для типа темы (из справочника). Если тип отсутствует — 2.</summary>
    public int GetDefaultWeightForThemeType(int themeTypeId)
    {
      if (themeTypeId <= 0) return 2;
      if (_themeTypes.TryGetValue(themeTypeId, out var rec) && rec.DefaultWeight > 0) return rec.DefaultWeight;
      return 2;
    }

    /// <summary>Разрешённые Id инфо-функций для типа темы. Пустой набор = без ограничений.</summary>
    public HashSet<int> GetAllowedInfoFuncIdsForThemeType(int themeTypeId)
    {
      var result = new HashSet<int>();
      if (themeTypeId <= 0) return result;
      if (!_themeTypes.TryGetValue(themeTypeId, out var rec) || rec.AllowedInfoFuncIds == null) return result;
      foreach (var id in rec.AllowedInfoFuncIds) result.Add(id);
      return result;
    }

    /// <summary>ID типов тем, используемые в справочнике типов ситуаций. Их нельзя удалять — есть ссылки.</summary>
    public static IReadOnlyList<int> GetThemeTypeIdsProtectedFromRemoval()
    {
      if (!SituationTypeSystem.IsInitialized) return Array.Empty<int>();
      return SituationTypeSystem.Instance.GetThemeTypeIdsInUse();
    }

    /// <summary>Можно ли удалить тип темы из справочника (false, если он зарезервирован в дефолтных типах ситуаций).</summary>
    public static bool CanRemoveThemeType(int themeTypeId)
    {
      var protectedIds = GetThemeTypeIdsProtectedFromRemoval();
      return !protectedIds.Contains(themeTypeId);
    }

    /// <summary>Справочник типов тем: индекс → описание. При инициализации — из theme_types.dat; иначе — дефолтный список из <see cref="DefaultThemeTypesList"/>.</summary>
    public static IReadOnlyList<(int Id, string Description)> GetDefaultThemeTypesForSettings()
    {
      if (IsInitialized)
        return Instance.GetThemeTypesForSettings();
      return DefaultThemeTypesList.Select(x => (x.Id, x.Description)).ToList();
    }

    /// <summary>Все типы тем из загруженного справочника (theme_types.dat) для выбора в привязках слотов 41–60.</summary>
    public IReadOnlyList<(int Id, string Description)> GetThemeTypesForSettings()
    {
      return _themeTypes
        .Where(kv => kv.Key >= 1)
        .OrderBy(kv => kv.Key)
        .Select(kv =>
        {
          var c = GetCanonicalThemeTypeDescription(kv.Key);
          return (kv.Key, !string.IsNullOrEmpty(c) ? c : (kv.Value.Description ?? ""));
        })
        .ToList();
    }

    /// <summary>Типы тем с привязкой инфо-функций для вывода на пульт (Id, Description, DefaultWeight, AllowedInfoFuncIds). Описание — из канонического списка в коде.</summary>
    public IReadOnlyList<(int Id, string Description, int DefaultWeight, IReadOnlyList<int> AllowedInfoFuncIds)> GetThemeTypesWithAllowedInfoFuncs()
    {
      return _themeTypes
        .Where(kv => kv.Key >= 1)
        .OrderBy(kv => kv.Key)
        .Select(kv =>
        {
          var c = GetCanonicalThemeTypeDescription(kv.Key);
          return (kv.Key, !string.IsNullOrEmpty(c) ? c : (kv.Value.Description ?? ""),
            kv.Value.DefaultWeight > 0 ? kv.Value.DefaultWeight : 2,
            (IReadOnlyList<int>)(kv.Value.AllowedInfoFuncIds?.OrderBy(x => x).ToList() ?? new List<int>()));
        })
        .ToList();
    }

    /// <summary>
    /// Фиксированный справочник типов тем из кода: Id и описания не редактируются в UI; вес и инфо-функции — из theme_types.dat.
    /// </summary>
    public IReadOnlyList<(int Id, string Description, int DefaultWeight, IReadOnlyList<int> AllowedInfoFuncIds)> GetFixedCatalogThemeTypesForUi()
    {
      NormalizeThemeTypesToCanonicalCatalog();
      var list = new List<(int, string, int, IReadOnlyList<int>)>();
      foreach (var (id, desc, defW) in DefaultThemeTypesList)
      {
        int weight = defW;
        var allowed = new List<int>();
        if (_themeTypes.TryGetValue(id, out var rec))
        {
          if (rec.DefaultWeight >= 1 && rec.DefaultWeight <= 10) weight = rec.DefaultWeight;
          if (rec.AllowedInfoFuncIds != null && rec.AllowedInfoFuncIds.Count > 0)
            allowed = rec.AllowedInfoFuncIds.OrderBy(x => x).ToList();
        }
        list.Add((id, desc ?? "", weight, allowed));
      }
      return list;
    }

    /// <summary>Сохранить только вес и списки инфо-функций; описания и набор Id тем — из кода.</summary>
    public (bool Success, string Error) SaveFixedCatalogThemeTypes(IEnumerable<(int Id, int DefaultWeight, IReadOnlyList<int> AllowedInfoFuncIds)> rows)
    {
      if (rows == null) return (false, "Нет данных для сохранения");
      var byId = rows.ToDictionary(r => r.Id, r => r);
      var canonicalIds = new HashSet<int>(DefaultThemeTypesList.Select(x => x.Id));
      foreach (var id in byId.Keys)
        if (!canonicalIds.Contains(id))
          return (false, $"Неизвестный Id типа темы: {id}");

      NormalizeThemeTypesToCanonicalCatalog();
      foreach (var (id, desc, defW) in DefaultThemeTypesList)
      {
        int w = defW;
        var allowed = new HashSet<int>();
        if (byId.TryGetValue(id, out var row))
        {
          w = row.DefaultWeight;
          if (w < 1) w = 2;
          if (w > 10) w = 10;
          if (row.AllowedInfoFuncIds != null)
            foreach (var x in row.AllowedInfoFuncIds)
              if (x > 0) allowed.Add(x);
        }
        else if (_themeTypes.TryGetValue(id, out var existing))
        {
          w = existing.DefaultWeight >= 1 && existing.DefaultWeight <= 10 ? existing.DefaultWeight : defW;
          if (existing.AllowedInfoFuncIds != null)
            foreach (var x in existing.AllowedInfoFuncIds)
              if (x > 0) allowed.Add(x);
        }
        _themeTypes[id] = new ThemeTypeData
        {
          Description = desc ?? "",
          DefaultWeight = w,
          AllowedInfoFuncIds = allowed
        };
      }
      foreach (var key in _themeTypes.Keys.ToList())
        if (!canonicalIds.Contains(key))
          _themeTypes.Remove(key);
      return SaveThemeTypes();
    }

    /// <summary>Обновить привязку инфо-функций для типа темы и сохранить.</summary>
    public (bool Success, string Error) SetAllowedInfoFuncIdsForThemeType(int themeTypeId, IEnumerable<int> ids)
    {
      if (themeTypeId < 1) return (false, "Некорректный Id типа темы");
      if (!_themeTypes.TryGetValue(themeTypeId, out var rec))
        return (false, "Тип темы не найден");
      rec.AllowedInfoFuncIds = ids != null ? new HashSet<int>(ids.Where(x => x > 0)) : new HashSet<int>();
      _themeTypes[themeTypeId] = rec;
      return SaveThemeTypes();
    }

    /// <summary>Типы тем, доступные для редактирования в UI: только те, что есть в справочнике, исключая типы, используемые в справочнике типов ситуаций. Возвращает Id, описание и вес по умолчанию.</summary>
    public IReadOnlyList<(int Id, string Description, int DefaultWeight)> GetEditableThemeTypes()
    {
      var protectedIds = GetThemeTypeIdsProtectedFromRemoval();
      return _themeTypes
        .Where(kv => kv.Key >= 1 && !protectedIds.Contains(kv.Key))
        .OrderBy(kv => kv.Key)
        .Select(kv => (kv.Key, kv.Value.Description ?? "", kv.Value.DefaultWeight > 0 ? kv.Value.DefaultWeight : 2))
        .ToList();
    }

    /// <summary>Обновить справочник типов тем из переданного списка (Id, Description, DefaultWeight) и сохранить theme_types.dat. Вес обязан быть >0.</summary>
    public (bool Success, string Error) UpdateThemeTypesFromEditable(IEnumerable<(int Id, string Description, int DefaultWeight)> records)
    {
      if (records == null) return (false, "Нет данных для сохранения");
      var protectedIds = GetThemeTypeIdsProtectedFromRemoval();
      var idsInRecords = new HashSet<int>(records.Where(r => r.Id >= 1).Select(r => r.Id));

      foreach (var r in records)
      {
        if (r.Id < 1) continue;
        int weight = r.DefaultWeight;
        if (weight < 1) weight = 2;
        if (weight > 10) weight = 10;
        if (!_themeTypes.TryGetValue(r.Id, out var existing))
          existing = new ThemeTypeData { AllowedInfoFuncIds = new HashSet<int>() };
        _themeTypes[r.Id] = new ThemeTypeData
        {
          Description = r.Description ?? "",
          DefaultWeight = weight,
          AllowedInfoFuncIds = existing.AllowedInfoFuncIds ?? new HashSet<int>()
        };
      }

      foreach (var id in _themeTypes.Keys.ToList())
      {
        if (!idsInRecords.Contains(id) && !protectedIds.Contains(id))
          _themeTypes.Remove(id);
      }

      return SaveThemeTypes();
    }

    /// <summary>Дефолтный список тем мышления.</summary>
    private static readonly IReadOnlyList<(int Id, string Description, int DefaultWeight)> DefaultThemeTypesList = new List<(int, string, int)>
    {
      (1, "Негативный эффект моторного автоматизма", 2),
      (2, "Негативный эффект ментального автоматизма", 2),
      (3, "Состояние Плохо", 3),
      (4, "Стимул с Пульта", 2),
      (5, "Поисковый интерес", 5),
      (6, "Обучение с учителем", 2),
      (7, "Игнорирование оператором", 1),
      (8, "Игра", 3),
      (9, "Неудовлетворенность существующим", 2),
      (10, "Непонимание", 2),
      (11, "Действие оператора", 1),
      (12, "Сомнение в штатном автоматизме", 5),
      (13, "Защита", 5),
      (14, "Страх", 5),
      (15, "Агрессия", 3),
      (16, "Есть объект высокой значимости", 2),
      (17, "Улучшение настроения", 2),
      (18, "Истощение сил и ресурсов", 5),
      (19, "Восстановление и исцеление", 5),
      (20, "Альтруизм", 3)
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

    private static HashSet<int> ParseAllowedInfoFuncIds(string raw)
    {
      var set = new HashSet<int>();
      if (string.IsNullOrWhiteSpace(raw)) return set;
      foreach (var token in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
      {
        if (int.TryParse(token.Trim(), out int id) && id > 0) set.Add(id);
      }
      return set;
    }

    private void LoadThemeTypes()
    {
      var path = Path.Combine(_dataPath, ThemeTypesFileName);
      _themeTypes = new Dictionary<int, ThemeTypeData>();
      if (File.Exists(path) && FileValidator.IsValidThemeTypesFile(path))
      {
        foreach (var line in File.ReadLines(path))
        {
          var t = line?.Trim();
          if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
          var p = t.Split('|');
          if (p.Length < 3) continue;
          if (!int.TryParse(p[0], out int id) || id < 1) continue;
          if (!int.TryParse(p[2], out int weight) || weight < 1) continue;
          var allowedRaw = p.Length >= 4 ? (p[3] ?? "") : "";
          _themeTypes[id] = new ThemeTypeData
          {
            Description = p[1].Trim(),
            DefaultWeight = weight,
            AllowedInfoFuncIds = ParseAllowedInfoFuncIds(allowedRaw)
          };
        }
      }
      NormalizeThemeTypesToCanonicalCatalog();
    }

    /// <summary>
    /// Оставляет в справочнике только Id из <see cref="DefaultThemeTypesList"/>; описания — из кода; вес и инфо-функции подтягивает из файла или дефолта.
    /// </summary>
    private void NormalizeThemeTypesToCanonicalCatalog()
    {
      var canonicalIds = new HashSet<int>(DefaultThemeTypesList.Select(x => x.Id));
      foreach (var key in _themeTypes.Keys.ToList())
      {
        if (!canonicalIds.Contains(key))
          _themeTypes.Remove(key);
      }
      foreach (var (id, desc, defW) in DefaultThemeTypesList)
      {
        if (!_themeTypes.TryGetValue(id, out var existing))
        {
          _themeTypes[id] = new ThemeTypeData { Description = desc, DefaultWeight = defW, AllowedInfoFuncIds = new HashSet<int>() };
        }
        else
        {
          existing.Description = desc;
          if (existing.DefaultWeight < 1 || existing.DefaultWeight > 10)
            existing.DefaultWeight = defW;
          if (existing.AllowedInfoFuncIds == null)
            existing.AllowedInfoFuncIds = new HashSet<int>();
          _themeTypes[id] = existing;
        }
      }
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
        foreach (var kv in _themeTypes.OrderBy(x => x.Key))
        {
          if (kv.Key >= 1)
          {
            var allowedStr = kv.Value.AllowedInfoFuncIds != null && kv.Value.AllowedInfoFuncIds.Count > 0
              ? string.Join(",", kv.Value.AllowedInfoFuncIds.OrderBy(x => x))
              : "";
            lines.Add($"{kv.Key}|{kv.Value.Description}|{kv.Value.DefaultWeight}|{allowedStr}");
          }
        }
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
