using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Фиксированный справочник событий агента (Id, Name). В файл не сохраняется.
  /// Числовые коды в логике резолва образа ситуации и триггеров темы должны совпадать с <see cref="Codes"/>.
  /// Ввод с пульта (настроение, воздействия) в движке идёт через слоты 21–60, а не через отдельные коды 6–7 резолвера.
  /// </summary>
  public static class AgentEventsCatalog
  {
    /// <summary>Коды событий агента (Id в справочнике). Использовать вместо «магических» чисел.</summary>
    public static class Codes
    {
      /// <summary>Действие агента</summary>
      public const int ResponseAction = 1;
      /// <summary>Автоматизм в ветке</summary>
      public const int AutomatizmInBranch = 2;
      /// <summary>Нужно мышление</summary>
      public const int NeedThinking = 3;
      /// <summary>Эксперимент</summary>
      public const int Experiment = 4;
      /// <summary>Игнор оператора</summary>
      public const int OperatorIgnore = 5;
      /// <summary>Стимул с пульта (привязка темы в слотах событий)</summary>
      public const int PultStimulus = 6;
      /// <summary>Игнор агента</summary>
      public const int AgentIgnore = 7;
      /// <summary>Высокая значимость объекта</summary>
      public const int HighObjectImportance = 8;
    }

    /// <summary>Запись справочника событий</summary>
    public sealed class Entry
    {
      /// <summary>Идентификатор события</summary>
      public int Id { get; }

      /// <summary>Название события</summary>
      public string Name { get; }

      /// <summary>Создаёт запись события</summary>
      public Entry(int id, string name)
      {
        Id = id;
        Name = name ?? "";
      }
    }

    private static readonly IReadOnlyList<Entry> Catalog = new List<Entry>
    {
      new Entry(Codes.ResponseAction, "Действие агента"),
      new Entry(Codes.AutomatizmInBranch, "Автоматизм в ветке"),
      new Entry(Codes.NeedThinking, "Нужно мышление"),
      new Entry(Codes.Experiment, "Эксперимент"),
      new Entry(Codes.OperatorIgnore, "Игнор оператора"),
      new Entry(Codes.PultStimulus, "Стимул с пульта"),
      new Entry(Codes.AgentIgnore, "Игнор агента"),
      new Entry(Codes.HighObjectImportance, "Высокая значимость объекта"),
      new Entry(9, ""),
      new Entry(10, "")
    };

    /// <summary>События для вывода на пульт (Id, Name). Записи с пустым Name — резерв, не показываются.</summary>
    public static IReadOnlyList<(int Id, string Name)> GetAllForPulpit()
    {
      return Catalog.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => (e.Id, e.Name)).ToList();
    }

    /// <summary>Проверить, существует ли событие с указанным Id</summary>
    public static bool Exists(int id)
    {
      return Catalog.Any(e => e.Id == id);
    }

    /// <summary>Получить название события по Id</summary>
    public static string GetName(int id)
    {
      var e = Catalog.FirstOrDefault(x => x.Id == id);
      return e?.Name ?? id.ToString();
    }

    #region Рекомендуемые привязки тем (только в памяти, не сохраняются)

    /// <summary>
    /// Рекомендуемые пары (код события, ThemeTypeId) для слотов событий 1–8.
    /// ThemeTypeId — из дефолтного списка <see cref="ThemeImageSystem"/> (theme_types.dat при первой инициализации).
    /// </summary>
    public static IReadOnlyList<(int EventAgentCode, int ThemeTypeId)> GetDefaultEventCodeThemeBindings() =>
      DefaultEventCodeThemePairs;

    private static readonly (int EventAgentCode, int ThemeTypeId)[] DefaultEventCodeThemePairs =
    {
      (Codes.ResponseAction, 5),
      (Codes.AutomatizmInBranch, 12),
      (Codes.NeedThinking, 10),
      (Codes.Experiment, 8),
      (Codes.OperatorIgnore, 7),
      (Codes.PultStimulus, 4),
      (Codes.AgentIgnore, 1),
      (Codes.HighObjectImportance, 16)
    };

    /// <summary>
    /// Рекомендуемые пары (MoodId, ThemeTypeId) для слотов настроения 21–28 (по одному слоту на каждое настроение 0–7 из справочника настроений образов действий).
    /// Слоты 29–40 в дефолте пустые (настроение не задано).
    /// </summary>
    public static IReadOnlyList<(int MoodId, int ThemeTypeId)> GetDefaultMoodThemeBindings() =>
      DefaultMoodThemePairs;

    /// <remarks>MoodId 0–7 соответствуют статическому справочнику настроений в ActionsImagesSystem.</remarks>
    private static readonly (int MoodId, int ThemeTypeId)[] DefaultMoodThemePairs =
    {
      (0, 17),
      (1, 5),
      (2, 3),
      (3, 8),
      (4, 6),
      (5, 15),
      (6, 13),
      (7, 9)
    };

    /// <summary>
    /// Подставить рекомендуемые привязки в слоты событий (1–20) и настроения (21–40) в переданных коллекциях.
    /// События: слоты 1–8 — код события и тема из <see cref="GetDefaultEventCodeThemeBindings"/>; 9–20 — очистка (EventAgentCode и ThemeTypeId «пустые»).
    /// Настроение: слоты 21–28 — MoodId 0..7 и темы из <see cref="GetDefaultMoodThemeBindings"/>; 29–40 — очистка.
    /// Воздействия (41–60) не трогает. Не записывает файл — только поля объектов <see cref="SituationTypeRecord"/>.
    /// </summary>
    public static void ApplyDefaultSituationSlotBindings(
        IList<SituationTypeRecord> eventSlots1to20,
        IList<SituationTypeRecord> moodSlots21to40)
    {
      int empty = SituationTypeSystem.EmptySlotValue;

      if (eventSlots1to20 != null)
      {
        foreach (var r in eventSlots1to20)
        {
          if (r == null || r.Id < 1 || r.Id > 20) continue;
          r.MoodId = empty;
          r.InfluenceId = empty;
          if (r.Id <= 8)
          {
            var p = DefaultEventCodeThemePairs[r.Id - 1];
            r.EventAgentCode = p.EventAgentCode;
            r.ThemeTypeId = p.ThemeTypeId;
          }
          else
          {
            r.EventAgentCode = -1;
            r.ThemeTypeId = -1;
          }
        }
      }

      if (moodSlots21to40 != null)
      {
        foreach (var r in moodSlots21to40)
        {
          if (r == null || r.Id < 21 || r.Id > 40) continue;
          r.EventAgentCode = -1;
          r.InfluenceId = empty;
          int idx = r.Id - 21;
          if (idx < DefaultMoodThemePairs.Length)
          {
            var p = DefaultMoodThemePairs[idx];
            r.MoodId = p.MoodId;
            r.ThemeTypeId = p.ThemeTypeId;
          }
          else
          {
            r.MoodId = empty;
            r.ThemeTypeId = -1;
          }
        }
      }
    }

    #endregion
  }
}
