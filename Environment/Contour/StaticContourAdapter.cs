using System.Collections.Generic;

namespace ISIDA.Niche.Contour
{
  /// <summary>
  /// Статичный контур MVP (фаза A): без внешнего ввода, нулевые дельты.
  /// </summary>
  public sealed class StaticContourAdapter : IContourAdapter
  {
    /// <summary>
    /// Создаёт статичный контур.
    /// </summary>
    /// <param name="contourId">Идентификатор контура для лога.</param>
    public StaticContourAdapter(string contourId)
    {
      ContourId = string.IsNullOrWhiteSpace(contourId) ? "static_mvp" : contourId.Trim();
    }

    /// <inheritdoc />
    public string ContourId { get; }

    /// <inheritdoc />
    public int LatencyPulses => 0;

    /// <inheritdoc />
    public IReadOnlyDictionary<int, float> GetNicheDeltasForPulse(int pulse)
    {
      return EmptyDeltas;
    }

    private static readonly Dictionary<int, float> EmptyDeltas = new Dictionary<int, float>();
  }
}
