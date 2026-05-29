using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Niche.Contour;
using System;

namespace ISIDA.Niche
{
  /// <summary>
  /// Координатор такта триады: Niche.Update → Coupling → Creature (§6.5, этап 4.3).
  /// </summary>
  public sealed class TriadOrchestrator : IDisposable
  {
    private readonly CouplingBridge _couplingBridge;
    private readonly TriadInstanceState _instanceState = new TriadInstanceState();
    private bool _disposed;

    /// <summary>
    /// Создаёт оркестратор триады.
    /// </summary>
    /// <param name="couplingBridge">Мост coupling.</param>
    /// <exception cref="ArgumentNullException">Если bridge null.</exception>
    public TriadOrchestrator(CouplingBridge couplingBridge)
    {
      _couplingBridge = couplingBridge ?? throw new ArgumentNullException(nameof(couplingBridge));
      _couplingBridge.SetTriadInstanceState(_instanceState);
    }

    /// <summary>Мост coupling Creature↔Niche.</summary>
    public CouplingBridge CouplingBridge => _couplingBridge;

    /// <summary>Состояние экземпляра триады.</summary>
    public TriadInstanceState InstanceState => _instanceState;

    /// <summary>Движок Niche (null, если host-only режим).</summary>
    public NicheEngine NicheEngine => _couplingBridge.NicheEngine;

    /// <summary>
    /// Один такт триады до UpdateStateOnly Creature.
    /// </summary>
    /// <param name="pulse">Глобальный пульс.</param>
    public void ProcessTriadPulse(int pulse)
    {
      _couplingBridge.ProcessPulseBeforeGomeostasis(pulse);
    }

    /// <summary>
    /// Перенос обучения на Niche₂ (§6.6).
    /// </summary>
    /// <param name="newInitialParams">Новые начальные параметры Niche.</param>
    /// <returns>True, если применено.</returns>
    public bool ApplyNicheTransfer(System.Collections.Generic.IReadOnlyDictionary<int, float> newInitialParams)
    {
      return _couplingBridge.ApplyNicheTransfer(newInitialParams);
    }

    /// <summary>
    /// Сброс диады (§6.12).
    /// </summary>
    /// <param name="resetType">Тип сброса.</param>
    /// <returns>Результат.</returns>
    public DyadResetResult ResetDyad(DyadResetType resetType)
    {
      return _couplingBridge.ResetDyad(resetType);
    }

    /// <summary>
    /// Передаёт probe-key оператора в контур Niche.
    /// </summary>
    /// <param name="probeKey">EnvironmentMetricProbeKey.</param>
    public void SetContourProbeKey(string probeKey)
    {
      _couplingBridge.SetContourProbeKey(probeKey);
    }

    /// <summary>
    /// Передаёт InputSnapshot контура напрямую (host API, §6.8).
    /// </summary>
    /// <param name="snapshot">Снимок входа.</param>
    /// <param name="probeKey">Опциональный probeKey для лога.</param>
    public void ApplyContourInputSnapshot(InputSnapshot snapshot, string probeKey = null)
    {
      _couplingBridge.ApplyContourInputSnapshot(snapshot, probeKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;
    }
  }
}
