using System;
using System.Collections.Generic;

namespace ISIDA.Niche
{
  /// <summary>
  /// In-memory состояние host-Niche: параметры, дрейф, снимки до/после такта.
  /// </summary>
  public sealed class NicheHostState : INicheParameterState
  {
    private readonly Dictionary<int, float> _values = new Dictionary<int, float>();
    private readonly Dictionary<int, float> _speedPerPulse = new Dictionary<int, float>();
    private Dictionary<int, float> _snapshotBeforePulse = new Dictionary<int, float>();
    private Dictionary<int, float> _snapshotAfterAction = new Dictionary<int, float>();
    private int _lastCreatureActionId;
    private int _lastCreatureActionPulse = -1;
    private bool _actionAppliedThisPulse;

    /// <summary>True, если задан хотя бы один параметр Niche.</summary>
    public bool IsInitialized => _values.Count > 0;

    /// <summary>ID последнего действия Creature, повлиявшего на Niche.</summary>
    public int LastCreatureActionId => _lastCreatureActionId;

    /// <summary>Пульс последнего coupling-действия.</summary>
    public int LastCreatureActionPulse => _lastCreatureActionPulse;

    /// <summary>
    /// Инициализирует параметры Niche из конфигурации.
    /// </summary>
    /// <param name="parameters">Описания параметров host-Niche.</param>
    public void Initialize(IEnumerable<NicheParameterDef> parameters)
    {
      _values.Clear();
      _speedPerPulse.Clear();
      if (parameters == null)
        return;

      foreach (var p in parameters)
      {
        if (p == null)
          continue;
        _values[p.ParamId] = Clamp(p.InitialValue);
        _speedPerPulse[p.ParamId] = p.SpeedPerPulse;
      }
    }

    /// <summary>
    /// Сбрасывает значения к начальным из конфигурации.
    /// </summary>
    /// <param name="parameters">Описания параметров.</param>
    public void ResetToInitial(IEnumerable<NicheParameterDef> parameters)
    {
      Initialize(parameters);
    }

    /// <summary>
    /// Восстанавливает значения из снимка (§6.12 NicheSoft).
    /// </summary>
    /// <param name="snapshot">paramId→value.</param>
    public void RestoreFromSnapshot(IReadOnlyDictionary<int, float> snapshot)
    {
      if (snapshot == null || snapshot.Count == 0)
        return;

      foreach (var kv in snapshot)
      {
        if (_values.ContainsKey(kv.Key))
          _values[kv.Key] = Clamp(kv.Value);
        else
          _values[kv.Key] = Clamp(kv.Value);
      }
    }

    /// <summary>
    /// Начало такта: сохраняет снимок «до» и сбрасывает флаг действия.
    /// </summary>
    public void BeginPulse()
    {
      _snapshotBeforePulse = CopyValues();
      _actionAppliedThisPulse = false;
    }

    /// <summary>
    /// Применяет спонтанный дрейф и/или contour-input к параметрам Niche.
    /// </summary>
    /// <param name="driftEnabled">Включён ли дрейф SpeedPerPulse.</param>
    /// <param name="contourDeltas">Дополнительные дельты от контура (paramId→delta).</param>
    public void ApplySpontaneousUpdate(bool driftEnabled, IReadOnlyDictionary<int, float> contourDeltas)
    {
      foreach (var kv in _values)
      {
        int id = kv.Key;
        float v = kv.Value;
        if (driftEnabled && _speedPerPulse.TryGetValue(id, out float speed) && Math.Abs(speed) > 0.0001f)
          v = Clamp(v + speed);

        if (contourDeltas != null && contourDeltas.TryGetValue(id, out float cd))
          v = Clamp(v + cd);

        _values[id] = v;
      }
    }

    /// <summary>
    /// Применяет coupling: дельта к параметру Niche от действия Creature.
    /// </summary>
    /// <param name="nicheParamId">ID параметра Niche.</param>
    /// <param name="delta">Изменение значения.</param>
    public void ApplyCouplingDelta(int nicheParamId, float delta)
    {
      if (!_values.ContainsKey(nicheParamId))
        _values[nicheParamId] = 50f;

      _values[nicheParamId] = Clamp(_values[nicheParamId] + delta);
    }

    /// <summary>
    /// Отмечает успешное действие Creature для расчёта niche_response_delta.
    /// </summary>
    /// <param name="actionId">ID адаптивного действия.</param>
    /// <param name="pulse">Номер пульса.</param>
    public void MarkCreatureAction(int actionId, int pulse)
    {
      _lastCreatureActionId = actionId;
      _lastCreatureActionPulse = pulse;
      _actionAppliedThisPulse = true;
      _snapshotAfterAction = CopyValues();
    }

    /// <summary>
    /// Завершение такта: снимок «после» для лога.
    /// </summary>
    /// <returns>Копия состояния Niche после такта.</returns>
    public Dictionary<int, float> EndPulse()
    {
      return CopyValues();
    }

    /// <summary>
    /// Текущие значения параметров Niche.
    /// </summary>
    /// <returns>Копия словаря paramId→value.</returns>
    public Dictionary<int, float> GetCurrentValues()
    {
      return CopyValues();
    }

    /// <summary>
    /// Снимок состояния до начала такта.
    /// </summary>
    /// <returns>Копия снимка.</returns>
    public Dictionary<int, float> GetSnapshotBeforePulse()
    {
      return new Dictionary<int, float>(_snapshotBeforePulse);
    }

    /// <summary>
    /// Расчёт spontaneous и response delta для лога (§5.3.1).
    /// </summary>
    /// <param name="stateAfter">Состояние после такта.</param>
    /// <param name="spontaneous">Выход: изменение без учёта действия Creature.</param>
    /// <param name="response">Выход: изменение, обусловленное действием.</param>
    public void ComputeDeltasForLog(
        Dictionary<int, float> stateAfter,
        out Dictionary<int, float> spontaneous,
        out Dictionary<int, float> response)
    {
      spontaneous = new Dictionary<int, float>();
      response = new Dictionary<int, float>();

      foreach (var kv in stateAfter)
      {
        int id = kv.Key;
        float before = _snapshotBeforePulse.TryGetValue(id, out float b) ? b : kv.Value;
        float after = kv.Value;
        float total = after - before;

        if (_actionAppliedThisPulse && _snapshotAfterAction.TryGetValue(id, out float mid))
        {
          spontaneous[id] = mid - before;
          response[id] = after - mid;
        }
        else
        {
          spontaneous[id] = total;
          response[id] = 0f;
        }
      }
    }

    private Dictionary<int, float> CopyValues()
    {
      return new Dictionary<int, float>(_values);
    }

    private static float Clamp(float v)
    {
      if (v < 0f) return 0f;
      if (v > 100f) return 100f;
      return v;
    }
  }
}
