using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Psychic.Automatism;
using ISIDA.Psychic.Understanding;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ISIDA.Psychic.Memory.Episodic
{
  /// <summary>
  /// Система моторной эпизодической памяти
  /// </summary>
  public sealed class EpisodicMemorySystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed;
    private readonly string _dataPath;

    private readonly AutomatizmTreeSystem _automatizmTree;
    private readonly UnderstandingTreeSystem _understandingTree;
    private readonly ProblemTreeSystem _problemTree;
    private readonly InformationEnvironmentSystem _infoEnv;
    private readonly GomeostasSystem _gomeostas;
    private readonly ActionsImagesSystem _actionsImages;

    /// <summary>Корневой узел дерева эпизодов</summary>
    public EpisodicMemoryNode Tree { get; } = new EpisodicMemoryNode { ID = 0 };
    /// <summary>Историческая лента кадров эпизодов</summary>
    public EpisodicMemoryHistory History { get; } = new EpisodicMemoryHistory();
    private readonly Dictionary<int, EpisodicMemoryNode> _nodesById = new Dictionary<int, EpisodicMemoryNode>();
    private readonly EpisodicMemoryTree _treeLogic = new EpisodicMemoryTree();
    private int _lastNodeId = 1;

    #region Инициализация

    private static EpisodicMemorySystem _instance;

    /// <summary>Глобальный экземпляр системы эпизодической памяти</summary>
    public static EpisodicMemorySystem Instance => _instance ??
        throw new InvalidOperationException("EpisodicMemorySystem не инициализирован.");

    /// <summary>Признак инициализации системы</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Инициализирует глобальный экземпляр системы эпизодической памяти</summary>
    /// <param name="psychicDataPath">Путь к данным психики (или null для стандартного)</param>
    /// <param name="automatizmTree">Система дерева автоматизмов</param>
    /// <param name="problemTree">Система дерева проблем</param>
    /// <param name="understandingTree">Дерево понимания ситуации (ключ ветки эпизодов)</param>
    /// <param name="infoEnv">Информационная среда</param>
    /// <param name="gomeostas">Система гомеостаза</param>
    /// <param name="actionsImages">Система образов действий</param>
    public static void InitializeInstance(
        string psychicDataPath,
        AutomatizmTreeSystem automatizmTree,
        ProblemTreeSystem problemTree,
        UnderstandingTreeSystem understandingTree,
        InformationEnvironmentSystem infoEnv,
        GomeostasSystem gomeostas,
        ActionsImagesSystem actionsImages)
    {
      if (_instance != null)
        throw new InvalidOperationException("EpisodicMemorySystem уже инициализирован.");

      _instance = new EpisodicMemorySystem(
          psychicDataPath,
          automatizmTree,
          problemTree,
          understandingTree,
          infoEnv,
          gomeostas,
          actionsImages);
    }

    private EpisodicMemorySystem(
        string psychicDataPath,
        AutomatizmTreeSystem automatizmTree,
        ProblemTreeSystem problemTree,
        UnderstandingTreeSystem understandingTree,
        InformationEnvironmentSystem infoEnv,
        GomeostasSystem gomeostas,
        ActionsImagesSystem actionsImages)
    {
      _dataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? System.IO.Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Memory", "Episodic")
          : System.IO.Path.Combine(psychicDataPath, "Memory", "Episodic");

      _automatizmTree = automatizmTree ?? throw new ArgumentNullException(nameof(automatizmTree));
      _understandingTree = understandingTree;
      _problemTree = problemTree;
      _infoEnv = infoEnv ?? throw new ArgumentNullException(nameof(infoEnv));
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _actionsImages = actionsImages ?? throw new ArgumentNullException(nameof(actionsImages));

      EpisodicMemoryStorage.EnsureDirectory(_dataPath);
      EpisodicMemoryStorage.LoadEpisodicTree(_dataPath, Tree, _nodesById, ref _lastNodeId);
      _nodesById[0] = Tree;
      EpisodicMemoryStorage.LoadEpisodicHistory(_dataPath, History);
      if (History.Entries.Count > 0)
        History.SetInterruption(0);
      _treeLogic.SetLastNodeId(_lastNodeId);
    }

    #endregion

    #region Текущие условия

    /// <summary>Базовое состояние: -1 Плохо, 0 Норма, 1 Хорошо</summary>
    private int GetBaseId()
    {
      var state = AppGlobalState.CurrentOverallState;
      if (state == AppGlobalState.HomeostasisState.Bad) return -1;
      if (state == AppGlobalState.HomeostasisState.Well) return 1;
      return 0;
    }

    /// <summary>ID образа эмоции из информационной среды (для дерева эпизодов)</summary>
    private int GetEmotionId()
    {
      try
      {
        return _infoEnv?.CurrentInformationEnvironment?.PsyEmotionId ?? 0;
      }
      catch { return 0; }
    }

    /// <summary>NodePID из дерева проблем</summary>
    private int GetNodePid(bool useOld)
    {
      if (_problemTree == null) return 0;
      return useOld ? _problemTree.OldDetectedActiveLastProblemNodeId : _problemTree.DetectedActiveLastProblemNodeId;
    }

    /// <summary>ID активного узла дерева понимания (Understanding)</summary>
    private int GetUnderstandingNodeId()
    {
      return _understandingTree?.DetectedActiveLastUnderstandingNodeId ?? 0;
    }

    /// <summary>Получить текущие условия ветки эпизодов (база, эмоция, узел понимания, узел проблем)</summary>
    /// <param name="useOldCondition">Использовать предыдущее состояние проблемы (для учительских правил)</param>
    /// <returns>При стадии &lt; 4 возвращает (0, 0, 0, 0)</returns>
    /// <remarks>Доступно с 4 стадии развития</remarks>
    public (int BaseId, int EmotionId, int UnderstandingNodeId, int NodePid) GetCurrentConditions(bool useOldCondition = false)
    {
      if (AppGlobalState.EvolutionStage < 4)
      {
        Logger.Warning($"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для эпизодической памяти");
        return (0, 0, 0, 0);
      }
      return (GetBaseId(), GetEmotionId(), GetUnderstandingNodeId(), GetNodePid(useOldCondition));
    }

    /// <summary>
    /// Последний узел моторной эпизодики в ленте истории (пропускает разрывы NodeId=-1).
    /// Для связи ментального эпизода с моторным кадром при фиксации цепочки инфо-функций.
    /// </summary>
    public int GetLastRecordedEpisodeNodeId()
    {
      _lock.EnterReadLock();
      try
      {
        var entries = History?.Entries;
        if (entries == null || entries.Count == 0)
          return 0;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
          var id = entries[i]?.NodeId ?? 0;
          if (id > 0)
            return id;
        }
        return 0;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Сохранение эпизода

    /// <summary>Записать новый эпизод</summary>
    /// <remarks>Доступно с 4 стадии развития</remarks>
    public int SaveNewEpisode(int triggerId, int actionId, int effect, int stimulsEffect, bool useOldCondition = false, bool isTeacher = false)
    {
      if (AppGlobalState.EvolutionStage < 4)
      {
        Logger.Warning($"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для эпизодической памяти");
        return -1;
      }
      _lock.EnterWriteLock();
      try
      {
        var (baseId, emotionId, understandingNodeId, nodePid) = GetCurrentConditions(useOldCondition);

        var pars = new EpisodicParams
        {
          Effect = isTeacher ? 0 : effect,
          Count = 1,
          StimulsEffect = stimulsEffect,
          IsTeacher = isTeacher
        };
        if (!isTeacher)
          pars.Effect = AddUtils.Clamp(pars.Effect, -10, 10);
        else
          pars.StimulsEffect = AddUtils.Clamp(pars.StimulsEffect, -10, 10);

        var condArr = new[] { baseId, emotionId, understandingNodeId, nodePid, triggerId, actionId };
        var (idOld, nodeOld) = _treeLogic.CheckBranchFromCondition(
            Tree, baseId, emotionId, understandingNodeId, nodePid, triggerId, actionId);

        // Обновлять существующий узел (AverageEffect) и выходить — только если нашли именно листовой узел с тем же Trigger+Action.
        // Иначе CheckBranchFromCondition мог вернуть родительский узел (при частичном совпадении по GetTrueLevel), и мы бы никогда не доращивали ветку.
        if (idOld > 0 && nodeOld != null && nodeOld.TriggerId == triggerId && nodeOld.ActionId == actionId)
        {
          History.Append(idOld, AppGlobalState.Lifetime);
          _treeLogic.AverageEffect(nodeOld, effect, stimulsEffect, isTeacher);
          return idOld;
        }

        var lastId = _treeLogic.AddBranch(Tree, 0, condArr, pars);
        if (lastId >= 0)
        {
          var node = _treeLogic.FindNodeById(Tree, lastId);
          if (node != null && !_nodesById.ContainsKey(lastId))
            _nodesById[lastId] = node;
          if (lastId > _lastNodeId) _lastNodeId = lastId;
          History.Append(lastId, AppGlobalState.Lifetime);
        }
        return lastId;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Вставить пустой кадр — конец темы</summary>
    /// <remarks>Доступно с 4 стадии развития</remarks>
    public void SetInterruption()
    {
      if (AppGlobalState.EvolutionStage < 4)
      {
        Logger.Warning($"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для эпизодической памяти");
        return;
      }
      History.SetInterruption(AppGlobalState.Lifetime);
    }

    #endregion

    #region Load / Save / Clear

    /// <summary>Очистить эпизодическую память (для пульта)</summary>
    /// <remarks>Доступно с 4 стадии развития</remarks>
    public void ClearEpisodicMemory()
    {
      if (AppGlobalState.EvolutionStage < 4)
      {
        Logger.Warning($"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для эпизодической памяти");
        return;
      }
      Clear();
    }

    /// <summary>Сохранить дерево и историю эпизодов на диск</summary>
    /// <returns>Успех и сообщение об ошибке при неудаче</returns>
    public (bool Success, string Error) Save()
    {
      _lock.EnterReadLock();
      try
      {
        var (ok, err) = EpisodicMemoryStorage.SaveEpisodicTree(_dataPath, Tree);
        if (!ok) return (false, err);
        var (ok2, err2) = EpisodicMemoryStorage.SaveEpisodicHistory(_dataPath, History);
        return ok2 ? (true, null) : (false, err2);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Очистить дерево и историю эпизодов в памяти. Запись на диск — при Dispose.</summary>
    public void Clear()
    {
      _lock.EnterWriteLock();
      try
      {
        Tree.Children.Clear();
        _nodesById.Clear();
        _nodesById[0] = Tree;
        History.Clear();
        _lastNodeId = 1;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Доступ по ID (O(1) через словарь)

    /// <summary>
    /// Получить узел дерева по ID. Использует словарь для быстрого доступа.
    /// </summary>
    /// <param name="id">ID узла эпизода</param>
    /// <returns>Узел или null</returns>
    public EpisodicMemoryNode GetNodeById(int id)
    {
      if (id < 0) return null;
      _lock.EnterReadLock();
      try
      {
        if (_nodesById.TryGetValue(id, out var node))
          return node;
        return _treeLogic.FindNodeById(Tree, id);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Доступ к правилам (для EpisodicMemoryRulesService)

    internal EpisodicMemoryTree TreeLogic => _treeLogic;
    internal EpisodicMemoryNode Root => Tree;

    #endregion

    #region Поиск правил (для PsychicSystem, stage 4)

    /// <summary>GPT-цепочка: цепочка правил с конечным позитивом для данного стимула</summary>
    public List<EpisodicRule> GetTargetChain(int triggerId, int limit = 0)
    {
      return EpisodicMemorySearch.GetTargetChain(this, triggerId, limit);
    }

    /// <summary>Лучшее правило по условиям (typeRule: 1-прямые, 2-учительские, 3-все)</summary>
    public EpisodicRule GetSingleBestRule(int typeRule, int triggerId)
    {
      return EpisodicMemorySearch.GetSingleBestRule(this, typeRule, triggerId);
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы, сохраняет данные</summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        var (ok, err) = Save();
        if (!ok && !string.IsNullOrEmpty(err))
          Logger.Warning($"Ошибка сохранения эпизодической памяти: {err}");
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
