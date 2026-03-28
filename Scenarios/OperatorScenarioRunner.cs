using ISIDA.Common;
using System;
using System.Linq;

namespace ISIDA.Scenarios
{
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
  }

  /// <summary>Выполнение сценария оператора по событиям пульса.</summary>
  public sealed class OperatorScenarioRunner
  {
    private ScenarioDocument _doc;
    private int _anchorPulse;
    private int _maxPulse;
    private Func<IOperatorScenarioPult> _getPult;
    private Action _cancelWaitingPeriod;
    private bool _running;
    private int _lastExecutedStepPulse;

    /// <summary>Истина, пока сценарий ожидает пульсы.</summary>
    public bool IsRunning => _running;

    /// <summary>Завершение прогона (любой исход).</summary>
    public event EventHandler<OperatorScenarioCompletedEventArgs> Finished;
    /// <summary>Вызывается при смене состояния «идёт / не идёт» (для обновления UI).</summary>
    public event Action RunningStateChanged;

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
      // Якорь — глобальный номер пульса в момент Start (последний завершённый на момент вызова).
      // Срабатывание шага: глобальный пульс == якорь + PulseWithinScenario (без пересчёта расписания).
      _anchorPulse = GlobalTimer.GlobalPulsCount;
      _lastExecutedStepPulse = 0;
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
        return;

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

      try
      {
        if (line.Kind == ScenarioLineKind.WaitClick)
        {
          _cancelWaitingPeriod?.Invoke();
          ScenarioRunnerDiagnostics.Write($"[WaitClick] step={line.StepIndex} global={globalPulseCount}");
        }
        else
        {
          var pult = _getPult();
          if (pult == null)
          {
            Fail("Пульт агента недоступен (откройте вкладку агента).");
            return;
          }

          if (line.ResetWaitingPeriod && AppGlobalState.EvolutionStage >= 3
              && AppGlobalState.WaitingForOperatorEvaluation)
            _cancelWaitingPeriod?.Invoke();

          var err = pult.TryApplyScenarioStimulus(
              line.ActionIds,
              line.Phrase ?? "",
              line.ToneId,
              line.MoodId);
          if (err != null)
          {
            ScenarioRunnerDiagnostics.Write($"[Apply FAIL] step={line.StepIndex} global={globalPulseCount} err={err}");
            Fail(err);
            return;
          }
          ScenarioRunnerDiagnostics.Write(
              $"[Apply OK] step={line.StepIndex} global={globalPulseCount} фраза={(line.Phrase ?? "").Length}симв действий={line.ActionIds?.Count ?? 0}");
        }

        int delta = line.PulseWithinScenario;
        _lastExecutedStepPulse = delta;
        if (delta >= _maxPulse)
        {
          Complete(new OperatorScenarioCompletedEventArgs
          {
            Success = true,
            LastExecutedPulseWithinScenario = delta,
            Document = _doc
          });
        }
      }
      catch (Exception ex)
      {
        Fail(ex.Message);
      }
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
      ScenarioRunnerDiagnostics.Write(
          $"[Finish] success={e.Success} userAbort={e.AbortedByUser} pulsStop={e.AbortedByPulsationStop} lastВнутрПульс={e.LastExecutedPulseWithinScenario} anchor={_anchorPulse} err={e.ErrorMessage ?? ""}");
      e.Document = _doc;
      e.AnchorGlobalPulse = _anchorPulse;
      _running = false;
      _doc = null;
      RunningStateChanged?.Invoke();
      Finished?.Invoke(this, e);
    }
  }
}
