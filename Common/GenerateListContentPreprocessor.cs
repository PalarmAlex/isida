using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Common
{
  /// <summary>
  /// Предобработка содержимого файлов генерации (automatizm_generate_list.csv, conditioned_reflex_generate_list.txt,
  /// genetic_reflex_generate_list.txt, primitives_generate_list.txt) перед парсингом.
  /// Удаляет скрытые символы (BOM, zero-width и т.п.) и делает Trim.
  /// </summary>
  public static class GenerateListContentPreprocessor
  {
    private static readonly HashSet<char> HiddenCharsToRemove = new HashSet<char>
    {
      '\uFEFF', // BOM
      '\u200B', '\u200C', '\u200D', '\u200E', '\u200F', '\u2060', // zero-width
      '\u00A0', // non-breaking space
      '\u2028', '\u2029'  // line/paragraph separators
    };

    /// <summary>
    /// Удаляет скрытые символы (BOM, zero-width и т.п.) из всего текста и выполняет Trim.
    /// Вызывать перед парсингом содержимого файлов генерации.
    /// </summary>
    /// <param name="content">Сырое содержимое файла (например, после File.ReadAllText).</param>
    /// <returns>Очищенный текст, готовый к разбиению на строки и парсингу. Null если вход null.</returns>
    public static string Preprocess(string content)
    {
      if (content == null)
        return null;
      if (content.Length == 0)
        return string.Empty;
      var normalized = new string(content.Where(c => !HiddenCharsToRemove.Contains(c)).ToArray());
      return normalized.Trim();
    }
  }
}
