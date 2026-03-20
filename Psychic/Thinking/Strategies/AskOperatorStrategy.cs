namespace ISIDA.Psychic.Thinking.Strategies
{
  /// <summary>
  /// Если решения не находится, а ситуация опасная/актуальная — запросить подсказку
  /// В текущей реализации возвращает флаг RequestParrotFromOperator для внешней обработки.
  /// </summary>
  public sealed class AskOperatorStrategy : IThinkingStrategy
  {
    /// <summary>
    /// Инфо-функция: запрос помощи у оператора ("попугайство").
    /// Активация: когда инфо-среда показывает опасность или крайне актуальную ситуацию.
    /// </summary>
    public string Id => "infoFunc_31";

    /// <summary>
    /// Один шаг инфо-функции: если текущая ситуация опасная/очень актуальная — просит подсказку у оператора.
    /// Иначе возвращает решение без действия.
    /// </summary>
    /// <param name="ctx">Контекст выполнения шага (текущий цикл и текущая информационная среда).</param>
    /// <returns>ThinkingDecision с RequestParrotFromOperator=true или None.</returns>
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

