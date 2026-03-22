using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ISIDA.Psychic.Automatism;
using ISIDA.Psychic.Thinking.Strategies;

namespace ISIDA.Psychic.Thinking
{
  /// <summary>
  /// Тексты для пользовательского лога циклов мышления (русский язык) и сборка сводок.
  /// </summary>
  internal static class ThinkingCycleLogMessages
  {
    /// <summary>Переводит внутренний код результата (DebugNote) в короткую русскую фразу.</summary>
    public static string TranslateDebugNote(string note)
    {
      if (string.IsNullOrWhiteSpace(note)) return "—";
      switch (note.Trim())
      {
        case "no_rule": return "нет подходящего эпизодического правила";
        case "no_recommendation": return "нет рекомендации по опыту циклов";
        case "no_candidates": return "нет подходящих моторных автоматизмов на ветке";
        case "not_urgent": return "нет срочности (опасность/актуальность) — запрос оператору не выполнялся";
        case "no_ctx": return "нет контекста цикла";
        case "no_episodic": return "эпизодическая память недоступна";
        case "no_trigger": return "нет стимула (образ действий)";
        case "no_memory": return "память опыта циклов недоступна";
        case "recommendation_equals_stimulus": return "рекомендация совпадает со стимулом — шаг пропущен";
        case "no_branch": return "нет узла ветки (UnresolvedNodeId)";
        case "picked_null": return "внутренняя ошибка выбора автоматизма";
        case "no_ie": return "нет текущей информационной среды";
        case "unknown_infoFunc": return "неизвестная инфо-функция";
        case "no_infoFunc_id": return "не задан идентификатор инфо-функции";
        case "waiting": return "ожидание с пульта";
        case "awaiting_evaluation": return "ожидание оценки полезности";
        case "return_to_interrupted": return "восстановление прерванного цикла";
        case "no_need": return "мышление не требуется";
        case "no_decision": return "решение не найдено";
        case "not_implemented": return "инфо-функция ещё не реализована";
        case "no_allowed_infoFuncs": return "для типа темы не задан список инфо-функций";
        case "dream_no_episodic": return "dreaming: нет эпизодической памяти";
        case "dream_no_history": return "dreaming: нет истории";
        case "dream_no_best": return "dreaming: не найден опорный кадр";
        case "dream_best_null": return "dreaming: узел не найден";
        case "dream_continue": return "dreaming: продолжить обход";
        default:
          if (note.StartsWith("insight_actionImg=", StringComparison.Ordinal))
          {
            var id = note.Substring("insight_actionImg=".Length);
            return $"инсайт: зафиксировать образ действий id={id}";
          }
          if (note.StartsWith("rule_actionImg=", StringComparison.Ordinal))
          {
            var id = note.Substring("rule_actionImg=".Length);
            return $"по правилу: образ действий id={id}";
          }
          if (note.StartsWith("recommend_actionImg=", StringComparison.Ordinal))
          {
            var id = note.Substring("recommend_actionImg=".Length);
            return $"рекомендация: образ действий id={id}";
          }
          if (note.StartsWith("random_atmz=", StringComparison.Ordinal))
            return "случайный автоматизм: " + note.Substring("random_atmz=".Length);
          if (note.StartsWith("existing_atmz_by_rule", StringComparison.Ordinal))
            return "найден существующий автоматизм по правилу: " + note;
          if (note.StartsWith("request_operator_help", StringComparison.Ordinal))
            return "запрос подсказки у оператора";
          return note;
      }
    }

    /// <summary>Строка-сигнатура для подавления повторов подряд (одинаковый исход перебора).</summary>
    public static string BuildInfoFuncBatchDigest(IReadOnlyList<(int FuncId, string DebugNote)> attempts)
    {
      if (attempts == null || attempts.Count == 0) return "batch:empty";
      return "batch:" + string.Join("|", attempts.Select(a => $"{a.FuncId}:{a.DebugNote ?? ""}"));
    }

    /// <summary>Одна строка на весь неуспешный перебор инфо-функций за пульс.</summary>
    public static string BuildInfoFuncBatchNoDecisionRu(IReadOnlyList<(int FuncId, string DebugNote)> attempts)
    {
      if (attempts == null || attempts.Count == 0)
        return "Инфо-функции не перебирались. Решение не найдено.";

      var sb = new StringBuilder();
      sb.Append("Перебор инфо-функций без исполнимого решения: ");
      for (int i = 0; i < attempts.Count; i++)
      {
        if (i > 0) sb.Append("; ");
        var e = attempts[i];
        var entry = InfoFunctionsCatalog.GetById(e.FuncId);
        var title = entry != null ? $"«{entry.Name}» (id={e.FuncId})" : $"id={e.FuncId}";
        sb.Append(title).Append(" — ").Append(TranslateDebugNote(e.DebugNote));
      }
      sb.Append(". Итог: решение не найдено.");
      return sb.ToString();
    }

    /// <summary>Успешный исход после вызова инфо-функции.</summary>
    public static string FormatInfoFuncSuccessRu(int infoFuncId, ThinkingDecision decision)
    {
      if (decision == null) return "Решение получено.";
      var entry = InfoFunctionsCatalog.GetById(infoFuncId);
      var title = entry != null ? $"«{entry.Name}» (id={infoFuncId})" : $"id={infoFuncId}";

      if (decision.RequestParrotFromOperator)
        return $"Инфо-функция {title}: запрос подсказки у оператора. {TranslateDebugNote(decision.DebugNote)}";

      if (decision.AutomatizmToExecute != null)
      {
        var a = decision.AutomatizmToExecute;
        return $"Инфо-функция {title}: выполнить автоматизм id={a.ID}, образ действий={a.ActionsImageID}. ({TranslateDebugNote(decision.DebugNote)})";
      }

      if (decision.ActionsImageIdToAutomatize > 0)
        return $"Инфо-функция {title}: зафиксировать образ действий id={decision.ActionsImageIdToAutomatize}. ({TranslateDebugNote(decision.DebugNote)})";

      return $"Инфо-функция {title}: {TranslateDebugNote(decision.DebugNote)}";
    }

    public static string FormatDreamingDecisionRu(ThinkingDecision decision)
    {
      if (decision == null) return "Решение в режиме dreaming.";
      return "Режим dreaming: " + TranslateDebugNote(decision.DebugNote);
    }
  }
}
