using ISIDA.Common;
using ISIDA.Psychic;
using ISIDA.Reflexes;
using System;
using System.Diagnostics;
using System.Linq;

namespace ISIDA.Scenarios
{
  /// <summary>Сообщение о выполняемом шаге сценария (для индикатора прогресса в UI).</summary>
  public sealed class OperatorScenarioStepProgressEventArgs : EventArgs
  {
    /// <summary>Номер шага по таблице сценария (как в редакторе).</summary>
    public int StepIndex { get; set; }
  }

  /// <summary>Результат завершения прогона сценария (успех, отмена, ошибка).</summary>
  public sealed class OperatorScenarioCompletedEventArgs : EventArgs
  {
    /// <summary>Истина, если все шаги выполнены без ошибок.</summary>
    public bool Success { get; set; }
    /// <summary>Прервано пользователем (кнопка «Стоп»).</summary>
    public bool AbortedByUser { get; set; }
    /// <summary>Прервано из‑за остановки пульсации.</summary>
    public bool AbortedByPulsationStop { get; set; }
    /// <summary>Номер пульса внутри сценария для последнего обработанного шага.</summary>
    public int LastExecutedPulseWithinScenario { get; set; }
    /// <summary>Текст ошибки при неуспехе (иначе пусто).</summary>
    public string ErrorMessage { get; set; }
    /// <summary>Сценарий, по которому шёл прогон.</summary>
    public ScenarioDocument Document { get; set; }

    /// <summary>Глобальный номер пульса на момент старта сценария (для сопоставления с логами).</summary>
    public int AnchorGlobalPulse { get; set; }

    /// <summary>Фактическое время прогона (wall clock) от <c>Start</c> до <c>Complete</c>.</summary>
    public TimeSpan ElapsedWallTime { get; set; }

    /// <summary>Количество глобальных пульсов, прошедших за время прогона.</summary>
    public int ElapsedPulses { get; set; }
  }

  /// <summary>Выполнение сценария оператора по событиям пульса.</summary>
  public sealed class OperatorScenarioRunner
  {
    private ScenarioDocument _doc;
    private int _anchorPulse;
    private int _maxPulse;
    private int _firstStepGlobalPulse;
    private Func<IOperatorScenarioPult> _getPult;
    private Action _cancelWaitingPeriod;
    private bool _running;
    private int _lastExecutedStepPulse;
    private bool _pendingSuccessCompletion;
    private Stopwatch _runStopwatch;

    /// <summary>Истина, пока сценарий ожидает пульсы.</summary>
    public bool IsRunning => _running;

    /// <summary>Завершение прогона (любой исход).</summary>
    public event EventHandler<OperatorScenarioCompletedEventArgs> Finished;
    /// <summary>Перед обработкой очередного шага (после сопоставления с пульсом).</summary>
    public event EventHandler<OperatorScenarioStepProgressEventArgs> StepProgress;
    /// <summary>Вызывается при смене состояния «идёт / не идёт» (для обновления UI).</summary>
    public event Action RunningStateChanged;
    /// <summary>Вызывается на каждом пульсе, пока сценарий ждёт активации психики (до первого шага).
    /// Параметры: (текущий глобальный пульс, глобальный пульс первого шага).</summary>
    public event Action<int, int> WaitingForActivation;

    /// <summary>Текущий прогон (пока <see cref="IsRunning"/>).</summary>
    public bool TryGetActiveRun(out ScenarioDocument document, out int anchorPulse)
    {
      document = _doc;
      anchorPulse = _anchorPulse;
      return _running && _doc != null;
    }

