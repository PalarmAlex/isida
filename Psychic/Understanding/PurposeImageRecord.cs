namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Образ цели мышления (мотивирующая потребность)
  /// </summary>
  public class PurposeImageRecord
  {
    /// <summary>ID образа</summary>
    public int Id { get; set; }

    /// <summary>Цель: 1=повторение, 2=улучшение</summary>
    public int Target { get; set; }

    /// <summary>Базовое состояние (MoodId)</summary>
    public int MoodId { get; set; }

    /// <summary>ID эмоции</summary>
    public int EmotionId { get; set; }

    /// <summary>ID образа ситуации</summary>
    public int SituationId { get; set; }
  }
}
