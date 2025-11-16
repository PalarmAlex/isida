using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Common
{
  /// <summary>
  /// Предоставляет статические методы для валидации антагонистических конфликтов между элементами.
  /// Используется для проверки невозможности одновременного выбора взаимоисключающих элементов.
  /// </summary>
  /// <remarks>
  /// Пример использования: проверка конфликтов между внешними воздействиями, адаптивными действиями 
  /// или стилями поведения, которые не могут быть активированы одновременно.
  /// </remarks>
  public static class AntagonistValidator
  {
    /// <summary>
    /// Проверяет наличие антагонистических конфликтов в списке идентификаторов.
    /// </summary>
    /// <param name="selectedIds">Коллекция выбранных идентификаторов для проверки на конфликты.</param>
    /// <param name="getAntagonistsFunc">Функция-обработчик, возвращающая список идентификаторов-антагонистов для заданного идентификатора.</param>
    /// <returns>Список объектов <see cref="AntagonistConflict"/> с описанием найденных конфликтов. Пустой список если конфликтов нет.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="getAntagonistsFunc"/> равен null.</exception>
    /// <example>
    /// <code>
    /// var conflicts = AntagonistValidator.ValidateAntagonists(selectedIds, id => GetAntagonistsForId(id));
    /// if (conflicts.Any()) { /* обработка конфликтов */ }
    /// </code>
    /// </example>
    public static List<AntagonistConflict> ValidateAntagonists(
        IEnumerable<int> selectedIds,
        Func<int, IEnumerable<int>> getAntagonistsFunc)
    {
      var conflicts = new List<AntagonistConflict>();
      var selectedList = selectedIds?.ToList() ?? new List<int>();

      if (!selectedList.Any()) return conflicts;

      // Проверяем каждую пару выбранных элементов
      for (int i = 0; i < selectedList.Count; i++)
      {
        for (int j = i + 1; j < selectedList.Count; j++)
        {
          int id1 = selectedList[i];
          int id2 = selectedList[j];

          var antagonists1 = getAntagonistsFunc(id1)?.ToList() ?? new List<int>();
          var antagonists2 = getAntagonistsFunc(id2)?.ToList() ?? new List<int>();

          if (antagonists1.Contains(id2) || antagonists2.Contains(id1))
          {
            conflicts.Add(new AntagonistConflict(id1, id2));
          }
        }
      }

      return conflicts;
    }

    /// <summary>
    /// Проверяет наличие антагонистических конфликтов с использованием предзагруженного словаря антагонистов.
    /// </summary>
    /// <param name="selectedIds">Коллекция выбранных идентификаторов для проверки на конфликты.</param>
    /// <param name="antagonistsMap">Словарь, где ключ - идентификатор элемента, значение - список его антагонистов.</param>
    /// <returns>Список объектов <see cref="AntagonistConflict"/> с описанием найденных конфликтов. Пустой список если конфликтов нет.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="antagonistsMap"/> равен null.</exception>
    /// <example>
    /// <code>
    /// var antagonistsMap = new Dictionary&lt;int, List&lt;int&gt;&gt; { { 1, new List&lt;int&gt; { 2, 3 } } };
    /// var conflicts = AntagonistValidator.ValidateAntagonists(selectedIds, antagonistsMap);
    /// </code>
    /// </example>
    public static List<AntagonistConflict> ValidateAntagonists(
        IEnumerable<int> selectedIds,
        Dictionary<int, List<int>> antagonistsMap)
    {
      return ValidateAntagonists(selectedIds, id =>
          antagonistsMap.ContainsKey(id) ? antagonistsMap[id] : new List<int>());
    }
  }

  /// <summary>
  /// Представляет информацию о конфликте между двумя антагонистическими элементами.
  /// </summary>
  /// <remarks>
  /// Используется для детального описания конфликтующих пар элементов при валидации.
  /// </remarks>
  public class AntagonistConflict
  {
    /// <summary>
    /// Получает идентификатор первого элемента в конфликтующей паре.
    /// </summary>
    /// <value>Целочисленный идентификатор первого элемента.</value>
    public int FirstId { get; }

    /// <summary>
    /// Получает идентификатор второго элемента в конфликтующей паре.
    /// </summary>
    /// <value>Целочисленный идентификатор второго элемента.</value>
    public int SecondId { get; }

    /// <summary>
    /// Получает текстовое описание конфликта между элементами.
    /// </summary>
    /// <value>Строка формата "Конфликт между ID {FirstId} и {SecondId}".</value>
    public string Message => $"Конфликт между ID {FirstId} и {SecondId}";

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="AntagonistConflict"/> с указанными идентификаторами конфликтующих элементов.
    /// </summary>
    /// <param name="firstId">Идентификатор первого элемента в конфликте.</param>
    /// <param name="secondId">Идентификатор второго элемента в конфликте.</param>
    public AntagonistConflict(int firstId, int secondId)
    {
      FirstId = firstId;
      SecondId = secondId;
    }
  }
}