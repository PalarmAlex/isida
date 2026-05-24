using System;

namespace ISIDA.Niche.Contour
{
  /// <summary>
  /// Нормализованный снимок входа контура за такт (§6.8).
  /// </summary>
  public sealed class InputSnapshot
  {
    /// <summary>Глобальный пульс (0 — если не задан).</summary>
    public int Pulse { get; set; }

    /// <summary>Каналы контура [0..100] per channel.</summary>
    public float[] Values { get; set; } = Array.Empty<float>();

    /// <summary>Отображение channelIndex → nicheParamId (из contour_probes.dat).</summary>
    public int[] ChannelToNicheParamId { get; set; } = Array.Empty<int>();

    /// <summary>Размерность вектора контура.</summary>
    public int Dim => Values?.Length ?? 0;

    /// <summary>
    /// Преобразует snapshot в дельты параметров Niche.
    /// </summary>
    /// <returns>nicheParamId → delta.</returns>
    public System.Collections.Generic.Dictionary<int, float> ToNicheDeltas()
    {
      var result = new System.Collections.Generic.Dictionary<int, float>();
      if (Values == null || ChannelToNicheParamId == null)
        return result;

      int count = Math.Min(Values.Length, ChannelToNicheParamId.Length);
      for (int i = 0; i < count; i++)
      {
        int nicheId = ChannelToNicheParamId[i];
        if (nicheId <= 0)
          continue;

        if (result.TryGetValue(nicheId, out float existing))
          result[nicheId] = existing + Values[i];
        else
          result[nicheId] = Values[i];
      }

      return result;
    }
  }

  /// <summary>Результат применения InputSnapshot контура на такте.</summary>
  public sealed class ContourInputApplication
  {
    /// <summary>EnvironmentMetricProbeKey (пусто для прямого snapshot).</summary>
    public string ProbeKey { get; set; } = string.Empty;

    /// <summary>Исходный InputSnapshot.</summary>
    public InputSnapshot Snapshot { get; set; }

    /// <summary>Дельты Niche после mapping.</summary>
    public System.Collections.Generic.IReadOnlyDictionary<int, float> NicheDeltas { get; set; }
  }
}
