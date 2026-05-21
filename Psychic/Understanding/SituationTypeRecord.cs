namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Запись справочника типов ситуаций.
  /// Id 1–20: слоты событий (код события симбионта + ThemeTypeId). Id 21–40: настроение (MoodId + ThemeTypeId). Id 41–60: воздействие (InfluenceId + ThemeTypeId).
  /// </summary>
  public class SituationTypeRecord
  {
    /// <summary>Уникальный ID слота (не изменять после использования в SituationImage)</summary>
    public int Id { get; set; }

    /// <summary>Код настроения из ActionsImagesSystem. -1=отсутствие, 0=Нормальное, 1+ — другие.</summary>
    public int MoodId { get; set; }

    /// <summary>Код воздействия из InfluenceActionSystem. -1=отсутствие.</summary>
    public int InfluenceId { get; set; }

    /// <summary>ID типа темы (справочник тем). -1=не задано.</summary>
    public int ThemeTypeId { get; set; } = -1;

    /// <summary>Код события симбионта из AgentEventsCatalog для слотов Id 1–20. -1=не задано.</summary>
    public int EventAgentCode { get; set; } = -1;

    /// <summary>Текст для отладки / отображения без UI-таблицы</summary>
    public string BindingDisplayText =>
      Id >= 21 && Id <= 40
        ? $"MoodId={MoodId}"
        : (Id >= 41 && Id <= 60
          ? $"InfluenceId={InfluenceId}"
          : (ThemeTypeId > 0 && ThemeImageSystem.IsInitialized
            ? ThemeImageSystem.Instance.GetThemeTypeDescription(ThemeTypeId)
            : (ThemeTypeId > 0 ? ThemeTypeId.ToString() : "—")));

    /// <summary>Слот события с привязкой темы (Id 1–20)</summary>
    public bool IsEventSlotWithTheme => Id >= 1 && Id <= 20;
  }
}
