using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ISIDA.Niche.Contour
{
  /// <summary>
  /// Контур с InputSnapshot через EnvironmentMetricProbeKey (§6.8).
  /// </summary>
  public sealed class ProbeContourAdapter : IContourAdapter
  {
    private readonly Dictionary<string, Dictionary<int, float>> _probeToDeltas =
        new Dictionary<string, Dictionary<int, float>>();
    private InputSnapshot _pendingSnapshot;
    private string _pendingProbeKey = string.Empty;
    private ContourInputApplication _lastApplication;

    /// <summary>
    /// Создаёт контур с загрузкой mapping probe→delta из Environment.
    /// </summary>
    /// <param name="contourId">Идентификатор контура.</param>
    /// <param name="environmentFolder">Каталог Environment.</param>
    public ProbeContourAdapter(string contourId, string environmentFolder)
    {
      ContourId = string.IsNullOrWhiteSpace(contourId) ? "probe_contour" : contourId.Trim();
      LoadProbes(environmentFolder);
    }

    /// <inheritdoc />
    public string ContourId { get; }

    /// <inheritdoc />
    public int LatencyPulses => 0;

    /// <summary>Последнее применение InputSnapshot (one-shot).</summary>
    public ContourInputApplication LastApplication => _lastApplication;

    /// <summary>
    /// Устанавливает активный ключ пробы → InputSnapshot для следующего такта.
    /// </summary>
    /// <param name="probeKey">Ключ из InfluenceAction.EnvironmentMetricProbeKey.</param>
    public void SetActiveProbeKey(string probeKey)
    {
      _pendingProbeKey = probeKey?.Trim() ?? string.Empty;
      _pendingSnapshot = BuildSnapshotFromProbeKey(_pendingProbeKey);
    }

    /// <summary>
    /// Устанавливает InputSnapshot напрямую (host API / внешний контур).
    /// </summary>
    /// <param name="snapshot">Снимок входа.</param>
    /// <param name="probeKey">Опциональный probeKey для лога.</param>
    public void ApplyInputSnapshot(InputSnapshot snapshot, string probeKey = null)
    {
      _pendingSnapshot = snapshot;
      _pendingProbeKey = probeKey?.Trim() ?? string.Empty;
    }

    /// <summary>Перезагружает mapping из contour_probes.dat.</summary>
    /// <param name="environmentFolder">Каталог Environment.</param>
    public void ReloadProbes(string environmentFolder)
    {
      _probeToDeltas.Clear();
      LoadProbes(environmentFolder);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<int, float> GetNicheDeltasForPulse(int pulse)
    {
      if (_pendingSnapshot == null || _pendingSnapshot.Dim == 0)
        return EmptyDeltas;

      var snapshot = _pendingSnapshot;
      if (snapshot.Pulse <= 0)
        snapshot.Pulse = pulse;

      var deltas = snapshot.ToNicheDeltas();
      _lastApplication = new ContourInputApplication
      {
        ProbeKey = _pendingProbeKey,
        Snapshot = snapshot,
        NicheDeltas = deltas
      };

      _pendingSnapshot = null;
      _pendingProbeKey = string.Empty;
      return deltas.Count == 0 ? EmptyDeltas : deltas;
    }

    private InputSnapshot BuildSnapshotFromProbeKey(string key)
    {
      if (string.IsNullOrEmpty(key))
        return null;

      if (!_probeToDeltas.TryGetValue(key, out Dictionary<int, float> map) || map.Count == 0)
        return null;

      var ordered = map.OrderBy(x => x.Key).ToList();
      return new InputSnapshot
      {
        Values = ordered.Select(x => x.Value).ToArray(),
        ChannelToNicheParamId = ordered.Select(x => x.Key).ToArray()
      };
    }

    private void LoadProbes(string environmentFolder)
    {
      if (string.IsNullOrWhiteSpace(environmentFolder))
        return;

      string path = Path.Combine(environmentFolder, "contour_probes.dat");
      if (!File.Exists(path))
        return;

      foreach (var raw in File.ReadAllLines(path))
      {
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#"))
          continue;

        string[] parts = line.Split('|');
        if (parts.Length < 3)
          continue;

        string probeKey = parts[0].Trim();
        if (probeKey.Length == 0)
          continue;

        if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nicheParamId))
          continue;
        if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float delta))
          continue;

        if (!_probeToDeltas.TryGetValue(probeKey, out Dictionary<int, float> map))
        {
          map = new Dictionary<int, float>();
          _probeToDeltas[probeKey] = map;
        }

        map[nicheParamId] = delta;
      }
    }

    private static readonly Dictionary<int, float> EmptyDeltas = new Dictionary<int, float>();
  }
}
