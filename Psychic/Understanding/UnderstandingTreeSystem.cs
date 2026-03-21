using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Дерево понимания ситуации (Understanding tree).
  /// 3 уровня: Mood, EmotionID, SituationID. Активируется после дерева автоматизмов.
  /// </summary>
  public sealed class UnderstandingTreeSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly string _dataPath;
    private readonly Dictionary<int, UnderstandingTreeNode> _nodesById = new Dictionary<int, UnderstandingTreeNode>();
    private int _lastNodeId;
    private bool _disposed;

    #region Инициализация

    private static UnderstandingTreeSystem _instance;

    /// <summary>Глобальный экземпляр</summary>
    public static UnderstandingTreeSystem Instance => _instance ??
        throw new InvalidOperationException("UnderstandingTreeSystem не инициализирован.");

    /// <summary>Признак инициализации</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Инициализировать экземпляр</summary>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("UnderstandingTreeSystem уже инициализирован.");
      _instance = new UnderstandingTreeSystem(psychicDataPath);
    }

    private UnderstandingTreeSystem(string psychicDataPath)
    {
      _dataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Understanding")
          : Path.Combine(psychicDataPath, "Understanding");

      EnsureDirectory();
      Load();

      if (Tree.Children.Count == 0)
      {
        CreateBasicUnderstandingTree();
        Save();
      }
    }

    #endregion

    #region Поля

    /// <summary>Корневой узел</summary>
    public UnderstandingTreeNode Tree { get; } = new UnderstandingTreeNode { Id = 0 };

    /// <summary>ID последнего активного узла</summary>
    public int DetectedActiveLastUnderstandingNodeId { get; private set; }

    /// <summary>Данные для активации дерева проблем (autTreeID, situationTreeID, themeId, purposeId)</summary>
    public (int AutTreeId, int SituationTreeId, int ThemeId, int PurposeId) ProblemTreeInfo { get; private set; }

    private SituationImageSystem _situationImageSystem;
    private SituationTypeSystem _situationTypeSystem;
    private ThemeImageSystem _themeImageSystem;
    private PurposeImageSystem _purposeImageSystem;

    /// <summary>Установить зависимости для дерева понимания (вызывается при инициализации движка после создания всех систем)</summary>
    public void SetDependencies(
      SituationImageSystem situationImageSystem,
      SituationTypeSystem situationTypeSystem,
      ThemeImageSystem themeImageSystem,
      PurposeImageSystem purposeImageSystem)
    {
      _situationImageSystem = situationImageSystem;
      _situationTypeSystem = situationTypeSystem;
      _themeImageSystem = themeImageSystem;
      _purposeImageSystem = purposeImageSystem;
    }

    #endregion

    #region Активация

    /// <summary>
    /// Активация дерева понимания. Вызывать после активации дерева автоматизмов.
    /// </summary>
    /// <param name="activationType">1 — объективная, 2 — произвольная переактивация</param>
    /// <param name="automatizmTreeNodeId">ID активного узла дерева автоматизмов</param>
    /// <param name="baseId">Базовое состояние (-1/0/1)</param>
    /// <param name="emotionId">ID образа эмоций</param>
    /// <param name="problemTree">Дерево проблем для обновления</param>
    /// <param name="situationContext">Контекст для выбора типа ситуации (наличие автоматизма в ветке, настроение/кнопки с пульта) или null</param>
    public void ActivateSituation(
      int activationType,
      int automatizmTreeNodeId,
      int baseId,
      int emotionId,
      ProblemTreeSystem problemTree,
      SituationImageContext situationContext = null)
    {
      if (AppGlobalState.EvolutionStage < 4) return;
      if (AppGlobalState.Lifetime < 4) return;

      int situationImageId = _situationImageSystem != null
          ? SituationImageService.GetCurSituationImageId(_situationImageSystem, automatizmTreeNodeId, situationContext)
          : 0;
      if (situationImageId == 0 && automatizmTreeNodeId > 0 && _situationImageSystem != null)
      {
        situationImageId = SituationImageService.GetCurSituationImageId(_situationImageSystem, 0, situationContext);
      }
      if (situationImageId == 0)
      {
        ProblemTreeInfo = (automatizmTreeNodeId, 0, 0, 0);
        if (problemTree != null)
          problemTree.UpdateActiveBranchFromUnderstandingInfo(automatizmTreeNodeId, 0);
        return;
      }

      int mood = baseId;
      int lev1 = mood;
      int lev2 = emotionId;
      int lev3 = situationImageId;
      var condArr = new[] { lev1, lev2, lev3 };

      _lock.EnterWriteLock();
      try
      {
        DetectedActiveLastUnderstandingNodeId = 0;
        int stepCount = 0;
        var foundId = FindOrExtendBranch(0, condArr, Tree, ref stepCount);
        DetectedActiveLastUnderstandingNodeId = foundId;

        int situationTypeId = _situationImageSystem?.GetById(situationImageId)?.SituationTypeId ?? 0;
        int themeId = RunNewThemeBySituationTypeId(situationTypeId, preferPulseResolvedTheme: true);
        int purposeId = GetMentalPurposeSimplified(baseId, emotionId, situationImageId);
        ProblemTreeInfo = (automatizmTreeNodeId, situationImageId, themeId, purposeId);

        if (problemTree != null)
          problemTree.UpdateActiveBranchFromUnderstandingInfo(automatizmTreeNodeId, situationImageId, themeId, purposeId);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    private int FindOrExtendBranch(int level, int[] cond, UnderstandingTreeNode node, ref int stepCount)
    {
      if (node == null || cond == null || level >= cond.Length)
        return node?.Id ?? 0;

      foreach (var child in node.Children)
      {
        int nodeVal = level == 0 ? child.Mood : level == 1 ? child.EmotionId : child.SituationId;
        if (nodeVal != cond[level])
          continue;

        stepCount = level + 1;
        if (level == 2)
          return child.Id;
        var found = FindOrExtendBranch(level + 1, cond, child, ref stepCount);
        if (found > 0)
          return found;
        return child.Id;
      }

      var newNode = AddBranch(node, level, cond);
      return newNode?.Id ?? 0;
    }

    /// <summary>
    /// Обновить тему мышления по коду события агента (<see cref="AgentEventsCatalog.Codes"/>).
    /// Возвращает ID образа темы; при отсутствии привязки используется тема по умолчанию.
    /// Вызывается при активации дерева понимания и из модулей (например, <see cref="AgentEventsCatalog.Codes.AgentIgnore"/>, <see cref="AgentEventsCatalog.Codes.HighObjectImportance"/>).
    /// </summary>
    /// <param name="situationTypeCode">Код из <see cref="AgentEventsCatalog"/>.</param>
    /// <returns>ID образа темы (ThemeImage) или 0 при ошибке.</returns>
    public int UpdateThemeByTrigger(int situationTypeCode)
    {
      return RunNewThemeBySituationTypeId(situationTypeCode, preferPulseResolvedTheme: false);
    }

    /// <summary>
    /// Обновить тему по триггеру и перезапустить дерево проблем с новой темой (для вызовов из модулей оценки автоматизмов, значимости и т.п.).
    /// </summary>
    /// <param name="situationTypeCode">Код из <see cref="AgentEventsCatalog"/>.</param>
    /// <param name="problemTree">Дерево проблем для обновления активной ветки.</param>
    public void UpdateThemeByTriggerAndRefreshProblemTree(int situationTypeCode, ProblemTreeSystem problemTree)
    {
      int themeId = UpdateThemeByTrigger(situationTypeCode);
      if (themeId == 0 || problemTree == null) return;
      var (autId, sitId, _, purposeId) = ProblemTreeInfo;
      ProblemTreeInfo = (autId, sitId, themeId, purposeId);
      problemTree.UpdateActiveBranchFromUnderstandingInfo(autId, sitId, themeId, purposeId);
    }

    /// <summary>Создать или получить образ темы по коду типа ситуации; при отсутствии привязки — тема по умолчанию.
    /// Конкуренция по весу: если уже есть активная тема с большим весом, она остаётся (новая не перекрывает).</summary>
    private int RunNewThemeBySituationTypeId(int situationTypeId, bool preferPulseResolvedTheme)
    {
      if (_themeImageSystem == null) return 0;
      try
      {
        var pulsCount = Math.Max(1, AppGlobalState.Lifetime);
        int themeTypeId = 0;
        if (preferPulseResolvedTheme)
        {
          themeTypeId = AppGlobalState.ResolvedThinkingThemeTypeId;
          if (themeTypeId <= 0 && _situationTypeSystem != null)
            themeTypeId = _situationTypeSystem.GetThemeTypeIdBySituationTypeId(situationTypeId);
        }
        else if (_situationTypeSystem != null)
        {
          themeTypeId = _situationTypeSystem.GetThemeTypeIdByAgentEventCode(situationTypeId);
          if (themeTypeId <= 0)
            themeTypeId = _situationTypeSystem.GetThemeTypeIdBySituationTypeId(situationTypeId);
        }
        if (themeTypeId <= 0)
          themeTypeId = _themeImageSystem.DefaultThemeTypeId;
        int weight = _themeImageSystem.GetDefaultWeightForThemeType(themeTypeId);
        var (id, newRecord) = _themeImageSystem.CreateThemeImageOrGet(weight, themeTypeId, pulsCount);
        if (newRecord == null) return id;

        int currentThemeId = ProblemTreeInfo.ThemeId;
        if (currentThemeId > 0)
        {
          var currentRec = _themeImageSystem.GetById(currentThemeId);
          if (currentRec != null && currentRec.Weight > newRecord.Weight)
            return currentThemeId;
        }

        return id;
      }
      catch
      {
        return 0;
      }
    }

    /// <summary>Упрощённая логика цели: получает или создаёт образ цели по настроению, эмоции и ситуации.</summary>
    private int GetMentalPurposeSimplified(int baseId, int emotionId, int situationImageId)
    {
      if (situationImageId <= 0) return 0;
      if (_purposeImageSystem == null) return 0;
      try
      {
        var target = 2;
        var moodId = baseId >= -1 && baseId <= 1 ? baseId : 0;
        var (id, _) = _purposeImageSystem.CreatePurposeImageOrGet(target, moodId, emotionId, situationImageId);
        return id;
      }
      catch
      {
        return 0;
      }
    }

    private UnderstandingTreeNode AddBranch(UnderstandingTreeNode parent, int level, int[] cond)
    {
      if (parent == null || level >= cond.Length) return null;
      int mood = level >= 0 ? cond[0] : 0;
      int emotionId = level >= 1 ? cond[1] : 0;
      int situationId = level >= 2 ? cond[2] : 0;

      var existing = parent.Children.FirstOrDefault(c =>
          (level < 1 || c.Mood == mood) &&
          (level < 2 || c.EmotionId == emotionId) &&
          (level < 3 || c.SituationId == situationId));
      if (existing != null)
        return existing;

      _lastNodeId++;
      var node = new UnderstandingTreeNode
      {
        Id = _lastNodeId,
        ParentId = parent.Id,
        ParentNode = parent,
        Mood = mood,
        EmotionId = emotionId,
        SituationId = situationId
      };
      parent.Children.Add(node);
      _nodesById[node.Id] = node;
      return node;
    }

    #endregion

    #region Базовая инициализация

    private void CreateBasicUnderstandingTree()
    {
      foreach (int mood in new[] { -1, 0, 1 })
      {
        _lastNodeId++;
        var node = new UnderstandingTreeNode
        {
          Id = _lastNodeId,
          ParentId = 0,
          ParentNode = Tree,
          Mood = mood,
          EmotionId = 0,
          SituationId = 0
        };
        Tree.Children.Add(node);
        _nodesById[node.Id] = node;
      }
    }

    #endregion

    #region Load / Save

    private const string FileName = "understanding_tree.dat";

    private void EnsureDirectory()
    {
      if (!string.IsNullOrEmpty(_dataPath) && !Directory.Exists(_dataPath))
        Directory.CreateDirectory(_dataPath);
    }

    private void Load()
    {
      var path = Path.Combine(_dataPath, FileName);
      Tree.Children.Clear();
      _nodesById.Clear();
      _nodesById[0] = Tree;
      _lastNodeId = 0;
      if (!File.Exists(path) || !FileValidator.IsValidUnderstandingTreeFile(path))
        return;

      var lines = File.ReadAllLines(path).ToList();
      int lineNum = 0;
      foreach (var line in lines)
      {
        lineNum++;
        if (lineNum <= 2) continue;
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 5) continue;
        if (!int.TryParse(p[0], out int id)) continue;
        if (!int.TryParse(p[1], out int parentId)) continue;
        if (!int.TryParse(p[2], out int mood)) continue;
        if (!int.TryParse(p[3], out int emotionId)) continue;
        if (!int.TryParse(p[4], out int situationId)) continue;

        var parent = parentId == 0 ? Tree : (_nodesById.TryGetValue(parentId, out var pn) ? pn : null);
        if (parent == null) continue;

        var node = new UnderstandingTreeNode
        {
          Id = id,
          ParentId = parentId,
          ParentNode = parent,
          Mood = mood,
          EmotionId = emotionId,
          SituationId = situationId
        };
        parent.Children.Add(node);
        _nodesById[id] = node;
        if (id > _lastNodeId) _lastNodeId = id;
      }
    }

    /// <summary>Очистить дерево понимания в памяти и в файле, оставить только базовую структуру (для перехода на младшую стадию).</summary>
    public (bool Success, string Error) Clear()
    {
      _lock.EnterWriteLock();
      try
      {
        Tree.Children.Clear();
        _nodesById.Clear();
        _nodesById[0] = Tree;
        _lastNodeId = 0;
        CreateBasicUnderstandingTree();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
      return Save();
    }

    /// <summary>Сохранить дерево</summary>
    public (bool Success, string Error) Save()
    {
      _lock.EnterReadLock();
      try
      {
        EnsureDirectory();
        var path = Path.Combine(_dataPath, FileName);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.UnderstandingTreeFormat,
          FileValidator.FileHeaders.UnderstandingTreeDesc
        };
        foreach (var node in Tree.Children)
          CollectLines(node, lines);

        var result = FileValidator.SafeSaveFile(
            path,
            lines,
            p => FileValidator.IsValidUnderstandingTreeFile(p),
            minLinesCount: 2,
            fileDescription: "дерева понимания ситуации");

        return result.Success ? (true, null) : (false, result.ErrorMessage);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    private void CollectLines(UnderstandingTreeNode n, List<string> lines)
    {
      lines.Add($"{n.Id}|{n.ParentId}|{n.Mood}|{n.EmotionId}|{n.SituationId}");
      foreach (var c in n.Children)
        CollectLines(c, lines);
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы, сохраняет дерево понимания ситуации на диск</summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        var (ok, err) = Save();
        if (!ok && !string.IsNullOrEmpty(err))
          Logger.Warning($"Ошибка сохранения Understanding tree: {err}");
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
      finally
      {
        _disposed = true;
      }
    }

    #endregion
  }
}
