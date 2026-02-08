using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static ISIDA.Actions.AdaptiveActionsSystem;
using static ISIDA.Psychic.Automatism.ActionsImagesSystem;
using static ISIDA.Psychic.Automatism.AutomatizmChainsSystem;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Сервис выполнения действий автоматизмов
  /// </summary>
  public sealed class AutomatismExecutionService : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;
    private AutomatizmSystem _automatizmSystem;
    private PsychicSystem _psychicSystem;
    private OrientationReflexSystem _orientationReflexSystem;
    private bool _disposed = false;

    #region Инициализация

    private static AutomatismExecutionService _instance;

    /// <summary>
    /// Глобальный экземпляр сервиса выполнения действий автоматизмов
    /// </summary>
    public static AutomatismExecutionService Instance => _instance ??
        throw new InvalidOperationException("AutomatismExecutionService не инициализирован.");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр сервиса выполнения действий автоматизмов
    /// </summary>
    public static void InitializeInstance(
        AdaptiveActionsSystem adaptiveActionsSystem,
        ActionsImagesSystem actionsImagesSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("AutomatismExecutionService уже инициализирован.");

      _instance = new AutomatismExecutionService(adaptiveActionsSystem, actionsImagesSystem);
    }

    /// <summary>
    /// Вторичная инициализация с зависимостями от систем психики
    /// </summary>
    public static void InitializeWithDependencies(
        AutomatizmSystem automatizmSystem,
        PsychicSystem psychicSystem,
        OrientationReflexSystem orientationReflexSystem,
        AutomatizmChainsSystem automatizmChainsSystem)
    {
      if (_instance == null)
        throw new InvalidOperationException("AutomatismExecutionService должен быть сначала инициализирован через InitializeInstance()");

      _instance.SetDependencies(automatizmSystem, psychicSystem, orientationReflexSystem);
      _instance.SetAutomatizmChainsSystem(automatizmChainsSystem);
    }

    private AutomatismExecutionService(
        AdaptiveActionsSystem adaptiveActionsSystem,
        ActionsImagesSystem actionsImagesSystem)
    {
      _adaptiveActionsSystem = adaptiveActionsSystem ??
          throw new ArgumentNullException(nameof(adaptiveActionsSystem));
      _actionsImagesSystem = actionsImagesSystem ??
          throw new ArgumentNullException(nameof(actionsImagesSystem));

      _reflexActionDuration = _adaptiveActionsSystem.ReflexActionDisplayDuration;

      _automatizmSystem = null;
      _psychicSystem = null;
      _orientationReflexSystem = null;
    }

    /// <summary>
    /// Устанавливает зависимости от систем психики
    /// </summary>
    private void SetDependencies(
        AutomatizmSystem automatizmSystem,
        PsychicSystem psychicSystem,
        OrientationReflexSystem orientationReflexSystem)
    {
      _automatizmSystem = automatizmSystem ??
          throw new ArgumentNullException(nameof(automatizmSystem));
      _psychicSystem = psychicSystem ??
          throw new ArgumentNullException(nameof(psychicSystem));
      _orientationReflexSystem = orientationReflexSystem ??
          throw new ArgumentNullException(nameof(orientationReflexSystem));
    }

    /// <summary>
    /// Проверяет, установлены ли все зависимости
    /// </summary>
    public bool AreDependenciesSet =>
        _automatizmSystem != null &&
        _psychicSystem != null &&
        _orientationReflexSystem != null;

    /// <summary>
    /// Проверяет, доступны ли цепочки автоматизмов
    /// </summary>
    public bool AreChainsAvailable =>
        AppGlobalState.EvolutionStage >= 2 && _automatizmChainsSystem != null;

    /// <summary>
    /// Устанавливает систему цепочек автоматизмов
    /// </summary>
    public void SetAutomatizmChainsSystem(AutomatizmChainsSystem automatizmChainsSystem)
    {
      _automatizmChainsSystem = automatizmChainsSystem ??
          throw new ArgumentNullException(nameof(automatizmChainsSystem));
    }

    #endregion

    /// <summary>
    /// Обрабатывает пульс для активных цепочек автоматизмов
    /// </summary>
    public void ProcessAutomatizmChainsPulse(int pulseCount)
    {
      if (_pendingChainActivation != null &&
          pulseCount >= _pendingChainActivation.StartPulse + _reflexActionDuration)
      {
        StartAutomatizmChain(_pendingChainActivation.ChainId, pulseCount);
        _pendingChainActivation = null;
      }

      if (!AreChainsAvailable || !_activeAutomatizmChains.Any())
        return;

      if (AppGlobalState.IsNewConditions ||
          (_pulseChainCompleted != 0 && pulseCount > _pulseChainCompleted + _reflexActionDuration))
      {
        StopAllAutomatizmChains(pulseCount);
        return;
      }

      var activeChainIds = _activeAutomatizmChains.Keys.ToList();

      foreach (var chainId in activeChainIds)
      {
        if (!_activeAutomatizmChains[chainId].IsWaitingForResult &&
            pulseCount >= _activeAutomatizmChains[chainId].LastStepPulse + _reflexActionDuration)
        {
          ExecuteNextChainStep(chainId, pulseCount);
        }
      }
    }

    #region Выполнение автоматизмов

    /// <summary>
    /// Выполняет автоматизм по его ID
    /// </summary>
    private (bool Success, string ErrorMessage) ExecuteAutomatizm(int automatizmId)
    {
      try
      {
        if (!AreDependenciesSet)
          return (false, "Зависимости AutomatismExecutionService не установлены. Вызовите InitializeWithDependencies()");

        var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);

        if (automatizm == null)
          return (false, $"Автоматизм с ID {automatizmId} не найден");

        if (automatizm.Usefulness < 0)
          return (false, $"Автоматизм {automatizmId} имеет отрицательную полезность и не может быть выполнен");

        var actionIds = GetActionsForAutomatizm(automatizm);
        if (actionIds == null || !actionIds.Any())
          return (false, $"Автоматизм {automatizmId} не содержит связанных действий");

        return ExecuteAdaptiveActions(actionIds, ActionActivationSource.Automatizm, automatizmId);
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка выполнения автоматизма {automatizmId}: {ex.Message}");
      }
    }

    /// <summary>
    /// Метод выполнения автоматизма с поддержкой цепочек
    /// </summary>
    public (bool Success, string ErrorMessage, bool ChainActivated) ExecuteAutomatizmWithChains(
        int automatizmId,
        int pulseCount)
    {
      StopAllAutomatizmChains(pulseCount);
      _pendingChainActivation = null;

      var result = ExecuteAutomatizm(automatizmId);

      if (!result.Success)
        return (false, result.ErrorMessage, false);

      var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);
      if (automatizm == null || automatizm.NextID <= 0)
        return (true, result.ErrorMessage, false);

      // Проверяем существование цепочки
      var chain = _automatizmChainsSystem.GetChain(automatizm.NextID);
      if (chain == null || chain.StartAutomatizmId != automatizmId)
        return (true, result.ErrorMessage, false);

      // Подготавливаем данные для запуска цепочки, но не запускаем сразу
      _pendingChainActivation = new PendingChainActivation
      {
        ChainId = automatizm.NextID,
        StartPulse = pulseCount,
        StartAutomatizmId = automatizmId
      };

      return (true, result.ErrorMessage, true);
    }

    /// <summary>
    /// Выполняет последовательность адаптивных действий автоматизма
    /// </summary>
    public (bool Success, string ErrorMessage) ExecuteAdaptiveActions(
        List<int> actionIds,
        ActionActivationSource activationSource,
        int automatizmId = 0)
    {
      if (actionIds == null || !actionIds.Any())
        return (false, "Нет действий для выполнения");

      if (!AreDependenciesSet)
        return (false, "Зависимости AutomatismExecutionService не установлены. Вызовите InitializeWithDependencies()");

      var results = new List<string>();
      var successfulActions = new List<int>();

      foreach (var actionId in actionIds)
      {
        try
        {
          var action = _adaptiveActionsSystem.GetAllAdaptiveActions()
              .FirstOrDefault(a => a.Id == actionId);

          if (action != null)
          {
            action.ActivationSource = activationSource;
            action.ActivationPulse = GlobalTimer.GlobalPulsCount;
          }

          bool applied = _adaptiveActionsSystem.ApplyAction(actionId);
          if (applied)
          {
            successfulActions.Add(actionId);
            results.Add($"Действие {actionId} выполнено успешно (Источник: {activationSource})");
          }
          else
            results.Add($"Действие {actionId} не может быть применено");
        }
        catch (Exception ex)
        {
          results.Add($"Ошибка выполнения действия {actionId}: {ex.Message}");
        }
      }

      if (successfulActions.Any() && automatizmId > 0)
        UpdateAutomatizmStatistics(automatizmId, successfulActions.Count == actionIds.Count);

      string message = successfulActions.Any()
          ? $"Успешно выполнено {successfulActions.Count} из {actionIds.Count} действий: {string.Join("; ", results)}"
          : $"Ни одно действие не выполнено: {string.Join("; ", results)}";

      return (successfulActions.Any(), message);
    }

    /// <summary>
    /// Обновляет статистику выполнения автоматизма
    /// </summary>
    private void UpdateAutomatizmStatistics(int automatizmId, bool success)
    {
      try
      {
        var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);
        if (automatizm == null)
          return;

        automatizm.Count++;
      }
      catch (Exception ex)
      {
        Logger.Warning(ex.Message);
      }
    }

    #endregion

    #region Поля и класссы для управления цепочками автоматизмов

    private AutomatizmChainsSystem _automatizmChainsSystem;
    private readonly Dictionary<int, ActiveAutomatizmChain> _activeAutomatizmChains = new Dictionary<int, ActiveAutomatizmChain>();
    private PendingChainActivation _pendingChainActivation = null;
    private int _pulseChainCompleted = 0;
    private int _reflexActionDuration = 0;

    /// <summary>
    /// Активная цепочка автоматизмов
    /// </summary>
    private class ActiveAutomatizmChain
    {
      public int ChainId { get; set; }
      public int CurrentLinkId { get; set; }
      public int StartPulse { get; set; }
      public int LastStepPulse { get; set; }
      public List<int> CompletedActions { get; set; } = new List<int>();
      public bool IsWaitingForResult { get; set; }
      public int LastStepUsefulness { get; set; }
    }

    private class PendingChainActivation
    {
      public int ChainId { get; set; }
      public int StartPulse { get; set; }
      public int StartAutomatizmId { get; set; }
    }

    #endregion

    #region Методы активации цепочек автоматизмов

    /// <summary>
    /// Проверяет и активирует цепочку автоматизмов для указанного автоматизма
    /// </summary>
    public (bool Activated, int ChainId) TryActivateChainForAutomatizm(int automatizmId, int pulseCount)
    {
      if (!AreChainsAvailable || !AreDependenciesSet)
        return (false, 0);

      var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);
      if (automatizm == null || automatizm.NextID <= 0)
        return (false, 0);

      // Проверяем существование цепочки
      var chain = _automatizmChainsSystem.GetChain(automatizm.NextID);
      if (chain == null)
        return (false, 0);

      // Проверяем, не активна ли уже эта цепочка
      if (_activeAutomatizmChains.ContainsKey(automatizm.NextID))
        return (false, automatizm.NextID);

      // Проверяем, что цепочка связана с этим автоматизмом
      if (chain.StartAutomatizmId != automatizmId)
          return (false, 0);

      return StartAutomatizmChain(automatizm.NextID, pulseCount);
    }

    /// <summary>
    /// Запускает цепочку автоматизмов
    /// </summary>
    private (bool Activated, int ChainId) StartAutomatizmChain(int chainId, int pulseCount)
    {
      if (!_automatizmChainsSystem.StartChain(chainId))
        return (false, 0);

      var activeChain = new ActiveAutomatizmChain
      {
        ChainId = chainId,
        StartPulse = pulseCount,
        LastStepPulse = pulseCount,
        CurrentLinkId = _automatizmChainsSystem.GetCurrentChainLink(chainId)
      };

      _activeAutomatizmChains[chainId] = activeChain;
      AppGlobalState.IsAutomatizmChainActive = true;
      Logger.Info($"Pulse: {pulseCount}. Запущена цепочка автоматизмов {chainId}");

      // Сразу выполняем первый шаг
      ExecuteNextChainStep(chainId, pulseCount);

      return (true, chainId);
    }

    /// <summary>
    /// Выполняет следующий шаг в активной цепочке
    /// </summary>
    private (bool Success, bool ChainCompleted) ExecuteNextChainStep(int chainId, int pulseCount)
    {
      if (!_activeAutomatizmChains.TryGetValue(chainId, out var activeChain))
        return (false, true);

      // Получаем текущее звено
      var chain = _automatizmChainsSystem.GetChain(chainId);
      var currentLink = chain?.Links.FirstOrDefault(l => l.ID == activeChain.CurrentLinkId);

      if (currentLink == null)
      {
        StopAutomatizmChain(chainId, pulseCount);
        return (false, true);
      }

      // Получаем образ действий по ID из звена цепочки
      var actionsImage = _actionsImagesSystem.GetActionsImage(currentLink.ActionsImageId);
      if (actionsImage == null || actionsImage.ActIdList == null || !actionsImage.ActIdList.Any())
      {
        Logger.Warning($"Образ действий {currentLink.ActionsImageId} не найден или не содержит действий");
        activeChain.LastStepUsefulness = -1;
        activeChain.IsWaitingForResult = false;

        // Переходим к следующему звену при неудаче
        return ProcessChainTransition(chainId, pulseCount, false);
      }

      // Выполняем действия из образа действий
      var result = ExecuteAdaptiveActions(
          actionsImage.ActIdList,
          ActionActivationSource.Automatizm,
          0);

      if (!result.Success)
      {
        Logger.Warning($"Ошибка выполнения действий из образа {currentLink.ActionsImageId} в цепочке {chainId}: {result.ErrorMessage}");
        activeChain.LastStepUsefulness = -1;
        activeChain.IsWaitingForResult = false;

        // Переходим к следующему звену при неудаче
        return ProcessChainTransition(chainId, pulseCount, false);
      }

      // Ждем оценки полезности
      activeChain.IsWaitingForResult = true;
      activeChain.LastStepPulse = pulseCount;

      return (true, false);
    }

    /// <summary>
    /// Обрабатывает переход к следующему звену в цепочке
    /// </summary>
    private (bool Success, bool ChainCompleted) ProcessChainTransition(int chainId, int pulseCount, bool useResult = true)
    {
      if (!_activeAutomatizmChains.TryGetValue(chainId, out var activeChain))
        return (false, true);

      // Если useResult = false (например, при ошибке выполнения), используем -1 как результат
      int resultUsefulness = useResult ? activeChain.LastStepUsefulness : -1;

      var result = _automatizmChainsSystem.ExecuteChainStep(
          chainId,
          resultUsefulness);

      if (result.ChainCompleted)
      {
        StopAutomatizmChain(chainId, pulseCount);
        return (true, true);
      }

      // Переходим к следующему звену
      activeChain.CurrentLinkId = result.NextLinkId;
      activeChain.IsWaitingForResult = false;
      activeChain.LastStepPulse = pulseCount;

      // Выполняем следующий шаг
      return ExecuteNextChainStep(chainId, pulseCount);
    }

    /// <summary>
    /// Устанавливает результат выполнения шага цепочки
    /// </summary>
    public bool SetChainStepResult(int chainId, int usefulness)
    {
      if (!_activeAutomatizmChains.TryGetValue(chainId, out var activeChain))
        return false;

      if (!activeChain.IsWaitingForResult)
        return false;

      activeChain.LastStepUsefulness = usefulness;
      activeChain.IsWaitingForResult = false;

      // Добавляем выполненное действие в список
      var chain = _automatizmChainsSystem.GetChain(chainId);
      var currentLink = chain?.Links.FirstOrDefault(l => l.ID == activeChain.CurrentLinkId);
      if (currentLink != null)
        activeChain.CompletedActions.Add(currentLink.ActionsImageId);

      Logger.Info($"Результат шага цепочки автоматизмов {chainId}: полезность={usefulness}");
      return true;
    }

    /// <summary>
    /// Останавливает выполнение цепочки
    /// </summary>
    private void StopAutomatizmChain(int chainId, int pulseCount)
    {
      if (_activeAutomatizmChains.Remove(chainId))
      {
        _automatizmChainsSystem.StopChain(chainId);
        _pulseChainCompleted = pulseCount;
      }
    }

    private void StopAllAutomatizmChains(int pulseCount)
    {
      foreach (var chainId in _activeAutomatizmChains.Keys.ToList())
      {
        StopAutomatizmChain(chainId, pulseCount);
      }
      _pulseChainCompleted = 0;
      AppGlobalState.IsAutomatizmChainActive = false;
      Logger.Info($"Pulse: {pulseCount}. Цепочка автоматизмов остановлена");

    }

    /// <summary>
    /// Проверяет наличие активной цепочки
    /// </summary>
    public bool IsAutomatizmChainActive(int chainId = 0)
    {
      if (chainId > 0)
        return _activeAutomatizmChains.ContainsKey(chainId);

      return _activeAutomatizmChains.Any();
    }

    /// <summary>
    /// Получает ID активной цепочки
    /// </summary>
    public int GetActiveAutomatizmChainId()
    {
      return _activeAutomatizmChains.Keys.FirstOrDefault();
    }

    /// <summary>
    /// Получает текущее звено активной цепочки
    /// </summary>
    public int GetCurrentAutomatizmChainLink(int chainId)
    {
      return _activeAutomatizmChains.TryGetValue(chainId, out var chain)
          ? chain.CurrentLinkId
          : 0;
    }

    /// <summary>
    /// Проверяет, ожидает ли цепочка результат выполнения
    /// </summary>
    public bool IsChainWaitingForResult(int chainId)
    {
      return _activeAutomatizmChains.TryGetValue(chainId, out var chain)
          && chain.IsWaitingForResult;
    }

    #endregion

    #region Получение информации об автоматизмах

    /// <summary>
    /// Получает список действий для автоматизма
    /// </summary>
    public List<int> GetActionsForAutomatizm(Automatizm atmz)
    {
      try
      {
        if (!AreDependenciesSet || atmz == null)
          return new List<int>();

        ActionsImage actImg = null;
        actImg = _actionsImagesSystem.GetActionsImage(atmz.ActionsImageID);
        return actImg.ActIdList;
      }
      catch
      {
        return new List<int>();
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые сервисом выполнения автоматизмов
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      _lock?.Dispose();
      _disposed = true;
    }

    #endregion
  }
}
