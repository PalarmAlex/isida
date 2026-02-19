using ISIDA.Common;
using ISIDA.Psychic.Automatism;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static ISIDA.Common.FileValidator;
using static ISIDA.Psychic.Automatism.ActionsImagesSystem;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Система управления цепочками автоматизмов
  /// </summary>
  public sealed class AutomatizmChainsSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly AutomatizmSystem _automatizmSystem;
    private bool _disposed = false;

    /// <summary>
    /// Событие удаления цепочки автоматизмов
    /// </summary>
    public event Action<int> AutomatizmChainDeleted;

    private void OnAutomatizmChainDeleted(int chainId)
    {
      AutomatizmChainDeleted?.Invoke(chainId);
    }

    #region Инициализация

    private static AutomatizmChainsSystem _instance;

    /// <summary>Глобальный экземпляр системы цепочек автоматизмов</summary>
    public static AutomatizmChainsSystem Instance => _instance ??
        throw new InvalidOperationException("AutomatizmChainsSystem не инициализирован.");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы цепочек автоматизмов
    /// </summary>
    public static void InitializeInstance(AutomatizmSystem automatizmSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("AutomatizmChainsSystem уже инициализирован.");

      if (!AutomatizmSystem.IsInitialized)
        throw new InvalidOperationException("AutomatizmSystem должен быть инициализирован перед AutomatizmChainsSystem");

      _instance = new AutomatizmChainsSystem(automatizmSystem);
    }

    private AutomatizmChainsSystem(AutomatizmSystem automatizmSystem)
    {
      _automatizmSystem = automatizmSystem ??
          throw new ArgumentNullException(nameof(automatizmSystem));

       _automatizmSystem.AutomatizmDeleted += OnAutomatizmDeleted;

      try
      {
        EnsureDataDirectory();
        LoadAutomatizmChains();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    /// <summary>
    /// Обработчик удаления автоматизма. Событие передаёт ID автоматизма (не ActionsImageId).
    /// Удаляются целые цепочки, у которых StartAutomatizmId совпадает с удалённым автоматизмом.
    /// </summary>
    private void OnAutomatizmDeleted(int automatizmId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_automatizmChains == null) return;

        var chainsToRemove = _automatizmChains.Values
            .Where(c => c.StartAutomatizmId == automatizmId)
            .Select(c => c.ID)
            .ToList();

        foreach (var chainId in chainsToRemove)
        {
          RemoveAutomatizmChainNoBlock(chainId);
        }

        if (chainsToRemove.Any())
        {
          SaveAutomatizmChainsCore();
          Logger.Info($"Удалены цепочки автоматизмов {string.Join(", ", chainsToRemove)} при удалении автоматизма {automatizmId}");
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Структуры данных

    /// <summary>Звено цепочки автоматизмов</summary>
    public class ChainLink
    {
      /// <summary>Уникальный идентификатор звена</summary>
      public int ID { get; set; }

      /// <summary>ID цепочки, к которой принадлежит звено</summary>
      public int ChainID { get; set; }

      /// <summary>ID образа действий для выполнения</summary>
      public int ActionsImageId { get; set; }

      /// <summary>ID следующего звена при успешном выполнении</summary>
      public int SuccessNextLink { get; set; }

      /// <summary>ID следующего звена при неудачном выполнении</summary>
      public int FailureNextLink { get; set; }

      /// <summary>Описание звена (для отладки)</summary>
      public string Description { get; set; }

      /// <summary>Оценка полезности звена цепочки. [-10...+10]</summary>
      public int ChainUsefulness { get; set; } = 0;
    }

    /// <summary>Цепочка автоматизмов</summary>
    public class AutomatizmChain
    {
      /// <summary>Уникальный идентификатор цепочки</summary>
      public int ID { get; set; }

      /// <summary>Наименование цепочки</summary>
      public string Name { get; set; }

      /// <summary>Описание цепочки</summary>
      public string Description { get; set; }

      /// <summary>Звенья цепочки</summary>
      public List<ChainLink> Links { get; set; } = new List<ChainLink>();

      /// <summary>ID узла дерева автоматизмов, к которому привязана цепочка</summary>
      public int TreeNodeId { get; set; }

      /// <summary>ID автоматизма, который запускает эту цепочку (NextID в автоматизме)</summary>
      public int StartAutomatizmId { get; set; }
    }

    #endregion

    #region Поля и свойства

    private const string AutomatizmChainsFileName = "AutomatizmChains";
    private readonly Dictionary<int, AutomatizmChain> _automatizmChains = new Dictionary<int, AutomatizmChain>();
    private int _lastChainId = 0;
    private int _lastLinkId = 0;

    /// <summary>
    /// Активные цепочки (ID цепочки -> текущее звено)
    /// </summary>
    private readonly Dictionary<int, int> _activeChains = new Dictionary<int, int>();

    /// <summary>
    /// Карта привязки образов действий к цепочкам (ActionsImageId -> ChainId)
    /// </summary>
    private readonly Dictionary<int, int> _actionsImageToChain = new Dictionary<int, int>();

    private string GetAutomatizmChainsFilePath()
    {
      var automatizmPath = Path.GetDirectoryName(_automatizmSystem.GetAutomatizmFilePath());
      return Path.Combine(automatizmPath, $"{AutomatizmChainsFileName}.dat");
    }

    /// <summary>
    /// Получает все цепочки автоматизмов
    /// </summary>
    public ReadOnlyDictionary<int, AutomatizmChain> GetAllAutomatizmChains()
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyDictionary<int, AutomatizmChain>(_automatizmChains);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Управление цепочками

    /// <summary>Получает цепочку по ID</summary>
    /// <param name="chainId">ID цепочки</param>
    /// <returns>Цепочка или null если не найдена</returns>
    public AutomatizmChain GetChain(int chainId)
    {
      _lock.EnterReadLock();
      try
      {
        return _automatizmChains.TryGetValue(chainId, out var chain) ? chain : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Получает звенья цепочки</summary>
    /// <param name="chainId">ID цепочки</param>
    /// <returns>Список звеньев цепочки</returns>
    public List<ChainLink> GetChainLinks(int chainId)
    {
      _lock.EnterReadLock();
      try
      {
        return _automatizmChains.TryGetValue(chainId, out var chain) ?
            new List<ChainLink>(chain.Links) : new List<ChainLink>();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Добавляет новую цепочку автоматизмов (обновленная версия)
    /// </summary>
    public (int ChainId, string[] Warnings) AddAutomatizmChain(
        string name, string description, List<ChainLink> links,
        int treeNodeId = 0, int startAutomatizmId = 0)
    {
      var warnings = new List<string>();

      if (AppGlobalState.EvolutionStage < 2)
        throw new InvalidOperationException("Цепочки автоматизмов доступны только начиная со стадии 2");

      if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Наименование цепочки не может быть пустым", nameof(name));

      if (links == null || !links.Any())
        throw new ArgumentException("Цепочка должна содержать хотя бы одно звено", nameof(links));

      // Проверяем существование образов действий вместо автоматизмов
      foreach (var link in links)
      {
        if (!DoesActionsImageExist(link.ActionsImageId))
          warnings.Add($"Образ действий с ID {link.ActionsImageId} не существует");

        // Если указан treeNodeId, проверяем существует ли автоматизм с этим образом действий
        if (treeNodeId > 0)
        {
          var automatizm = GetAutomatizmByActionsImageAndNode(link.ActionsImageId, treeNodeId);
          if (automatizm == null)
            warnings.Add($"Для узла дерева {treeNodeId} нет автоматизма с образом действий {link.ActionsImageId}");
        }

        // Проверяем дублирование образов действий в цепочке
        int duplicateCount = links.Count(l => l.ActionsImageId == link.ActionsImageId);
        if (duplicateCount > 1)
          warnings.Add($"Образ действий {link.ActionsImageId} повторяется {duplicateCount} раз в цепочке");
      }

      _lock.EnterWriteLock();
      try
      {
        int newId = ++_lastChainId;

        var chain = new AutomatizmChain
        {
          ID = newId,
          Name = name,
          Description = description,
          Links = links,
          TreeNodeId = treeNodeId,
          StartAutomatizmId = startAutomatizmId
        };

        _automatizmChains.Add(newId, chain);

        // Обновляем карту привязок (ActionsImageId -> ChainId)
        foreach (var link in links)
        {
          _actionsImageToChain[link.ActionsImageId] = newId;
        }

        return (newId, warnings.ToArray());
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Удаляет цепочку автоматизмов</summary>
    /// <param name="chainId">ID цепочки для удаления</param>
    /// <returns>True если цепочка удалена, иначе False</returns>
    public bool RemoveAutomatizmChain(int chainId)
    {
      _lock.EnterWriteLock();
      try
      {
        return RemoveAutomatizmChainNoBlock(chainId);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    internal bool RemoveAutomatizmChainNoBlock(int chainId)
    {
      try
      {
        if (AppGlobalState.EvolutionStage < 2)
          throw new InvalidOperationException("Цепочки автоматизмов доступны только начиная со стадии 2");

        if (!_automatizmChains.TryGetValue(chainId, out var chain))
          throw new KeyNotFoundException($"Цепочка с ID {chainId} не найдена");

        var linkIds = chain.Links.Select(l => l.ID).ToList();

        StopChain(chainId);

        foreach (var link in chain.Links)
        {
          _actionsImageToChain.Remove(link.ActionsImageId);
        }

        bool removed = _automatizmChains.Remove(chainId);

        if (removed)
        {
          SaveAutomatizmChainsCore();
          OnAutomatizmChainDeleted(chainId);
          Logger.Info($"Цепочка автоматизмов {chainId} удалена. Удалено звеньев: {linkIds.Count}");
        }

        return removed;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return false;
      }
    }

    /// <summary>Проверяет, используется ли образ действий в цепочках</summary>
    /// <param name="actionsImageId">ID образа действий</param>
    /// <returns>True если автоматизм используется в цепочках</returns>
    public bool IsAutomatizmUsedInChains(int actionsImageId)
    {
      _lock.EnterReadLock();
      try
      {
        return _automatizmChains.Values
            .Any(chain => chain.Links.Any(link => link.ActionsImageId == actionsImageId));
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Проверяет существование цепочки и звена</summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="linkId">ID звена</param>
    /// <returns>True если цепочка и звено существуют</returns>
    public bool ChainAndLinkExist(int chainId, int linkId)
    {
      _lock.EnterReadLock();
      try
      {
        if (!_automatizmChains.TryGetValue(chainId, out var chain))
          return false;

        return chain.Links.Any(l => l.ID == linkId);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Добавляет звено к существующей цепочке</summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="actionsImageId">ID образа действий</param>
    /// <param name="successNextLink">ID следующего звена при успехе</param>
    /// <param name="failureNextLink">ID следующего звена при неудаче</param>
    /// <param name="description">Описание звена</param>
    /// <param name="ChainUsefulness">Оценка полезности звена цепочки</param>
    /// <returns>ID созданного звена и предупреждения</returns>
    public (int LinkId, string[] Warnings) AddChainLink(
        int chainId, int actionsImageId, int successNextLink,
        int failureNextLink, string description, int ChainUsefulness = 0)
    {
      var warnings = new List<string>();

      _lock.EnterWriteLock();
      try
      {
        if (AppGlobalState.EvolutionStage < 2)
          throw new InvalidOperationException("Цепочки автоматизмов доступны только начиная со стадии 2");

        if (!_automatizmChains.TryGetValue(chainId, out var chain))
          throw new KeyNotFoundException($"Цепочка с ID {chainId} не найдена");

        if (!DoesActionsImageExist(actionsImageId))
          warnings.Add($"Образ действий с ID {actionsImageId} не существует");

        // Если у цепочки есть привязка к узлу дерева, проверяем существование автоматизма
        if (chain.TreeNodeId > 0)
        {
          var automatizm = GetAutomatizmByActionsImageAndNode(actionsImageId, chain.TreeNodeId);
          if (automatizm == null)
            warnings.Add($"Для узла дерева {chain.TreeNodeId} нет автоматизма с образом действий {actionsImageId}");
        }

        // Проверяем, не используется ли уже образ действий в другой цепочке
        if (_actionsImageToChain.ContainsKey(actionsImageId) && _actionsImageToChain[actionsImageId] != chainId)
        {
          warnings.Add($"Образ действий {actionsImageId} уже используется в цепочке {_actionsImageToChain[actionsImageId]}");
        }

        if (failureNextLink != 0)
        {
          var existingLink = chain.Links.FirstOrDefault(l => l.ID == failureNextLink);
          if (existingLink == null)
            warnings.Add($"Следующее звено при неудаче (ID:{failureNextLink}) не найдено в цепочке");
          else if (failureNextLink <= chain.Links.Max(l => l.ID))
            warnings.Add($"Ссылка на предыдущее звено (ID:{failureNextLink}) запрещена");
        }

        int newLinkId = ++_lastLinkId;

        var link = new ChainLink
        {
          ID = newLinkId,
          ChainID = chainId,
          ActionsImageId = actionsImageId,
          SuccessNextLink = successNextLink,
          FailureNextLink = failureNextLink,
          Description = description ?? $"Звено {newLinkId}",
          ChainUsefulness = SettingsValidator.ClampChainLinkUsefulness(ChainUsefulness)
        };

        chain.Links.Add(link);
        _actionsImageToChain[actionsImageId] = chainId;

        return (newLinkId, warnings.ToArray());
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обновляет существующее звено цепочки
    /// </summary>
    public (bool Success, string[] Warnings) UpdateChainLink(
        int chainId, int linkId, int actionsImageId, int successNextLink,
        int failureNextLink, string description, int ChainUsefulness = 0)
    {
      var warnings = new List<string>();

      _lock.EnterWriteLock();
      try
      {
        if (AppGlobalState.EvolutionStage < 2)
          throw new InvalidOperationException("Цепочки автоматизмов доступны только начиная со стадии 2");

        if (!_automatizmChains.TryGetValue(chainId, out var chain))
          throw new KeyNotFoundException($"Цепочка с ID {chainId} не найдена");

        var link = chain.Links.FirstOrDefault(l => l.ID == linkId);
        if (link == null)
          throw new KeyNotFoundException($"Звено с ID {linkId} не найдено в цепочке {chainId}");

        var allAutomatizms = _automatizmSystem.GetAllAutomatizms();
        if (!allAutomatizms.Any(a => a.ID == actionsImageId))
          warnings.Add($"Автоматизм с ID {actionsImageId} не существует");

        // Если меняем автоматизм, обновляем карту привязок
        if (link.ActionsImageId != actionsImageId)
        {
          // Убираем старую привязку
          _actionsImageToChain.Remove(link.ActionsImageId);

          // Проверяем, не используется ли новый автоматизм в другой цепочке
          if (_actionsImageToChain.ContainsKey(actionsImageId) && _actionsImageToChain[actionsImageId] != chainId)
            warnings.Add($"Автоматизм {actionsImageId} уже используется в цепочке {_actionsImageToChain[actionsImageId]}");
          else
            _actionsImageToChain[actionsImageId] = chainId;
        }

        if (successNextLink != 0 && successNextLink != linkId)
        {
          var existingLink = chain.Links.FirstOrDefault(l => l.ID == successNextLink);
          if (existingLink == null)
            warnings.Add($"Следующее звено при успехе (ID:{successNextLink}) не найдено");
          else if (successNextLink <= linkId)
            warnings.Add($"Ссылка на предыдущее звено (ID:{successNextLink}) запрещена");
        }

        if (failureNextLink != 0 && failureNextLink != linkId)
        {
          var existingLink = chain.Links.FirstOrDefault(l => l.ID == failureNextLink);
          if (existingLink == null)
            warnings.Add($"Следующее звено при неудаче (ID:{failureNextLink}) не найдено");
          else if (failureNextLink <= linkId)
            warnings.Add($"Ссылка на предыдущее звено (ID:{failureNextLink}) запрещена");
        }

        link.ActionsImageId = actionsImageId;
        link.SuccessNextLink = successNextLink;
        link.FailureNextLink = failureNextLink;
        link.Description = description ?? link.Description;
        link.ChainUsefulness = SettingsValidator.ClampChainLinkUsefulness(ChainUsefulness);

        SaveAutomatizmChainsCore();
        return (true, warnings.ToArray());
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Удаляет звено из цепочки</summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="linkId">ID звена</param>
    /// <param name="reconnectLinks">Обновлять ли ссылки других звеньев на удаляемое</param>
    /// <returns>True если звено удалено</returns>
    /// <exception cref="InvalidOperationException">Если удаление последнего звена</exception>
    public bool RemoveChainLink(int chainId, int linkId, bool reconnectLinks = false)
    {
      _lock.EnterWriteLock();
      try
      {
        if (AppGlobalState.EvolutionStage < 2)
          throw new InvalidOperationException("Цепочки автоматизмов доступны только начиная со стадии 2");

        if (!_automatizmChains.TryGetValue(chainId, out var chain))
          throw new KeyNotFoundException($"Цепочка с ID {chainId} не найдена");

        var linkToRemove = chain.Links.FirstOrDefault(l => l.ID == linkId);
        if (linkToRemove == null)
          return false;

        if (chain.Links.Count <= 1)
        {
          throw new InvalidOperationException(
              $"Невозможно удалить последнее звено {linkId} из цепочки {chainId}. " +
              "Цепочка должна содержать хотя бы одно звено. " +
              "Для удаления цепочки используйте метод RemoveAutomatizmChain().");
        }

        var referencingLinks = chain.Links.Where(l =>
            l.SuccessNextLink == linkId || l.FailureNextLink == linkId).ToList();

        if (referencingLinks.Any())
        {
          if (!reconnectLinks)
            throw new InvalidOperationException(
                $"Звено {linkId} используется другими звеньями: " +
                string.Join(", ", referencingLinks.Select(l => l.ID)));

          foreach (var refLink in referencingLinks)
          {
            if (refLink.SuccessNextLink == linkId)
              refLink.SuccessNextLink = 0;
            if (refLink.FailureNextLink == linkId)
              refLink.FailureNextLink = 0;
          }
        }

        // Убираем из карты привязок
        _actionsImageToChain.Remove(linkToRemove.ActionsImageId);

        bool removed = chain.Links.Remove(linkToRemove);
        if (removed)
          SaveAutomatizmChainsCore();

        return removed;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Проверяет целостность цепочки
    /// </summary>
    public (bool IsValid, string[] Issues) ValidateChain(int chainId)
    {
      var issues = new List<string>();

      _lock.EnterReadLock();
      try
      {
        if (!_automatizmChains.TryGetValue(chainId, out var chain))
        {
          issues.Add($"Цепочка с ID {chainId} не найдена");
          return (false, issues.ToArray());
        }

        if (chain.Links.Count < 1)
        {
          issues.Add("Цепочка должна содержать хотя бы одно звено");
          return (false, issues.ToArray());
        }

        var allAutomatizms = _automatizmSystem.GetAllAutomatizms();
        foreach (var link in chain.Links)
        {
          if (!allAutomatizms.Any(a => a.ID == link.ActionsImageId))
            issues.Add($"Образ действий {link.ActionsImageId} в звене {link.ID} не существует");

          if (link.SuccessNextLink != 0 && link.SuccessNextLink <= link.ID)
            issues.Add($"Звено {link.ID} ссылается на предыдущее звено {link.SuccessNextLink}");

          if (link.FailureNextLink != 0 && link.FailureNextLink <= link.ID)
            issues.Add($"Звено {link.ID} ссылается на предыдущее звено {link.FailureNextLink}");

          if (link.SuccessNextLink != 0 && link.SuccessNextLink != link.ID &&
              !chain.Links.Any(l => l.ID == link.SuccessNextLink))
            issues.Add($"Звено {link.ID}: следующее при успехе {link.SuccessNextLink} не найдено");

          if (link.FailureNextLink != 0 && link.FailureNextLink != link.ID &&
              !chain.Links.Any(l => l.ID == link.FailureNextLink))
            issues.Add($"Звено {link.ID}: следующее при неудаче {link.FailureNextLink} не найдено");
        }

        var terminalLinks = chain.Links.Where(l =>
            l.SuccessNextLink == 0 && l.FailureNextLink == 0).ToList();

        if (terminalLinks.Count == 0)
        {
          issues.Add("Цепочка не содержит конечных звеньев (бесконечный цикл)");
        }

        return (!issues.Any(), issues.ToArray());
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Проверяет, существует ли образ действий
    /// </summary>
    private bool DoesActionsImageExist(int actionsImageId)
    {
      if (!ActionsImagesSystem.IsInitialized)
        return false;

      return ActionsImagesSystem.Instance.GetActionsImage(actionsImageId) != null;
    }

    /// <summary>
    /// Получает автоматизм по ID образа действий для указанного узла дерева
    /// </summary>
    private Automatizm GetAutomatizmByActionsImageAndNode(int actionsImageId, int treeNodeId)
    {
      if (!ActionsImagesSystem.IsInitialized || _automatizmSystem == null)
        return null;

      var automatizms = _automatizmSystem.GetMotorsAutomatizmListFromTreeId(treeNodeId);
      return automatizms.FirstOrDefault(a => a.ActionsImageID == actionsImageId);
    }

    #endregion

    #region Работа с активными цепочками

    /// <summary>
    /// Запускает выполнение цепочки автоматизмов
    /// </summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="startLinkId">ID звена, с которого начать (0 для начала цепочки)</param>
    /// <returns>True если цепочка успешно запущена</returns>
    public bool StartChain(int chainId, int startLinkId = 0)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_automatizmChains.TryGetValue(chainId, out var chain))
          return false;

        if (startLinkId == 0)
        {
          // Начинаем с первого звена
          var firstLink = chain.Links.OrderBy(l => l.ID).FirstOrDefault();
          if (firstLink == null)
            return false;

          _activeChains[chainId] = firstLink.ID;
        }
        else
        {
          // Проверяем существование звена
          if (!chain.Links.Any(l => l.ID == startLinkId))
            return false;

          _activeChains[chainId] = startLinkId;
        }
        return true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Получаем данные для следующего шага в активной цепочке
    /// </summary>
    internal (int ExecutedActionsImageId, int NextLinkId, bool ChainCompleted)
        GetNextChainStepData(int chainId, int usefulness)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_activeChains.TryGetValue(chainId, out int currentLinkId))
          return (0, 0, true);

        if (!_automatizmChains.TryGetValue(chainId, out var chain))
        {
          StopChain(chainId);
          return (0, 0, true);
        }

        var currentLink = chain.Links.FirstOrDefault(l => l.ID == currentLinkId);
        if (currentLink == null)
        {
          StopChain(chainId);
          return (0, 0, true);
        }

        int nextLinkId = 0;

        if (usefulness >= 0)
          nextLinkId = currentLink.SuccessNextLink;
        else
          nextLinkId = currentLink.FailureNextLink;

        if (nextLinkId == 0)
        {
          Logger.Info($"Цепочка {chainId}: следующее звено не указано, завершение");
          StopChain(chainId);
          return (currentLink.ActionsImageId, 0, true);
        }

        var nextLink = chain.Links.FirstOrDefault(l => l.ID == nextLinkId);
        if (nextLink == null)
        {
          Logger.Warning($"Следующее звено {nextLinkId} не найдено в цепочке {chainId}");
          StopChain(chainId);
          return (currentLink.ActionsImageId, 0, true);
        }

        _activeChains[chainId] = nextLinkId;

        Logger.Info($"Цепочка {chainId}: переход от звена {currentLinkId} к {nextLinkId}");
        return (currentLink.ActionsImageId, nextLinkId, false);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обновляет полезность звена цепочки
    /// </summary>
    public bool UpdateLinkUsefulness(int chainId, int linkId, int usefulness)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_automatizmChains.TryGetValue(chainId, out var chain))
          return false;

        var link = chain.Links.FirstOrDefault(l => l.ID == linkId);
        if (link == null)
          return false;

        link.ChainUsefulness += usefulness;
        // Ограничиваем значение в диапазоне [-10:10]
        link.ChainUsefulness = SettingsValidator.ClampChainLinkUsefulness(link.ChainUsefulness);

        return true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Останавливает выполнение цепочки
    /// </summary>
    /// <param name="chainId">ID цепочки</param>
    public void StopChain(int chainId)
    {
      _activeChains.Remove(chainId);
    }

    /// <summary>
    /// Проверяет, активна ли цепочка
    /// </summary>
    /// <param name="chainId">ID цепочки</param>
    public bool IsChainActive(int chainId)
    {
      _lock.EnterReadLock();
      try
      {
        return _activeChains.ContainsKey(chainId);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает текущее активное звено цепочки
    /// </summary>
    /// <param name="chainId">ID цепочки</param>
    /// <returns>ID текущего звена или 0 если цепочка не активна</returns>
    public int GetCurrentChainLink(int chainId)
    {
      _lock.EnterReadLock();
      try
      {
        return _activeChains.TryGetValue(chainId, out int linkId) ? linkId : 0;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает цепочку по ID образа действий
    /// </summary>
    /// <param name="actionsImageId">ID автоматизма</param>
    /// <returns>ID цепочки или 0</returns>
    public int GetChainByActionsImage(int actionsImageId)
    {
      _lock.EnterReadLock();
      try
      {
        return _actionsImageToChain.TryGetValue(actionsImageId, out int chainId) ? chainId : 0;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает цепочку по узлу дерева автоматизмов
    /// </summary>
    /// <param name="treeNodeId">ID узла дерева автоматизмов</param>
    /// <returns>ID цепочки или 0</returns>
    public int GetChainByTreeNode(int treeNodeId)
    {
      _lock.EnterReadLock();
      try
      {
        var chain = _automatizmChains.Values.FirstOrDefault(c => c.TreeNodeId == treeNodeId);
        return chain?.ID ?? 0;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Работа с файлами

    private void EnsureDataDirectory()
    {
      string directory = Path.GetDirectoryName(GetAutomatizmChainsFilePath());
      if (!Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }
    }

    /// <summary>
    /// Загружает цепочки автоматизмов из файла
    /// </summary>
    private void LoadAutomatizmChains()
    {
      string filePath = GetAutomatizmChainsFilePath();

      if (!File.Exists(filePath))
      {
        CreateDefaultAutomatizmChainsFile();
        return;
      }

      if (!IsValidAutomatizmChainsFile(filePath))
      {
        CreateDefaultAutomatizmChainsFile();
        return;
      }

      try
      {
        _lock.EnterWriteLock();
        try
        {
          _automatizmChains.Clear();
          _lastChainId = 0;
          _lastLinkId = 0;
          _actionsImageToChain.Clear();

          AutomatizmChain currentChain = null;

          foreach (var line in File.ReadLines(filePath))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');

            if (parts.Length >= 4 && parts[0] == "CHAIN")
            {
              if (int.TryParse(parts[1], out int chainId) && chainId > 0)
              {
                currentChain = new AutomatizmChain
                {
                  ID = chainId,
                  Name = parts.Length > 2 ? parts[2] : "",
                  Description = parts.Length > 3 ? parts[3] : "",
                  TreeNodeId = parts.Length > 4 && int.TryParse(parts[4], out int treeNodeId) ? treeNodeId : 0,
                  StartAutomatizmId = parts.Length > 5 && int.TryParse(parts[5], out int startAutomatizmId) ? startAutomatizmId : 0,
                  Links = new List<ChainLink>()
                };

                _automatizmChains[chainId] = currentChain;
                if (chainId > _lastChainId)
                  _lastChainId = chainId;
              }
              continue;
            }

            if (parts.Length >= 5 && parts[0] == "LINK" && currentChain != null)
            {
              if (int.TryParse(parts[1], out int linkId) &&
                  int.TryParse(parts[2], out int actionsImageId) &&
                  int.TryParse(parts[3], out int successNext) &&
                  int.TryParse(parts[4], out int failureNext))
              {
                string description = parts.Length > 5 ? parts[5] : "";
                int useFulnes = parts.Length > 6 && int.TryParse(parts[6], out int thr) ? thr : 0;

                var link = new ChainLink
                {
                  ID = linkId,
                  ChainID = currentChain.ID,
                  ActionsImageId = actionsImageId,
                  SuccessNextLink = successNext,
                  FailureNextLink = failureNext,
                  Description = description,
                  ChainUsefulness = SettingsValidator.ClampChainLinkUsefulness(useFulnes)
                };

                currentChain.Links.Add(link);
                _actionsImageToChain[actionsImageId] = currentChain.ID;

                if (linkId > _lastLinkId)
                  _lastLinkId = linkId;
              }
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
        Logger.Error(ex.Message);
        CreateDefaultAutomatizmChainsFile();
      }
    }

    private void CreateDefaultAutomatizmChainsFile()
    {
      EnsureDataDirectory();

      var lines = new List<string>
    {
        FileHeaders.AutomatizmChainsFormat,
        FileHeaders.AutomatizmChainsChain,
        FileHeaders.AutomatizmChainsLink,
        FileHeaders.AutomatizmChainsChainDesc,
        FileHeaders.AutomatizmChainsNameDesc,
        FileHeaders.AutomatizmChainsTreeNodeDesc,
        FileHeaders.AutomatizmChainsStartAutomatizmDesc,
        FileHeaders.AutomatizmChainsLinkDesc,
        FileHeaders.AutomatizmChainsAutomatizmDesc,
        FileHeaders.AutomatizmChainsSuccessDesc,
        FileHeaders.AutomatizmChainsFailureDesc,
        FileHeaders.AutomatizmChainsThresholdDesc
    };

      File.WriteAllLines(GetAutomatizmChainsFilePath(), lines);
    }

    /// <summary>Сохраняет цепочки автоматизмов в файл</summary>
    public (bool Success, string ErrorMessage) SaveAutomatizmChains()
    {
      _lock.EnterWriteLock();
      try
      {
        return SaveAutomatizmChainsCore();
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка при сохранении цепочек автоматизмов: {ex.Message}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    private (bool Success, string ErrorMessage) SaveAutomatizmChainsCore()
    {
      if (AppGlobalState.EvolutionStage < 2)
        throw new InvalidOperationException("Цепочки автоматизмов доступны только начиная со стадии 2");

      var lines = new List<string>
    {
        FileHeaders.AutomatizmChainsFormat,
        FileHeaders.AutomatizmChainsChain,
        FileHeaders.AutomatizmChainsLink,
        FileHeaders.AutomatizmChainsChainDesc,
        FileHeaders.AutomatizmChainsNameDesc,
        FileHeaders.AutomatizmChainsTreeNodeDesc,
        FileHeaders.AutomatizmChainsStartAutomatizmDesc,
        FileHeaders.AutomatizmChainsLinkDesc,
        FileHeaders.AutomatizmChainsAutomatizmDesc,
        FileHeaders.AutomatizmChainsSuccessDesc,
        FileHeaders.AutomatizmChainsFailureDesc,
        FileHeaders.AutomatizmChainsThresholdDesc
    };
      lines.Add("");

      if (_automatizmChains.Count > 0)
      {
        foreach (var chain in _automatizmChains.Values.OrderBy(c => c.ID))
        {
          lines.Add($"CHAIN|{chain.ID}|{chain.Name ?? ""}|{chain.Description ?? ""}|{chain.TreeNodeId}|{chain.StartAutomatizmId}");

          foreach (var link in chain.Links.OrderBy(l => l.ID))
          {
            lines.Add($"LINK|{link.ID}|{link.ActionsImageId}|{link.SuccessNextLink}|{link.FailureNextLink}|{link.Description ?? ""}|{link.ChainUsefulness}");
          }
          lines.Add("");
        }
      }

      var result = SafeSaveFile(
          GetAutomatizmChainsFilePath(),
          lines,
          IsValidAutomatizmChainsFile,
          minLinesCount: 13,
          fileDescription: "цепочек автоматизмов");

      if (!result.Success)
        Logger.Warning($"Ошибка сохранения цепочек автоматизмов: {result.ErrorMessage}");

      return result;
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы системы цепочек автоматизмов</summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        if (_automatizmSystem != null)
          _automatizmSystem.AutomatizmDeleted -= OnAutomatizmDeleted;

        if(AppGlobalState.EvolutionStage >= 2)
          SaveAutomatizmChains();
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