    /// <summary>Начинает выполнение: привязка к глобальному счётчику пульсов и шагам по <see cref="ScenarioLineRow.PulseWithinScenario"/>.</summary>
    /// <param name="doc">Документ сценария.</param>
    /// <param name="getPult">Фабрика пульта; может вернуть null, если UI недоступен.</param>
    /// <param name="cancelWaitingPeriod">Сброс периода ожидания (клик по плашке, сброс по шагу).</param>
    public void Start(
        ScenarioDocument doc,
        Func<IOperatorScenarioPult> getPult,
        Action cancelWaitingPeriod)
    {
      if (_running)
        throw new InvalidOperationException("Сценарий уже выполняется.");
      _doc = doc ?? throw new ArgumentNullException(nameof(doc));
      _getPult = getPult ?? throw new ArgumentNullException(nameof(getPult));
      _cancelWaitingPeriod = cancelWaitingPeriod;

      _maxPulse = _doc.Lines.Count == 0 ? 0 : _doc.Lines.Max(r => r.PulseWithinScenario);
      // Якорь — глобальный номер пульса в момент Start. Шаг: global == якорь + PulseWithinScenario.
      _anchorPulse = GlobalTimer.GlobalPulsCount;
      // Дерево автоматизмов активируется с пульса MinGlobalPulseForAutomatizmTreeActivation (включительно).
      // Первый стимул сценария должен попасть как минимум на СЛЕДУЮЩИЙ пульс (+1), чтобы дерево успело
      // обработать хотя бы один «холостой» пульс и построить внутреннее состояние.
      if (_doc.Lines.Count > 0)
      {
        int minPulseInDoc = _doc.Lines.Min(r => r.PulseWithinScenario);
        int firstStepGlobal = _anchorPulse + minPulseInDoc;
        int minActivation = PsychicSystem.MinGlobalPulseForAutomatizmTreeActivation + 1;
        if (firstStepGlobal < minActivation)
        {
          int adjusted = minActivation - minPulseInDoc;
          ScenarioRunnerDiagnostics.Write(
              $"[Start] сдвиг якоря: было anchor={_anchorPulse}, первый глоб.пульс шага={firstStepGlobal} < {minActivation} → anchor={adjusted}");
          _anchorPulse = adjusted;
        }
      }
      _firstStepGlobalPulse = _doc.Lines.Count > 0
          ? _anchorPulse + _doc.Lines.Min(r => r.PulseWithinScenario)
          : _anchorPulse + 1;
      _lastExecutedStepPulse = 0;
      _pendingSuccessCompletion = false;
      _runStopwatch = Stopwatch.StartNew();
      _running = true;
      // До первого шага сбросить «висящее» ожидание оценки/зеркало с ручной сессии — иначе первый стимул не получает ОР+эхо (блок по WaitingForOperatorEvaluation).
      _cancelWaitingPeriod?.Invoke();
      {
        var id = _doc.Header?.Id ?? 0;
        var schedule = string.Join(", ", _doc.Lines.Select(l =>
            $"s{l.StepIndex}:внутрПульс={l.PulseWithinScenario}->глоб={_anchorPulse + l.PulseWithinScenario}"));
        ScenarioRunnerDiagnostics.Write(
            $"[Start] scenarioId={id} anchorGlobal={_anchorPulse} (глобальный счётчик в момент Start) schedule=[{schedule}] maxВнутрПульс={_maxPulse}");
      }
      RunningStateChanged?.Invoke();
    }

    /// <summary>Останавливает сценарий по команде пользователя.</summary>
    public void StopUser()
    {
      if (!_running)
        return;
      Complete(new OperatorScenarioCompletedEventArgs
      {
        Success = false,
        AbortedByUser = true,
        LastExecutedPulseWithinScenario = _lastExecutedStepPulse,
        Document = _doc
      });
    }

    /// <summary>Вызывается хостом при остановке пульсации.</summary>
    public void OnPulsationStopped()
    {
      if (!_running)
        return;
      Complete(new OperatorScenarioCompletedEventArgs
      {
        Success = false,
        AbortedByPulsationStop = true,
        LastExecutedPulseWithinScenario = _lastExecutedStepPulse,
        Document = _doc
      });
    }

