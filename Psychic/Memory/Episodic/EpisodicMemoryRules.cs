using System;
using System.Collections.Generic;

namespace ISIDA.Psychic.Memory.Episodic
{
  /// <summary>
  /// Поиск и использование правил из эпизодической памяти
  /// </summary>
  public static class EpisodicMemoryRules
  {
    /// <summary>Масштаб веса учителя относительно прямого правила (оценка с пульта — в Importence)</summary>
    public const int TeacherUtilityScaleK = 10;

    /// <summary>Вес эффекта по Count</summary>
    public static int GetWpower(int effect, int count)
    {
      if (count < 3) return effect;
      if (count < 6) return effect * 2;
      return effect * 3;
    }

    /// <summary>Подписанная валентность: для учителя — оценка (Importence), иначе Effect</summary>
    public static int SignedValence(EpisodicRule r)
    {
      if (r == null) return 0;
      return r.IsTeacher ? r.Importence : r.Effect;
    }

    /// <summary>Единая полезность правила: прямое — GetWpower(Effect); учительское — k·sign·GetWpower(|оценка|, Count)</summary>
    public static int RuleUtility(EpisodicRule r)
    {
      if (r == null) return 0;
      if (!r.IsTeacher)
        return GetWpower(r.Effect, r.Count);
      int sign = r.Importence >= 0 ? 1 : -1;
      int mag = Math.Abs(r.Importence);
      return TeacherUtilityScaleK * sign * GetWpower(mag, r.Count);
    }

    /// <summary>Найти лучшее правило по RuleUtility</summary>
    public static (int Index, EpisodicRule Rule) FindBestRule(IReadOnlyList<EpisodicRule> rules)
    {
      if (rules == null || rules.Count == 0)
        return (-1, null);

      int maxVal = -1000;
      EpisodicRule best = null;
      int idx = -1;
      for (int i = 0; i < rules.Count; i++)
      {
        var r = rules[i];
        int w = RuleUtility(r);
        if (w > maxVal)
        {
          maxVal = w;
          best = r;
          idx = i;
        }
      }
      return (idx, best);
    }

    /// <summary>Найти худшее правило</summary>
    public static (int Index, EpisodicRule Rule) FindWorseRule(IReadOnlyList<EpisodicRule> rules)
    {
      if (rules == null || rules.Count == 0)
        return (-1, null);

      int minVal = 1000;
      EpisodicRule worst = null;
      int idx = -1;
      for (int i = 0; i < rules.Count; i++)
      {
        var r = rules[i];
        int w = RuleUtility(r);
        if (w < minVal)
        {
          minVal = w;
          worst = r;
          idx = i;
        }
      }
      return (idx, worst);
    }
  }
}
