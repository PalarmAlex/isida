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
    private readonly SituationTypeSystem _situationTypeSystem;
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
    /// <param name="psychicDataPath">Путь к данным психики</param>
    /// <param name="situationTypeSystem">Справочник типов ситуаций (должен быть инициализирован ранее)</param>
    public static void InitializeInstance(string psychicDataPath, SituationTypeSystem situationTypeSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("SituationImageSystem уже инициализирован.");
      if (situationTypeSystem == null)
        throw new ArgumentNullException(nameof(situationTypeSystem));
      _instance = new SituationImageSystem(psychicDataPath, situationTypeSystem);
    }

    private SituationImageSystem(string psychicDataPath, SituationTypeSystem situationTypeSystem)
    {
      _situationTypeSystem = situationTypeSystem ?? throw new ArgumentNullException(nameof(situationTypeSystem));
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

    /// <summary>ID текущей ситуации для активации Understanding (без контекста — тип 3 при nodeId==0, иначе 4).</summary>
    public int GetCurSituationImageId(int automatizmTreeNodeId)
    {
      return GetCurSituationImageId(automatizmTreeNodeId, null);
    }

    /// <summary>ID текущей ситуации с учётом контекста. Логика по приоритетам.</summary>
    /// <remarks>
    /// 1) nodeId==0 → тип 3 (NeedThinking). 2) LastRun&gt;0 и истекло ожидание → тип 5 (OperatorIgnore).
    /// 3) LastRun&gt;0 и ожидание не истекло → тип 1 (ResponseAction). 4) hasAutomatismInBranch → тип 2 (AutomatizmRun).
    /// 5) из context: настроение/кнопки (типы 11–17, 21–37 при наличии в справочнике). 6) иначе тип 4 (Experiment).
    /// </remarks>
    public int GetCurSituationImageId(int automatizmTreeNodeId, SituationImageContext context)
    {
      if (automatizmTreeNodeId < 0) return 0;
      if (_situationTypeSystem == null) return 0;

      int typeId = ResolveSituationTypeId(automatizmTreeNodeId, context);
      if (typeId <= 0 || !_situationTypeSystem.Exists(typeId))
        typeId = GetDefaultSituationTypeId();
      if (typeId <= 0) return 0;

      var (id, _) = CreateOrGet(automatizmTreeNodeId, typeId, true);
      return id;
    }

    /// <summary>Определить тип ситуации по приоритетам.</summary>
    private int ResolveSituationTypeId(int automatizmTreeNodeId, SituationImageContext context)
    {
      const int NeedThinking = 3;
      const int Experiment = 4;
      const int OperatorIgnore = 5;
      const int ResponseAction = 1;
      const int AutomatizmRun = 2;

      if (automatizmTreeNodeId == 0)
        return NeedThinking;

      int lastRun = AppGlobalState.LastRunAutomatizmPulsCount;
      int waitingPeriod = AppGlobalState.WaitingPeriodForActionsVal;
      int currentPulse = GlobalTimer.GlobalPulsCount;

      if (lastRun > 0)
      {
        if ((lastRun + waitingPeriod) < currentPulse)
          return OperatorIgnore;
        return ResponseAction;
      }

      if (context?.HasAutomatismInBranch == true)
        return AutomatizmRun;

      int sitId = ResolvePultMoodOrActionType(context);
      if (sitId > 0 && _situationTypeSystem.Exists(sitId))
        return sitId;

      return Experiment;
    }

    private static int ResolvePultMoodOrActionType(SituationImageContext context)
    {
      if (context == null) return 0;
      int maxPrior = 0;
      int sitId = 0;
      if (context.MoodId != 0)
      {
        int prior = GetPrioritetOfPultMoodActions(context.MoodId);
        if (prior > maxPrior) { maxPrior = prior; sitId = 10 + context.MoodId; }
      }
      if (context.ActionIds != null && context.ActionIds.Length > 0)
      {
        foreach (int actId in context.ActionIds)
        {
          int prior = GetPrioritetOfPultButtonActions(actId);
          if (prior > maxPrior) { maxPrior = prior; sitId = 20 + actId; }
        }
      }
      return sitId;
    }

    /// <summary>Приоритет настроения с пульта.</summary>
    private static int GetPrioritetOfPultMoodActions(int moodId)
    {
      switch (moodId)
      {
        case 1: return 1;
        case 6: return 2;
        case 7: return 3;
        case 3: return 4;
        case 4: return 5;
        case 2: return 6;
        case 5: return 7;
        default: return 0;
      }
    }

    /// <summary>Приоритет кнопки действия с пульта.</summary>
    private static int GetPrioritetOfPultButtonActions(int actionId)
    {
      switch (actionId)
      {
        case 6: return 1;
        case 16: return 2;
        case 9: return 3;
        case 11: return 4;
        case 2: return 5;
        case 1: return 6;
        case 17: return 7;
        case 5: return 8;
        case 14: return 9;
        case 15: return 10;
        case 12: return 11;
        case 7: return 12;
        case 8: return 13;
        case 13: return 3;
        case 4: return 15;
        case 10: return 16;
        case 3: return 17;
        default: return 0;
      }
    }

    private int GetDefaultSituationTypeId()
    {
      if (_situationTypeSystem == null) return 4;
      var t = _situationTypeSystem.GetByCode("Experiment") ?? _situationTypeSystem.GetById(4);
      return t?.Id ?? 4;
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
