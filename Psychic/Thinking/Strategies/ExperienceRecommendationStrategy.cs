using ISIDA.Common;

namespace ISIDA.Psychic.Thinking.Strategies
{
  /// <summary>
  /// Упрощённый «следующий шаг по опыту»: если для (проблема, тема, цель) есть запомненное действие, предложить его.
  /// </summary>
  internal sealed class ExperienceRecommendationStrategy : IThinkingStrategy
  {
    private readonly ThinkingExperienceMemory _memory;

    internal ExperienceRecommendationStrategy(ThinkingExperienceMemory memory)
    {
      _memory = memory;
    }

    public string Id => "experience.recommend_action";

    public ThinkingDecision TryStep(ThinkingStrategyContext ctx)
    {
      if (_memory == null || ctx?.Cycle == null) return ThinkingDecision.None("no_memory");

      var c = ctx.Cycle;
      var rec = _memory.TryGetRecommendedAction(c.ProblemNodeId, c.ThemeId, c.PurposeId);
      if (rec <= 0) return ThinkingDecision.None("no_recommendation");

      if (c.UnresolvedActionsImageId > 0 && rec == c.UnresolvedActionsImageId)
        return ThinkingDecision.None("recommendation_equals_stimulus");

      Logger.Info($"Опыт циклов: рекомендован образ действий={rec}, проблема={c.ProblemNodeId}, тема={c.ThemeId}, цель={c.PurposeId}");
      return new ThinkingDecision
      {
        ActionsImageIdToAutomatize = rec,
        DebugNote = $"recommend_actionImg={rec}"
      };
    }
  }
}

