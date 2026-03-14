namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Образ темы мышления
  /// </summary>
  /// <remarks>
  /// Type соответствует ThemeTypeStr (0=Нет темы, 17=Улучшение настроения и т.д.)
  /// </remarks>
  public class ThemeImageRecord
  {
    /// <summary>ID образа</summary>
    public int Id { get; set; }

    /// <summary>Вес значимости (1–10)</summary>
    public int Weight { get; set; }

    /// <summary>Тип темы (ThemeTypeStr index)</summary>
    public int Type { get; set; }

    /// <summary>Время актуализации (PulsCount)</summary>
    public int PulsCount { get; set; }
  }
}
