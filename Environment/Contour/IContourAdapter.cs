using System.Collections.Generic;

namespace ISIDA.Niche.Contour
{
  /// <summary>
  /// Адаптер контура: внешний мир → параметры Niche (§6.8).
  /// </summary>
  public interface IContourAdapter
  {
    /// <summary>Идентификатор контура.</summary>
    string ContourId { get; }

    /// <summary>Задержка отклика в пульсах.</summary>
    int LatencyPulses { get; }

    /// <summary>
    /// Возвращает дельты параметров Niche для текущего пульса.
    /// </summary>
    /// <param name="pulse">Глобальный номер пульса.</param>
    /// <returns>nicheParamId → delta; пустой словарь если нет изменений.</returns>
    IReadOnlyDictionary<int, float> GetNicheDeltasForPulse(int pulse);
  }
}
