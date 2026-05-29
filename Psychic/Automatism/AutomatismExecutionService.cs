using ISIDA.Actions;
using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using static ISIDA.Actions.AdaptiveActionsSystem;
using static ISIDA.Psychic.Automatism.ActionsImagesSystem;

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
    private ResearchLogger _researchLogger;
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
        AutomatizmChainsSystem automatizmChainsSystem,
        ResearchLogger researchLogger = null)
    {
      if (_instance == null)
        throw new InvalidOperationException("AutomatismExecutionService должен быть сначала инициализирован через InitializeInstance()");

      _instance.SetDependencies(automatizmSystem, psychicSystem);
      _instance.SetAutomatizmChainsSystem(automatizmChainsSystem);
      _instance.SetResearchLogger(researchLogger);
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
    }

    /// <summary>
    /// Устанавливает зависимости от систем психики
    /// </summary>
    private void SetDependencies(
        AutomatizmSystem automatizmSystem,
        PsychicSystem psychicSystem)
    {
      _automatizmSystem = automatizmSystem ??
          throw new ArgumentNullException(nameof(automatizmSystem));
      _psychicSystem = psychicSystem ??
          throw new ArgumentNullException(nameof(psychicSystem));
    }

    /// <summary>
    /// Проверяет, установлены ли все зависимости
    /// </summary>
    public bool AreDependenciesSet =>
        _automatizmSystem != null &&
        _psychicSystem != null;

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

    /// <summary>
    /// Устанавливает логгер для записи цепочек
    /// </summary>
    public void SetResearchLogger(ResearchLogger researchLogger)
    {
      _researchLogger = researchLogger;
    }

    #endregion

    /// <summary>
    /// Обрабатывает пульс для активных цепочек автоматизмов
    /// </summary>
    public void ProcessAutomatizmChainsPulse(int pulseCount)
    {
      // Проверяем отложенную активацию цепочки
      if (_pendingChainActivation != null &&
          pulseCount >= _pendingChainActivation.StartPulse + _reflexActionDuration)
      {
        StartAutomatizmChain(_pendingChainActivation.ChainId, pulseCount);
        _pendingChainActivation = null;
        return;
      }

      // Нет активной цепочки или цепочки недоступны
      if (!AreChainsAvailable || _activeChain == null)
        return;

      // Проверка на смену условий
      if (AppGlobalState.IsNewConditions)
      {
        StopCurrentAutomatizmChain(pulseCount);
        Logger.Info($"Цепочка автоматизмов завершена из-за смены условий");
        return;
      }

      var chain = _activeChain;

      // Если цепочка ожидает результат, проверяем, не пора ли выбрать следующее звено
      if (chain.IsWaitingForResult)
      {
        // Проверяем, истекло ли время ожидания оценки
        if (pulseCount >= chain.LastStepPulse + _reflexActionDuration)
        {
          int finalEvaluation;

          // Если оператор не дал ни одной оценки, используем +1 по умолчанию
          if (!chain.OperatorEvaluated)
          {
            finalEvaluation = 1;
            Logger.Info($"Время ожидания оценки истекло, оператор не менял переключатель, установлена полезность=1");
          }
          else
          {
            // Используем ПОСЛЕДНЮЮ оценку оператора
            finalEvaluation = chain.LastEvaluation.Value;
            Logger.Info($"Время ожидания оценки истекло, используется последняя оценка: {finalEvaluation}");
          }

          // Обновляем полезность звена в системе
          _automatizmChainsSystem.UpdateLinkUsefulness(chain.ChainId, chain.CurrentLinkId, finalEvaluation);

          // Выбираем следующее звено на основе финальной оценки
          var nextStep = _automatizmChainsSystem.GetNextChainStepData(chain.ChainId, finalEvaluation);

          if (nextStep.ChainCompleted)
            StopCurrentAutomatizmChain(pulseCount);
          else
          {
            // Переходим к следующему звену
            chain.CurrentLinkId = nextStep.NextLinkId;
            chain.LastStepPulse = pulseCount;
            chain.IsWaitingForResult = false;

            // Сбрасываем оценку для нового звена
            chain.LastEvaluation = null;

            // Выполняем следующее звено
            ExecuteChainLink(pulseCount);
          }
        }
      }
      // Если не ожидает результат, запускаем следующее звено
      else if (pulseCount >= chain.LastStepPulse + _reflexActionDuration)
        ExecuteChainLink(pulseCount);
    }

    #region Выполнение автоматизмов

    /// <summary>
    /// Выполняет автоматизм по его ID
    /// </summary>
    private (bool Success, string ErrorMessage) ExecuteAutomatizm(int automatizmId)
    {
      try
      {
        if (AppGlobalState.HostEnvironmentDegraded)
          return (false, "Исполнение подавлено: внешняя среда хоста в режиме деградации");

        if (!AreDependenciesSet)
          return (false, "Зависимости AutomatismExecutionService не установлены. Вызовите InitializeWithDependencies()");

        var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);

        if (automatizm == null)
          return (false, $"Автоматизм с ID {automatizmId} не найден");

        if (automatizm.Usefulness < 0)
          return (false, $"Автоматизм {automatizmId} имеет отрицательную полезность и не может быть выполнен");

        var actionsImage = _actionsImagesSystem.GetActionsImage(automatizm.ActionsImageID);
        if (actionsImage == null)
          return (false, $"Не найден образ действий ID={automatizm.ActionsImageID} для автоматизма {automatizmId}");

        int phraseId = actionsImage.PhraseIdList?.FirstOrDefault() ?? 0;
        var actionIds = actionsImage.ActIdList?.ToList() ?? new List<int>();

        bool isVerbalResponse = phraseId > 0;
        if (!actionIds.Any())
        {
          if (!isVerbalResponse)
            return (false, $"Автоматизм {automatizmId} не содержит связанных действий");

          if (_adaptiveActionsSystem.DefaultAdaptiveActionId <= 0)
            return (false, "Невозможно выполнить вербальный автоматизм: не задано действие по умолчанию");

          // Фраза без моторного действия выполняется через действие по умолчанию.
          actionIds.Add(_adaptiveActionsSystem.DefaultAdaptiveActionId);
        }

        var activationSource = isVerbalResponse
          ? ActionActivationSource.AutomatizmVerbalResponse
          : ActionActivationSource.Automatizm;

        int actionImageIdForToneMood = isVerbalResponse ? automatizm.ActionsImageID : 0;
        return ExecuteAdaptiveActions(actionIds, activationSource, automatizmId, phraseId, actionImageIdForToneMood);
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка выполнения автоматизма {automatizmId}: {ex.Message}");
      }
    }

    /// <summary>
    /// Метод выполнения автоматизма с поддержкой цепочек.
    /// Момент отложенного старта цепочки привязывается к <see cref="GlobalTimer.GlobalPulsCount"/> (а не к устаревшему
    /// <c>PulseCount</c> психики), т.к. сценарий может вызвать активацию до <c>ProcessPsychicPulse</c> на том же пульсе.
    /// </summary>
    public (bool Success, string ErrorMessage, bool ChainActivated) ExecuteAutomatizmWithChains(int automatizmId)
    {
      int chainSchedulePulse = GlobalTimer.GlobalPulsCount;

      StopCurrentAutomatizmChain(chainSchedulePulse);
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
        StartPulse = chainSchedulePulse,
        StartAutomatizmId = automatizmId
      };

      return (true, result.ErrorMessage, true);
    }

    /// <summary>
    /// Выполняет последовательность адаптивных действий автоматизма
    /// </summary>
    /// <param name="actionIds">Список ID действий для выполнения</param>
    /// <param name="activationSource">Источник активации (автоматизм, вербальный ответ и т.д.)</param>
    /// <param name="automatizmId">ID автоматизма (0 для цепочек)</param>
    /// <param name="phraseId">ID фразы при вербальном ответе</param>
    /// <param name="actionImageIdForToneMood">ID образа действий автоматизма для отображения тона/настроения (при вербальном ответе)</param>
    public (bool Success, string ErrorMessage) ExecuteAdaptiveActions(
        List<int> actionIds,
        ActionActivationSource activationSource,
        int automatizmId = 0,
        int phraseId = 0,
        int actionImageIdForToneMood = 0)
    {
      if (actionIds == null || !actionIds.Any())
        return (false, "Нет действий для выполнения");

      if (!AreDependenciesSet)
        return (false, "Зависимости AutomatismExecutionService не установлены. Вызовите InitializeWithDependencies()");

      if (AppGlobalState.HostEnvironmentDegraded)
        return (false, "Исполнение подавлено: внешняя среда хоста в режиме деградации");

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

          bool applied = _adaptiveActionsSystem.ApplyAction(actionId, phraseId, actionImageIdForToneMood);
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
    private ActiveAutomatizmChain _activeChain = null;
    private PendingChainActivation _pendingChainActivation = null;
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
      /// <summary>Состояние гомеостаза на момент начала ожидания (для оценки по эффекту стимула)</summary>
      public int StateAtWaitStart { get; set; }
      /// <summary>Была ли оценка задана (по стимулу или по таймауту с дефолтом)</summary>
      public bool OperatorEvaluated => LastEvaluation.HasValue;
      public int? LastEvaluation { get; set; }
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

      // Если уже есть активная цепочка - не активируем новую
      if (_activeChain != null)
        return (false, 0);

      var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);
      if (automatizm == null || automatizm.NextID <= 0)
        return (false, 0);

      // Проверяем существование цепочки
      var chain = _automatizmChainsSystem.GetChain(automatizm.NextID);
      if (chain == null)
        return (false, 0);

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

      // Останавливаем предыдущую цепочку, если была
      if (_activeChain != null)
        StopCurrentAutomatizmChain(pulseCount);

      _activeChain = new ActiveAutomatizmChain
      {
        ChainId = chainId,
        StartPulse = pulseCount,
        LastStepPulse = pulseCount,
        LastEvaluation = null,
        CurrentLinkId = _automatizmChainsSystem.GetCurrentChainLink(chainId),
        StateAtWaitStart = (int)AppGlobalState.CurrentOverallState
      };

      AppGlobalState.IsAutomatizmChainActive = true;
      Logger.Info($"Запущена цепочка автоматизмов {chainId}");

      // Регистрируем цепочку для логирования
      _researchLogger?.RegisterActiveChain(chainId, $"AutomatizmChain_{chainId}", "Automatizm");

      // Сразу выполняем первый шаг (логирование произойдет там)
      ExecuteChainLink(pulseCount);

      return (true, chainId);
    }

    /// <summary>
    /// Выполняет звено цепочки. Звенья с ChainUsefulness &lt; 0 не выполняются — переход по FailureNextLink.
    /// </summary>
    private void ExecuteChainLink(int pulseCount)
    {
      if (_activeChain == null)
        return;

      // Пропуск звеньев с отрицательной полезностью (ветвление 3.1)
      var (canExecute, linkId) = _automatizmChainsSystem.TrySkipToExecutableLink(_activeChain.ChainId);
      if (!canExecute)
      {
        StopCurrentAutomatizmChain(pulseCount);
        return;
      }
      _activeChain.CurrentLinkId = linkId;

      // Получаем текущее звено
      var chain = _automatizmChainsSystem.GetChain(_activeChain.ChainId);
      var currentLink = chain?.Links.FirstOrDefault(l => l.ID == _activeChain.CurrentLinkId);

      if (currentLink == null)
      {
        StopCurrentAutomatizmChain(pulseCount);
        return;
      }

      // Получаем образ действий
      var actionsImage = _actionsImagesSystem.GetActionsImage(currentLink.ActionsImageId);
      if (actionsImage == null)
      {
        Logger.Warning($"Образ действий {currentLink.ActionsImageId} не найден");
        StopCurrentAutomatizmChain(pulseCount);
        return;
      }

      int phraseId = actionsImage.PhraseIdList?.FirstOrDefault() ?? 0;
      var actionIds = actionsImage.ActIdList?.ToList() ?? new List<int>();
      bool isVerbalOnly = !actionIds.Any() && phraseId > 0;

      if (!actionIds.Any() && phraseId <= 0)
      {
        Logger.Warning($"Образ действий {currentLink.ActionsImageId} не содержит ни действий, ни фразы");
        StopCurrentAutomatizmChain(pulseCount);
        return;
      }

      if (isVerbalOnly)
      {
        if (_adaptiveActionsSystem.DefaultAdaptiveActionId <= 0)
        {
          Logger.Warning($"Невозможно выполнить вербальное звено: не задано действие по умолчанию");
          StopCurrentAutomatizmChain(pulseCount);
          return;
        }
        actionIds.Add(_adaptiveActionsSystem.DefaultAdaptiveActionId);
      }

      var activationSource = isVerbalOnly ? ActionActivationSource.AutomatizmVerbalResponse : ActionActivationSource.Automatizm;
      int actionImageIdForToneMood = phraseId > 0 ? currentLink.ActionsImageId : 0;

      // Выполняем действия (для вербальной части передаём ID образа звена — для отображения тона и настроения)
      var result = ExecuteAdaptiveActions(
          actionIds,
          activationSource,
          0,
          phraseId,
          actionImageIdForToneMood);

      if (!result.Success)
      {
        Logger.Warning($"Ошибка выполнения действий из образа {currentLink.ActionsImageId} в цепочке {_activeChain.ChainId}");
        StopCurrentAutomatizmChain(pulseCount);
        return;
      }

      // Логируем выполнение звена цепочки (используем первое действие из списка)
      int firstActionId = actionIds.First();
      _researchLogger?.LogChainLinkExecution(_activeChain.ChainId, _activeChain.CurrentLinkId, firstActionId, pulseCount);

      // Устанавливаем состояние ожидания оценки (оценка — по эффекту стимула оператора в период ожидания)
      _activeChain.IsWaitingForResult = true;
      _activeChain.LastStepPulse = pulseCount;
      _activeChain.StateAtWaitStart = (int)AppGlobalState.CurrentOverallState;

      Logger.Info($"Выполнено звено {_activeChain.CurrentLinkId} цепочки {_activeChain.ChainId}, " +
                  $"ожидание оценки в течение {_reflexActionDuration} пульсов");
    }

    /// <summary>
    /// Применяет эффект стимула оператора к активному звену цепочки и переходит к следующему звену (или завершает цепочку).
    /// Вызывается после применения воздействий с пульта в период ожидания. Оценка звена: -1, 0 или +1 по изменению состояния гомеостаза.
    /// </summary>
    public bool ApplyStimulusEffectAndAdvanceChain()
    {
      if (_activeChain == null || !_activeChain.IsWaitingForResult)
        return false;

      int chainId = _activeChain.ChainId;
      int stateNow = (int)AppGlobalState.CurrentOverallState;
      int stateBefore = _activeChain.StateAtWaitStart;
      int effect = stateNow > stateBefore ? 1 : (stateNow < stateBefore ? -1 : 0);

      _activeChain.LastEvaluation = effect;
      Logger.Info($"Оценка звена {_activeChain.CurrentLinkId} цепочки {chainId} по эффекту стимула: {effect} (состояние {stateBefore} → {stateNow})");

      _automatizmChainsSystem.UpdateLinkUsefulness(chainId, _activeChain.CurrentLinkId, effect);
      var nextStep = _automatizmChainsSystem.GetNextChainStepData(chainId, effect);

      int pulseCount = GlobalTimer.GlobalPulsCount;

      if (nextStep.ChainCompleted)
      {
        StopCurrentAutomatizmChain(pulseCount);
        return true;
      }

      _activeChain.CurrentLinkId = nextStep.NextLinkId;
      _activeChain.LastStepPulse = pulseCount;
      _activeChain.IsWaitingForResult = false;
      _activeChain.LastEvaluation = null;

      ExecuteChainLink(pulseCount);
      return true;
    }

    /// <summary>
    /// Устанавливает результат выполнения шага цепочки (используется только для цепочек рефлексов; для автоматизмов — ApplyStimulusEffectAndAdvanceChain).
    /// </summary>
    public bool SetChainStepResult(int chainId, int usefulness)
    {
      // Проверяем, что это текущая активная цепочка
      if (_activeChain == null || _activeChain.ChainId != chainId)
        return false;

      if (!_activeChain.IsWaitingForResult)
        return false;

      // Логируем только изменение оценки
      if (_activeChain.LastEvaluation != usefulness)
      {
        Logger.Info($"Оценка звена {_activeChain.CurrentLinkId} цепочки {chainId} изменена: " +
                   $"{_activeChain.LastEvaluation?.ToString() ?? "null"} -> {usefulness}");
      }

      _activeChain.LastEvaluation = usefulness;
      return true;
    }

    /// <summary>
    /// Останавливает выполнение цепочки
    /// </summary>
    private void StopCurrentAutomatizmChain(int pulseCount)
    {
      if (_activeChain == null)
        return;

      int chainId = _activeChain.ChainId;
      int completedLinksCount = _activeChain.CompletedActions.Count;
      bool? finalEvaluation = _activeChain.LastEvaluation.HasValue ? (bool?)(_activeChain.LastEvaluation.Value == 1) : null;
            
      // Логируем завершение цепочки ПЕРЕД её очисткой
      _researchLogger?.LogChainCompletion(chainId, pulseCount, completedLinksCount, finalEvaluation);
      
      _activeChain = null;
      _automatizmChainsSystem.StopChain(chainId);
      AppGlobalState.IsAutomatizmChainActive = false;

      Logger.Info($"Цепочка автоматизмов {chainId} остановлена");
    }

    /// <summary>
    /// Проверяет наличие активной цепочки
    /// </summary>
    public bool IsAutomatizmChainActive(int chainId = 0)
    {
      if (_activeChain == null)
        return false;

      if (chainId > 0)
        return _activeChain.ChainId == chainId;

      return true;
    }

    /// <summary>
    /// Получает ID активной цепочки
    /// </summary>
    public int GetActiveAutomatizmChainId()
    {
      return _activeChain?.ChainId ?? 0;
    }

    /// <summary>
    /// Получает текущее звено активной цепочки
    /// </summary>
    public int GetCurrentAutomatizmChainLink(int chainId)
    {
      if (_activeChain == null || _activeChain.ChainId != chainId)
        return 0;

      return _activeChain.CurrentLinkId;
    }

    /// <summary>
    /// Проверяет, ожидает ли цепочка результат выполнения
    /// </summary>
    public bool IsChainWaitingForResult(int chainId)
    {
      return _activeChain != null &&
             _activeChain.ChainId == chainId &&
             _activeChain.IsWaitingForResult;
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
      _instance = null;
    }

    #endregion
  }
}
