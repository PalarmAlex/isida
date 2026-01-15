using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Дерево рефлексов - безусловных и условных
  /// </summary>
  public sealed class ReflexTreeSystem : IDisposable
  {
    private readonly GeneticReflexesSystem _geneticReflexesSystem;
    private readonly ConditionedReflexesSystem _conditionedReflexesSystem;
    private readonly PerceptionImagesSystem _perceptionImagesSystem;
    private readonly ReflexChainsSystem _reflexChainsSystem;
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    #region Инициализация

    private static ReflexTreeSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы дерева рефлексов
    /// </summary>
    public static ReflexTreeSystem Instance => _instance ??
        throw new InvalidOperationException("ReflexTreeSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы дерева рефлексов
    /// </summary>
    public static void InitializeInstance(
        GeneticReflexesSystem geneticReflexesSystem,
        ConditionedReflexesSystem conditionedReflexesSystem,
        PerceptionImagesSystem perceptionImagesSystem,
        ReflexChainsSystem reflexChainsSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("ReflexTreeSystem уже инициализирован.");

      _instance = new ReflexTreeSystem(geneticReflexesSystem, conditionedReflexesSystem, perceptionImagesSystem, reflexChainsSystem);
    }

    private ReflexTreeSystem(
        GeneticReflexesSystem geneticReflexesSystem,
        ConditionedReflexesSystem conditionedReflexesSystem,
        PerceptionImagesSystem perceptionImagesSystem,
        ReflexChainsSystem reflexChainsSystem)
    {
      try
      {
        _geneticReflexesSystem = geneticReflexesSystem ?? throw new ArgumentNullException(nameof(geneticReflexesSystem));
        _conditionedReflexesSystem = conditionedReflexesSystem ?? throw new ArgumentNullException(nameof(conditionedReflexesSystem));
        _perceptionImagesSystem = perceptionImagesSystem ?? throw new ArgumentNullException(nameof(perceptionImagesSystem));
        _reflexChainsSystem = reflexChainsSystem ?? throw new ArgumentNullException(nameof(reflexChainsSystem));

        _geneticReflexesSystem.GeneticReflexDeleted += OnGeneticReflexDeleted;
        _geneticReflexesSystem.MultipleGeneticReflexesDeleted += OnMultipleGeneticReflexesDeleted;
        _geneticReflexesSystem.GeneticReflexCreated += OnGeneticReflexCreated;
        _reflexChainsSystem.ReflexChainDeleted += OnReflexChainDeleted;

        _conditionedReflexesSystem.ConditionedReflexCreated += OnConditionedReflexCreated;
        _conditionedReflexesSystem.ConditionedReflexDeleted += OnConditionedReflexDeleted;
        _conditionedReflexesSystem.MultipleConditionedReflexesDeleted += OnMultipleConditionedReflexesDeleted;

        EnsureDataDirectory();
        LoadReflexTree();
        if (ReflexTree.Children.Count == 0)
          CreateBasicReflexTree();
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка инициализации ReflexTreeSystem: {ex.Message}");
        throw;
      }
    }

    /// <summary>
    /// Обработчик удаления цепочки рефлексов
    /// </summary>
    private void OnReflexChainDeleted(int chainId)
    {
      ClearChainReferences(chainId);
    }

    private void OnGeneticReflexDeleted(int reflexId)
    {
      RemoveGeneticReflexReferencesOptimized(reflexId);
    }

    private void OnMultipleGeneticReflexesDeleted(List<int> reflexIds)
    {
      RemoveMultipleGeneticReflexReferences(reflexIds);
    }

    private void OnGeneticReflexCreated(GeneticReflexesSystem.GeneticReflexCreatedEventArgs e)
    {
      try
      {
        int styleImageId = 0;
        int actionImageId = 0;

        if (PerceptionImagesSystem.IsInitialized)
        {
          if (e.Level2 != null && e.Level2.Any())
            styleImageId = _perceptionImagesSystem.AddBehaviorStyleImage(e.Level2);

          if (e.Level3 != null && e.Level3.Any())
            // фразу не передаем - рефлексы не учитывают вербальное воздействие
            actionImageId = _perceptionImagesSystem.AddPerceptionImage(e.Level3, new List<int>());
        }

        int[] conditionArr = new int[] { e.Level1, styleImageId, actionImageId };
        int treeNodeId = FindOrCreateNodeForReflex(conditionArr, e.ReflexId, e.ReflexChainID);
        if (treeNodeId > 0) // сохранение дерева уже есть в FindOrCreateNodeForReflex()
          Logger.Info($"Рефлекс {e.ReflexId} привязан к узлу дерева ID: {treeNodeId}, цепочка: {e.ReflexChainID}");
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка привязки рефлекса {e.ReflexId} к дереву: {ex.Message}");
      }
    }

    /// <summary>
    /// Обработчик создания условного рефлекса
    /// </summary>
    private void OnConditionedReflexCreated(ConditionedReflexesSystem.ConditionedReflexCreatedEventArgs e)
    {
      try
      {
        int styleImageId = 0;

        if (PerceptionImagesSystem.IsInitialized && e.Level2 != null && e.Level2.Any())
          styleImageId = _perceptionImagesSystem.AddBehaviorStyleImage(e.Level2);

        int[] conditionArr = new int[] { e.Level1, styleImageId, e.Level3 };
        int treeNodeId = FindOrCreateNodeForReflex(conditionArr, 0, 0); // 0 для geneticReflexId, так как это условный

        if (treeNodeId > 0)
        {
          var node = FindNodeByID(treeNodeId);
          if (node != null)
          {
            node.ConditionedReflex = e.ReflexId;
            SaveReflexTreeInternal();
            Logger.Info($"Условный рефлекс {e.ReflexId} привязан к узлу дерева ID: {treeNodeId}");
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка привязки условного рефлекса {e.ReflexId} к дереву: {ex.Message}");
      }
    }

    /// <summary>
    /// Обработчик удаления условного рефлекса
    /// </summary>
    private void OnConditionedReflexDeleted(int reflexId)
    {
      ClearConditionedReflexReferences(reflexId);
    }

    /// <summary>
    /// Обработчик массового удаления условных рефлексов
    /// </summary>
    private void OnMultipleConditionedReflexesDeleted(List<int> reflexIds)
    {
      ClearMultipleConditionedReflexReferences(reflexIds);
    }

    #endregion

    #region Константы и структуры

    private const string ReflexTreeFileName = "ReflexTree";

    /// <summary>
    /// Узел дерева рефлексов
    /// Формат: ID|ParentID|BaseID|StyleID|ActionID|GeneticReflexID|ConditionedReflex|ReflexChainID
    /// </summary>
    public class ReflexNode
    {
      /// <summary>
      /// Уникальный идентификатор узла
      /// </summary>
      public int ID { get; set; }

      /// <summary>
      /// Базовое состояние (-1: Плохо, 0: Норма, 1: Хорошо)
      /// </summary>
      public int BaseID { get; set; }

      /// <summary>
      /// ID образа стилей поведения - сочетание активностей Базовых контекстов
      /// </summary>
      public int StyleID { get; set; }

      /// <summary>
      /// ID образа пусковых стимулов - сочетание воздействий
      /// </summary>
      public int ActionID { get; set; }

      /// <summary>
      /// ID безусловного рефлекса
      /// </summary>
      public int GeneticReflexID { get; set; }

      /// <summary>
      /// ID условного рефлекса (если есть, блокирует GeneticReflexID)
      /// </summary>
      public int ConditionedReflex { get; set; }

      /// <summary>
      /// ID цепочки рефлексов, связанной с узлом
      /// </summary>
      public int ReflexChainID { get; set; }

      /// <summary>
      /// Флаг, указывающий что узел содержит цепочку
      /// </summary>
      public bool IsChainNode => ReflexChainID > 0;

      /// <summary>
      /// Получает активный рефлекс (условный имеет приоритет над безусловным)
      /// </summary>
      public int ActiveReflex => ConditionedReflex > 0 ? ConditionedReflex : GeneticReflexID;

      /// <summary>
      /// Дочерние узлы
      /// </summary>
      public List<ReflexNode> Children { get; set; } = new List<ReflexNode>();

      /// <summary>
      /// ID родительского узла
      /// </summary>
      public int ParentID { get; set; }

      /// <summary>
      /// Ссылка на родительский узел
      /// </summary>
      public ReflexNode ParentNode { get; set; }
    }

    /// <summary>
    /// Активные цепочки рефлексов
    /// </summary>
    public class ActiveChain
    {
      /// <summary>
      /// ID активной цепочки
      /// </summary>
      public int ChainID { get; set; }

      /// <summary>
      /// ID текущего активного звена
      /// </summary>
      public int CurrentLinkID { get; set; }

      /// <summary>
      /// Пульс начала выполнения цепочки
      /// </summary>
      public int StartPulse { get; set; }

      /// <summary>
      /// Текущий пульс выполнения цепочки
      /// </summary>
      public int CurrentPulse { get; set; }
    }

    #endregion

    #region Поля и свойства

    private readonly ReflexNode ReflexTree = new ReflexNode();
    private readonly List<ReflexNode> ReflexTreeFromID = new List<ReflexNode>();
    private readonly Dictionary<int, ActiveChain> _activeChains = new Dictionary<int, ActiveChain>();
    private int _lastReflexNodeID = 0;
    private int _detectedLastNodeID = 0;
    private int _detectedLevel = 0;

    /// <summary>
    /// Текущий последний распознанный узел дерева - результат распознавания
    /// </summary>
    public int DetectedLastNodeID => _detectedLastNodeID;

    /// <summary>
    /// Уровень, на котором был найден узел (0 - только базовое состояние, 1 - состояние + стиль, 2 - состояние + стиль + триггер)
    /// </summary>
    public int DetectedLevel => _detectedLevel;

    #endregion

    #region Управление деревом рефлексов

    /// <summary>
    /// Создает новый узел дерева рефлексов
    /// </summary>
    public (int ID, ReflexNode Node) CreateNewReflexNode(ReflexNode parent, int id, int baseID,
        int styleID, int actionID, int geneticReflexID, int conditionedReflex,
        int reflexChainID, bool checkUnicum)
    {
      if (checkUnicum)
      {
        var (oldID, oldNode) = FindReflexTreeNodeFromCondition(baseID, styleID, actionID);
        if (oldID > 0) return (oldID, oldNode);
      }

      if (id == 0)
      {
        _lastReflexNodeID++;
        id = _lastReflexNodeID;
      }
      else
      {
        if (_lastReflexNodeID < id)
          _lastReflexNodeID = id;
      }

      var node = new ReflexNode
      {
        ID = id,
        ParentNode = parent,
        ParentID = parent?.ID ?? 0,
        BaseID = baseID,
        StyleID = styleID,
        ActionID = actionID,
        GeneticReflexID = geneticReflexID,
        ConditionedReflex = conditionedReflex,
        ReflexChainID = reflexChainID
      };

      parent?.Children.Add(node);
      WriteReflexTreeFromID(id, node);

      return (id, node);
    }

    /// <summary>
    /// Находит узел по ID
    /// </summary>
    public ReflexNode FindNodeByID(int id)
    {
      if (id < 0 || id >= ReflexTreeFromID.Count)
        return null;

      var node = ReflexTreeFromID[id];

      return node?.ID == id ? node : null;
    }

    /// <summary>
    /// Записывает узел в массив по ID
    /// </summary>
    private void WriteReflexTreeFromID(int index, ReflexNode value)
    {
      if (index >= ReflexTreeFromID.Count)
      {
        int newSize = Math.Max(index + 1, ReflexTreeFromID.Count * 2);
        while (ReflexTreeFromID.Count <= newSize)
          ReflexTreeFromID.Add(null);
      }
      ReflexTreeFromID[index] = value;
    }

    /// <summary>
    /// Находит конечный узел по условиям
    /// </summary>
    public (int ID, ReflexNode Node) FindReflexTreeNodeFromCondition(int baseID, int styleID, int actionID)
    {
      foreach (var node in ReflexTreeFromID)
      {
        if (node != null && node.BaseID == baseID && node.StyleID == styleID && node.ActionID == actionID)
          return (node.ID, node);
      }
      return (0, null);
    }

    /// <summary>
    /// Распознавание условий в дереве рефлексов
    /// </summary>
    /// <param name="conditionArr">Массив условий [baseID, styleID, actionID]</param>
    public void ConditionsDetection(int[] conditionArr)
    {
      _detectedLastNodeID = 0;
      _detectedLevel = 0;

      foreach (var node in ReflexTree.Children)
      {
        if (conditionArr[0] == node.BaseID)
        {
          _detectedLastNodeID = node.ID;
          _detectedLevel = 0;
          var remainingConditions = conditionArr.Skip(1).ToArray();
          GetReflexTreeNode(1, remainingConditions, node);
          break; // только одно из Базовых состояний
        }
      }

      if (_detectedLastNodeID == 0)
        _detectedLevel = -1;
    }

    private void GetReflexTreeNode(int level, int[] conditions, ReflexNode node)
    {
      if (conditions.Length == 0) return;

      var remainingConditions = conditions.Skip(1).ToArray();

      foreach (var child in node.Children)
      {
        int levelID;
        switch (level)
        {
          case 1:
            levelID = child.StyleID;
            break;
          case 2:
            levelID = child.ActionID;
            break;
          default:
            levelID = 0;
            break;
        }

        if (conditions[0] != levelID) continue;

        _detectedLastNodeID = child.ID;
        _detectedLevel = level;
        GetReflexTreeNode(level + 1, remainingConditions, child);
        return;
      }
    }

    /// <summary>
    /// Создает новую ветку с новым рефлексом
    /// </summary>
    private int CreateNewReflexToTreeFromNodes(int level, int[] conditions, ReflexNode node,
        int geneticReflexId = 0, int reflexChainID = 0)
    {
      if (node == null || level >= conditions.Length)
      {
        // Если достигли конца условий или последний узел - привязываем рефлекс
        if (node != null)
        {
          if (geneticReflexId > 0)
          {
            node.GeneticReflexID = geneticReflexId;
          }
          if (reflexChainID > 0)
          {
            node.ReflexChainID = reflexChainID;
          }
        }
        return node?.ID ?? 0;
      }

      int id;

      switch (level)
      {
        case 0: // Базовое состояние
          (id, _) = CreateNewReflexNode(node, 0, conditions[0], 0, 0, 0, 0, 0, true);
          break;
        case 1: // Стиль поведения
          (id, _) = CreateNewReflexNode(node, 0, node.BaseID, conditions[1], 0, 0, 0, 0, true);
          break;
        case 2: // Пусковой стимул
          (id, _) = CreateNewReflexNode(node, 0, node.BaseID, node.StyleID, conditions[2],
              geneticReflexId, 0, reflexChainID, true);
          break;
        default:
          return node.ID;
      }

      var newNode = FindNodeByID(id);
      if (newNode != null)
      {
        return CreateNewReflexToTreeFromNodes(level + 1, conditions, newNode, geneticReflexId, reflexChainID);
      }

      return id;
    }

    /// <summary>
    /// Находит или создает узел дерева для указанных условий и привязывает рефлекс
    /// </summary>
    public int FindOrCreateNodeForReflex(int[] conditionArr, int geneticReflexId, int reflexChainID = 0)
    {
      _lock.EnterWriteLock();
      try
      {
        if (conditionArr == null || conditionArr.Length < 3)
        {
          Logger.Error("Недопустимый массив условий в FindOrCreateNodeForReflex");
          return 0;
        }

        int baseID = conditionArr[0];
        int styleID = conditionArr[1];
        int actionID = conditionArr[2];

        var (existingId, existingNode) = FindReflexTreeNodeFromCondition(baseID, styleID, actionID);

        if (existingId > 0 && existingNode != null)
        {
          existingNode.GeneticReflexID = geneticReflexId;
          if (reflexChainID > 0)
            existingNode.ReflexChainID = reflexChainID;

          SaveReflexTreeInternal();
          return existingId;
        }

        // Если узел не найден - создаем новую ветку
        // Активируем дерево для поиска подходящего места
        ConditionsDetection(conditionArr);
        int detectedNodeId = DetectedLastNodeID;

        if (detectedNodeId > 0)
        {
          int level = GetLevelFromNodeID(detectedNodeId);
          var detectedNode = FindNodeByID(detectedNodeId);

          if (detectedNode != null)
          {
            if (detectedNode.BaseID != baseID)
            {
              // Если базовое состояние не совпадает, начинаем с корня
              detectedNode = ReflexTree;
              level = 0;
            }

            // Создаем ветку от найденного узла
            int lastNodeId = CreateNewReflexToTreeFromNodes(level, conditionArr, detectedNode,
                geneticReflexId, reflexChainID);

            var newNode = FindNodeByID(lastNodeId);
            if (newNode != null)
            {
              SaveReflexTreeInternal();
              return lastNodeId;
            }
          }
        }

        // Если не найден подходящий узел или detectedNodeId = 0, создаем с нуля от корня
        int newNodeIdFromRoot = CreateNewReflexToTreeFromNodes(0, conditionArr, ReflexTree,
            geneticReflexId, reflexChainID);
        var newNodeFromRoot = FindNodeByID(newNodeIdFromRoot);

        if (newNodeFromRoot != null)
        {
          SaveReflexTreeInternal();
          return newNodeIdFromRoot;
        }

        Logger.Error($"Не удалось создать узел для рефлекса {geneticReflexId} с условиями [{baseID}, {styleID}, {actionID}]");
        return 0;
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка в FindOrCreateNodeForReflex: {ex.Message}");
        return 0;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Находит уровень вложения узла в ветке
    /// </summary>
    private int GetLevelFromNodeID(int nodeId)
    {
      var node = FindNodeByID(nodeId);
      if (node == null) return 0;

      int level = 0;
      var currentNode = node;
      while (currentNode.ParentNode != null)
      {
        level++;
        currentNode = currentNode.ParentNode;
      }
      return level;
    }

    /// <summary>
    /// Получить все узлы дерева
    /// </summary>
    public List<ReflexNode> GetAllNodes()
    {
      var nodes = new List<ReflexNode>();
      CollectNodesRecursive(ReflexTree, nodes);
      return nodes;
    }

    private void CollectNodesRecursive(ReflexNode node, List<ReflexNode> nodes)
    {
      if (node == null) return;

      nodes.Add(node);

      foreach (var child in node.Children)
      {
        CollectNodesRecursive(child, nodes);
      }
    }

    #endregion

    #region Методы для работы с цепочками

    /// <summary>
    /// Привязывает цепочку рефлексов к узлу дерева
    /// </summary>
    public bool AttachChainToNode(int nodeId, int chainId)
    {
      _lock.EnterWriteLock();
      try
      {
        var node = FindNodeByID(nodeId);
        if (node == null)
        {
          Logger.Error($"Узел с ID {nodeId} не найден");
          return false;
        }

        // Проверяем существование цепочки
        if (!_reflexChainsSystem.GetAllReflexChains().ContainsKey(chainId))
        {
          Logger.Error($"Цепочка с ID {chainId} не найдена");
          return false;
        }

        node.ReflexChainID = chainId;

        SaveReflexTreeInternal();
        Logger.Info($"Цепочка {chainId} привязана к узлу {nodeId}");

        return true;
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка привязки цепочки к узлу: {ex.Message}");
        return false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Отвязывает цепочку рефлексов от узла дерева
    /// </summary>
    public bool DetachChainFromNode(int nodeId)
    {
      _lock.EnterWriteLock();
      try
      {
        var node = FindNodeByID(nodeId);
        if (node == null)
        {
          Logger.Error($"Узел с ID {nodeId} не найден");
          return false;
        }

        int oldChainId = node.ReflexChainID;
        node.ReflexChainID = 0;

        SaveReflexTreeInternal();
        Logger.Info($"Цепочка {oldChainId} отвязана от узла {nodeId}");

        return true;
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка отвязки цепочки от узла: {ex.Message}");
        return false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Получает текущий активный пульс цепочки</summary>
    /// <param name="chainID">ID цепочки</param>
    /// <returns>Текущий пульс или 0 если цепочка не активна</returns>
    public int GetCurrentChainPulse(int chainID)
    {
      return _activeChains.TryGetValue(chainID, out var chain) ? chain.CurrentPulse : 0;
    }

    /// <summary>Находит подходящую цепочку для текущих условий</summary>
    /// <param name="baseID">Базовое состояние гомеостаза</param>
    /// <param name="styleID">ID образа стилей поведения</param>
    /// <param name="actionID">ID образа пусковых стимулов</param>
    /// <returns>ID цепочки</returns>
    public int FindSuitableChain(int baseID, int styleID, int actionID)
    {
      var (nodeID, node) = FindReflexTreeNodeFromCondition(baseID, styleID, actionID);

      if (node != null && node.IsChainNode)
      {
        return node.ReflexChainID;
      }

      return 0;
    }

    /// <summary>Активирует цепочку рефлексов</summary>
    /// <param name="chainID">ID цепочки</param>
    /// <param name="startLinkID">ID стартового звена</param>
    /// <param name="currentPulse">Текущий пульс активации</param>
    /// <returns>True если цепочка активирована</returns>
    public bool ActivateChain(int chainID, int startLinkID, int currentPulse)
    {
      var chainLinks = _reflexChainsSystem.GetChainLinks(chainID);
      if (!chainLinks.Any())
        return false;

      var activeChain = new ActiveChain
      {
        ChainID = chainID,
        CurrentLinkID = startLinkID,
        StartPulse = currentPulse,
        CurrentPulse = currentPulse
      };

      _activeChains[chainID] = activeChain;
      return true;
    }

    /// <summary>Выполняет шаг активной цепочки</summary>
    /// <param name="chainID">ID цепочки</param>
    /// <param name="currentPulse">Текущий пульс выполнения</param>
    /// <param name="previousStepSuccess">Результат предыдущего шага</param>
    /// <returns>Результат выполнения шага</returns>
    public (bool Success, bool ChainCompleted, int ExecutedActionId, int NextLinkId)
        ExecuteChainStep(int chainID, int currentPulse, bool previousStepSuccess)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_activeChains.TryGetValue(chainID, out var activeChain))
          return (false, true, 0, 0);

        var chainLinks = _reflexChainsSystem.GetChainLinks(chainID);
        var currentLink = chainLinks.FirstOrDefault(l => l.ID == activeChain.CurrentLinkID);

        if (currentLink == null)
        {
          _activeChains.Remove(chainID);
          return (false, true, 0, 0);
        }

        activeChain.CurrentPulse = currentPulse;
        int nextLinkId = previousStepSuccess ? currentLink.SuccessNextLink : currentLink.FailureNextLink;

        if (nextLinkId == 0)
        {
          _activeChains.Remove(chainID);
          return (true, true, currentLink.ActionId, 0);
        }

        activeChain.CurrentLinkID = nextLinkId;
        return (true, false, currentLink.ActionId, nextLinkId);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Деактивирует цепочку</summary>
    /// <param name="chainID">ID цепочки для деактивации</param>
    public void DeactivateChain(int chainID)
    {
      _activeChains.Remove(chainID);
    }

    /// <summary>Получает активные цепочки</summary>
    /// <returns>Словарь активных цепочек</returns>
    public ReadOnlyDictionary<int, ActiveChain> GetActiveChains()
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyDictionary<int, ActiveChain>(_activeChains);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Проверяет, активна ли цепочка</summary>
    /// <param name="chainID">ID цепочки</param>
    /// <returns>True если цепочка активна</returns>
    public bool IsChainActive(int chainID)
    {
      return _activeChains.ContainsKey(chainID);
    }

    /// <summary>Получает текущее звено активной цепочки</summary>
    /// <param name="chainID">ID цепочки</param>
    /// <returns>ID текущего звена или 0 если цепочка не активна</returns>
    public int GetCurrentChainLink(int chainID)
    {
      return _activeChains.TryGetValue(chainID, out var chain) ? chain.CurrentLinkID : 0;
    }

    /// <summary>
    /// Очищает ссылки на цепочку рефлексов в дереве
    /// </summary>
    public void ClearChainReferences(int chainId)
    {
      if (chainId <= 0) return;

      int clearedCount = 0;

      _lock.EnterWriteLock();
      try
      {
        ClearChainFromNode(ReflexTree, chainId, ref clearedCount);
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      if (clearedCount > 0)
      {
        var (success, errorMessage) = SaveReflexTreeInternal();
        if (!success)
          Logger.Error($"Не удалось сохранить дерево после очистки ссылок на цепочку {chainId}: {errorMessage}");
        else
          Logger.Info($"Очищены ссылки на цепочку {chainId} в {clearedCount} узлах дерева");
      }
    }

    /// <summary>
    /// Рекурсивно очищает ссылки на цепочку из узла и его дочерних узлов
    /// </summary>
    private void ClearChainFromNode(ReflexNode node, int chainId, ref int clearedCount)
    {
      if (node == null) return;

      if (node.ReflexChainID == chainId)
      {
        node.ReflexChainID = 0;
        clearedCount++;
      }

      foreach (var child in node.Children)
      {
        ClearChainFromNode(child, chainId, ref clearedCount);
      }
    }

    #endregion

    #region Очистка ссылок на удаленные безусловные рефлексы

    /// <summary>
    /// Удаляет ссылки на безусловный рефлекс из дерева рефлексов (оптимизированная версия)
    /// </summary>
    /// <param name="geneticReflexId">ID удаляемого безусловного рефлекса</param>
    public void RemoveGeneticReflexReferencesOptimized(int geneticReflexId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (geneticReflexId <= 0) return;

        int removedCount = 0;

        // Используем рекурсивный обход дерева вместо полного перебора массива
        RemoveReflexFromNode(ReflexTree, geneticReflexId, ref removedCount);

        if (removedCount > 0)
        {
          SaveReflexTreeInternal();
          Logger.Error($"Удалено {removedCount} ссылок на безусловный рефлекс ID {geneticReflexId}");
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка удаления ссылок на безусловный рефлекс: {ex.Message}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Удаляет ссылки на несколько безусловных рефлексов из дерева
    /// </summary>
    /// <param name="geneticReflexIds">Список ID удаляемых безусловных рефлексов</param>
    public void RemoveMultipleGeneticReflexReferences(IEnumerable<int> geneticReflexIds)
    {
      _lock.EnterWriteLock();
      try
      {
        if (geneticReflexIds == null) return;

        var reflexIdsSet = new HashSet<int>(geneticReflexIds.Where(id => id > 0));
        if (reflexIdsSet.Count == 0) return;

        int removedCount = 0;
        RemoveMultipleReflexesFromNode(ReflexTree, reflexIdsSet, ref removedCount);

        if (removedCount > 0)
        {
          SaveReflexTreeInternal();
          Logger.Error($"Удалено {removedCount} ссылок на {reflexIdsSet.Count} безусловных рефлексов");
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка удаления множественных ссылок на безусловные рефлексы: {ex.Message}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Рекурсивно удаляет ссылки на рефлекс из узла и его дочерних узлов
    /// </summary>
    private void RemoveReflexFromNode(ReflexNode node, int geneticReflexId, ref int removedCount)
    {
      if (node == null) return;

      if (node.GeneticReflexID == geneticReflexId)
      {
        node.GeneticReflexID = 0;
        node.ReflexChainID = 0;
        removedCount++;
      }

      foreach (var child in node.Children)
      {
        RemoveReflexFromNode(child, geneticReflexId, ref removedCount);
      }
    }

    /// <summary>
    /// Рекурсивно удаляет ссылки на несколько рефлексов из узла и его дочерних узлов
    /// </summary>
    private void RemoveMultipleReflexesFromNode(ReflexNode node, HashSet<int> geneticReflexIds, ref int removedCount)
    {
      if (node == null) return;

      if (geneticReflexIds.Contains(node.GeneticReflexID))
      {
        node.GeneticReflexID = 0;
        node.ReflexChainID = 0;
        removedCount++;
      }

      foreach (var child in node.Children)
      {
        RemoveMultipleReflexesFromNode(child, geneticReflexIds, ref removedCount);
      }
    }

    /// <summary>
    /// Очищает все ссылки на безусловные рефлексы в дереве
    /// </summary>
    public void ClearAllGeneticReflexReferences()
    {
      _lock.EnterWriteLock();
      try
      {
        int clearedCount = 0;

        // Используем рекурсивный обход для очистки всех ссылок
        ClearAllReflexesFromNode(ReflexTree, ref clearedCount);

        if (clearedCount > 0)
        {
          SaveReflexTreeInternal();
          Logger.Error($"Очищено {clearedCount} ссылок на безусловные рефлексы");
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка очистки ссылок на безусловные рефлексы: {ex.Message}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Рекурсивно очищает все ссылки на рефлексы из узла и его дочерних узлов
    /// </summary>
    private void ClearAllReflexesFromNode(ReflexNode node, ref int clearedCount)
    {
      if (node == null) return;

      if (node.GeneticReflexID > 0)
      {
        node.GeneticReflexID = 0;
        node.ReflexChainID = 0;
        clearedCount++;
      }

      foreach (var child in node.Children)
      {
        ClearAllReflexesFromNode(child, ref clearedCount);
      }
    }

    #endregion

    #region Очистка ссылок на удаленные условные рефлексы

    /// <summary>
    /// Удаляет ссылки на условный рефлекс из дерева рефлексов
    /// </summary>
    public void ClearConditionedReflexReferences(int conditionedReflexId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (conditionedReflexId <= 0) return;

        int removedCount = 0;
        RemoveConditionedReflexFromNode(ReflexTree, conditionedReflexId, ref removedCount);

        if (removedCount > 0)
        {
          SaveReflexTreeInternal();
          Logger.Info($"Удалено {removedCount} ссылок на условный рефлекс ID {conditionedReflexId}");
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка удаления ссылок на условный рефлекс: {ex.Message}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Удаляет ссылки на несколько условных рефлексов из дерева
    /// </summary>
    public void ClearMultipleConditionedReflexReferences(IEnumerable<int> conditionedReflexIds)
    {
      _lock.EnterWriteLock();
      try
      {
        if (conditionedReflexIds == null) return;

        var reflexIdsSet = new HashSet<int>(conditionedReflexIds.Where(id => id > 0));
        if (reflexIdsSet.Count == 0) return;

        int removedCount = 0;
        RemoveMultipleConditionedReflexesFromNode(ReflexTree, reflexIdsSet, ref removedCount);

        if (removedCount > 0)
        {
          SaveReflexTreeInternal();
          Logger.Info($"Удалено {removedCount} ссылок на {reflexIdsSet.Count} условных рефлексов");
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка удаления множественных ссылок на условные рефлексы: {ex.Message}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Рекурсивно удаляет ссылки на условный рефлекс из узла и его дочерних узлов
    /// </summary>
    private void RemoveConditionedReflexFromNode(ReflexNode node, int conditionedReflexId, ref int removedCount)
    {
      if (node == null) return;

      if (node.ConditionedReflex == conditionedReflexId)
      {
        node.ConditionedReflex = 0;
        removedCount++;
      }

      foreach (var child in node.Children)
      {
        RemoveConditionedReflexFromNode(child, conditionedReflexId, ref removedCount);
      }
    }

    /// <summary>
    /// Рекурсивно удаляет ссылки на несколько условных рефлексов из узла и его дочерних узлов
    /// </summary>
    private void RemoveMultipleConditionedReflexesFromNode(ReflexNode node, HashSet<int> conditionedReflexIds, ref int removedCount)
    {
      if (node == null) return;

      if (conditionedReflexIds.Contains(node.ConditionedReflex))
      {
        node.ConditionedReflex = 0;
        removedCount++;
      }

      foreach (var child in node.Children)
      {
        RemoveMultipleConditionedReflexesFromNode(child, conditionedReflexIds, ref removedCount);
      }
    }

    #endregion

    #region Работа с файлами

    /// <summary>
    /// Создает каталог данных, если его нет
    /// </summary>
    private void EnsureDataDirectory()
    {
      string directory = Path.GetDirectoryName(GetReflexTreeFilePath());
      if (!Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }
    }
    private string GetReflexTreeFilePath()
    {
      string reflexesPath = _geneticReflexesSystem.GetGeneticReflexesFilePath();
      string directory = Path.GetDirectoryName(reflexesPath);
      return Path.Combine(directory, $"{ReflexTreeFileName}.dat");
    }

    /// <summary>
    /// Проверяет валидность файла дерева рефлексов
    /// </summary>
    private bool IsValidReflexTreeFile(string filePath)
    {
      if (!File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidReflexTreeFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла дерева рефлексов
    /// </summary>
    private bool IsValidReflexTreeFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');

        if (parts.Length < 8) // Исправлено: 8 полей
          return false;

        if (!int.TryParse(parts[0], out _))
          return false;

        return true;
      }

      return true;
    }

    /// <summary>
    /// Загружает дерево рефлексов из файла
    /// </summary>
    private void LoadReflexTree()
    {
      string filePath = GetReflexTreeFilePath();

      if (!IsValidReflexTreeFile(filePath))
        return;

      try
      {
        _lock.EnterWriteLock();
        try
        {
          CreateNulLevelReflexTree();

          foreach (var line in File.ReadLines(filePath))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');
            if (parts.Length < 8)
              continue;

            if (!int.TryParse(parts[0], out int id) ||
                !int.TryParse(parts[1], out int parentID) ||
                !int.TryParse(parts[2], out int baseID) ||
                !int.TryParse(parts[3], out int styleID) ||
                !int.TryParse(parts[4], out int actionID) ||
                !int.TryParse(parts[5], out int geneticReflexID) ||
                !int.TryParse(parts[6], out int conditionedReflex) ||
                !int.TryParse(parts[7], out int reflexChainID))
              continue;

            var parentNode = FindNodeByID(parentID);
            if (parentNode != null)
            {
              CreateNewReflexNode(parentNode, id, baseID, styleID, actionID,
                  geneticReflexID, conditionedReflex, reflexChainID, false);
            }
          }
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Error loading reflex tree: {ex.Message}");
      }
    }

    /// <summary>
    /// Создает нулевой уровень дерева рефлексов
    /// </summary>
    private void CreateNulLevelReflexTree()
    {
      ReflexTree.ID = 0;
      WriteReflexTreeFromID(ReflexTree.ID, ReflexTree);
    }

    /// <summary>
    /// Создает первые три ветки базовых состояний
    /// </summary>
    private void CreateBasicReflexTree()
    {
      CreateNewReflexNode(ReflexTree, 0, -1, 0, 0, 0, 0, 0, false);
      CreateNewReflexNode(ReflexTree, 0, 0, 0, 0, 0, 0, 0, false);
      CreateNewReflexNode(ReflexTree, 0, 1, 0, 0, 0, 0, 0, false);

      SaveReflexTreeInternal();
    }

    /// <summary>
    /// Сохраняет дерево рефлексов в файл
    /// </summary>
    internal (bool Success, string ErrorMessage) SaveReflexTreeInternal()
    {
      try
      {
        var lines = new List<string>
        {
            "# ID|ParentID|BaseID|StyleID|ActionID|GeneticReflexID|ConditionedReflex|ReflexChainID",
            "# BaseID: -1: Плохо, 0: Норма, 1: Хорошо",
            "# StyleID: ID образа стилей поведения",
            "# ActionID: ID образа пусковых стимулов",
            "# ReflexChainID: ID цепочки рефлексов (0 если нет)"
        };

        foreach (var child in ReflexTree.Children)
        {
          lines.AddRange(GetReflexNodeStrings(child));
        }

        var lineCount = 3;
        if (lines.Count == 2)
          lineCount = 2;

        var result = FileValidator.SafeSaveFile(
            GetReflexTreeFilePath(),
            lines,
            content => IsValidReflexTreeFile(string.Join(Environment.NewLine, content)),
            minLinesCount: lineCount,
            fileDescription: "дерева рефлексов");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    /// <summary>
    /// Сохраняет дерево рефлексов в файл
    /// </summary>
    public (bool Success, string ErrorMessage) SaveReflexTree()
    {
      _lock.EnterReadLock();
      try
      {
        return SaveReflexTreeInternal();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает строки представления узла и его детей
    /// </summary>
    private List<string> GetReflexNodeStrings(ReflexNode node)
    {
      var lines = new List<string>();

      // Формат: ID|ParentID|BaseID|StyleID|ActionID|GeneticReflexID|ConditionedReflex|ReflexChainID
      lines.Add($"{node.ID}|{node.ParentID}|{node.BaseID}|{node.StyleID}|{node.ActionID}|" +
                $"{node.GeneticReflexID}|{node.ConditionedReflex}|{node.ReflexChainID}");

      foreach (var child in node.Children)
      {
        lines.AddRange(GetReflexNodeStrings(child));
      }

      return lines;
    }

    /// <summary>
    /// Сохраняет все атрибуты рефлексов
    /// </summary>
    public void SaveReflexesAttributes()
    {
      SaveReflexTreeInternal();
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом ReflexTreeSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        // Отписываемся от событий GeneticReflexesSystem
        if (_geneticReflexesSystem != null)
        {
          _geneticReflexesSystem.GeneticReflexDeleted -= OnGeneticReflexDeleted;
          _geneticReflexesSystem.GeneticReflexCreated -= OnGeneticReflexCreated;
          _geneticReflexesSystem.MultipleGeneticReflexesDeleted -= OnMultipleGeneticReflexesDeleted;
        }

        if (_conditionedReflexesSystem != null)
        {
          _conditionedReflexesSystem.ConditionedReflexCreated -= OnConditionedReflexCreated;
          _conditionedReflexesSystem.ConditionedReflexDeleted -= OnConditionedReflexDeleted;
          _conditionedReflexesSystem.MultipleConditionedReflexesDeleted -= OnMultipleConditionedReflexesDeleted;
        }

        SaveReflexesAttributes();
      }
      catch (Exception ex)
      {
        Logger.Error($"Error during disposal: {ex.Message}");
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