    /// <summary>Вызывается хостом на глобальном пульсе после <c>UpdateStateOnly</c>, до <c>ProcessPsychicPulse</c>
    /// (стимул после дрейфа гомеостаза на этом пульсе, до психики — см. <c>GlobalTimer.OnPulseAfterGomeostasisBeforePsychic</c>).</summary>
    /// <param name="globalPulseCount">Текущее значение глобального счётчика пульсов.</param>
    public void OnGlobalPulseBeforeProcessing(int globalPulseCount)
    {
      if (!_running)
        return;

      if (globalPulseCount < _anchorPulse + 1)
      {
        WaitingForActivation?.Invoke(globalPulseCount, _firstStepGlobalPulse);
        return;
      }

      ScenarioLineRow line = null;
      foreach (var row in _doc.Lines)
      {
        if (globalPulseCount == _anchorPulse + row.PulseWithinScenario)
        {
          line = row;
          break;
        }
      }

      {
        int d = globalPulseCount - _anchorPulse;
        string hit = line == null ? "-" : $"step{line.StepIndex},внутр={line.PulseWithinScenario}";
        ScenarioRunnerDiagnostics.Write(
            $"[Pulse] global={globalPulseCount} anchor={_anchorPulse} дельтаОтСтарта={d} совпадение={hit}");
      }

      if (line == null)
        return;

      StepProgress?.Invoke(this, new OperatorScenarioStepProgressEventArgs { StepIndex = line.StepIndex });

      try
      {
        if (line.Kind == ScenarioLineKind.WaitClick)
        {
          _cancelWaitingPeriod?.Invoke();
          ScenarioRunnerDiagnostics.Write($"[WaitClick] step={line.StepIndex} global={globalPulseCount}");
        }
        else
        {
          if (line.ResetWaitingPeriod && AppGlobalState.EvolutionStage >= 2
              && AppGlobalState.WaitingForOperatorEvaluation)
            _cancelWaitingPeriod?.Invoke();

          bool hasPhrase = !string.IsNullOrWhiteSpace(line.Phrase);
          bool hasActions = line.ActionIds != null && line.ActionIds.Count > 0;
          int colorStep = AgentVisualColor.IsValidCode(line.VisualColorId) ? line.VisualColorId : AgentVisualColor.White;
          bool hasVisualColor = colorStep != AgentVisualColor.White;

          if (hasPhrase || hasActions || hasVisualColor)
          {
            var pult = _getPult();
            if (pult == null)
            {
              Fail("Пульт агента недоступен (откройте вкладку агента).");
              return;
            }

            var err = pult.TryApplyScenarioStimulus(
                line.ActionIds,
                line.Phrase ?? "",
                line.ToneId,
                line.MoodId,
                colorStep);
            if (err != null)
            {
              ScenarioRunnerDiagnostics.Write($"[Apply FAIL] step={line.StepIndex} global={globalPulseCount} err={err}");
              Fail(err);
              return;
            }
            ScenarioRunnerDiagnostics.Write(
                $"[Apply OK] step={line.StepIndex} global={globalPulseCount} фраза={(line.Phrase ?? "").Length}симв действий={line.ActionIds?.Count ?? 0}");
          }
          else
          {
            string note = line.ResetWaitingPeriod
                ? "сброс периода ожидания выполнен выше, стимул не подаётся"
                : "пустая строка (маркер пульса), стимул не подаётся";
            ScenarioRunnerDiagnostics.Write(
                $"[SkipStimulus] step={line.StepIndex} global={globalPulseCount} — {note}");
          }
        }

        int delta = line.PulseWithinScenario;
        _lastExecutedStepPulse = delta;
        if (delta >= _maxPulse)
        {
          _pendingSuccessCompletion = true;
          ScenarioRunnerDiagnostics.Write(
              $"[PendingComplete] step={line.StepIndex} global={globalPulseCount} — завершение отложено до OnPulseCompleted (LogSystemState + Flush)");
        }
      }
      catch (Exception ex)
      {
        Fail(ex.Message);
      }
    }

    /// <summary>Вызывается хостом после <see cref="OnGlobalPulseBeforeProcessing"/> и полной обработки пульса
    /// (включая ProcessPsychicPulse, FlushBufferedAgentRowToMemoryNow, LogSystemState).
    /// Если на этом пульсе был последний шаг сценария, завершает прогон.
    /// Это гарантирует, что MemoryLogManager содержит записи за последний пульс до построения отчёта.</summary>
    public void TryFinishAfterPulseCompleted()
    {
      if (!_running || !_pendingSuccessCompletion)
        return;
      _pendingSuccessCompletion = false;
      ScenarioRunnerDiagnostics.Write(
          $"[TryFinishAfterPulseCompleted] завершаем сценарий, lastВнутрПульс={_lastExecutedStepPulse}");
      Complete(new OperatorScenarioCompletedEventArgs
      {
        Success = true,
        LastExecutedPulseWithinScenario = _lastExecutedStepPulse,
        Document = _doc
      });
    }

    private void Fail(string message)
    {
      Complete(new OperatorScenarioCompletedEventArgs
      {
        Success = false,
        ErrorMessage = message,
        LastExecutedPulseWithinScenario = _lastExecutedStepPulse,
        Document = _doc
      });
    }

    private void Complete(OperatorScenarioCompletedEventArgs e)
    {
      if (!_running)
        return;
      _runStopwatch?.Stop();
      e.ElapsedWallTime = _runStopwatch?.Elapsed ?? TimeSpan.Zero;
      e.ElapsedPulses = GlobalTimer.GlobalPulsCount - _anchorPulse;
      ScenarioRunnerDiagnostics.Write(
          $"[Finish] success={e.Success} userAbort={e.AbortedByUser} pulsStop={e.AbortedByPulsationStop} lastВнутрПульс={e.LastExecutedPulseWithinScenario} anchor={_anchorPulse} elapsed={e.ElapsedWallTime.TotalSeconds:F2}s pulses={e.ElapsedPulses} err={e.ErrorMessage ?? ""}");
      e.Document = _doc;
      e.AnchorGlobalPulse = _anchorPulse;
      _running = false;
      _doc = null;
      _runStopwatch = null;
      RunningStateChanged?.Invoke();
      Finished?.Invoke(this, e);
    }
  }
}
