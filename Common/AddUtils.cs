using System;
using System.Collections.Generic;
using System.Globalization;
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

      return listStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
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

      return listStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
          .Select(s => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : 0.0)
          .ToList();
    }

    /// <summary>
    /// Преобразует список чисел с плавающей точкой в строку
    /// </summary>
    /// <param name="list">Список чисел с плавающей точкой</param>
    /// <returns>Строка в формате "1.5,2.3,3.7"</returns>
    public static string DoubleListToString(List<double> list)
    {
      return list != null && list.Count > 0
          ? string.Join(",", list.Select(d => d.ToString(CultureInfo.InvariantCulture)))
          : string.Empty;
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

    /// <summary>
    /// Приводит целое число к заданному диапазону [min, max].
    /// </summary>
    public static int Clamp(int value, int min, int max)
    {
      return Math.Max(min, Math.Min(max, value));
    }

    /// <summary>
    /// Приводит число с плавающей точкой к заданному диапазону [min, max].
    /// </summary>
    public static float Clamp(float value, float min, float max)
    {
      return Math.Max(min, Math.Min(max, value));
    }

    /// <summary>
    /// Сравнивает два словаря строк на равенство (порядок ключей не важен, ключи регистронезависимы).
    /// <para>null и пустой словарь считаются равными.</para>
    /// </summary>
    public static bool DictEquals(Dictionary<string, string> d1, Dictionary<string, string> d2)
    {
      bool empty1 = d1 == null || d1.Count == 0;
      bool empty2 = d2 == null || d2.Count == 0;
      if (empty1 && empty2)
        return true;
      if (empty1 || empty2)
        return false;
      if (d1.Count != d2.Count)
        return false;

      foreach (var kvp in d1)
      {
        if (!d2.TryGetValue(kvp.Key, out string other))
          return false;
        if (!string.Equals(kvp.Value, other, StringComparison.Ordinal))
          return false;
      }
      return true;
    }

    /// <summary>Разделитель пар «ключ=значение» внутри одного столбца параметров.</summary>
    private const char ParamsPairSeparator = ';';
    /// <summary>Разделитель ключа и значения внутри пары.</summary>
    private const char ParamsKeyValueSeparator = '=';

    /// <summary>
    /// Сериализует словарь параметров в строку столбца формата <c>key1=value1;key2=value2</c>.
    /// Ключи сортируются регистронезависимо для детерминированной записи. Пустой/null → пустая строка.
    /// </summary>
    public static string ParamsToString(Dictionary<string, string> @params)
    {
      if (@params == null || @params.Count == 0)
        return string.Empty;

      return string.Join(
          ParamsPairSeparator.ToString(),
          @params
              .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
              .Select(kv => kv.Key + ParamsKeyValueSeparator + (kv.Value ?? string.Empty)));
    }

    /// <summary>
    /// Разбирает строку столбца параметров формата <c>key1=value1;key2=value2</c> в словарь
    /// (регистронезависимые ключи). Пустая/null строка → пустой словарь. Записи без ключа пропускаются.
    /// Значение может содержать «=» (разделяется по первому вхождению).
    /// </summary>
    public static Dictionary<string, string> ParseParams(string paramsStr)
    {
      var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      if (string.IsNullOrWhiteSpace(paramsStr))
        return result;

      foreach (var pair in paramsStr.Split(new[] { ParamsPairSeparator }, StringSplitOptions.RemoveEmptyEntries))
      {
        var trimmed = pair.Trim();
        if (trimmed.Length == 0)
          continue;

        int eq = trimmed.IndexOf(ParamsKeyValueSeparator);
        if (eq <= 0)
          continue; // нет ключа — пропускаем

        string key = trimmed.Substring(0, eq).Trim();
        if (key.Length == 0)
          continue;

        string value = trimmed.Substring(eq + 1);
        result[key] = value;
      }
      return result;
    }
  }
}
