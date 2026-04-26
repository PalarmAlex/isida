namespace ISIDA.Psychic.Thinking
{
  /// <summary>Снимок полей цикла мышления до снятия по подтверждённой полезности решения (для агентного лога).</summary>
  public sealed class MainThinkingCycleClosedLogPayload
  {
    /// <summary>Создаёт снимок полей цикла.</summary>
    /// <param name="cycleId">Идентификатор экземпляра цикла.</param>
    /// <param name="weight">Вес цикла на момент снятия.</param>
    /// <param name="themeId">Образ темы.</param>
    /// <param name="purposeId">Образ цели.</param>
    /// <param name="problemNodeId">Узел дерева проблем.</param>
    /// <param name="lastStrategyId">Последняя стратегия (например infoFunc_28).</param>
    /// <param name="pendingSolutionAutomatizmId">Автоматизм решения.</param>
    /// <param name="confirmedUsefulness">Подтверждённая полезность.</param>
    public MainThinkingCycleClosedLogPayload(
        int cycleId,
        int weight,
        int themeId,
        int purposeId,
        int problemNodeId,
        string lastStrategyId,
        int pendingSolutionAutomatizmId,
        int confirmedUsefulness)
    {
      CycleId = cycleId;
      Weight = weight;
      ThemeId = themeId;
      PurposeId = purposeId;
      ProblemNodeId = problemNodeId;
      LastStrategyId = lastStrategyId ?? "";
      PendingSolutionAutomatizmId = pendingSolutionAutomatizmId;
      ConfirmedUsefulness = confirmedUsefulness;
    }

    /// <summary>Идентификатор экземпляра цикла.</summary>
    public int CycleId { get; }

    /// <summary>Вес цикла.</summary>
    public int Weight { get; }

    /// <summary>Образ темы.</summary>
    public int ThemeId { get; }

    /// <summary>Образ цели.</summary>
    public int PurposeId { get; }

    /// <summary>Узел дерева проблем.</summary>
    public int ProblemNodeId { get; }

    /// <summary>Последняя стратегия.</summary>
    public string LastStrategyId { get; }

    /// <summary>Автоматизм решения.</summary>
    public int PendingSolutionAutomatizmId { get; }

    /// <summary>Полезность, по которой цикл снят.</summary>
    public int ConfirmedUsefulness { get; }
  }
}
