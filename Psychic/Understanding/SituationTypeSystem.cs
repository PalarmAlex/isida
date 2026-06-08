using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Справочник типов ситуаций: связь ID с MoodId (настроение) и InfluenceId (воздействие).
  /// Привязка тем к событиям — через справочник. Редактируется на пульте.
  /// </summary>
  public sealed class SituationTypeSystem : IDisposable
  {
    /// <summary>Код «отсутствие значения» для слотов. 0 — Нормальное (настроение).</summary>
    public const int EmptySlotValue = -1;

    private readonly string _dataPath;
    private readonly Dictionary<int, SituationTypeRecord> _byId = new Dictionary<int, SituationTypeRecord>();
    private readonly Dictionary<int, int> _byMoodId = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _byInfluenceId = new Dictionary<int, int>();
    private int _nextMoodId = 21;
    private int _nextInfluenceId = 41;
    private bool _disposed;

    #region Инициализация

    private static SituationTypeSystem _instance;

    /// <summary>Глобальный экземпляр</summary>
    public static SituationTypeSystem Instance => _instance ??
        throw new InvalidOperationException("SituationTypeSystem не инициализирован.");

    /// <summary>Признак инициализации</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Инициализировать экземпляр</summary>
    public static void InitializeInstance(string dataFolderPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("SituationTypeSystem уже инициализирован.");
      _instance = new SituationTypeSystem(dataFolderPath);
    }

    private SituationTypeSystem(string dataFolderPath)
    {
      _dataPath = IsidaDataPaths.ResolvePsychicSubmoduleFolder(dataFolderPath, "Understanding");
      EnsureDirectory();
      var path = Path.Combine(_dataPath, FileName);
      if (!File.Exists(path))
        CreateDefaultSituationTypesFile(path);
      Load();
      EnsureSlots();
      RebuildIndexes();
    }

    /// <summary>
    /// Досоздаёт слоты: 1–20 (события), 21–40 (настроение), 41–60 (воздействие). Без записей по умолчанию.
    /// </summary>
    private void EnsureSlots()
    {
      for (int id = 1; id <= 20; id++)
      {
        if (!_byId.ContainsKey(id))
          _byId[id] = new SituationTypeRecord { Id = id, MoodId = EmptySlotValue, InfluenceId = EmptySlotValue, ThemeTypeId = -1, EventAgentCode = -1 };
      }
      for (int id = 21; id <= 40; id++)
      {
        if (!_byId.ContainsKey(id))
          _byId[id] = new SituationTypeRecord { Id = id, MoodId = EmptySlotValue, InfluenceId = EmptySlotValue, ThemeTypeId = -1, EventAgentCode = -1 };
      }
      for (int id = 41; id <= 60; id++)
      {
        if (!_byId.ContainsKey(id))
          _byId[id] = new SituationTypeRecord { Id = id, MoodId = EmptySlotValue, InfluenceId = EmptySlotValue, ThemeTypeId = -1, EventAgentCode = -1 };
      }
    }

    /// <summary>
    /// Вызвать EnsureSlots и сохранить, если были добавлены новые записи. Вызывается при открытии страницы.
    /// </summary>
    public (bool Saved, string Error) EnsureSlotsAndSaveIfNeeded()
    {
      EnsureSlots();
      RebuildIndexes();
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

    /// <summary>ThemeTypeId по ID типа ситуации. 0 если не задано или запись не найдена.</summary>
    public int GetThemeTypeIdBySituationTypeId(int situationTypeId)
    {
      var rec = GetById(situationTypeId);
      if (rec == null || rec.ThemeTypeId <= 0) return 0;
      return rec.ThemeTypeId;
    }

    /// <summary>ThemeTypeId по коду события симбионта (слоты 1–20: EventAgentCode или совпадение Id с кодом для совместимости).</summary>
    public int GetThemeTypeIdByAgentEventCode(int eventCode)
    {
      if (eventCode <= 0) return 0;
      foreach (var r in _byId.Values)
      {
        if (r == null || r.Id < 1 || r.Id > 20) continue;
        if (r.EventAgentCode > 0)
        {
          if (r.EventAgentCode == eventCode && r.ThemeTypeId > 0) return r.ThemeTypeId;
        }
        else if (r.Id == eventCode && r.ThemeTypeId > 0)
          return r.ThemeTypeId;
      }
      return 0;
    }

    /// <summary>ID типов тем, используемые в любых слотах справочника. Не удалять эти темы — есть ссылки в situation_types.</summary>
    public IReadOnlyList<int> GetThemeTypeIdsInUse()
    {
      var list = new List<int>();
      foreach (var rec in _byId.Values)
      {
        if (rec != null && rec.ThemeTypeId > 0 && !list.Contains(rec.ThemeTypeId))
          list.Add(rec.ThemeTypeId);
      }
      return list;
    }

    /// <summary>
    /// Очистить справочник, оставив только слоты ID 1–10 (при переходе со стадии 4 на 3).
    /// </summary>
    public (bool Success, string Error) ClearExceptDefaults()
    {
      var toRemove = _byId.Keys.Where(id => id < 1 || id > 10).ToList();
      foreach (int id in toRemove)
      {
        if (_byId.TryGetValue(id, out var rec))
        {
          _byId.Remove(id);
          if (rec.MoodId >= 0) _byMoodId.Remove(rec.MoodId);
          if (rec.InfluenceId >= 0) _byInfluenceId.Remove(rec.InfluenceId);
        }
      }
      EnsureSlots();
      RebuildIndexes();
      return (true, null);
    }

    #endregion

    #region Создание и удаление

    /// <summary>Создать запись по MoodId (ID 21–40). Дубликаты не создаются. 0 = Нормальное.</summary>
    public (int Id, string Error) AddByMoodId(int moodId)
    {
      if (moodId < 0) return (0, "MoodId должен быть >= 0 (0=Нормальное)");
      if (FindByMoodId(moodId) != null) return (0, "Запись с таким MoodId уже есть");
      if (_nextMoodId > 40) return (0, "Превышен лимит ID для настроения (21–40)");
      int id = _nextMoodId++;
      var rec = new SituationTypeRecord
      {
        Id = id,
        MoodId = moodId,
        InfluenceId = EmptySlotValue,
        ThemeTypeId = -1,
        EventAgentCode = -1
      };
      _byId[id] = rec;
      _byMoodId[moodId] = id;
      return (id, null);
    }

    /// <summary>Создать запись по InfluenceId (ID 41–60). Дубликаты не создаются.</summary>
    public (int Id, string Error) AddByInfluenceId(int influenceId)
    {
      if (influenceId < 0) return (0, "InfluenceId должен быть >= 0");
      if (FindByInfluenceId(influenceId) != null) return (0, "Запись с таким InfluenceId уже есть");
      if (_nextInfluenceId > 60) return (0, "Превышен лимит ID для воздействия (41–60)");
      int id = _nextInfluenceId++;
      var rec = new SituationTypeRecord
      {
        Id = id,
        MoodId = EmptySlotValue,
        InfluenceId = influenceId,
        ThemeTypeId = -1,
        EventAgentCode = -1
      };
      _byId[id] = rec;
      _byInfluenceId[influenceId] = id;
      return (id, null);
    }

    /// <summary>Удалить запись.</summary>
    public (bool Success, string Error) Remove(int id)
    {
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
      _nextMoodId = _byId.Keys.Where(k => k >= 21 && k <= 40).DefaultIfEmpty(20).Max() + 1;
      _nextInfluenceId = _byId.Keys.Where(k => k >= 41 && k <= 60).DefaultIfEmpty(40).Max() + 1;
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
        int.TryParse(p[3], out themeTypeId);
        int eventAgentCode = -1;
        if (p.Length >= 5) int.TryParse(p[4], out eventAgentCode);
        _byId[id] = new SituationTypeRecord
        {
          Id = id,
          MoodId = moodId,
          InfluenceId = influenceId,
          ThemeTypeId = themeTypeId,
          EventAgentCode = eventAgentCode
        };
      }
    }

    /// <summary>Проверка на дубликаты MoodId в слотах 21–40 и InfluenceId в слотах 41–60. Пустые слоты (0 и ниже) не считаются.</summary>
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
          if (r == null || r.Id < 21 || r.Id > 40) continue;
          if (r.MoodId < 0) continue;
          if (usedMood.Contains(r.MoodId))
            return (false, $"Дубликат настроения: «{r.MoodId}» встречается в нескольких слотах (21–40). Укажите уникальные значения.");
          usedMood.Add(r.MoodId);
        }
      }
      if (influenceRecords != null)
      {
        var usedInfluence = new HashSet<int>();
        foreach (var r in influenceRecords)
        {
          if (r == null || r.Id < 41 || r.Id > 60) continue;
          if (r.InfluenceId < 0) continue;
          if (usedInfluence.Contains(r.InfluenceId))
            return (false, $"Дубликат воздействия: «{r.InfluenceId}» встречается в нескольких слотах (41–60). Укажите уникальные значения.");
          usedInfluence.Add(r.InfluenceId);
        }
      }
      return (true, null);
    }

    /// <summary>
    /// Уникальность <see cref="SituationTypeRecord.ThemeTypeId"/> внутри каждого диапазона слотов отдельно: 1–20 (события), 21–40 (настроение), 41–60 (воздействие).
    /// Одна и та же тема может повторяться в разных диапазонах. Записи с ThemeTypeId ≤ 0 не учитываются.
    /// </summary>
    public (bool Valid, string Error) ValidateThemeTypeIdUniqueness(IEnumerable<SituationTypeRecord> allRecordsWithTheme)
    {
      if (allRecordsWithTheme == null) return (true, null);
      var list = allRecordsWithTheme as IList<SituationTypeRecord> ?? allRecordsWithTheme.ToList();

      var ranges = new (int Min, int Max, string Label)[]
      {
        (1, 20, "события (1–20)"),
        (21, 40, "настроение (21–40)"),
        (41, 60, "воздействия (41–60)")
      };

      foreach (var rng in ranges)
      {
        var themeToSlotId = new Dictionary<int, int>();
        foreach (var r in list)
        {
          if (r == null || r.ThemeTypeId <= 0) continue;
          if (r.Id < rng.Min || r.Id > rng.Max) continue;
          if (themeToSlotId.TryGetValue(r.ThemeTypeId, out int existingSlot))
            return (false, $"В диапазоне «{rng.Label}» тема с ID {r.ThemeTypeId} уже привязана к слоту {existingSlot}. Выберите другую тему или освободите слот {existingSlot}.");
          themeToSlotId[r.ThemeTypeId] = r.Id;
        }
      }

      return (true, null);
    }

    /// <summary>
    /// Если выбрана тема (ThemeTypeId &gt; 0), в слоте должна быть задана связь: код события (1–20), настроение (21–40) или воздействие (41–60). Иначе сохранение недопустимо.
    /// </summary>
    public (bool Valid, string Error) ValidateThemeRequiresLinkField(IEnumerable<SituationTypeRecord> records)
    {
      if (records == null) return (true, null);
      foreach (var r in records)
      {
        if (r == null || r.ThemeTypeId <= 0) continue;
        if (r.Id >= 1 && r.Id <= 20)
        {
          if (r.EventAgentCode <= 0)
            return (false, $"Слот события {r.Id}: выбрана тема (ThemeTypeId={r.ThemeTypeId}), но не задан код события симбионта. Укажите событие или сбросьте тему (—).");
        }
        else if (r.Id >= 21 && r.Id <= 40)
        {
          if (r.MoodId < 0)
            return (false, $"Слот настроения {r.Id}: выбрана тема (ThemeTypeId={r.ThemeTypeId}), но настроение не задано (MoodId=-1). Укажите настроение или сбросьте тему (—).");
        }
        else if (r.Id >= 41 && r.Id <= 60)
        {
          if (r.InfluenceId < 0)
            return (false, $"Слот воздействия {r.Id}: выбрана тема (ThemeTypeId={r.ThemeTypeId}), но воздействие не задано (InfluenceId=-1). Укажите воздействие или сбросьте тему (—).");
        }
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
          existing.EventAgentCode = r.EventAgentCode;
        }
      }
    }

    /// <summary>Сохранить справочник</summary>
    public (bool Success, string Error) Save()
    {
      try
      {
        RebuildIndexes();
        var (themeUniqueOk, themeUniqueErr) = ValidateThemeTypeIdUniqueness(_byId.Values);
        if (!themeUniqueOk)
          return (false, themeUniqueErr);
        var (linkOk, linkErr) = ValidateThemeRequiresLinkField(_byId.Values);
        if (!linkOk)
          return (false, linkErr);
        EnsureDirectory();
        var path = Path.Combine(_dataPath, FileName);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.SituationTypesFormat,
          FileValidator.FileHeaders.SituationTypesDesc
        };
        foreach (var r in _byId.Values.OrderBy(x => x.Id))
          lines.Add($"{r.Id}|{r.MoodId}|{r.InfluenceId}|{r.ThemeTypeId}|{r.EventAgentCode}");

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

    private void CreateDefaultSituationTypesFile(string path)
    {
      try
      {
        var lines = new List<string>
        {
          FileValidator.FileHeaders.SituationTypesFormat,
          FileValidator.FileHeaders.SituationTypesDesc
        };
        File.WriteAllLines(path, lines);
      }
      catch (Exception ex)
      {
        Logger.Warning($"Не удалось создать файл типов ситуаций по умолчанию: {ex.Message}");
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
      _instance = null;
    }

    #endregion
  }
}
