using System.Collections.Generic;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Запись справочника типов ситуаций. Связь: Id типа — MoodId (настроение) или InfluenceId (воздействие) или ThemeTypeId (тема для Id 6–10, 41–60).
  /// Id 1–5 — обязательные фиксированные типы. Id 6–10 — дефолтные привязки тем (движок). Id 11–20 — настроение. Id 21–40 — воздействия. Id 41–60 — привязки тем для инфо-функций.
  /// </summary>
  public class SituationTypeRecord
  {
    /// <summary>Уникальный ID (не изменять после использования в SituationImage)</summary>
    public int Id { get; set; }

    /// <summary>Код настроения из ActionsImagesSystem. -1=отсутствие, 0=Нормальное, 1+ — другие.</summary>
    public int MoodId { get; set; }

    /// <summary>Код воздействия из InfluenceActionSystem. -1=отсутствие.</summary>
    public int InfluenceId { get; set; }

    /// <summary>ID типа темы (справочник тем). -1=не задано; для Id 6–10 и 41–60 — привязка к теме.</summary>
    public int ThemeTypeId { get; set; } = -1;

    /// <summary>
    /// Разрешенные ID инфо-функций/стратегий (int).
    /// Пустой список = ограничений нет по этой привязке (движок может выбирать из других привязок/дефолтов).
    /// </summary>
    public Dictionary<int, int> AllowedInfoFuncIds { get; set; } = new Dictionary<int, int>();

    /// <summary>Текст для колонки «Привязка» в UI: для Id 1–5 — MoodId/InfluenceId, для 6–10 — название темы или «—».</summary>
    public string BindingDisplayText =>
      Id <= 5
        ? $"MoodId={MoodId}, InfluenceId={InfluenceId}"
        : (ThemeTypeId > 0 && ThemeImageSystem.IsInitialized
          ? ThemeImageSystem.Instance.GetThemeTypeDescription(ThemeTypeId)
          : (ThemeTypeId > 0 ? ThemeTypeId.ToString() : "—"));

    /// <summary>Слот дефолтной привязки темы (Id 6–10): в UI показывается ComboBox выбора темы.</summary>
    public bool IsDefaultThemeSlot => Id >= 6 && Id <= 10;
  }
}
