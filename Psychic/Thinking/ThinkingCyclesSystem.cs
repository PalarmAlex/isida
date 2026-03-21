using ISIDA.Common;
using ISIDA.Psychic.Automatism;
using ISIDA.Psychic.Memory.Episodic;
using ISIDA.Psychic.Thinking.Strategies;
using ISIDA.Psychic.Understanding;
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

    private readonly List<IThinkingStrategy> _strategies = new List<IThinkingStrategy>();
    private readonly ThinkingExperienceMemory _experienceMemory = new ThinkingExperienceMemory();

    internal ThinkingExperienceMemory ExperienceMemory => _experienceMemory;

    private int _lastStimulusPulse = 0;
    private const int WaitingTimeForDreaming = 10;
    private const int InsightCompare = 5;
    private readonly ThinkingInterruptMemory _interruptMemory = new ThinkingInterruptMemory();

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
    public ThinkingCyclesSystem(
      InformationEnvironmentSystem informationEnvironmentSystem,
      EpisodicMemorySystem episodicMemorySystem,
      UnderstandingTreeSystem understandingTreeSystem,
      ProblemTreeSystem problemTreeSystem,
      AutomatizmSystem automatizmSystem)
    {
      _informationEnvironmentSystem = informationEnvironmentSystem ?? throw new ArgumentNullException(nameof(informationEnvironmentSystem));
      _episodicMemorySystem = episodicMemorySystem; // может быть null на ранних стадиях
      _understandingTreeSystem = understandingTreeSystem;
      _problemTreeSystem = problemTreeSystem;
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
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
          LastUpdatedUtc = src.LastUpdatedUtc
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
      finally { _lock.ExitReadLock(); }
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
          var head = $"Cycle#{c.Order} id={c.Id} main={c.IsMainCycle} idle={c.IsIdle} dreaming={c.Dreaming} w={c.Weight} steps={c.StepCount} node={c.UnresolvedNodeId} stimImg={c.UnresolvedActionsImageId} prob={c.ProblemNodeId} theme={c.ThemeId} purpose={c.PurposeId}";
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
    /// Создать/сбросить главный цикл под новый стимул (вызывается при провале 2 уровня).
    /// </summary>
    public ThinkingCycleInfo OnUnresolvedProblem(ThinkingCycleContext ctx)
    {
      if (ctx == null) return null;

      _lock.EnterWriteLock();
      try
      {
        var main = _cycles.FirstOrDefault(c => c.IsMainCycle);
        if (main == null)
        {
          main = new ThinkingCycleInfo
          {
            Id = _nextId++,
            Order = _nextOrder++,
            IsMainCycle = true
          };
          _cycles.Add(main);
        }
        else
        {
          // Прерывание: если пришёл новый нерешённый стимул в важной ситуации — запомнить предыдущий контекст
          if (ctx.Danger || ctx.VeryActualSituation)
          {
            _interruptMemory.Push(new ThinkingInterruptImage
            {
              UnresolvedNodeId = main.UnresolvedNodeId,
              UnresolvedActionsImageId = main.UnresolvedActionsImageId,
              ProblemNodeId = main.ProblemNodeId,
              ThemeId = main.ThemeId,
              PurposeId = main.PurposeId,
              SavedPulse = ctx.PulseCount
            });
          }

          // новый стимул делает этот цикл главным и сбрасывает шаги
          foreach (var c in _cycles) c.IsMainCycle = false;
          main.IsMainCycle = true;
          ResetCycle(main);
        }

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

        main.Log.Add($"[p{ctx.PulseCount}] Unresolved@L2: node={ctx.AutomatizmNodeId}, stimImg={ctx.StimulusActionsImageId}, prob={ctx.ProblemNodeId}, theme={ctx.ThemeId}, purpose={ctx.PurposeId}");
        return main;
      }
      finally { _lock.ExitWriteLock(); }
    }

    private static int ComputeInitialWeight(ThinkingCycleContext ctx)
    {
      var w = 1;
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
      c.Log.Clear();
      c.LastUpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Пройти диспетчеризацию циклов на текущем пульсе.
    /// Возвращает первое найденное решение, которое надо выполнить (обычно от главного цикла).
    /// </summary>
    public ThinkingDecision DispatchCycles(int pulseCount, bool isSleeping, bool isSleepingDream)
    {
      if (_informationEnvironmentSystem?.CurrentInformationEnvironment == null)
        return null;

      // Во время ожидания оценки оператора нельзя запускать новые решения из thinking-cycles,
      // иначе получаем повторные исполнения одного и того же автомата при отсутствии ответа.
      if (AppGlobalState.WaitingForOperatorEvaluation)
        return null;

      _lock.EnterUpgradeableReadLock();
      try
      {
        if (_cycles.Count == 0) return null;

        // Обновить ожидание для главного цикла
        var main = _cycles.FirstOrDefault(c => c.IsMainCycle);
        if (main != null)
          main.IsWaitingPeriod = _informationEnvironmentSystem.CurrentInformationEnvironment.IsWaitingPeriod;

        // Удаление слишком старых/лишних циклов (упрощённо)
        if (pulseCount % 30 == 0)
          ReduceCycles(pulseCount);

        // Главный цикл всегда первый
        var ordered = _cycles
          .Where(c => c != null)
          .OrderByDescending(c => c.IsMainCycle)
          .ThenByDescending(c => c.Weight)
          .ThenBy(c => c.Order)
          .ToList();

        foreach (var cycle in ordered)
        {
          if (!cycle.IsMainCycle && cycle.IsIdle && (pulseCount % 5 != 0))
            continue;

          var decision = RunCycleStep(pulseCount, cycle, isSleeping, isSleepingDream);
          if (decision != null && (decision.AutomatizmToExecute != null || decision.ActionsImageIdToAutomatize > 0 || decision.RequestParrotFromOperator))
            return decision;
        }

        return null;
      }
      finally { _lock.ExitUpgradeableReadLock(); }
    }

    private void ReduceCycles(int pulseCount)
    {
      _lock.EnterWriteLock();
      try
      {
        // удалить циклы старше часа (3600 пульсов), кроме главного
        _cycles.RemoveAll(c => c != null && !c.IsMainCycle && (pulseCount - c.CreatedPulse) > 3600);

        // оставить максимум 10 циклов: главный + наиболее весомые
        if (_cycles.Count <= 10) return;
        var main = _cycles.FirstOrDefault(c => c.IsMainCycle);
        var rest = _cycles.Where(c => !c.IsMainCycle).OrderByDescending(c => c.Weight).ThenByDescending(c => c.CreatedPulse).Take(9).ToList();
        _cycles.Clear();
        if (main != null) _cycles.Add(main);
        _cycles.AddRange(rest);
      }
      catch (Exception ex)
      {
        Logger.Error($"ThinkingCycles.ReduceCycles failed: {ex.Message}");
      }
      finally { _lock.ExitWriteLock(); }
    }

    private ThinkingDecision RunCycleStep(int pulseCount, ThinkingCycleInfo cycle, bool isSleeping, bool isSleepingDream)
    {
      if (cycle == null) return null;
      if (isSleeping) return null; // пока не реализован сон/сновидения

      cycle.StepCount++;
      cycle.LastUpdatedUtc = DateTime.UtcNow;

      // Режим ожидания — не исполнять действий
      if (cycle.IsWaitingPeriod)
      {
        cycle.IsIdle = true;
        cycle.Log.Add($"[p{pulseCount}] WaitingPeriod");
        return ThinkingDecision.None("waiting");
      }

      // Пассивный режим (dreaming): когда нет внешних стимулов и нет острой задачи
      var env = _informationEnvironmentSystem.CurrentInformationEnvironment;
      if (cycle.IsMainCycle && !cycle.Dreaming && ShouldStartDreaming(pulseCount, env))
      {
        cycle.Dreaming = true;
        cycle.IsIdle = false;
        cycle.Log.Add($"[p{pulseCount}] DreamingStart");
      }
      if (cycle.IsMainCycle && cycle.Dreaming)
      {
        var dreamDecision = RunDreamingStep(pulseCount, cycle);
        if (dreamDecision != null && (dreamDecision.AutomatizmToExecute != null || dreamDecision.ActionsImageIdToAutomatize > 0))
        {
          cycle.Log.Add($"[p{pulseCount}] DreamingDecision => {dreamDecision.DebugNote}");
          cycle.Dreaming = false; // после найденного решения выходим из пассивного режима
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
            cycle.Log.Add($"[p{pulseCount}] ReturnToInterrupted: node={img.UnresolvedNodeId}, stimImg={img.UnresolvedActionsImageId}, prob={img.ProblemNodeId}");
            return ThinkingDecision.None("return_to_interrupted");
          }
        }

        cycle.IsIdle = true;
        cycle.Log.Add($"[p{pulseCount}] NoNeedThinking");
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
        CurrentStaffAutomatizm = cycle.UnresolvedNodeId > 0 ? _automatizmSystem.GetMotorsAutomatizmListFromTreeId(cycle.UnresolvedNodeId).FirstOrDefault() : null
      };

      // Allowed-list инфо-функций из справочника тем мышления (ThemeImageSystem)
      var allowedInfoFuncIds = GetAllowedInfoFuncIdsForCycle(cycle);
      var idsToTry = allowedInfoFuncIds.Count > 0
        ? allowedInfoFuncIds.ToList()
        : InfoFunctionsCatalog.GetAllIds();

      _lock.EnterReadLock();
      try
      {
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
            cycle.Log.Add($"[p{pulseCount}] InfoFunc={infoFuncId} => {decision.DebugNote}");
            if (decision.AutomatizmToExecute != null || decision.ActionsImageIdToAutomatize > 0 || decision.RequestParrotFromOperator)
            {
              var actionImg = decision.AutomatizmToExecute?.ActionsImageID ?? decision.ActionsImageIdToAutomatize;
              _experienceMemory.RecordRecommendation(cycle.ProblemNodeId, cycle.ThemeId, cycle.PurposeId, actionImg);
              cycle.IsIdle = false;
              return decision;
            }
          }
        }
      }
      finally { _lock.ExitReadLock(); }

      cycle.IsIdle = true;
      cycle.Log.Add($"[p{pulseCount}] NoDecision");
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
        cycle.Log.Add($"[p{pulseCount}] InsightFromEpisode id={bestNode.ID} stimEff={bestNode.Params.StimulsEffect} actionImg={bestNode.ActionId}");
        return new ThinkingDecision
        {
          ActionsImageIdToAutomatize = bestNode.ActionId,
          DebugNote = $"insight_actionImg={bestNode.ActionId}"
        };
      }

      return ThinkingDecision.None("dream_continue");
    }
  }
}

