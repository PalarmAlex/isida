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
    /// <summary>Код «отсутствие значения» для слотов. 0 — Нормальное (настроение).</summary>
    public const int EmptySlotValue = -1;

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
      var path = Path.Combine(_dataPath, FileName);
      if (!File.Exists(path))
        CreateDefaultSituationTypesFile(path);
      Load();
      EnsureDefaultTypes();
      EnsureSlots();
      RebuildIndexes();
    }

    /// <summary>
    /// Досоздаёт записи 6–10 (расширение по умолчанию), 11–20 (слоты MoodId) и 21–40 (слоты InfluenceId), если их нет.
    /// </summary>
    /// <summary>Досоздаёт записи 7–10, 11–20 (MoodId), 21–40 (InfluenceId) и 41–60 (привязка тем для инфо-функций). Id 6 создаётся в EnsureDefaultTypes.</summary>
    private void EnsureSlots()
    {
      for (int id = 7; id <= 10; id++)
      {
        if (!_byId.ContainsKey(id))
          _byId[id] = new SituationTypeRecord { Id = id, MoodId = EmptySlotValue, InfluenceId = EmptySlotValue, ThemeTypeId = -1, Description = "" };
      }
      for (int id = 11; id <= 20; id++)
      {
        if (!_byId.ContainsKey(id))
          _byId[id] = new SituationTypeRecord { Id = id, MoodId = EmptySlotValue, InfluenceId = EmptySlotValue, ThemeTypeId = -1, Description = "" };
      }
      for (int id = 21; id <= 40; id++)
      {
        if (!_byId.ContainsKey(id))
          _byId[id] = new SituationTypeRecord { Id = id, MoodId = EmptySlotValue, InfluenceId = EmptySlotValue, ThemeTypeId = -1, Description = "" };
      }
      for (int id = 41; id <= 60; id++)
      {
        if (!_byId.ContainsKey(id))
          _byId[id] = new SituationTypeRecord { Id = id, MoodId = EmptySlotValue, InfluenceId = EmptySlotValue, ThemeTypeId = -1, Description = "" };
      }
    }

    /// <summary>
    /// Вызвать EnsureSlots и сохранить, если были добавлены новые записи. Вызывается при открытии страницы.
    /// </summary>
    public (bool Saved, string Error) EnsureSlotsAndSaveIfNeeded()
    {
      int countBefore = _byId.Count;
      EnsureSlots();
      RebuildIndexes();
      if (_byId.Count > countBefore)
      {
        var (ok, err) = Save();
        return (ok, err);
      }
      return (true, null);
    }

    #endregion

    #region Доступ

    /// <summary>Получить тип по ID</summary>
    public SituationTypeRecord GetById(int id)
    {
      return _byId.TryGetValue(id, out var r) ? r : null;
    }

    /// <summary>ID типа ситуации по MoodId (настроение). 0 если не найдено. 0 = Нормальное — валидный код.</summary>
    public int GetIdByMoodId(int moodId)
    {
      return moodId < 0 ? 0 : (_byMoodId.TryGetValue(moodId, out int id) ? id : 0);
    }

    /// <summary>ID типа ситуации по InfluenceId (воздействие). 0 если не найдено.</summary>
    public int GetIdByInfluenceId(int influenceId)
    {
      return influenceId < 0 ? 0 : (_byInfluenceId.TryGetValue(influenceId, out int id) ? id : 0);
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

    /// <summary>ThemeTypeId по ID типа ситуации (1–10, 41–60). 0 если не задано или запись не найдена.</summary>
    public int GetThemeTypeIdBySituationTypeId(int situationTypeId)
    {
      var rec = GetById(situationTypeId);
      if (rec == null || rec.ThemeTypeId <= 0) return 0;
      return rec.ThemeTypeId;
    }

    /// <summary>ThemeTypeId, используемые в дефолтных слотах (Id 1–10). Не удалять эти темы из справочника типов тем.</summary>
    public IReadOnlyList<int> GetThemeTypeIdsUsedInDefaultSlots()
    {
      var list = new List<int>();
      for (int id = 1; id <= 10; id++)
      {
        var rec = GetById(id);
        if (rec != null && rec.ThemeTypeId > 0 && !list.Contains(rec.ThemeTypeId))
          list.Add(rec.ThemeTypeId);
      }
      return list;
    }

    /// <summary>Обязательная запись по умолчанию (1–5) — удалять нельзя</summary>
    public static bool IsRequiredDefault(int id)
    {
      return Array.IndexOf(DefaultRequiredIds, id) >= 0;
    }

    /// <summary>
    /// Очистить справочник, оставив только дефолтные записи (ID 1–5).
    /// Вызывается при переходе с стадии 4 на 3.
    /// </summary>
    /// <returns>(true, null) при успехе; (false, сообщение) при ошибке</returns>
    public (bool Success, string Error) ClearExceptDefaults()
    {
      var toRemove = _byId.Keys.Where(id => !IsRequiredDefault(id)).ToList();
      foreach (int id in toRemove)
      {
        if (_byId.TryGetValue(id, out var rec))
        {
          _byId.Remove(id);
          if (rec.MoodId >= 0) _byMoodId.Remove(rec.MoodId);
          if (rec.InfluenceId >= 0) _byInfluenceId.Remove(rec.InfluenceId);
        }
      }
      EnsureDefaultTypes();
      RebuildIndexes();
      return Save();
    }

    #endregion

    #region Создание и удаление

    /// <summary>Создать запись по MoodId (ID 11–20). Дубликаты не создаются. 0 = Нормальное.</summary>
    public (int Id, string Error) AddByMoodId(int moodId, string description)
    {
      if (moodId < 0) return (0, "MoodId должен быть >= 0 (0=Нормальное)");
      if (FindByMoodId(moodId) != null) return (0, "Запись с таким MoodId уже есть");
      if (_nextMoodId > 20) return (0, "Превышен лимит ID для настроения (11–20)");
      int id = _nextMoodId++;
      var rec = new SituationTypeRecord { Id = id, MoodId = moodId, InfluenceId = EmptySlotValue, ThemeTypeId = -1, Description = description ?? "" };
      _byId[id] = rec;
      _byMoodId[moodId] = id;
      return (id, null);
    }

    /// <summary>Создать запись по InfluenceId (ID 21+). Дубликаты не создаются.</summary>
    public (int Id, string Error) AddByInfluenceId(int influenceId, string description)
    {
      if (influenceId < 0) return (0, "InfluenceId должен быть >= 0");
      if (FindByInfluenceId(influenceId) != null) return (0, "Запись с таким InfluenceId уже есть");
      int id = _nextInfluenceId++;
      var rec = new SituationTypeRecord { Id = id, MoodId = EmptySlotValue, InfluenceId = influenceId, ThemeTypeId = -1, Description = description ?? "" };
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
      if (rec.MoodId >= 0) _byMoodId.Remove(rec.MoodId);
      if (rec.InfluenceId >= 0) _byInfluenceId.Remove(rec.InfluenceId);
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
        if (r.MoodId >= 0) _byMoodId[r.MoodId] = r.Id;
        if (r.InfluenceId >= 0) _byInfluenceId[r.InfluenceId] = r.Id;
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
        if (!int.TryParse(p[1], out int moodId)) moodId = EmptySlotValue;
        if (!int.TryParse(p[2], out int influenceId)) influenceId = EmptySlotValue;
        int themeTypeId = -1;
        var desc = "";
        if (p.Length >= 5)
        {
          int.TryParse(p[3], out themeTypeId);
          desc = p[4] ?? "";
        }
        else
          desc = p.Length > 3 ? p[3] : "";
        _byId[id] = new SituationTypeRecord { Id = id, MoodId = moodId, InfluenceId = influenceId, ThemeTypeId = themeTypeId, Description = desc };
      }
    }

    /// <summary>Проверка на дубликаты MoodId в слотах 11–20 и InfluenceId в слотах 21–40. Пустые слоты (0 и ниже) не считаются.</summary>
    /// <returns>(true, null) если валидно; (false, "сообщение") при дублях</returns>
    public (bool Valid, string Error) ValidateRecordsNoDuplicates(
        IEnumerable<SituationTypeRecord> moodRecords,
        IEnumerable<SituationTypeRecord> influenceRecords)
    {
      if (moodRecords != null)
      {
        var usedMood = new HashSet<int>();
        foreach (var r in moodRecords)
        {
          if (r == null || r.Id < 11 || r.Id > 20) continue;
          if (r.MoodId < 0) continue;
          if (usedMood.Contains(r.MoodId))
            return (false, $"Дубликат настроения: «{r.MoodId}» встречается в нескольких слотах (11–20). Укажите уникальные значения.");
          usedMood.Add(r.MoodId);
        }
      }
      if (influenceRecords != null)
      {
        var usedInfluence = new HashSet<int>();
        foreach (var r in influenceRecords)
        {
          if (r == null || r.Id < 21 || r.Id > 40) continue;
          if (r.InfluenceId < 0) continue;
          if (usedInfluence.Contains(r.InfluenceId))
            return (false, $"Дубликат воздействия: «{r.InfluenceId}» встречается в нескольких слотах (21–40). Укажите уникальные значения.");
          usedInfluence.Add(r.InfluenceId);
        }
      }
      return (true, null);
    }

    /// <summary>Проверка уникальности пары (ID типа ситуации, ThemeTypeId): один ThemeTypeId не может быть привязан к разным ID. Записи с ThemeTypeId&lt;=0 пропускаются.</summary>
    public (bool Valid, string Error) ValidateThemeTypeIdUniqueness(IEnumerable<SituationTypeRecord> allRecordsWithTheme)
    {
      if (allRecordsWithTheme == null) return (true, null);
      var themeToId = new Dictionary<int, int>();
      foreach (var r in allRecordsWithTheme)
      {
        if (r == null || r.ThemeTypeId <= 0) continue;
        if (themeToId.TryGetValue(r.ThemeTypeId, out int existingId))
          return (false, $"Тема с ID {r.ThemeTypeId} уже привязана к типу ситуации ID {existingId}. Выберите другую тему или освободите слот ID {existingId}.");
        themeToId[r.ThemeTypeId] = r.Id;
      }
      return (true, null);
    }

    /// <summary>Синхронизировать данные из переданных записей в _byId. Вызывать перед Save, чтобы гарантировать сохранение отредактированных значений из UI.</summary>
    public void UpdateFromRecords(IEnumerable<SituationTypeRecord> records)
    {
      if (records == null) return;
      foreach (var r in records)
      {
        if (r == null || r.Id <= 0) continue;
        if (_byId.TryGetValue(r.Id, out var existing))
        {
          existing.MoodId = r.MoodId;
          existing.InfluenceId = r.InfluenceId;
          existing.ThemeTypeId = r.ThemeTypeId;
          existing.Description = r.Description ?? "";
        }
      }
    }

    /// <summary>Сохранить справочник</summary>
    public (bool Success, string Error) Save()
    {
      try
      {
        RebuildIndexes();
        EnsureDirectory();
        var path = Path.Combine(_dataPath, FileName);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.SituationTypesFormat,
          FileValidator.FileHeaders.SituationTypesDesc
        };
        foreach (var r in _byId.Values.OrderBy(x => x.Id))
          lines.Add($"{r.Id}|{r.MoodId}|{r.InfluenceId}|{r.ThemeTypeId}|{r.Description ?? ""}");

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

    /// <summary>Содержимое файла по умолчанию при первом запуске (только записи 1–10). Редактирование — в situation_types.dat.</summary>
    private static readonly string[] DefaultSituationTypesFileLines =
    {
      "1|-1|-1|11|Ответное действие",
      "2|-1|-1|11|Запуск автоматизма",
      "3|-1|-1|10|Нужно осмысление",
      "4|-1|-1|5|Экспериментировать",
      "5|-1|-1|7|Игнор оператора",
      "6|-1|-1|4|Стимул с пульта",
      "7|-1|-1|1|Негативный эффект моторного автоматизма",
      "8|-1|-1|16|Есть объект высокой значимости",
      "9|-1|-1|-1|",
      "10|-1|-1|-1|"
    };

    private void CreateDefaultSituationTypesFile(string path)
    {
      try
      {
        var lines = new List<string>
        {
          FileValidator.FileHeaders.SituationTypesFormat,
          FileValidator.FileHeaders.SituationTypesDesc
        };
        lines.AddRange(DefaultSituationTypesFileLines);
        File.WriteAllLines(path, lines);
      }
      catch (Exception ex)
      {
        Logger.Warning($"Не удалось создать файл типов ситуаций по умолчанию: {ex.Message}");
      }
    }

    /// <summary>Читает из situation_types.dat записи с Id в диапазоне 1–10.</summary>
    private List<SituationTypeRecord> ReadDefaultTypeDefinitionsFromFile()
    {
      var path = Path.Combine(_dataPath, FileName);
      var result = new List<SituationTypeRecord>();
      if (!File.Exists(path) || !FileValidator.IsValidSituationTypeFile(path))
        return result;
      foreach (var line in File.ReadLines(path))
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 4) continue;
        if (!int.TryParse(p[0], out int id) || id < 1 || id > 10) continue;
        if (!int.TryParse(p[1], out int moodId)) moodId = EmptySlotValue;
        if (!int.TryParse(p[2], out int influenceId)) influenceId = EmptySlotValue;
        int themeTypeId = -1;
        var desc = "";
        if (p.Length >= 5)
        {
          int.TryParse(p[3], out themeTypeId);
          desc = p[4] ?? "";
        }
        else
          desc = p.Length > 3 ? p[3] : "";
        result.Add(new SituationTypeRecord { Id = id, MoodId = moodId, InfluenceId = influenceId, ThemeTypeId = themeTypeId, Description = desc });
      }
      return result;
    }

    /// <summary>ID типов тем, зарезервированные в дефолтных типах ситуаций (записи 1–10 из файла). Новый ID темы не должен совпадать с ними.</summary>
    public static IReadOnlyList<int> GetThemeTypeIdsReservedInDefaultTypes()
    {
      if (_instance == null) return Array.Empty<int>();
      var list = new List<int>();
      for (int id = 1; id <= 10; id++)
      {
        var rec = _instance.GetById(id);
        if (rec != null && rec.ThemeTypeId > 0 && !list.Contains(rec.ThemeTypeId))
          list.Add(rec.ThemeTypeId);
      }
      return list;
    }

    private void EnsureDefaultTypes()
    {
      foreach (var rec in ReadDefaultTypeDefinitionsFromFile())
      {
        if (!_byId.ContainsKey(rec.Id))
          _byId[rec.Id] = rec;
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
