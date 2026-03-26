using ISIDA.Common;
using System;
using System.Collections.Generic;
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
  }

  /// <summary>Выполнение сценария оператора по событиям пульса.</summary>
  public sealed class OperatorScenarioRunner
  {
    private ScenarioDocument _doc;
    private int _anchorPulse;
    private Dictionary<int, ScenarioLineRow> _byPulse;
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

      _byPulse = _doc.Lines.ToDictionary(r => r.PulseWithinScenario, r => r);
      _maxPulse = _byPulse.Count == 0 ? 0 : _byPulse.Keys.Max();
      _anchorPulse = GlobalTimer.GlobalPulsCount;
      _lastExecutedStepPulse = 0;
      _running = true;
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

    /// <summary>Вызывается хостом после завершения очередного глобального пульса.</summary>
    /// <param name="globalPulseCount">Текущее значение глобального счётчика пульсов.</param>
    public void OnPulseCompleted(int globalPulseCount)
    {
      if (!_running)
        return;

      int stepPulse = globalPulseCount - _anchorPulse;
      if (stepPulse < 1)
        return;

      if (!_byPulse.TryGetValue(stepPulse, out var line))
        return;

      try
      {
        if (line.Kind == ScenarioLineKind.WaitClick)
        {
          _cancelWaitingPeriod?.Invoke();
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
            Fail(err);
            return;
          }
        }

        _lastExecutedStepPulse = stepPulse;
        if (stepPulse >= _maxPulse)
        {
          Complete(new OperatorScenarioCompletedEventArgs
          {
            Success = true,
            LastExecutedPulseWithinScenario = stepPulse,
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
      e.Document = _doc;
      _running = false;
      _byPulse = null;
      _doc = null;
      RunningStateChanged?.Invoke();
      Finished?.Invoke(this, e);
    }
  }
}
