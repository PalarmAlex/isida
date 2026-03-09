namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Запись справочника типов ситуаций (SituationType).
  /// Редактируется на пульте. Id неизменяем после создания.
  /// </summary>
  public class SituationTypeRecord
  {
    /// <summary>Уникальный ID (не изменять после использования)</summary>
    public int Id { get; set; }

    /// <summary>Отображаемое название</summary>
    public string Name { get; set; }

    /// <summary>Символьный код для логики (например, ResponseAction, Experiment)</summary>
    public string Code { get; set; }
  }
}
