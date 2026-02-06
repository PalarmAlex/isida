using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Common
{
  /// <summary>
  /// Утилиты для работы со списками и коллекциями
  /// </summary>
  public static class AddUtils
  {
    /// <summary>
    /// Парсит строку со списком целых чисел
    /// </summary>
    /// <param name="listStr">Строка со списком чисел (формат: "1,2,3,4")</param>
    /// <returns>Список целых чисел</returns>
    public static List<int> ParseIntList(string listStr)
    {
      if (string.IsNullOrWhiteSpace(listStr))
        return new List<int>();

      return listStr.Split(',', (char)StringSplitOptions.RemoveEmptyEntries)
          .Select(s => int.TryParse(s.Trim(), out int result) ? result : 0)
          .ToList();
    }

    /// <summary>
    /// Преобразует список целых чисел в строку
    /// </summary>
    /// <param name="list">Список целых чисел</param>
    /// <returns>Строка в формате "1,2,3,4" или пустая строка</returns>
    public static string IntListToString(List<int> list)
    {
      return list != null && list.Count > 0 ? string.Join(",", list) : string.Empty;
    }

    /// <summary>
    /// Парсит строку со списком чисел с плавающей точкой
    /// </summary>
    /// <param name="listStr">Строка со списком чисел (формат: "1.5,2.3,3.7")</param>
    /// <returns>Список чисел с плавающей точкой</returns>
    public static List<double> ParseDoubleList(string listStr)
    {
      if (string.IsNullOrWhiteSpace(listStr))
        return new List<double>();

      return listStr.Split(',', (char)StringSplitOptions.RemoveEmptyEntries)
          .Select(s => double.TryParse(s.Trim(), out double result) ? result : 0.0)
          .ToList();
    }

    /// <summary>
    /// Преобразует список чисел с плавающей точкой в строку
    /// </summary>
    /// <param name="list">Список чисел с плавающей точкой</param>
    /// <returns>Строка в формате "1.5,2.3,3.7"</returns>
    public static string DoubleListToString(List<double> list)
    {
      return list != null && list.Count > 0 ? string.Join(",", list) : string.Empty;
    }

    /// <summary>
    /// Сравнивает два списка на равенство (порядок не важен)
    /// </summary>
    /// <param name="list1">Первый список</param>
    /// <param name="list2">Второй список</param>
    /// <returns>True если списки содержат одинаковые элементы</returns>
    public static bool AreListsEqual(List<int> list1, List<int> list2)
    {
      if (list1 == null && list2 == null)
        return true;

      if (list1 == null && list2 != null && list2.Count == 0)
        return true;
      if (list2 == null && list1 != null && list1.Count == 0)
        return true;

      if (list1 == null || list2 == null)
        return false;

      if (list1.Count != list2.Count)
        return false;

      return list1.OrderBy(x => x).SequenceEqual(list2.OrderBy(x => x));
    }

    /// <summary>
    /// Сравнение float с погрешностью epsilon (по умолчанию 1E-04).
    /// <para>Возвращает True, если a ≤ b с учетом погрешности</para>
    /// </summary>
    public static bool FloatLessOrEqual(float a, float b, float tolerance = 0.0001f)
    {
      return a < b || Math.Abs(a - b) <= tolerance;
    }
  }
}
