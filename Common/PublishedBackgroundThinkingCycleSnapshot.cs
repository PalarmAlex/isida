namespace ISIDA.Common
{
  /// <summary>Снимок одного фонового цикла мышления для логирования и UI (без лога цикла).</summary>
  public sealed class PublishedBackgroundThinkingCycleSnapshot
  {
    /// <summary>Создаёт снимок полей цикла для публикации в <see cref="AppGlobalState"/>.</summary>
    /// <param name="id">Идентификатор экземпляра цикла.</param>
    /// <param name="weight">Вес в диспетчере.</param>
    /// <param name="themeId">Образ темы.</param>
    /// <param name="purposeId">Образ цели.</param>
    /// <param name="problemNodeId">Узел дерева проблем.</param>
    /// <param name="lastStrategyId">Последняя стратегия (инфо-функция).</param>
    /// <param name="awaitingEvaluation">Ожидается оценка решения.</param>
    /// <param name="pendingSolutionAutomatizmId">Автоматизм решения при ожидании оценки.</param>
    public PublishedBackgroundThinkingCycleSnapshot(int id, int weight, int themeId, int purposeId,
        int problemNodeId, string lastStrategyId, bool awaitingEvaluation, int pendingSolutionAutomatizmId)
    {
      Id = id;
      Weight = weight;
      ThemeId = themeId;
      PurposeId = purposeId;
      ProblemNodeId = problemNodeId;
      LastStrategyId = lastStrategyId;
      AwaitingEvaluation = awaitingEvaluation;
      PendingSolutionAutomatizmId = pendingSolutionAutomatizmId;
    }

    /// <summary>Идентификатор экземпляра цикла.</summary>
    public int Id { get; }

    /// <summary>Вес в диспетчере (сортировка в колонке «Циклы Ф» — по убыванию).</summary>
    public int Weight { get; }

    /// <summary>Образ темы.</summary>
    public int ThemeId { get; }

    /// <summary>Образ цели.</summary>
    public int PurposeId { get; }

    /// <summary>Узел дерева проблем.</summary>
    public int ProblemNodeId { get; }

    /// <summary>Последняя стратегия.</summary>
    public string LastStrategyId { get; }

    /// <summary>Ожидается оценка решения.</summary>
    public bool AwaitingEvaluation { get; }

    /// <summary>ID автоматизма решения при ожидании оценки.</summary>
    public int PendingSolutionAutomatizmId { get; }
  }
}
