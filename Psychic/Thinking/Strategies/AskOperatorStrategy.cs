namespace ISIDA.Psychic.Thinking.Strategies
{
  /// <summary>
  /// Если решения не находится, а ситуация опасная/актуальная — запросить подсказку (аналог BOT infoFunc13).
  /// В текущей реализации возвращает флаг RequestParrotFromOperator для внешней обработки.
  /// </summary>
  public sealed class AskOperatorStrategy : IThinkingStrategy
  {
    /// <inheritdoc />
    public string Id => "operator.ask_or_parrot";

    /// <inheritdoc />
    public ThinkingDecision TryStep(ThinkingStrategyContext ctx)
    {
      if (ctx?.InformationEnvironmentSystem?.CurrentInformationEnvironment == null) return ThinkingDecision.None("no_ie");
      var env = ctx.InformationEnvironmentSystem.CurrentInformationEnvironment;

      if (env.Danger || env.VeryActualSituation)
      {
        return new ThinkingDecision
        {
          RequestParrotFromOperator = true,
          DebugNote = "request_operator_help"
        };
      }

      return ThinkingDecision.None("not_urgent");
    }
  }
}

