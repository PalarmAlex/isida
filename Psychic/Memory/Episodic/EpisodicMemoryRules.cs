using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic.Memory.Episodic
{
  /// <summary>
  /// Поиск и использование правил из эпизодической памяти
  /// </summary>
  public static class EpisodicMemoryRules
  {
    /// <summary>Вес эффекта по Count</summary>
    public static int GetWpower(int effect, int count)
    {
      if (count < 3) return effect;
      if (count < 6) return effect * 2;
      return effect * 3;
    }

    /// <summary>Найти лучшее правило по Effect*Count</summary>
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
        int w = GetWpower(r.Effect == EpisodicMemoryRulesService.TeacherRuleEffect ? 1 : r.Effect, r.Count);
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
        int w = GetWpower(r.Effect == EpisodicMemoryRulesService.TeacherRuleEffect ? 1 : r.Effect, r.Count);
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
