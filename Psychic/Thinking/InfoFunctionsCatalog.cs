using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic.Thinking
{
  /// <summary>
  /// Справочник инфо-функций, фиксированный в коде. В файл не сохраняется.
  /// Дополняется по мере надобности.
  /// </summary>
  public static class InfoFunctionsCatalog
  {
    /// <summary>Запись справочника инфо-функций</summary>
    public sealed class Entry
    {
      /// <summary>Идентификатор инфо-функции</summary>
      public int Id { get; }

      /// <summary>Название инфо-функции</summary>
      public string Name { get; }

      /// <summary>Описание инфо-функции</summary>
      public string Description { get; }

      /// <summary>Создаёт запись инфо-функции</summary>
      /// <param name="id">Идентификатор</param>
      /// <param name="name">Название</param>
      /// <param name="description">Описание</param>
      public Entry(int id, string name, string description)
      {
        Id = id;
        Name = name ?? "";
        Description = description ?? "";
      }
    }

    private static readonly IReadOnlyList<Entry> Catalog = new List<Entry>
    {
      new Entry(14, "Эпизодическое правило", "Поиск следующего действия по эпизодической памяти (правила/цепочки)"),
      new Entry(17, "Рекомендация по опыту", "Рекомендация действия по ранее записанному опыту циклов"),
      new Entry(25, "Случайный автоматизм", "Случайная проба моторного автоматизма из текущей ветки"),
      new Entry(31, "Запрос оператору", "Запрос помощи у оператора (попугайство)")
    };

    /// <summary>Получить все записи справочника для вывода на пульт</summary>
    public static IReadOnlyList<(int Id, string Name, string Description)> GetAll()
    {
      return Catalog.Select(e => (e.Id, e.Name, e.Description)).ToList();
    }

    /// <summary>Проверить, существует ли инфо-функция с указанным Id</summary>
    public static bool Exists(int id)
    {
      return Catalog.Any(e => e.Id == id);
    }

    /// <summary>Получить запись по Id</summary>
    public static Entry GetById(int id)
    {
      return Catalog.FirstOrDefault(e => e.Id == id);
    }

    /// <summary>Список всех Id инфо-функций (для перебора при отсутствии allowed-фильтра)</summary>
    public static IReadOnlyList<int> GetAllIds()
    {
      return Catalog.Select(e => e.Id).ToList();
    }
  }
}
