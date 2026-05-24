using System.Collections.Generic;

namespace ISIDA.Niche
{
  /// <summary>
  /// Результат первичного AOE по каналу Niche (§5.3.2).
  /// </summary>
  public sealed class NicheAoeResult
  {
    /// <summary>ID автоматизма Creature.</summary>
    public int AutomatizmId { get; set; }

    /// <summary>Исход окна AOE.</summary>
    public AoeOutcome Outcome { get; set; }

    /// <summary>Оценка полезности (−1 / 0 / +1); значимо только для Success/Fail.</summary>
    public int Assessment { get; set; }

    /// <summary>Пульс открытия окна (выполнение automatizm).</summary>
    public int ActionPulse { get; set; }

    /// <summary>Пульс закрытия окна.</summary>
    public int ClosePulse { get; set; }
  }
}
