using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic.Thinking
{
  /// <summary>
  /// Буфер текущей цепочки вызовов инфо-функций (аналог <c>infoFuncSequence</c> в BOT).
  /// Очищается при смене цели (эквивалент infoFunc8), после успешной записи в ментальную эпизодику и при явном сбросе опыта циклов.
  /// </summary>
  public sealed class MentalAutomatizmSession
  {
    private readonly object _sync = new object();
    private readonly List<int> _executed = new List<int>();

    /// <summary>
    /// Очищает буфер выполненных инфо-функций.
    /// </summary>
    public void Clear()
    {
      lock (_sync)
      {
        _executed.Clear();
      }
    }

    /// <summary>
    /// Добавляет идентификатор инфо-функции в цепочку, если он не относится к «шагам цели» (14, 17, 26 в BOT).
    /// </summary>
    /// <param name="infoFuncId">Идентификатор инфо-функции из справочника.</param>
    public void RecordIfApplicable(int infoFuncId)
    {
      if (infoFuncId <= 0) return;
      // Ментальная цель (ИФ 8): сбрасывает буфер в Execute — не добавлять как шаг цепочки.
      if (infoFuncId == 8) return;
      if (IsPurposeActionInfoFunc(infoFuncId)) return;
      lock (_sync)
      {
        _executed.Add(infoFuncId);
      }
    }

    /// <summary>
    /// Возвращает копию текущей цепочки для сопоставления префикса с сохранёнными эпизодами.
    /// </summary>
    /// <returns>Снимок списка идентификаторов инфо-функций.</returns>
    public IReadOnlyList<int> GetExecutedSnapshot()
    {
      lock (_sync)
      {
        return _executed.ToList();
      }
    }

    /// <summary>
    /// Строка цепочки для отладки и UI (номера через запятую).
    /// </summary>
    /// <returns>Текст или пустая строка.</returns>
    public string FormatTraceLine()
    {
      lock (_sync)
      {
        return _executed.Count == 0 ? string.Empty : string.Join(",", _executed);
      }
    }

    /// <summary>
    /// Определяет, относится ли инфо-функция к шагам, после которых в BOT не добавляют вызов в последовательность (цель / запуск автоматизма по смыслу).
    /// </summary>
    /// <param name="infoFuncId">Идентификатор инфо-функции.</param>
    /// <returns>True, если запись в буфер нужно пропустить.</returns>
    public static bool IsPurposeActionInfoFunc(int infoFuncId)
    {
      return infoFuncId == 14 || infoFuncId == 17 || infoFuncId == 26;
    }
  }
}
