using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ISIDA.Common
{
  /// <summary>Форматирует длинные описания для многострочных подсказок WPF.</summary>
  public static class TooltipMultilineText
  {
    private static readonly Regex EnvironmentMetricHeaderRegex =
        new Regex(@"^\d+:[+\-]?\d+ — ", RegexOptions.CultureInvariant);

    /// <summary>Разбивает длинное описание метрики на строки (по «; »).</summary>
    public static string Format(string text)
    {
      if (string.IsNullOrWhiteSpace(text))
        return string.Empty;

      text = CollapseWhitespace(text);
      if (text.IndexOf("; ", StringComparison.Ordinal) < 0)
        return text;

      var parts = text.Split(new[] { "; " }, StringSplitOptions.None);
      return string.Join(Environment.NewLine,
          parts.Select(p => p.Trim()).Where(p => p.Length > 0));
    }

    /// <summary>Форматирует подсказку колонки «Среда» (несколько метрик с описаниями).</summary>
    public static string FormatEnvironmentPressureTooltip(string text)
    {
      if (string.IsNullOrWhiteSpace(text))
        return string.Empty;

      var lines = text.Replace("\r\n", "\n").Split('\n');
      var sb = new StringBuilder();
      var descriptionLines = new List<string>();

      void FlushDescription()
      {
        if (descriptionLines.Count == 0)
          return;
        if (descriptionLines.Count == 1 && !LooksLikeEnvironmentMetricDescription(descriptionLines[0]))
        {
          if (sb.Length > 0)
            sb.AppendLine();
          sb.Append(descriptionLines[0]);
        }
        else
        {
          sb.AppendLine();
          if (descriptionLines.Count == 1)
            sb.Append(Format(descriptionLines[0]));
          else
            sb.Append(string.Join(Environment.NewLine, descriptionLines));
        }
        descriptionLines.Clear();
      }

      foreach (string rawLine in lines)
      {
        string line = rawLine.Trim();
        if (line.Length == 0)
        {
          FlushDescription();
          if (sb.Length > 0 && sb[sb.Length - 1] != '\n')
            sb.AppendLine();
          continue;
        }

        if (EnvironmentMetricHeaderRegex.IsMatch(line) || EnvironmentMetricMagnitudeLineRegex.IsMatch(line))
        {
          FlushDescription();
          if (sb.Length > 0)
            sb.AppendLine();
          sb.Append(line);
          continue;
        }

        descriptionLines.Add(line);
      }

      FlushDescription();
      return sb.ToString().TrimEnd();
    }

    /// <summary>Блок подсказки для одной метрики среды: имя, id:величина, описание.</summary>
    public static string FormatEnvironmentMetricBlock(int actionId, string signedMagnitudeText, string name, string description)
    {
      var sb = new StringBuilder();
      string displayName = string.IsNullOrWhiteSpace(name) ? $"id={actionId}" : name.Trim();
      sb.AppendLine(displayName);
      sb.Append(actionId.ToString(CultureInfo.InvariantCulture));
      sb.Append(':');
      sb.Append(signedMagnitudeText);
      if (!string.IsNullOrWhiteSpace(description))
      {
        sb.AppendLine();
        sb.Append(Format(description));
      }
      return sb.ToString().TrimEnd();
    }

    private static readonly Regex EnvironmentMetricMagnitudeLineRegex =
        new Regex(@"^\d+:[+\-]?\d+$", RegexOptions.CultureInvariant);

    private static bool LooksLikeEnvironmentMetricDescription(string line)
    {
      if (string.IsNullOrWhiteSpace(line))
        return false;
      line = line.Trim();
      if (line.IndexOf("; ", StringComparison.Ordinal) >= 0)
        return true;
      if (line.StartsWith("Деталь:", StringComparison.Ordinal) ||
          line.StartsWith("Сборка:", StringComparison.Ordinal) ||
          line.StartsWith("Один критерий", StringComparison.Ordinal))
        return true;
      return line.Length > 96;
    }

    private static string CollapseWhitespace(string text)
    {
      if (string.IsNullOrWhiteSpace(text))
        return string.Empty;
      text = text.Trim().Replace("\r\n", " ").Replace('\n', ' ');
      while (text.Contains("  "))
        text = text.Replace("  ", " ");
      return text;
    }
  }
}
