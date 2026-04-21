using System;

namespace ISIDA.Research
{
  /// <summary>Тип ожидаемого значения в последних колонках строки pipe.</summary>
  public enum HarnessValueKind
  {
    /// <summary>Ожидание 0/1 (false/true).</summary>
    Boolean,
    /// <summary>Целое ожидание.</summary>
    Int32,
    /// <summary>Только −1, 0 или +1 (оценка оператора).</summary>
    TrinaryInt,
    /// <summary>Вещественное ожидание (сравнение с допуском).</summary>
    Float
  }

  /// <summary>Описание одного выхода метода (ожидание сравнивается с фактом калькулятора).</summary>
  public sealed class ResearchHarnessResultColumn
  {
    /// <summary>Создаёт описание выходной колонки pipe-прогона.</summary>
    /// <param name="label">Подпись столбца (отчёт, подсказки).</param>
    /// <param name="kind">Тип значения для разбора ожидания и сравнения с фактом.</param>
    public ResearchHarnessResultColumn(string label, HarnessValueKind kind)
    {
      Label = label ?? throw new ArgumentNullException(nameof(label));
      Kind = kind;
    }

    /// <summary>Подпись выходной колонки.</summary>
    public string Label { get; }

    /// <summary>Тип ожидаемого значения в этой колонке.</summary>
    public HarnessValueKind Kind { get; }
  }
}
