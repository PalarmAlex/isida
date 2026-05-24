using System.Collections.Generic;

namespace ISIDA.Niche
{
  /// <summary>Срез состояния гомеостаза Niche для сопоставления рефлексов.</summary>
  public sealed class NicheHomeostasisSlice
  {
    /// <summary>Интегральное базовое состояние (Level1).</summary>
    public int BaseStateId { get; set; }

    /// <summary>Активные стили поведения (Level2).</summary>
    public List<int> ActiveStyleIds { get; set; } = new List<int>();
  }
}
