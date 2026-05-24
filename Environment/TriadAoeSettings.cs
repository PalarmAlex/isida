namespace ISIDA.Niche
{
  /// <summary>
  /// Параметры окна AOE по отклику Niche (§5.3).
  /// </summary>
  public sealed class TriadAoeSettings
  {
    /// <summary>Число тактов для скользящего baseline спонтанной дельты Niche.</summary>
    public int BaselineWindowN { get; set; } = 20;

    /// <summary>Порог |niche_response_delta| поверх baseline.</summary>
    public float ResponseThreshold { get; set; } = 0.5f;

    /// <summary>Горизонт K пульсов после действия для поиска отклика.</summary>
    public int CorrelationHorizonK { get; set; } = 3;

    /// <summary>Длина окна оценки W_eval в пульсах.</summary>
    public int EvalWindowPulses { get; set; } = 30;
  }
}
