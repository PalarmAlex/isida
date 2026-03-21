using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Фиксированный справочник событий агента (Id, Name). В файл не сохраняется.
  /// Примечание по кодам 6–7: в <see cref="SituationImageSystem.ResolveSituationTypeId"/> стимул с пульта не возвращается
  /// отдельными типами 6 и 7 — внешний ввод идёт через контекст (MoodId, ActionIds) и слоты справочника 21–60.
  /// Коды 6 (и резерв 7) в каталоге — для привязки темы в слотах 1–20 и будущего расширения; при необходимости
  /// задайте в слоте события <see cref="SituationTypeRecord.EventAgentCode"/> = 6.
  /// </summary>
  public static class AgentEventsCatalog
  {
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
      new Entry(1, "Действие агента"),
      new Entry(2, "Автоматизм в ветке"),
      new Entry(3, "Нужно мышление"),
      new Entry(4, "Эксперимент"),
      new Entry(5, "Игнор оператора"),
      new Entry(6, "Стимул с пульта"),
      new Entry(7, "Игнор агента"),
      new Entry(8, ""),
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
  }
}
