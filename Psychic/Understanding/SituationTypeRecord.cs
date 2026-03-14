namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Запись справочника типов ситуаций. Связь: Id типа — MoodId (настроение) или InfluenceId (воздействие).
  /// Id 1–5 — обязательные фиксированные типы. Id 11–20 — настроение (ActionsImagesSystem._moodDictionary).
  /// Id 21+ — воздействия (InfluenceActionSystem).
  /// </summary>
  public class SituationTypeRecord
  {
    /// <summary>Уникальный ID (не изменять после использования в SituationImage)</summary>
    public int Id { get; set; }

    /// <summary>Код настроения из ActionsImagesSystem (0 для типов 1–5 и для записей по воздействию)</summary>
    public int MoodId { get; set; }

    /// <summary>Код воздействия из InfluenceActionSystem (0 для типов 1–5 и для записей по настроению)</summary>
    public int InfluenceId { get; set; }

    /// <summary>Описание типа</summary>
    public string Description { get; set; }
  }
}
