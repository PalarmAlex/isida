using System;
using System.Collections.Concurrent;

namespace ISIDA.Psychic.Thinking
{
  internal readonly struct ThinkingExperienceKey : IEquatable<ThinkingExperienceKey>
  {
    public ThinkingExperienceKey(int problemNodeId, int themeId, int purposeId)
    {
      ProblemNodeId = problemNodeId;
      ThemeId = themeId;
      PurposeId = purposeId;
    }

    public int ProblemNodeId { get; }
    public int ThemeId { get; }
    public int PurposeId { get; }

    public bool Equals(ThinkingExperienceKey other) =>
      ProblemNodeId == other.ProblemNodeId && ThemeId == other.ThemeId && PurposeId == other.PurposeId;

    public override bool Equals(object obj) => obj is ThinkingExperienceKey other && Equals(other);

    public override int GetHashCode()
    {
      unchecked
      {
        int h = 17;
        h = (h * 31) + ProblemNodeId.GetHashCode();
        h = (h * 31) + ThemeId.GetHashCode();
        h = (h * 31) + PurposeId.GetHashCode();
        return h;
      }
    }
  }

  /// <summary>
  /// Минимальная «ментальная память опыта» для циклов: запоминаем рекомендованное действие по (проблема, тема, цель).
  /// </summary>
  internal sealed class ThinkingExperienceMemory
  {
    private readonly ConcurrentDictionary<ThinkingExperienceKey, int> _bestActionByKey = new ConcurrentDictionary<ThinkingExperienceKey, int>();

    public int TryGetRecommendedAction(int problemNodeId, int themeId, int purposeId)
    {
      var key = new ThinkingExperienceKey(problemNodeId, themeId, purposeId);
      return _bestActionByKey.TryGetValue(key, out var actionId) ? actionId : 0;
    }

    public void RecordRecommendation(int problemNodeId, int themeId, int purposeId, int actionsImageId)
    {
      if (actionsImageId <= 0) return;
      var key = new ThinkingExperienceKey(problemNodeId, themeId, purposeId);
      _bestActionByKey[key] = actionsImageId;
    }

    /// <summary>Сброс рекомендаций (предзапуск сценария, очистка стадии 4 и т.п.).</summary>
    public void Clear()
    {
      _bestActionByKey.Clear();
    }
  }
}

