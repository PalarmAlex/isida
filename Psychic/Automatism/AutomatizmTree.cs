using ISIDA.Common;
using ISIDA.Psychic.Understanding;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Узел дерева автоматизмов
  /// </summary>
  public class AutomatizmNode
  {
    /// <summary>
    /// ID узла
    /// </summary>
    public int ID { get; set; }

    /// <summary>
    /// Базовое состояние: -1 - Плохо, 0 - Норма, 1 - Хорошо
    /// </summary>
    public int BaseID { get; set; }

    /// <summary>
    /// ID эмоции
    /// </summary>
    public int EmotionID { get; set; }

    /// <summary>
    /// ID образа сочетания действий с Пульта
    /// </summary>
    public int ActivityID { get; set; }

    /// <summary>
    /// ID образа контекста сообщения: сочетание Tone и Mood
    /// </summary>
    public int ToneMoodID { get; set; }

    /// <summary>
    /// ID первого символа фразы
    /// </summary>
    public int SimbolID { get; set; }

    /// <summary>
    /// ID вербального образа
    /// </summary>
    public int VerbID { get; set; }

    /// <summary>
    /// Дочерние узлы (ветвление)
    /// </summary>
    public List<AutomatizmNode> Children { get; set; } = new List<AutomatizmNode>();

    /// <summary>
    /// ID родителя
    /// </summary>
    public int ParentID { get; set; }

    /// <summary>
    /// Адрес родителя
    /// </summary>
    public AutomatizmNode ParentNode { get; set; }
  }

  /// <summary>
  /// Система дерева автоматизмов
  /// </summary>
  public sealed class AutomatizmTreeSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private readonly string _psychicDataPath;

    #region Инициализация

    private static AutomatizmTreeSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы дерева автоматизмов
    /// </summary>
    public static AutomatizmTreeSystem Instance => _instance ??
        throw new InvalidOperationException("AutomatizmTreeSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы дерева автоматизмов
    /// </summary>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("AutomatizmTreeSystem уже инициализирован.");

      _instance = new AutomatizmTreeSystem(psychicDataPath);
    }

    private AutomatizmTreeSystem(string psychicDataPath = null)
    {
      _psychicDataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Automatism")
          : Path.Combine(psychicDataPath, "Automatism");

      try
      {
        EnsureDataDirectory();
        LoadAutomatizmTree();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Константы и поля

    private const string AutomatizmTreeFileName = "automatizm_tree";

    /// <summary>
    /// Основное дерево автоматизмов
    /// </summary>
    public AutomatizmNode Tree { get; private set; } = new AutomatizmNode { ID = 0 };

    /// <summary>
    /// Узлы дерева по ID (для быстрого доступа)
    /// </summary>
    private readonly Dictionary<int, AutomatizmNode> _nodesById = new Dictionary<int, AutomatizmNode>();

    /// <summary>
    /// Последовательность узлов активной ветки
    /// </summary>
    public List<int> ActiveBranchNodeArr { get; private set; } = new List<int>();

    /// <summary>
    /// ID последнего активного узла при активации дерева
    /// </summary>
    public int DetectedActiveLastNodeId { get; private set; }

    /// <summary>
    /// Нераспознанный остаток - НОВИЗНА
    /// </summary>
    public List<int> CurrentAutomatizmTreeEnd { get; private set; }

    /// <summary>
    /// Текущий шаг при активации
    /// </summary>
    private int _currentStepCount = 0;

    /// <summary>
    /// ID последнего созданного узла
    /// </summary>
    private int _lastAutomatizmNodeId = 0;

    /// <summary>
    /// Запрет на сканирование дерева в это время
    /// </summary>
    private bool _notAllowScanInTreeThisTime = false;

    /// <summary>
    /// Ссылка на дерево проблем (вторичная инициализация)
    /// </summary>
    private ProblemTreeSystem _problemTree;

    #endregion

    #region Вторичная инициализация

    /// <summary>
    /// Установить ссылку на дерево проблем (вызывать после инициализации ProblemTree)
    /// </summary>
    public void SetProblemTree(ProblemTreeSystem problemTree)
    {
      _problemTree = problemTree;
    }

    #endregion

    #region Управление узлами дерева

    /// <summary>
    /// Создает новый узел дерева автоматизмов.
    /// Не допускает создание узла, когда оба ActivityID и VerbID равны 0 (допускается только для прямых потомков корня — три базовые ветки).
    /// </summary>
    public (int Id, AutomatizmNode Node) CreateNewAutomatizmNode(
        AutomatizmNode parent,
        int id,
        int baseId,
        int emotionId,
        int activityId,
        int toneMoodId,
        int simbolId,
        int verbID,
        bool checkUnicum = true)
    {
      if (parent == null)
        return (0, null);

      // Не допускаем под не-корнем только узлы «только BaseID» (все остальные 0) — иначе разрешаем эмоцию, toneMood и т.д.
      if (checkUnicum && parent.ID != 0
          && emotionId == 0 && activityId == 0 && toneMoodId == 0 && simbolId == 0 && verbID == 0)
        return (0, null);

      try
      {
        if (checkUnicum)
        {
          var existing = FindAutomatizmTreeNodeFromCondition(baseId, emotionId, activityId, toneMoodId, simbolId, verbID);
          if (existing.Node != null)
            return existing;
        }

        if (id == 0)
        {
          _lastAutomatizmNodeId++;
          id = _lastAutomatizmNodeId;
        }
        else
        {
          if (_lastAutomatizmNodeId < id)
            _lastAutomatizmNodeId = id;
        }

        var node = new AutomatizmNode
        {
          ID = id,
          ParentNode = parent,
          ParentID = parent.ID,
          BaseID = baseId,
          EmotionID = emotionId,
          ActivityID = activityId,
          ToneMoodID = toneMoodId,
          SimbolID = simbolId,
          VerbID = verbID
        };

        parent.Children.Add(node);
        _nodesById[id] = node;

        return (id, node);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    /// <summary>
    /// Находит конечный узел по условиям
    /// </summary>
    public (int Id, AutomatizmNode Node) FindAutomatizmTreeNodeFromCondition(
        int baseId,
        int emotionId,
        int activityId,
        int toneMoodId,
        int simbolId,
        int verbID)
    {
      _lock.EnterReadLock();
      try
      {
        foreach (var child in Tree.Children)
        {
          var result = CheckAutomatizmTree(child, baseId, emotionId, activityId, toneMoodId, simbolId, verbID);
          if (result.Node != null)
            return result;
        }
        return (0, null);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Рекурсивно проверяет дерево на соответствие условиям
    /// </summary>
    private (int Id, AutomatizmNode Node) CheckAutomatizmTree(
        AutomatizmNode node,
        int baseId,
        int emotionId,
        int activityId,
        int toneMoodId,
        int simbolId,
        int verbID)
    {
      if (node.BaseID == baseId && node.EmotionID == emotionId &&
          node.ActivityID == activityId && toneMoodId == node.ToneMoodID &&
          node.SimbolID == simbolId && node.VerbID == verbID)
      {
        return (node.ID, node);
      }

      if (node.Children == null || node.Children.Count == 0)
        return (0, null);

      foreach (var child in node.Children)
      {
        var result = CheckAutomatizmTree(child, baseId, emotionId, activityId, toneMoodId, simbolId, verbID);
        if (result.Node != null)
          return result;
      }

      return (0, null);
    }

    /// <summary>
    /// Создает первые три ветки базовых состояний
    /// </summary>
    public void CreateBasicAutomatizmTree()
    {
      _notAllowScanInTreeThisTime = true;

      try
      {
        CreateNewAutomatizmNode(Tree, 0, -1, 0, 0, 0, 0, 0, false);
        CreateNewAutomatizmNode(Tree, 0, 0, 0, 0, 0, 0, 0, false);
        CreateNewAutomatizmNode(Tree, 0, 1, 0, 0, 0, 0, 0, false);
      }
      finally
      {
        _notAllowScanInTreeThisTime = false;
      }
    }

    /// <summary>
    /// Получает узел по ID
    /// </summary>
    public AutomatizmNode GetNodeById(int id)
    {
      _lock.EnterReadLock();
      try
      {
        return _nodesById.TryGetValue(id, out var node) ? node : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Полностью очищает дерево автоматизмов, сбрасывая все данные
    /// </summary>
    /// <returns>True если очистка выполнена успешно</returns>
    internal bool ClearTree()
    {
      _lock.EnterWriteLock();
      try
      {
        Tree = new AutomatizmNode { ID = 0 };
        _nodesById.Clear();
        _nodesById[0] = Tree;

        _lastAutomatizmNodeId = 0;
        DetectedActiveLastNodeId = 0;
        ActiveBranchNodeArr.Clear();
        CurrentAutomatizmTreeEnd = null;
        _currentStepCount = 0;
        _notAllowScanInTreeThisTime = false;

        CreateBasicAutomatizmTree();

        var saveResult = SaveAutomatizmTreeNoLock();
        if (!saveResult.Success)
        {
          Logger.Error($"Не удалось сохранить очищенное дерево: {saveResult.ErrorMessage}");
          return false;
        }

        return true;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Активация дерева

    /// <summary>
    /// Инициализирует структуры дерева
    /// </summary>
    private void InitializeTree()
    {
      Tree = new AutomatizmNode { ID = 0 };
      _nodesById.Clear();
      _nodesById[0] = Tree;
      _lastAutomatizmNodeId = 0;
    }

    /// <summary>
    /// Активация дерева автоматизмов
    /// </summary>
    public int AutomatizmTreeActivation(
        int baseId,
        int emotionId,
        int activityId,
        int toneMoodId,
        int simbolId,
        int verbID,
        bool isUnrecognizedPhrase = false)
    {
      if (_notAllowScanInTreeThisTime || AppGlobalState.EvolutionStage < 2)
        return 0;

      _notAllowScanInTreeThisTime = true;

      try
      {
        DetectedActiveLastNodeId = 0;
        ActiveBranchNodeArr.Clear();
        CurrentAutomatizmTreeEnd = null;
        _currentStepCount = 0;
        AppGlobalState.CurrentFindAtmzStepCount = _currentStepCount;

        var condArr = GetActiveConditionsArr(baseId, emotionId, activityId, toneMoodId, simbolId, verbID);

        foreach (var node in Tree.Children)
        {
          if (condArr[0] == node.BaseID)
          {
            DetectedActiveLastNodeId = node.ID;
            var remaining = condArr.Skip(1).ToList();
            ConditionAutomatizmFound(1, remaining, node);
            break;
          }
        }

        if (DetectedActiveLastNodeId > 0)
        {
          var conditionsCount = GetConditionsCount(condArr);
          CurrentAutomatizmTreeEnd = condArr.Skip(_currentStepCount).ToList();

          if (_currentStepCount < conditionsCount)
            DetectedActiveLastNodeId = FormingBranch(DetectedActiveLastNodeId, _currentStepCount, condArr);
        }
        else
        {
          DetectedActiveLastNodeId = FormingBranch(0, _currentStepCount, condArr);
          CurrentAutomatizmTreeEnd = condArr;
        }

        if (AppGlobalState.EvolutionStage >= 4 && _problemTree != null)
          _problemTree.UpdateActiveBranchFromAutomatizmTree(DetectedActiveLastNodeId);

        return DetectedActiveLastNodeId;
      }
      finally
      {
        _notAllowScanInTreeThisTime = false;
      }
    }

    /// <summary>
    /// Рекурсивный поиск по условиям
    /// </summary>
    private void ConditionAutomatizmFound(int level, List<int> conditions, AutomatizmNode node)
    {
      if (conditions == null || conditions.Count == 0)
        return;

      // Уровень, с которого при необходимости строить ветку (если не найдём совпадение или нет детей)
      _currentStepCount = level;

      var remaining = conditions.Skip(1).ToList();

      foreach (var child in node.Children)
      {
        int val = 0;
        switch (level)
        {
          case 0:
            val = child.BaseID;
            break;
          case 1:
            val = child.EmotionID;
            break;
          case 2:
            val = child.ActivityID;
            break;
          case 3:
            val = child.ToneMoodID;
            break;
          case 4:
            val = child.SimbolID;
            break;
          case 5:
            val = child.VerbID;
            break;
        }

        if (conditions[0] == val)
        {
          DetectedActiveLastNodeId = child.ID;
          ActiveBranchNodeArr.Add(child.ID);
        }
        else
        {
          // Не сбрасывать на level-1: нужно строить ветку с текущего уровня (level), иначе FormingBranch создаст узел уровня 0 под уже найденным узлом
          _currentStepCount = level;
          AppGlobalState.CurrentFindAtmzStepCount = _currentStepCount;
          continue;
        }

        level++;
        _currentStepCount = level;
        AppGlobalState.CurrentFindAtmzStepCount = _currentStepCount;
        ConditionAutomatizmFound(level, remaining, child);
        return;
      }
    }

    /// <summary>
    /// Создание ветки, начиная с заданного узла
    /// </summary>
    private int FormingBranch(int fromId, int lastLevel, List<int> condArr)
    {
      AutomatizmNode lastNode = fromId > 0 ? GetNodeById(fromId) : Tree;
      if (lastNode == null)
        return 0;

      // Когда от корня (fromId==0) и lastLevel > 0, нельзя вешать узлы на Tree —
      // иначе все уровни (emotion, activity, …) станут прямыми детьми корня (плоское дерево).
      // Сначала привязываемся к базовой ветке (узел с BaseID), затем строим от неё.
      if (fromId == 0 && lastLevel > 0 && condArr != null && condArr.Count > 0)
      {
        int baseId = condArr[0];
        foreach (var child in Tree.Children)
        {
          if (child.BaseID == baseId)
          {
            return AddNewBranchFromNodes(1, condArr, child);
          }
        }
      }

      return AddNewBranchFromNodes(lastLevel, condArr, lastNode);
    }

    /// <summary>
    /// Создание новой ветки с новым узлом
    /// </summary>
    private int AddNewBranchFromNodes(int level, List<int> conditions, AutomatizmNode node)
    {
      if (node == null || level >= conditions.Count)
        return node?.ID ?? 0;

      int id = 0;
      int baseId = conditions[0];
      int emotionId = level > 0 ? conditions[1] : 0;
      int activityId = level > 1 ? conditions[2] : 0;
      int toneMoodId = level > 2 ? conditions[3] : 0;
      int simbolId = level > 3 ? conditions[4] : 0;
      int verbID = level > 4 ? conditions[5] : 0;

      switch (level)
      {
        case 0:
          (id, _) = CreateNewAutomatizmNode(node, 0, baseId, 0, 0, 0, 0, 0, true);
          break;
        case 1:
          (id, _) = CreateNewAutomatizmNode(node, 0, baseId, emotionId, 0, 0, 0, 0, true);
          break;
        case 2:
          (id, _) = CreateNewAutomatizmNode(node, 0, baseId, emotionId, activityId, 0, 0, 0, true);
          break;
        case 3:
          (id, _) = CreateNewAutomatizmNode(node, 0, baseId, emotionId, activityId, toneMoodId, 0, 0, true);
          break;
        case 4:
          (id, _) = CreateNewAutomatizmNode(node, 0, baseId, emotionId, activityId, toneMoodId, simbolId, 0, true);
          break;
        case 5:
          (id, _) = CreateNewAutomatizmNode(node, 0, baseId, emotionId, activityId, toneMoodId, simbolId, verbID, true);
          break;
      }

      level++;
      var newNode = GetNodeById(id);
      if (newNode == null)
        return 0;

      return AddNewBranchFromNodes(level, conditions, newNode);
    }

    /// <summary>
    /// Создает последовательность уровней условий
    /// </summary>
    private List<int> GetActiveConditionsArr(int lev1, int lev2, int lev3, int lev4, int lev5, int lev6)
    {
      return new List<int> { lev1, lev2, lev3, lev4, lev5, lev6 };
    }

    /// <summary>
    /// Подсчитывает количество ненулевых условий
    /// </summary>
    private int GetConditionsCount(List<int> condArr)
    {
      return condArr.Count(c => c > 0);
    }

    #endregion

    #region Работа с файлами

    /// <summary>
    /// Создает каталог данных, если его нет
    /// </summary>
    private void EnsureDataDirectory()
    {
      if (!Directory.Exists(_psychicDataPath))
        Directory.CreateDirectory(_psychicDataPath);
    }

    private string GetAutomatizmTreeFilePath()
    {
      return Path.Combine(_psychicDataPath, $"{AutomatizmTreeFileName}.dat");
    }

    /// <summary>
    /// Загружает дерево автоматизмов из файла.
    /// Файл должен быть сохранён в порядке обхода в глубину (родитель перед детьми), как у дерева рефлексов.
    /// Если в файле у узла ParentID=0, но по условиям он не базовая ветка — родитель восстанавливается по иерархии условий.
    /// </summary>
    private void LoadAutomatizmTree()
    {
      string filePath = GetAutomatizmTreeFilePath();

      // Если файл не существует или невалиден, создаем новый
      if (!File.Exists(filePath) || !FileValidator.IsValidAutomatizmTreeFile(filePath))
      {
        try
        {
          EnsureDataDirectory();
          var lines = new List<string>
            {
              FileValidator.FileHeaders.AutomatizmTreeFormat,
              FileValidator.FileHeaders.AutomatizmTreeFields1,
              FileValidator.FileHeaders.AutomatizmTreeFields2,
              FileValidator.FileHeaders.AutomatizmTreeFields3,
              FileValidator.FileHeaders.AutomatizmTreeFields4,
              FileValidator.FileHeaders.AutomatizmTreeFields5,
              FileValidator.FileHeaders.AutomatizmTreeFields6,
              FileValidator.FileHeaders.AutomatizmTreeFields7,
              FileValidator.FileHeaders.AutomatizmTreeFields8
            };

          File.WriteAllLines(filePath, lines);

          InitializeTree();
          return;
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
          throw;
        }
      }

      try
      {
        InitializeTree();

        // Пропускаем строки заголовков (первые 9 строк)
        int lineNumber = 0;
        foreach (var line in File.ReadLines(filePath))
        {
          lineNumber++;
          if (lineNumber <= 9) // Пропускаем 9 строк заголовков
            continue;

          var trimmedLine = line.Trim();
          if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
            continue;

          var parts = trimmedLine.Split('|');
          if (parts.Length < 8)
            continue;

          if (!int.TryParse(parts[0], out int id))
            continue;

          if (!int.TryParse(parts[1], out int parentId))
            continue;

          if (!int.TryParse(parts[2], out int baseId))
            continue;

          if (!int.TryParse(parts[3], out int emotionId))
            emotionId = 0;

          if (!int.TryParse(parts[4], out int activityId))
            activityId = 0;

          if (!int.TryParse(parts[5], out int toneMoodId))
            toneMoodId = 0;

          if (!int.TryParse(parts[6], out int simbolId))
            simbolId = 0;

          if (!int.TryParse(parts[7], out int verbID))
            verbID = 0;

          AutomatizmNode parent = GetNodeById(parentId);
          // Восстановление родителя при «плоском» файле (у всех ParentID=0): для не-базовых узлов ищем родителя по условиям
          if (parentId == 0 && !IsBaseBranchNode(baseId, emotionId, activityId, toneMoodId, simbolId, verbID))
          {
            var (pBase, pEmo, pAct, pTone, pSim, pVerb) = GetParentCondition(baseId, emotionId, activityId, toneMoodId, simbolId, verbID);
            var (_, parentNode) = FindAutomatizmTreeNodeFromCondition(pBase, pEmo, pAct, pTone, pSim, pVerb);
            if (parentNode != null)
              parent = parentNode;
          }

          if (parent != null)
            CreateNewAutomatizmNode(parent, id, baseId, emotionId, activityId, toneMoodId, simbolId, verbID, false);
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    /// <summary>
    /// Узел считается базовой веткой (прямой потомок корня), если задан только BaseID и он из {-1,0,1}.
    /// </summary>
    private static bool IsBaseBranchNode(int baseId, int emotionId, int activityId, int toneMoodId, int simbolId, int verbID)
    {
      return emotionId == 0 && activityId == 0 && toneMoodId == 0 && simbolId == 0 && verbID == 0
          && (baseId == -1 || baseId == 0 || baseId == 1);
    }

    /// <summary>
    /// Возвращает условия «родительского» узла (на один уровень иерархии выше).
    /// </summary>
    private static (int BaseID, int EmotionID, int ActivityID, int ToneMoodID, int SimbolID, int VerbID) GetParentCondition(
        int baseId, int emotionId, int activityId, int toneMoodId, int simbolId, int verbID)
    {
      if (verbID != 0)
        return (baseId, emotionId, activityId, toneMoodId, simbolId, 0);
      if (simbolId != 0)
        return (baseId, emotionId, activityId, toneMoodId, 0, 0);
      if (toneMoodId != 0)
        return (baseId, emotionId, activityId, 0, 0, 0);
      if (activityId != 0)
        return (baseId, emotionId, 0, 0, 0, 0);
      if (emotionId != 0)
        return (baseId, 0, 0, 0, 0, 0);
      return (baseId, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// Сохраняет дерево автоматизмов в файл
    /// </summary>
    public (bool Success, string ErrorMessage) SaveAutomatizmTree()
    {
      _lock.EnterReadLock();
      try
      {
        return SaveAutomatizmTreeNoLock();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Возвращает строки для узла и всех его потомков (обход в глубину), как в ReflexTreeSystem.GetReflexNodeStrings.
    /// Порядок: родитель → дети, чтобы при загрузке по порядку строк родитель уже был в словаре.
    /// </summary>
    private static List<string> GetAutomatizmNodeStrings(AutomatizmNode node)
    {
      var lines = new List<string>();
      if (node.ID == 0)
        return lines;

      lines.Add($"{node.ID}|{node.ParentID}|{node.BaseID}|{node.EmotionID}|" +
                $"{node.ActivityID}|{node.ToneMoodID}|{node.SimbolID}|{node.VerbID}");

      foreach (var child in node.Children)
      {
        lines.AddRange(GetAutomatizmNodeStrings(child));
      }

      return lines;
    }

    /// <summary>
    /// Сохраняет дерево автоматизмов в файл (без блокировки)
    /// </summary>
    private (bool Success, string ErrorMessage) SaveAutomatizmTreeNoLock()
    {
      try
      {
        var lines = new List<string>
        {
          FileValidator.FileHeaders.AutomatizmTreeFormat,
          FileValidator.FileHeaders.AutomatizmTreeFields1,
          FileValidator.FileHeaders.AutomatizmTreeFields2,
          FileValidator.FileHeaders.AutomatizmTreeFields3,
          FileValidator.FileHeaders.AutomatizmTreeFields4,
          FileValidator.FileHeaders.AutomatizmTreeFields5,
          FileValidator.FileHeaders.AutomatizmTreeFields6,
          FileValidator.FileHeaders.AutomatizmTreeFields7,
          FileValidator.FileHeaders.AutomatizmTreeFields8
        };

        // Обход в глубину (как у дерева рефлексов): родитель всегда перед детьми — при загрузке по порядку родитель уже в словаре
        foreach (var child in Tree.Children)
        {
          lines.AddRange(GetAutomatizmNodeStrings(child));
        }

        var result = FileValidator.SafeSaveFile(
            GetAutomatizmTreeFilePath(),
            lines,
            content => FileValidator.IsValidAutomatizmTreeFile(string.Join(Environment.NewLine, content)),
            minLinesCount: 9,
            fileDescription: "дерева автоматизмов");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом AutomatizmTreeSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        SaveAutomatizmTree();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
      }
    }

    #endregion
  }
}