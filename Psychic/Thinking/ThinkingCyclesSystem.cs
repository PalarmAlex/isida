using ISIDA.Common;
using ISIDA.Psychic.Automatism;
using ISIDA.Psychic.Memory.Episodic;
using ISIDA.Psychic.Thinking.Strategies;
using ISIDA.Psychic.Understanding;
using AgentCodes = ISIDA.Psychic.Understanding.AgentEventsCatalog.Codes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic.Thinking
{
  /// <summary>
  /// Диспетчер циклов мышления (3-й уровень): главный цикл + фоновые, шаги по пульсу.
  /// Не исполняет автоматизмы напрямую — возвращает решения наружу.
  /// </summary>
  public sealed class ThinkingCyclesSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed;

    private readonly List<ThinkingCycleInfo> _cycles = new List<ThinkingCycleInfo>();
    private int _nextId = 1;
    private int _nextOrder = 1;

    private readonly InformationEnvironmentSystem _informationEnvironmentSystem;
    private readonly EpisodicMemorySystem _episodicMemorySystem;
    private readonly UnderstandingTreeSystem _understandingTreeSystem;
    private readonly ProblemTreeSystem _problemTreeSystem;
    private readonly AutomatizmSystem _automatizmSystem;
    private readonly MentalEpisodicTreeSystem _mentalEpisodicTreeSystem;
    private readonly MentalAutomatizmSession _mentalAutomatizmSession;
    private readonly AutomatizmChainsSystem _automatizmChainsSystem;

    private readonly List<IThinkingStrategy> _strategies = new List<IThinkingStrategy>();
    private readonly ThinkingExperienceMemory _experienceMemory = new ThinkingExperienceMemory();

    internal ThinkingExperienceMemory ExperienceMemory => _experienceMemory;

    private int _lastStimulusPulse = 0;
    private const int WaitingTimeForDreaming = 10;
    private const int InsightCompare = 5;
    private readonly ThinkingInterruptMemory _interruptMemory = new ThinkingInterruptMemory();

    /// <summary>Сигнатура последней «типовой» строки лога по циклу — не дублировать при неизменном исходе.</summary>
    private readonly Dictionary<int, string> _lastCondensedLogDigestByCycleId = new Dictionary<int, string>();

    /// <summary>Уведомление перед снятием цикла с подтверждённым решением (полезность ≥ 1): для агентного лога/UI до удаления экземпляра.</summary>
    private Action<int, MainThinkingCycleClosedLogPayload> _onMainCycleClosedAfterConfirmedSolution;

    /// <summary>Базовый вес главного цикла при создании (фоновый наследует его после демоута).</summary>
    private const int MainCycleWeightBase = 100;

    /// <summary>Устаревшие поля конфига: раньше loss = B + age/A. Сохраняем для совместимости сериализации настроек.</summary>
    private int _decayAgeDivisor = 100;
    private int _decayBase = 1;
    private int _mainMaxAgePulses = 1000;

    /// <summary>Целевой горизонт затухания фонового веса (пульсы): при весе ~MainCycleWeightBase цикл «естественно» сходит на нет за ~столько же тактов.</summary>
    private int _backgroundFadeTargetPulses = 1000;

    /// <summary>Задать параметры затухания веса фоновых циклов и срока жизни главного (из настроек).</summary>
    /// <param name="decayAgeDivisor">Устарело (не используется в формуле затухания).</param>
    /// <param name="decayBase">Устарело (не используется в формуле затухания).</param>
    /// <param name="mainMaxAgePulses">Максимальный возраст главного цикла в пульсах до принудительного снятия.</param>
    /// <param name="backgroundFadeTargetPulses">Целевой горизонт (пульсы) затухания веса фонового цикла.</param>
    public void ApplyDecayParameters(int decayAgeDivisor, int decayBase, int mainMaxAgePulses, int backgroundFadeTargetPulses = 1000)
    {
      _lock.EnterWriteLock();
      try
      {
        _decayAgeDivisor = decayAgeDivisor <= 0 ? 100 : decayAgeDivisor;
        _decayBase = decayBase < 0 ? 0 : decayBase;
        _mainMaxAgePulses = mainMaxAgePulses <= 0 ? 1000 : mainMaxAgePulses;
        _backgroundFadeTargetPulses = backgroundFadeTargetPulses <= 0 ? 1000 : backgroundFadeTargetPulses;
      }
      finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Уведомить систему о новом стимуле (для отложенного запуска dreaming).</summary>
    /// <param name="pulseCount">Номер пульса, на котором произошёл стимул.</param>
    public void NotifyStimulus(int pulseCount)
    {
      if (pulseCount <= 0) return;
      _lastStimulusPulse = pulseCount;
    }

    /// <summary>
    /// Создаёт диспетчер циклов мышления с привязкой к подсистемам психики.
    /// </summary>
    /// <param name="informationEnvironmentSystem">Система информационной среды.</param>
    /// <param name="episodicMemorySystem">Эпизодическая память (может быть null).</param>
    /// <param name="understandingTreeSystem">Дерево понимания.</param>
    /// <param name="problemTreeSystem">Дерево проблем.</param>
    /// <param name="automatizmSystem">Система автоматизмов.</param>
    /// <param name="mentalEpisodicTreeSystem">Ментальная эпизодическая память (цепочки ИФ) или null.</param>
    /// <param name="mentalAutomatizmSession">Общий буфер текущей цепочки инфо-функций или null.</param>
    /// <param name="automatizmChainsSystem">Цепочки автоматизмов по узлу дерева или null.</param>
    public ThinkingCyclesSystem(
      InformationEnvironmentSystem informationEnvironmentSystem,
      EpisodicMemorySystem episodicMemorySystem,
      UnderstandingTreeSystem understandingTreeSystem,
      ProblemTreeSystem problemTreeSystem,
      AutomatizmSystem automatizmSystem,
      MentalEpisodicTreeSystem mentalEpisodicTreeSystem = null,
      MentalAutomatizmSession mentalAutomatizmSession = null,
      AutomatizmChainsSystem automatizmChainsSystem = null)
    {
      _informationEnvironmentSystem = informationEnvironmentSystem ?? throw new ArgumentNullException(nameof(informationEnvironmentSystem));
      _episodicMemorySystem = episodicMemorySystem; // может быть null на ранних стадиях
      _understandingTreeSystem = understandingTreeSystem;
      _problemTreeSystem = problemTreeSystem;
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
      _mentalEpisodicTreeSystem = mentalEpisodicTreeSystem;
      _mentalAutomatizmSession = mentalAutomatizmSession;
      _automatizmChainsSystem = automatizmChainsSystem;
    }

    /// <summary>Освобождает ресурсы диспетчера циклов.</summary>
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      _lock?.Dispose();
    }

    /// <summary>Регистрирует стратегию 3-го уровня (если ещё не зарегистрирована с таким Id).</summary>
    /// <param name="strategy">Стратегия мышления.</param>
    public void RegisterStrategy(IThinkingStrategy strategy)
    {
      if (strategy == null) return;
      _lock.EnterWriteLock();
      try
      {
        if (_strategies.Any(s => s.Id == strategy.Id)) return;
        _strategies.Add(strategy);
      }
      finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Регистрирует обработчик снятия цикла после подтверждения полезности решения (для <see cref="ISIDA.Common.ResearchLogger"/>).</summary>
    /// <param name="handler">Пульс и снимок полей цикла до удаления; вызывается под внутренним lock диспетчера.</param>
    public void SetMainCycleClosedAfterConfirmedSolutionLogger(Action<int, MainThinkingCycleClosedLogPayload> handler)
    {
      _lock.EnterWriteLock();
      try
      {
        _onMainCycleClosedAfterConfirmedSolution = handler;
      }
      finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Возвращает снимок текущего главного цикла (или null).</summary>
    /// <returns>Главный цикл или null.</returns>
    public ThinkingCycleInfo GetMainCycleSnapshot()
    {
      return GetMainCycleSnapshot(maxLogLinesPerCycle: 50);
    }

    /// <summary>
    /// Возвращает копию снимка текущего главного цикла (или null) с усечением лога.
    /// </summary>
    /// <param name="maxLogLinesPerCycle">Максимум последних строк лога для возврата.</param>
    /// <returns>Копия главного цикла или null.</returns>
    public ThinkingCycleInfo GetMainCycleSnapshot(int maxLogLinesPerCycle)
    {
      _lock.EnterReadLock();
      try
      {
        var src = _cycles.FirstOrDefault(c => c != null && c.IsMainCycle);
        if (src == null) return null;
        return CopyCycleForSnapshot(src, maxLogLinesPerCycle);
      }
      finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Возвращает копию снимка цикла по идентификатору (или null).
    /// </summary>
    /// <param name="cycleId">Идентификатор цикла.</param>
    /// <param name="maxLogLinesPerCycle">Максимум последних строк лога для возврата.</param>
    /// <returns>Копия цикла или null.</returns>
    public ThinkingCycleInfo GetCycleSnapshotById(int cycleId, int maxLogLinesPerCycle)
    {
      if (cycleId <= 0) return null;
      _lock.EnterReadLock();
      try
      {
        var src = _cycles.FirstOrDefault(c => c != null && c.Id == cycleId);
        if (src == null) return null;
        return CopyCycleForSnapshot(src, maxLogLinesPerCycle);
      }
      finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Краткий снимок всех циклов без логов (для матрицы на UI).
    /// </summary>
    /// <returns>Список элементов.</returns>
    public IReadOnlyList<ThinkingCycleListItem> GetAllCyclesLightweightSnapshot()
    {
      _lock.EnterReadLock();
      try
      {
        var list = new List<ThinkingCycleListItem>();
        foreach (var c in _cycles.Where(x => x != null))
        {
          GetUiBorderFlags(c, out var awaiting, out var noSol);
          list.Add(new ThinkingCycleListItem
          {
            Id = c.Id,
            Order = c.Order,
            Weight = c.Weight,
            IsMainCycle = c.IsMainCycle,
            IsIdle = c.IsIdle,
            Dreaming = c.Dreaming,
            AwaitingEvaluation = c.AwaitingEvaluation,
            PendingSolutionAutomatizmId = c.PendingSolutionAutomatizmId,
            StepCount = c.StepCount,
            ShowAwaitingEvaluationBorder = awaiting,
            ShowNoSolutionBorder = noSol,
            ThemeId = c.ThemeId,
            PurposeId = c.PurposeId,
            ProblemNodeId = c.ProblemNodeId,
            LastStrategyId = c.LastStrategyId
          });
        }
        return list;
      }
      finally { _lock.ExitReadLock(); }
    }

    private static ThinkingCycleInfo CopyCycleForSnapshot(ThinkingCycleInfo src, int maxLogLinesPerCycle)
    {
      var copy = new ThinkingCycleInfo
      {
        Id = src.Id,
        Order = src.Order,
        IsMainCycle = src.IsMainCycle,
        CreatedPulse = src.CreatedPulse,
        StepCount = src.StepCount,
        IsIdle = src.IsIdle,
        IsWaitingPeriod = src.IsWaitingPeriod,
        Dreaming = src.Dreaming,
        Weight = src.Weight,
        UnresolvedNodeId = src.UnresolvedNodeId,
        UnresolvedActionsImageId = src.UnresolvedActionsImageId,
        ProblemNodeId = src.ProblemNodeId,
        ThemeId = src.ThemeId,
        PurposeId = src.PurposeId,
        LastStrategyId = src.LastStrategyId,
        LastUpdatedUtc = src.LastUpdatedUtc,
        PendingSolutionAutomatizmId = src.PendingSolutionAutomatizmId,
        PendingSolutionBindPulse = src.PendingSolutionBindPulse,
        AwaitingEvaluation = src.AwaitingEvaluation
      };

      var log = src.Log;
      if (log != null && log.Count > 0)
      {
        var max = Math.Max(0, maxLogLinesPerCycle);
        var skip = max == 0 ? log.Count : Math.Max(0, log.Count - max);
        foreach (var line in log.Skip(skip))
          copy.Log.Add(line);
      }

      return copy;
    }

    /// <summary>
    /// Правила обводок плашек UI: сначала ожидание оценки, иначе «нет решения».
    /// </summary>
    /// <param name="c">Цикл.</param>
    /// <param name="awaitingEvaluationBorder">Тёмно-зелёная обводка.</param>
    /// <param name="noSolutionBorder">Красная обводка.</param>
    private static void GetUiBorderFlags(ThinkingCycleInfo c, out bool awaitingEvaluationBorder, out bool noSolutionBorder)
    {
      awaitingEvaluationBorder = c.AwaitingEvaluation;
      if (awaitingEvaluationBorder)
      {
        noSolutionBorder = false;
        return;
      }
      noSolutionBorder = c.PendingSolutionAutomatizmId <= 0;
    }

    /// <summary>Формирует отладочный снимок всех циклов и их логов.</summary>
    /// <param name="maxLogLinesPerCycle">Максимум строк лога на цикл.</param>
    /// <returns>Текстовый снимок для диагностики.</returns>
    public string GetDebugSnapshot(int maxLogLinesPerCycle = 5)
    {
      _lock.EnterReadLock();
      try
      {
        if (_cycles.Count == 0) return "ThinkingCycles: none";
        var lines = new List<string>();
        foreach (var c in _cycles.OrderByDescending(x => x.IsMainCycle).ThenBy(x => x.Order))
        {
          if (c == null) continue;
          var head = $"Cycle#{c.Order} id={c.Id} main={c.IsMainCycle} idle={c.IsIdle} awaitingEval={c.AwaitingEvaluation} dreaming={c.Dreaming} w={c.Weight} steps={c.StepCount} node={c.UnresolvedNodeId} stimImg={c.UnresolvedActionsImageId} prob={c.ProblemNodeId} theme={c.ThemeId} purpose={c.PurposeId}";
          lines.Add(head);
          var take = Math.Min(maxLogLinesPerCycle, c.Log.Count);
          if (take > 0)
          {
            foreach (var l in c.Log.Skip(Math.Max(0, c.Log.Count - take)))
              lines.Add("  " + l);
          }
        }
        return string.Join("\n", lines);
      }
      finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Сбрасывает «память опыта» циклов (рекомендации по ключу проблема/тема/цель) и временное состояние
    /// зарегистрированных стратегий (курсоры, RNG). Не трогает список активных циклов; для полного сброса см. <see cref="ClearAllCycles"/>.
    /// </summary>
    public void ClearThinkingExperienceMemory()
    {
      _lock.EnterWriteLock();
      try
      {
        _experienceMemory.Clear();
        ResetRegisteredStrategiesTransientStateUnsynchronized();
        _mentalAutomatizmSession?.Clear();
      }
      finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Удаляет все циклы и связанное состояние диспетчера. Вызывается на стадиях &lt; 4,
    /// где циклы мышления не используются (иначе остаток после стадии 4 продолжал бы шагать по пульсу).
    /// </summary>
    public void ClearAllCycles()
    {
      _lock.EnterWriteLock();
      try
      {
        // Опыт циклов живёт в ОЗУ вне списка _cycles; при пустых циклах ранний return иначе оставлял бы «хвост»
        // после стадии 4 / сценария и ломал бы воспроизводимость (случайная ветка vs рекомендация).
        _experienceMemory.Clear();
        ResetRegisteredStrategiesTransientStateUnsynchronized();
        _mentalAutomatizmSession?.Clear();
        _lastStimulusPulse = 0;

        if (_cycles.Count == 0 && _interruptMemory.Count == 0 && _lastCondensedLogDigestByCycleId.Count == 0)
          return;
        _cycles.Clear();
        _interruptMemory.Clear();
        _lastCondensedLogDigestByCycleId.Clear();
        _nextId = 1;
        _nextOrder = 1;
      }
      finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Курсоры и RNG стратегий — не часть файлов стадии 4, но должны сбрасываться вместе с очисткой вышестоящей стадии.</summary>
    private void ResetRegisteredStrategiesTransientStateUnsynchronized()
    {
      foreach (var s in _strategies)
      {
        if (s is InfoFunctionsStrategy info)
          info.ResetTransientStateAfterHigherStageCleared();
      }
    }

    /// <summary>
    /// Создать/сбросить главный цикл под новый стимул (вызывается при провале 2 уровня).
    /// </summary>
    public ThinkingCycleInfo OnUnresolvedProblem(ThinkingCycleContext ctx)
    {
      if (ctx == null) return null;

      _lock.EnterWriteLock();
      try
      {
        var previousMain = _cycles.FirstOrDefault(c => c.IsMainCycle);

        if (previousMain != null && (ctx.Danger || ctx.VeryActualSituation))
        {
          _interruptMemory.Push(new ThinkingInterruptImage
          {
            UnresolvedNodeId = previousMain.UnresolvedNodeId,
            UnresolvedActionsImageId = previousMain.UnresolvedActionsImageId,
            ProblemNodeId = previousMain.ProblemNodeId,
            ThemeId = previousMain.ThemeId,
            PurposeId = previousMain.PurposeId,
            SavedPulse = ctx.PulseCount
          });
        }

        foreach (var c in _cycles)
          c.IsMainCycle = false;

        // Бывший главный остаётся в списке как фоновый: иначе его CreatedPulse от старого главного
        // даёт большой age, и ApplyBackgroundWeightDecay в том же пульсе сжигает вес до 0 и удаляет цикл.
        foreach (var c in _cycles.Where(x => x != null))
        {
          c.CreatedPulse = ctx.PulseCount;
          if (c.Weight < 1)
            c.Weight = 1;
        }

        var main = new ThinkingCycleInfo
        {
          Id = _nextId++,
          Order = _nextOrder++,
          IsMainCycle = true
        };
        _cycles.Add(main);

        main.CreatedPulse = ctx.PulseCount;
        main.UnresolvedNodeId = ctx.AutomatizmNodeId;
        main.UnresolvedActionsImageId = ctx.StimulusActionsImageId;
        main.ProblemNodeId = ctx.ProblemNodeId;
        main.ThemeId = ctx.ThemeId;
        main.PurposeId = ctx.PurposeId;
        main.IsWaitingPeriod = ctx.IsWaitingPeriod;
        main.IsIdle = false;
        main.Dreaming = false;
        main.Weight = ComputeInitialWeight(ctx);
        main.LastUpdatedUtc = DateTime.UtcNow;

        main.Log.Add($"[p{ctx.PulseCount}] Нерешённый уровень 2: узел={ctx.AutomatizmNodeId}, стимул={ctx.StimulusActionsImageId}, проблема={ctx.ProblemNodeId}, тема={ctx.ThemeId}, цель={ctx.PurposeId}");
        return main;
      }
      finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Краткое описание списка циклов для логов (только под lock).</summary>
    private string FormatCyclesSnapshotLocked()
    {
      if (_cycles.Count == 0) return "empty";
      return string.Join(" | ", _cycles.Where(c => c != null).Select(c =>
        $"id={c.Id},o={c.Order},main={c.IsMainCycle},w={c.Weight}"));
    }

    private static int ComputeInitialWeight(ThinkingCycleContext ctx)
    {
      var w = MainCycleWeightBase;
      if (ctx.Danger) w += 5;
      if (ctx.VeryActualSituation) w += 2;
      return w;
    }

    private static void ResetCycle(ThinkingCycleInfo c)
    {
      c.StepCount = 0;
      c.IsIdle = false;
      c.IsWaitingPeriod = false;
      c.Dreaming = false;
      c.Weight = 0;
      c.LastStrategyId = null;
      c.AwaitingEvaluation = false;
      c.PendingSolutionAutomatizmId = 0;
      c.PendingSolutionBindPulse = 0;
      c.Log.Clear();
      c.LastUpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// После успешного исполнения решения цикла: привязка автоматизма к ожиданию оценки (полезность и т.д.).
    /// Вызывается из PsychicSystem после исполнения решения (вне lock диспетчера циклов).
    /// </summary>
    internal void NotifySolutionExecutedAfterDispatch(int cycleId, int automatizmId, int bindPulse)
    {
      if (cycleId <= 0 || automatizmId <= 0) return;
      _lock.EnterWriteLock();
      try
      {
        var c = _cycles.FirstOrDefault(x => x != null && x.Id == cycleId);
        if (c == null) return;
        c.PendingSolutionAutomatizmId = automatizmId;
        c.PendingSolutionBindPulse = bindPulse;
        c.AwaitingEvaluation = true;
        c.Log.Add($"[p{bindPulse}] Ожидание оценки: привязан автоматизм id={automatizmId}");
      }
      finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Пройти диспетчеризацию циклов на текущем пульсе.
    /// Возвращает первое найденное решение, которое надо выполнить (обычно от главного цикла).
    /// </summary>
    public ThinkingDecision DispatchCycles(int pulseCount, bool isSleeping)
    {
      if (_informationEnvironmentSystem?.CurrentInformationEnvironment == null)
        return null;

      bool blockNewThinkingSteps = AppGlobalState.WaitingForOperatorEvaluation;

      _lock.EnterWriteLock();
      try
      {
        if (_cycles.Count == 0) return null;

        ApplyLifecyclePhase(pulseCount);

        // Обновить ожидание для главного цикла
        var main = _cycles.FirstOrDefault(c => c.IsMainCycle);
        if (main != null)
          main.IsWaitingPeriod = _informationEnvironmentSystem.CurrentInformationEnvironment.IsWaitingPeriod;

        // Во время ожидания оценки оператора нельзя запускать новые решения из thinking-cycles,
        // иначе получаем повторные исполнения одного и того же автомата при отсутствии ответа.
        // Фазу жизни (в т.ч. EvaluatePendingCycleSolutions по полезности) при этом выполнять нужно —
        // иначе цикл «ожидающий оценки» никогда не снимется, пока глобальный флаг ожидания блокирует вход в диспетчер.
        if (blockNewThinkingSteps)
          return null;

        // Главный цикл всегда первый (обход по снимку Id — список может сокращаться при закрытии цикла).
        var orderedIds = _cycles
          .Where(c => c != null)
          .OrderByDescending(c => c.IsMainCycle)
          .ThenByDescending(c => c.Weight)
          .ThenBy(c => c.Order)
          .Select(c => c.Id)
          .ToList();

        foreach (var cycleId in orderedIds)
        {
          var cycle = _cycles.FirstOrDefault(c => c != null && c.Id == cycleId);
          if (cycle == null) continue;

          if (!cycle.IsMainCycle && cycle.IsIdle && (pulseCount % 5 != 0))
            continue;

          var decision = RunCycleStep(pulseCount, cycle, isSleeping);
          if (decision != null)
          {
            if (decision.CloseCycleImmediately)
              RemoveCycleById(cycle.Id, "RunCycleStep CloseCycleImmediately", pulseCount);

            if (decision.AutomatizmToExecute != null || decision.ActionsImageIdToAutomatize > 0 || decision.RequestParrotFromOperator)
              return decision;
          }
        }

        return null;
      }
      finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Фаза жизни: оценка ранее предложенных решений, срок главного цикла, затухание веса фоновых.
    /// Вызывается в начале <see cref="DispatchCycles"/> (до поиска новых решений на этом пульсе).
    /// </summary>
    /// <remarks>Вызывается под <see cref="ReaderWriterLockSlim"/> в режиме записи.</remarks>
    private void ApplyLifecyclePhase(int pulseCount)
    {
      try
      {
        EvaluatePendingCycleSolutions(pulseCount);
        EnforceMainCycleMaxAge(pulseCount);
        ApplyBackgroundWeightDecay(pulseCount);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    /// <summary>
    /// Единая точка оценки решений циклов за пульс: подтверждение по полезности автоматизма и снятие цикла.
    /// Дополнять сюда же новые критерии успешного/неуспешного завершения.
    /// </summary>
    private void EvaluatePendingCycleSolutions(int pulseCount)
    {
      var toRemoveSuccess = new List<int>();
      var toRemoveFail = new List<int>();
      foreach (var c in _cycles)
      {
        if (c == null || c.PendingSolutionAutomatizmId <= 0) continue;
        if (pulseCount <= c.PendingSolutionBindPulse) continue;

        var atmz = _automatizmSystem.GetAutomatizmById(c.PendingSolutionAutomatizmId);
        if (atmz == null) continue;

        if (atmz.Usefulness >= 1)
        {
          c.Log.Add($"[p{pulseCount}] Решение подтверждено: автоматизм id={c.PendingSolutionAutomatizmId}, полезность={atmz.Usefulness}");
          toRemoveSuccess.Add(c.Id);
        }
        else if (atmz.Usefulness < 0)
        {
          c.Log.Add($"[p{pulseCount}] Решение отвергнуто: автоматизм id={c.PendingSolutionAutomatizmId}, полезность={atmz.Usefulness}");
          toRemoveFail.Add(c.Id);
        }
      }

      foreach (var id in toRemoveSuccess)
      {
        var c = _cycles.FirstOrDefault(x => x != null && x.Id == id);
        if (c != null)
        {
          var atmz = _automatizmSystem.GetAutomatizmById(c.PendingSolutionAutomatizmId);
          int usefulness = atmz?.Usefulness ?? 0;
          FinalizeMentalAutomatizmForCycle(c, usefulness);
          var handler = _onMainCycleClosedAfterConfirmedSolution;
          if (handler != null)
          {
            var payload = new MainThinkingCycleClosedLogPayload(
              c.Id, c.Weight, c.ThemeId, c.PurposeId, c.ProblemNodeId, c.LastStrategyId ?? "",
              c.PendingSolutionAutomatizmId, usefulness);
            handler(pulseCount, payload);
          }
        }
        RemoveCycleById(id, "EvaluatePendingCycleSolutions usefulness>=1", pulseCount);
      }

      foreach (var id in toRemoveFail)
      {
        var c = _cycles.FirstOrDefault(x => x != null && x.Id == id);
        if (c != null)
        {
          var atmz = _automatizmSystem.GetAutomatizmById(c.PendingSolutionAutomatizmId);
          int usefulness = atmz?.Usefulness ?? 0;
          FinalizeMentalAutomatizmForCycle(c, usefulness);
        }
        RemoveCycleById(id, "EvaluatePendingCycleSolutions usefulness<0", pulseCount);
      }
    }

    /// <summary>
    /// Запись цепочки инфо-функций в ментальное дерево и сброс буфера (после оценки полезности решения).
    /// </summary>
    /// <param name="cycle">Цикл мышления с контекстом проблема/тема/цель.</param>
    /// <param name="effect">Полезность автоматизма (в т.ч. отрицательная).</param>
    private void FinalizeMentalAutomatizmForCycle(ThinkingCycleInfo cycle, int effect)
    {
      if (_mentalEpisodicTreeSystem == null || _mentalAutomatizmSession == null || cycle == null)
        return;

      int nodePid = cycle.ProblemNodeId > 0 ? cycle.ProblemNodeId : cycle.UnresolvedNodeId;
      var chain = _mentalAutomatizmSession.GetExecutedSnapshot();
      var lastMotorEpisodic = 0;
      if (_episodicMemorySystem != null && EpisodicMemorySystem.IsInitialized)
        lastMotorEpisodic = _episodicMemorySystem.GetLastRecordedEpisodeNodeId();
      _mentalEpisodicTreeSystem.SaveOrUpdate(nodePid, cycle.ThemeId, cycle.PurposeId, chain, effect, lastMotorEpisodic);
      _mentalAutomatizmSession.Clear();
    }

    private void EnforceMainCycleMaxAge(int pulseCount)
    {
      var main = _cycles.FirstOrDefault(c => c != null && c.IsMainCycle);
      if (main == null) return;
      if (pulseCount - main.CreatedPulse < _mainMaxAgePulses) return;

      main.Log.Add($"[p{pulseCount}] Срок главного цикла истёк ({_mainMaxAgePulses} пульсов), цикл снят");
      RemoveCycleById(main.Id, "EnforceMainCycleMaxAge", pulseCount);
    }

    /// <summary>
    /// Затухание фона: целевой горизонт <see cref="_backgroundFadeTargetPulses"/>.
    /// При весе ≤ горизонта — не чаще 1 пункта за ceil(горизонт/вес) «пульсов возраста» (порядка горизонта пульсов на полный разряд).
    /// При весе &gt; горизонта — снятие ceil(вес/горизонт) за пульс, чтобы очень тяжёлые циклы тоже укладывались примерно в горизонт.
    /// </summary>
    private void ApplyBackgroundWeightDecay(int pulseCount)
    {
      int fadeTarget = _backgroundFadeTargetPulses <= 0 ? 1000 : _backgroundFadeTargetPulses;
      var toRemove = new List<int>();
      foreach (var c in _cycles)
      {
        if (c == null || c.IsMainCycle) continue;

        int age = Math.Max(0, pulseCount - c.CreatedPulse);
        // В пульс перевода в фон CreatedPulse уже сброшен — не затухать в том же такте.
        if (age == 0) continue;

        int w = c.Weight;
        if (w <= 0)
        {
          toRemove.Add(c.Id);
          continue;
        }

        int loss;
        if (w > fadeTarget)
          loss = Math.Max(1, w / fadeTarget);
        else
        {
          int period = (fadeTarget + w - 1) / w;
          if (period < 1) period = 1;
          if (age % period != 0) continue;
          loss = 1;
        }

        c.Weight -= loss;
        if (c.Weight <= 0)
        {
          c.Log.Add($"[p{pulseCount}] Вес фонового цикла обнулён затуханием (снятие за пульс={loss}, возраст={age})");
          toRemove.Add(c.Id);
        }
      }
      foreach (var id in toRemove)
        RemoveCycleById(id, "ApplyBackgroundWeightDecay weight<=0", pulseCount);
    }

    private void RemoveCycleById(int cycleId, string reason, int pulseCount = 0)
    {
      var idx = _cycles.FindIndex(c => c != null && c.Id == cycleId);
      if (idx < 0)
        return;

      var removed = _cycles[idx];
      var wasMain = removed.IsMainCycle;

      _cycles.RemoveAt(idx);
      ForgetCondensedLogDigest(cycleId);
      if (wasMain)
        PromoteBestBackgroundToMain(pulseCount);
    }

    private void ForgetCondensedLogDigest(int cycleId)
    {
      if (cycleId <= 0) return;
      _lastCondensedLogDigestByCycleId.Remove(cycleId);
    }

    /// <summary>Добавляет строку в лог цикла только если сигнатура <paramref name="digest"/> изменилась с прошлого раза.</summary>
    private void AppendCondensedCycleLog(ThinkingCycleInfo cycle, int pulseCount, string digest, string messageRu)
    {
      if (cycle == null || string.IsNullOrEmpty(digest)) return;
      if (_lastCondensedLogDigestByCycleId.TryGetValue(cycle.Id, out var prev) && prev == digest)
        return;
      _lastCondensedLogDigestByCycleId[cycle.Id] = digest;
      cycle.Log.Add($"[p{pulseCount}] {messageRu}");
    }

    private void PromoteBestBackgroundToMain(int pulseCount)
    {
      if (_cycles.Any(c => c != null && c.IsMainCycle))
        return;

      var best = _cycles
        .Where(c => c != null)
        .OrderByDescending(c => c.Weight)
        .ThenByDescending(c => c.CreatedPulse)
        .FirstOrDefault();
      if (best != null)
      {
        foreach (var c in _cycles)
          if (c != null) c.IsMainCycle = false;
        best.IsMainCycle = true;
        if (pulseCount > 0)
        {
          best.CreatedPulse = pulseCount;
          if (best.Weight < MainCycleWeightBase)
            best.Weight = MainCycleWeightBase;
        }
      }
    }

    private ThinkingDecision RunCycleStep(int pulseCount, ThinkingCycleInfo cycle, bool isSleeping)
    {
      if (cycle == null) return null;

      cycle.StepCount++;
      cycle.LastUpdatedUtc = DateTime.UtcNow;

      if (isSleeping)
      {
        if (cycle.IsWaitingPeriod)
        {
          cycle.IsIdle = true;
          return ThinkingDecision.None("sleep_waiting");
        }
        if (cycle.AwaitingEvaluation)
        {
          cycle.IsIdle = true;
          return ThinkingDecision.None("sleep_await_eval");
        }
        if (cycle.IsMainCycle)
        {
          var sleepDream = RunDreamingStep(pulseCount, cycle);
          if (sleepDream != null && (sleepDream.AutomatizmToExecute != null || sleepDream.ActionsImageIdToAutomatize > 0))
          {
            ForgetCondensedLogDigest(cycle.Id);
            cycle.Log.Add($"[p{pulseCount}] Сон: {ThinkingCycleLogMessages.FormatDreamingDecisionRu(sleepDream)}");
            sleepDream.CycleId = cycle.Id;
            cycle.IsIdle = false;
            return sleepDream;
          }
        }
        cycle.IsIdle = true;
        return ThinkingDecision.None("sleep_quiet");
      }

      // Режим ожидания — не исполнять действий
      if (cycle.IsWaitingPeriod)
      {
        cycle.IsIdle = true;
        AppendCondensedCycleLog(cycle, pulseCount, "digest:waiting_period",
          "Ожидание оценки с пульта — перебор мышления не выполняется.");
        return ThinkingDecision.None("waiting");
      }

      if (cycle.AwaitingEvaluation)
      {
        AppendCondensedCycleLog(cycle, pulseCount, "digest:await_eval:" + cycle.PendingSolutionAutomatizmId,
          $"Ожидание оценки полезности автоматизма (id={cycle.PendingSolutionAutomatizmId}).");
        return ThinkingDecision.None("awaiting_evaluation");
      }

      // Пассивный режим (dreaming): когда нет внешних стимулов и нет острой задачи
      var env = _informationEnvironmentSystem.CurrentInformationEnvironment;
      if (cycle.IsMainCycle && !cycle.Dreaming && ShouldStartDreaming(pulseCount, env))
      {
        cycle.Dreaming = true;
        cycle.IsIdle = false;
        cycle.Log.Add($"[p{pulseCount}] Запущен пассивный режим (dreaming).");
        AppGlobalState.RecordStimulusAgentEvent(AgentCodes.PassiveReprocessing);
      }
      if (cycle.IsMainCycle && cycle.Dreaming)
      {
        var dreamDecision = RunDreamingStep(pulseCount, cycle);
        if (dreamDecision != null && (dreamDecision.AutomatizmToExecute != null || dreamDecision.ActionsImageIdToAutomatize > 0))
        {
          ForgetCondensedLogDigest(cycle.Id);
          cycle.Log.Add($"[p{pulseCount}] {ThinkingCycleLogMessages.FormatDreamingDecisionRu(dreamDecision)}");
          cycle.Dreaming = false; // после найденного решения выходим из пассивного режима
          dreamDecision.CycleId = cycle.Id;
          return dreamDecision;
        }
      }

      // Если в IE больше нет флага нерешённой проблемы — цикл уходит в idle
      if (!env.UnresolvedAtThinkingLevel2 && !env.NeedThinkingAboutAutomatizm)
      {
        // Попытка вернуться к прерванному (только в спокойной ситуации и только главному циклу)
        if (cycle.IsMainCycle && _interruptMemory.Count > 0 && !env.Danger && !env.VeryActualSituation)
        {
          var img = _interruptMemory.PopLast();
          if (img != null)
          {
            cycle.UnresolvedNodeId = img.UnresolvedNodeId;
            cycle.UnresolvedActionsImageId = img.UnresolvedActionsImageId;
            cycle.ProblemNodeId = img.ProblemNodeId;
            cycle.ThemeId = img.ThemeId;
            cycle.PurposeId = img.PurposeId;
            env.NeedThinkingAboutAutomatizm = true;
            cycle.IsIdle = false;
            ForgetCondensedLogDigest(cycle.Id);
            cycle.Log.Add($"[p{pulseCount}] Восстановление прерванного цикла: узел={img.UnresolvedNodeId}, стимул={img.UnresolvedActionsImageId}, проблема={img.ProblemNodeId}");
            return ThinkingDecision.None("return_to_interrupted");
          }
        }

        cycle.IsIdle = true;
        AppendCondensedCycleLog(cycle, pulseCount, "digest:no_need_thinking",
          "В инфо-среде нет нерешённой задачи на уровне 2 — мышление не требуется.");
        return ThinkingDecision.None("no_need");
      }

      // Собрать контекст стратегии
      var ctx = new ThinkingStrategyContext
      {
        PulseCount = pulseCount,
        Cycle = cycle,
        InformationEnvironmentSystem = _informationEnvironmentSystem,
        EpisodicMemorySystem = _episodicMemorySystem,
        UnderstandingTreeSystem = _understandingTreeSystem,
        ProblemTreeSystem = _problemTreeSystem,
        AutomatizmSystem = _automatizmSystem,
        MentalAutomatizmSession = _mentalAutomatizmSession,
        AutomatizmChainsSystem = _automatizmChainsSystem,
        CurrentStaffAutomatizm = cycle.UnresolvedNodeId > 0 ? _automatizmSystem.GetMotorsAutomatizmListFromTreeId(cycle.UnresolvedNodeId).FirstOrDefault() : null
      };

      // Allowed-list инфо-функций из справочника тем мышления (ThemeImageSystem). Пустой список — мышление по ИФ не выполняется.
      var allowedInfoFuncIds = GetAllowedInfoFuncIdsForCycle(cycle);
      if (allowedInfoFuncIds.Count == 0)
      {
        cycle.IsIdle = true;
        AppendCondensedCycleLog(cycle, pulseCount, "no_allowed_infoFuncs",
          "Для типа темы не заданы инфо-функции — перебор не выполняется.");
        return ThinkingDecision.None("no_allowed_infoFuncs");
      }

      var allowedSet = new HashSet<int>(allowedInfoFuncIds);
      var idsToTry = allowedInfoFuncIds.OrderBy(x => x).ToList();
      if (_mentalEpisodicTreeSystem != null && _mentalAutomatizmSession != null)
      {
        int nodePid = cycle.ProblemNodeId > 0 ? cycle.ProblemNodeId : cycle.UnresolvedNodeId;
        var prefix = _mentalAutomatizmSession.GetExecutedSnapshot();
        var mentalNext = _mentalEpisodicTreeSystem.TryResolveNextInfoFunc(
          nodePid, cycle.ThemeId, cycle.PurposeId, prefix, exactOnly: false);
        if (mentalNext.HasValue && mentalNext.Value > 0
            && allowedSet.Contains(mentalNext.Value) && InfoFunctionsCatalog.Exists(mentalNext.Value))
        {
          idsToTry = idsToTry.Where(id => id != mentalNext.Value).ToList();
          idsToTry.Insert(0, mentalNext.Value);
        }
      }

      var batchAttempts = new List<(int FuncId, string DebugNote)>();
      foreach (var infoFuncId in idsToTry)
      {
        if (!InfoFunctionsCatalog.Exists(infoFuncId)) continue;
        var lastIdStr = cycle.LastStrategyId;
        if (!string.IsNullOrWhiteSpace(lastIdStr) && lastIdStr == $"infoFunc_{infoFuncId}")
          continue;

        ctx.OptionalInfoFuncId = infoFuncId;
        var strategy = _strategies.OfType<InfoFunctionsStrategy>().FirstOrDefault();
        if (strategy == null) continue;

        var decision = strategy.TryStep(ctx);
        cycle.LastStrategyId = $"infoFunc_{infoFuncId}";
        if (decision != null)
        {
          batchAttempts.Add((infoFuncId, decision.DebugNote));
          if (decision.AutomatizmToExecute != null || decision.ActionsImageIdToAutomatize > 0 || decision.RequestParrotFromOperator)
          {
            ForgetCondensedLogDigest(cycle.Id);
            cycle.Log.Add($"[p{pulseCount}] {ThinkingCycleLogMessages.FormatInfoFuncSuccessRu(infoFuncId, decision)}");
            var actionImg = decision.AutomatizmToExecute?.ActionsImageID ?? decision.ActionsImageIdToAutomatize;
            _experienceMemory.RecordRecommendation(cycle.ProblemNodeId, cycle.ThemeId, cycle.PurposeId, actionImg);
            cycle.IsIdle = false;
            decision.CycleId = cycle.Id;
            return decision;
          }
        }
      }

      cycle.IsIdle = true;
      var digest = ThinkingCycleLogMessages.BuildInfoFuncBatchDigest(batchAttempts);
      AppendCondensedCycleLog(cycle, pulseCount, digest, ThinkingCycleLogMessages.BuildInfoFuncBatchNoDecisionRu(batchAttempts));
      return ThinkingDecision.None("no_decision");
    }

    /// <summary>Получить разрешённые Id инфо-функций по теме мышления из справочника тем (ThemeImageSystem).</summary>
    private static HashSet<int> GetAllowedInfoFuncIdsForCycle(ThinkingCycleInfo cycle)
    {
      if (cycle == null) return new HashSet<int>();
      if (!ThemeImageSystem.IsInitialized) return new HashSet<int>();

      int themeTypeId = 0;
      if (cycle.ThemeId > 0)
      {
        var themeRec = ThemeImageSystem.Instance.GetById(cycle.ThemeId);
        themeTypeId = themeRec?.Type ?? 0;
      }
      if (themeTypeId <= 0) return new HashSet<int>();

      return ThemeImageSystem.Instance.GetAllowedInfoFuncIdsForThemeType(themeTypeId);
    }

    private bool ShouldStartDreaming(int pulseCount, InformationEnvironmentSystem.InformationEnvironment env)
    {
      if (env == null) return false;
      if (env.IsWaitingPeriod) return false;
      if (env.UnresolvedAtThinkingLevel2 || env.NeedThinkingAboutAutomatizm) return false;
      if (_lastStimulusPulse <= 0) return false;
      return (pulseCount - _lastStimulusPulse) > WaitingTimeForDreaming;
    }

    private ThinkingDecision RunDreamingStep(int pulseCount, ThinkingCycleInfo cycle)
    {
      if (_episodicMemorySystem == null) return ThinkingDecision.None("dream_no_episodic");
      if (_episodicMemorySystem.History?.Entries == null || _episodicMemorySystem.History.Entries.Count == 0)
        return ThinkingDecision.None("dream_no_history");

      // Выбрать опорный кадр: из последних 20 выбрать максимальный по |StimulsEffect|
      var entries = _episodicMemorySystem.History.Entries;
      var tail = entries.Skip(Math.Max(0, entries.Count - 20)).ToList();
      int bestId = 0;
      int bestAbs = 0;
      foreach (var e in tail)
      {
        if (e == null || e.NodeId <= 0) continue;
        var node = _episodicMemorySystem.GetNodeById(e.NodeId);
        if (node?.Params == null) continue;
        var abs = Math.Abs(node.Params.StimulsEffect);
        if (abs > bestAbs)
        {
          bestAbs = abs;
          bestId = node.ID;
        }
      }
      if (bestId <= 0) return ThinkingDecision.None("dream_no_best");

      var bestNode = _episodicMemorySystem.GetNodeById(bestId);
      if (bestNode == null) return ThinkingDecision.None("dream_best_null");

      // Перенести результат в IE (контекст инфо-картины) только для главного цикла
      if (_informationEnvironmentSystem?.CurrentInformationEnvironment != null)
      {
        _informationEnvironmentSystem.CurrentInformationEnvironment.ActualEpisodicMemoryID = bestNode.ID;
        _informationEnvironmentSystem.CurrentInformationEnvironment.ActionsImageID = bestNode.TriggerId;
        _informationEnvironmentSystem.CurrentInformationEnvironment.AnswerImageID = bestNode.ActionId;
      }

      // Insight: если кадр очень значим — попробовать действие из него
      if (bestAbs > InsightCompare && bestNode.ActionId > 0)
      {
        ForgetCondensedLogDigest(cycle.Id);
        cycle.Log.Add($"[p{pulseCount}] Инсайт из эпизода: узел id={bestNode.ID}, |эффект стимула|={bestNode.Params.StimulsEffect}, образ действий={bestNode.ActionId}");
        return new ThinkingDecision
        {
          CycleId = cycle.Id,
          ActionsImageIdToAutomatize = bestNode.ActionId,
          DebugNote = $"insight_actionImg={bestNode.ActionId}"
        };
      }

      return ThinkingDecision.None("dream_continue");
    }
  }
}

