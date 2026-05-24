using ISIDA.Common;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Niche
{
  /// <summary>
  /// Первичный AOE по отклику Niche: baseline, окно W_eval, исходы Success/Fail/NoResponse/Ambiguous (§5.3).
  /// </summary>
  public sealed class AoeEvaluationService : IDisposable
  {
    private readonly object _lock = new object();
    private TriadExperimentConfig _config;
    private TriadInstanceState _triadInstanceState;
    private IReadOnlyCollection<int> _mappedCreatureParamIds = Array.Empty<int>();
    private readonly Dictionary<int, Queue<float>> _spontaneousBaselineSamples = new Dictionary<int, Queue<float>>();
    private NicheAoeWindow _activeWindow;
    private bool _pendingFinalize;
    private int _pendingClosePulse;
    private bool _disposed;

    /// <summary>
    /// Подключает состояние экземпляра триады (§4.6).
    /// </summary>
    /// <param name="instanceState">Состояние или null (fallback AppGlobalState).</param>
    public void SetTriadInstanceState(TriadInstanceState instanceState)
    {
      _triadInstanceState = instanceState;
    }

    /// <summary>True, если первичный AOE по Niche включён (фаза B+).</summary>
    public bool IsNichePrimaryActive =>
        _config != null && _config.Phase >= TriadPhase.B;

    /// <summary>True, пока открыто окно ожидания отклика Niche.</summary>
    public bool IsWaitingForNicheResponse
    {
      get
      {
        lock (_lock)
          return _activeWindow != null;
      }
    }

    /// <summary>
    /// Обновляет конфигурацию и список параметров Creature из mapping Niche→Creature.
    /// </summary>
    /// <param name="config">Конфигурация триады.</param>
    public void Configure(TriadExperimentConfig config)
    {
      lock (_lock)
      {
        _config = config ?? new TriadExperimentConfig();
        _mappedCreatureParamIds = _config.NicheToCreature?
            .Select(m => m.CreatureParamId)
            .Distinct()
            .ToList() ?? new List<int>();
        ResetInternalState();
      }
    }

    /// <summary>
    /// Открывает окно AOE после успешного выполнения automatizm (фаза B+).
    /// </summary>
    /// <param name="automatizmId">ID automatizm Creature.</param>
    /// <param name="actionPulse">Глобальный пульс выполнения.</param>
    /// <param name="creatureParamsBefore">Снимок параметров Creature до отклика Niche.</param>
    /// <param name="overallStateBefore">Интегральное состояние до отклика.</param>
    public void OpenWindow(
        int automatizmId,
        int actionPulse,
        IReadOnlyDictionary<int, float> creatureParamsBefore,
        AppGlobalState.HomeostasisState overallStateBefore)
    {
      if (!IsNichePrimaryActive || automatizmId <= 0)
        return;

      lock (_lock)
      {
        var settings = GetSettings();
        _activeWindow = new NicheAoeWindow
        {
          AutomatizmId = automatizmId,
          ActionPulse = actionPulse,
          PulsesRemaining = settings.EvalWindowPulses,
          CreatureParamsBefore = CopySnapshot(creatureParamsBefore),
          OverallStateBefore = overallStateBefore,
          NicheResponseDetected = false
        };
        _pendingFinalize = false;
        StartNicheWaitingWindow(automatizmId, settings.EvalWindowPulses);
        Logger.Info($"AOE Niche: окно открыто для automatizm ID={automatizmId}, W_eval={settings.EvalWindowPulses}");
      }
    }

    private void StartNicheWaitingWindow(int automatizmId, int windowPulses)
    {
      if (_triadInstanceState != null)
        _triadInstanceState.StartWaitingForNicheResponse(automatizmId, windowPulses);
      else
        AppGlobalState.StartWaitingForNicheResponse(automatizmId, windowPulses);
    }

    private void ResetNicheWaitingWindow()
    {
      if (_triadInstanceState != null)
        _triadInstanceState.ResetWaitingForNicheResponse();
      else
        AppGlobalState.ResetWaitingForNicheResponse();
    }

    /// <summary>
    /// Обрабатывает такт диады: baseline, детекция отклика, закрытие окна.
    /// </summary>
    /// <param name="entry">Запись лога диады за такт.</param>
    public void RecordDyadPulse(DyadPulseLogEntry entry)
    {
      if (entry == null)
        return;

      lock (_lock)
      {
        UpdateBaselineFromSpontaneous(entry);

        if (_activeWindow == null || _pendingFinalize)
          return;

        var settings = GetSettings();
        if (entry.Pulse >= _activeWindow.ActionPulse &&
            entry.Pulse <= _activeWindow.ActionPulse + settings.CorrelationHorizonK)
        {
          if (DetectNicheResponse(entry, settings))
            _activeWindow.NicheResponseDetected = true;
        }

        if (entry.Pulse >= _activeWindow.ActionPulse)
          _activeWindow.PulsesRemaining = settings.EvalWindowPulses - (entry.Pulse - _activeWindow.ActionPulse);

        if (_activeWindow.PulsesRemaining <= 0)
          ScheduleFinalize(entry.Pulse);
        else if (_activeWindow.NicheResponseDetected && entry.Pulse > _activeWindow.ActionPulse)
          ScheduleFinalize(entry.Pulse);
      }
    }

    /// <summary>
    /// Завершает отложенный AOE после <c>UpdateStateOnly</c> и возвращает результат (один раз).
    /// </summary>
    /// <param name="gomeostas">Гомеостаз Creature.</param>
    /// <param name="calculator">Калькулятор оценки.</param>
    /// <param name="result">Исход AOE.</param>
    /// <returns>True, если результат готов.</returns>
    public bool TryFinalizePending(
        GomeostasSystem gomeostas,
        HomeostasisCalculator calculator,
        out NicheAoeResult result)
    {
      result = null;
      if (gomeostas == null || calculator == null)
        return false;

      NicheAoeWindow window;
      int closePulse;

      lock (_lock)
      {
        if (!_pendingFinalize || _activeWindow == null)
          return false;

        window = _activeWindow;
        closePulse = _pendingClosePulse;
        _pendingFinalize = false;
        _activeWindow = null;
        ResetNicheWaitingWindow();
      }

      AoeOutcome outcome;
      int assessment = 0;

      if (window.NicheResponseDetected)
      {
        var currentParams = gomeostas.GetAllParameters();
        assessment = calculator.ComputeNicheMappedAutomatizmAssessment(
            window.CreatureParamsBefore,
            currentParams,
            _mappedCreatureParamIds,
            window.OverallStateBefore,
            AppGlobalState.CurrentOverallState);

        if (assessment > 0)
          outcome = AoeOutcome.Success;
        else if (assessment < 0)
          outcome = AoeOutcome.Fail;
        else
          outcome = AoeOutcome.Ambiguous;
      }
      else if (HasMappedCreatureChange(gomeostas, window.CreatureParamsBefore))
      {
        outcome = AoeOutcome.Ambiguous;
      }
      else
      {
        outcome = AoeOutcome.NoResponse;
      }

      result = new NicheAoeResult
      {
        AutomatizmId = window.AutomatizmId,
        Outcome = outcome,
        Assessment = outcome == AoeOutcome.Success || outcome == AoeOutcome.Fail ? assessment : 0,
        ActionPulse = window.ActionPulse,
        ClosePulse = closePulse
      };

      Logger.Info($"AOE Niche: automatizm ID={result.AutomatizmId}, outcome={result.Outcome}, assessment={result.Assessment}");
      return true;
    }

    /// <summary>
    /// Сбрасывает окно и baseline (§6.12).
    /// </summary>
    public void Reset()
    {
      lock (_lock)
        ResetInternalState();
    }

    /// <inheritdoc />
    public void Dispose()
    {
      if (_disposed)
        return;
      Reset();
      _disposed = true;
    }

    private void ResetInternalState()
    {
      _activeWindow = null;
      _pendingFinalize = false;
      _pendingClosePulse = 0;
      _spontaneousBaselineSamples.Clear();
      ResetNicheWaitingWindow();
    }

    private TriadAoeSettings GetSettings()
    {
      return _config?.AoeSettings ?? new TriadAoeSettings();
    }

    private void UpdateBaselineFromSpontaneous(DyadPulseLogEntry entry)
    {
      if (entry.CreatureActionId != 0 || entry.NicheSpontaneousDelta == null)
        return;

      int n = GetSettings().BaselineWindowN;
      if (n < 1)
        n = 1;

      foreach (var kv in entry.NicheSpontaneousDelta)
      {
        if (!_spontaneousBaselineSamples.TryGetValue(kv.Key, out Queue<float> q))
        {
          q = new Queue<float>();
          _spontaneousBaselineSamples[kv.Key] = q;
        }

        q.Enqueue(Math.Abs(kv.Value));
        while (q.Count > n)
          q.Dequeue();
      }
    }

    private bool DetectNicheResponse(DyadPulseLogEntry entry, TriadAoeSettings settings)
    {
      if (entry.LastCreatureUpdateOrigin == StimulusOrigin.Niche)
        return true;

      if (entry.NicheResponseDelta == null || entry.NicheResponseDelta.Count == 0)
        return false;

      foreach (var kv in entry.NicheResponseDelta)
      {
        float baseline = GetBaselineForParam(kv.Key);
        if (Math.Abs(kv.Value) > baseline + settings.ResponseThreshold)
          return true;
      }

      return false;
    }

    private float GetBaselineForParam(int nicheParamId)
    {
      if (!_spontaneousBaselineSamples.TryGetValue(nicheParamId, out Queue<float> q) || q.Count == 0)
        return 0f;

      float sum = 0f;
      foreach (float v in q)
        sum += v;
      return sum / q.Count;
    }

    private void ScheduleFinalize(int closePulse)
    {
      if (_activeWindow == null || _pendingFinalize)
        return;

      _pendingFinalize = true;
      _pendingClosePulse = closePulse;
    }

    private bool HasMappedCreatureChange(GomeostasSystem gomeostas, IReadOnlyDictionary<int, float> before)
    {
      if (before == null || before.Count == 0 || _mappedCreatureParamIds.Count == 0)
        return false;

      float threshold = GetSettings().ResponseThreshold;
      foreach (int paramId in _mappedCreatureParamIds)
      {
        if (!before.TryGetValue(paramId, out float oldVal))
          continue;

        var param = gomeostas.GetParameter(paramId);
        if (param == null)
          continue;

        if (Math.Abs(param.Value - oldVal) > threshold)
          return true;
      }

      return false;
    }

    private static Dictionary<int, float> CopySnapshot(IReadOnlyDictionary<int, float> source)
    {
      if (source == null || source.Count == 0)
        return new Dictionary<int, float>();

      var copy = new Dictionary<int, float>(source.Count);
      foreach (var kv in source)
        copy[kv.Key] = kv.Value;
      return copy;
    }

    private sealed class NicheAoeWindow
    {
      public int AutomatizmId { get; set; }
      public int ActionPulse { get; set; }
      public int PulsesRemaining { get; set; }
      public Dictionary<int, float> CreatureParamsBefore { get; set; }
      public AppGlobalState.HomeostasisState OverallStateBefore { get; set; }
      public bool NicheResponseDetected { get; set; }
    }
  }
}
