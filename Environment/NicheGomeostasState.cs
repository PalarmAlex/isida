using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;

namespace ISIDA.Niche
{
  /// <summary>
  /// Состояние Niche на базе <see cref="NicheSymbiontContext"/> (универсальный симбионт).
  /// </summary>
  public sealed class NicheGomeostasState : INicheParameterState, IDisposable
  {
    private readonly Dictionary<int, float> _snapshotBeforePulse = new Dictionary<int, float>();
    private readonly Dictionary<int, float> _snapshotAfterAction = new Dictionary<int, float>();
    private NicheSymbiontContext _context;
    private int _lastCreatureActionId;
    private int _lastCreatureActionPulse = -1;
    private bool _actionAppliedThisPulse;
    private bool _disposed;

    /// <summary>Контекст симбионта Niche.</summary>
    public NicheSymbiontContext Context => _context;

    /// <summary>Гомеостаз Niche.</summary>
    public GomeostasSystem Gomeostas => _context?.Gomeostas;

    /// <inheritdoc />
    public bool IsInitialized => Gomeostas != null && Gomeostas.GetAllParameters().Count > 0;

    /// <inheritdoc />
    public int LastCreatureActionId => _lastCreatureActionId;

    /// <inheritdoc />
    public int LastCreatureActionPulse => _lastCreatureActionPulse;

    /// <summary>
    /// Создаёт гомеостаз и стек рефлексов Niche.
    /// </summary>
    public void Initialize(
        string nicheDataFolder,
        RoleProfile roleProfile,
        IEnumerable<NicheParameterDef> fallbackNicheParams = null)
    {
      DisposeContext();
      _context = new NicheSymbiontContext();
      _context.Initialize(nicheDataFolder, roleProfile, fallbackNicheParams);
    }

    /// <inheritdoc />
    public void Initialize(IEnumerable<NicheParameterDef> parameters)
    {
      if (Gomeostas == null || parameters == null)
        return;

      var values = new Dictionary<int, float>();
      foreach (var p in parameters)
      {
        if (p == null || p.ParamId <= 0)
          continue;
        values[p.ParamId] = Clamp(p.InitialValue);
      }

      if (values.Count > 0)
        Gomeostas.HostBatchUpdateParameterValues(values);
    }

    /// <inheritdoc />
    public void ResetToInitial(IEnumerable<NicheParameterDef> parameters) => Initialize(parameters);

    /// <inheritdoc />
    public void RestoreFromSnapshot(IReadOnlyDictionary<int, float> snapshot)
    {
      if (Gomeostas == null || snapshot == null || snapshot.Count == 0)
        return;

      var filtered = new Dictionary<int, float>();
      foreach (var kv in snapshot)
      {
        if (Gomeostas.GetParameter(kv.Key) != null)
          filtered[kv.Key] = Clamp(kv.Value);
      }

      if (filtered.Count > 0)
        Gomeostas.HostBatchUpdateParameterValues(filtered);
    }

    /// <inheritdoc />
    public void BeginPulse()
    {
      _snapshotBeforePulse.Clear();
      foreach (var kv in GetCurrentValues())
        _snapshotBeforePulse[kv.Key] = kv.Value;
      _actionAppliedThisPulse = false;
    }

    /// <inheritdoc />
    public void ApplySpontaneousUpdate(bool driftEnabled, IReadOnlyDictionary<int, float> contourDeltas)
    {
      Gomeostas?.DetachedNichePulseUpdate(driftEnabled, contourDeltas);
    }

    /// <inheritdoc />
    public void ApplyCouplingDelta(int nicheParamId, float delta)
    {
      Gomeostas?.DetachedApplyParameterDelta(nicheParamId, delta);
    }

    /// <inheritdoc />
    public void MarkCreatureAction(int actionId, int pulse)
    {
      _lastCreatureActionId = actionId;
      _lastCreatureActionPulse = pulse;
      _actionAppliedThisPulse = true;
      _snapshotAfterAction.Clear();
      foreach (var kv in GetCurrentValues())
        _snapshotAfterAction[kv.Key] = kv.Value;
    }

    /// <summary>
    /// Рефлексы Niche после coupling (БР + опц. УР).
    /// </summary>
    public int ApplySymbiontReflexes(int creatureActionId)
    {
      if (_context == null)
        return 0;

      int applied = NicheSymbiontGeneticReflexActivator.ApplyAfterCreatureAction(_context, this, creatureActionId);

      if (_context.ConditionedReflexes != null)
      {
        applied += _context.ConditionedReflexes.ApplyAfterCreatureAction(
            Gomeostas,
            creatureActionId,
            id => NicheSymbiontGeneticReflexActivator.ApplyGeneticReflexById(_context, id));
      }

      return applied;
    }

    /// <inheritdoc />
    public Dictionary<int, float> EndPulse() => GetCurrentValues();

    /// <inheritdoc />
    public Dictionary<int, float> GetCurrentValues()
    {
      if (Gomeostas == null)
        return new Dictionary<int, float>();

      var result = new Dictionary<int, float>();
      foreach (var p in Gomeostas.GetAllParameters())
        result[p.Id] = p.Value;
      return result;
    }

    /// <inheritdoc />
    public Dictionary<int, float> GetSnapshotBeforePulse() => new Dictionary<int, float>(_snapshotBeforePulse);

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;
      DisposeContext();
    }

    private void DisposeContext()
    {
      _context?.Dispose();
      _context = null;
    }

    private static float Clamp(float v)
    {
      if (v < 0f) return 0f;
      if (v > 100f) return 100f;
      return v;
    }
  }
}
