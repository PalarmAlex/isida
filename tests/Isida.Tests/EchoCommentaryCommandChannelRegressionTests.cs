using System.Collections.Generic;
using ISIDA.Actions;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регрессионные тесты для Случая 44: echo-комментаторский мотор command-канала (стадия 3, вариант 1).
  /// <para>
  /// Доработка добавляет детерминированную связь Command-паттерн → AdaptiveAction по полю
  /// <see cref="AdaptiveActionsSystem.AdaptiveAction.CommandPatternId"/>, чтобы зеркальный мотор
  /// нес РЕАЛЬНОЕ адаптивное действие команды (якорь рецепта в VELUM), а не саму команду. При этом
  /// команда не копируется в <c>commandPatternIdList</c> мотора, а запуск помечается источником
  /// <see cref="AdaptiveActionsSystem.ActionActivationSource.AutomatizmEchoCommentary"/> — диспатч
  /// в VELUM обязан быть пропущен (агент лишь комментирует действие оператора, не повторяя его).
  /// </para>
  /// Проверяется чистая API-часть механизма (<see cref="AdaptiveActionsSystem"/>) — детерминизм 1:N,
  /// схлопывание дублей, пропуск несвязанных ID и границы. Зеркальный мотор и запуск с echo-источником
  /// зависят от полного <c>PsychicSystem</c>/VELUM и здесь не поднимаются (см. AGENTS.md, «Тесты»).
  /// Каждое действие получает уникальный CommandPatternId через <see cref="EngineFixture.NextSeed"/>:
  /// реестр адаптивных действий не сбрасывается между тестами коллекции, поэтому ассерты привязаны
  /// к явно созданным ID, а не к глобальному содержимому справочника.
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class EchoCommentaryCommandChannelRegressionTests : EngineIntegrationTestBase
  {
    private readonly int _commandPatternId;
    private readonly int _linkedActionIdLow;   // связь с _commandPatternId, минимальный ID
    private readonly int _linkedActionIdHigh;  // связь с тем же _commandPatternId (1:N)
    private readonly int _unlinkedActionId;    // без связи с командой

    public EchoCommentaryCommandChannelRegressionTests(EngineFixture engine) : base(engine)
    {
      // Уникальный паттерн команды: ни одно иное действие коллекции на него не ссылается.
      _commandPatternId = Engine.NextSeed();

      // AddAction разрешён только в стадии 0 (см. AdaptiveActionsSystem.AddAction).
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        (_linkedActionIdLow, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Echo-действие-низ", "", Vigor: 5);
        (_linkedActionIdHigh, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Echo-действие-верх", "", Vigor: 5);
        (_unlinkedActionId, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Echo-действие-без-связи", "", Vigor: 5);
      }
      finally
      {
        AppGlobalState.EvolutionStage = stage;
      }

      // Связь ставится напрямую (публичный сеттер CommandPatternId), как InfluenceActionId в
      // MirrorAutomatizmServiceTests — чтобы не зависеть от наличия паттерна в CommandPhrases.dat.
      AdaptiveActionsSystem.Instance.GetAdaptiveAction(_linkedActionIdLow).CommandPatternId = _commandPatternId;
      AdaptiveActionsSystem.Instance.GetAdaptiveAction(_linkedActionIdHigh).CommandPatternId = _commandPatternId;
      // _unlinkedActionId остаётся с CommandPatternId == 0 (нет связи).
    }

    private static AdaptiveActionsSystem Sys => AdaptiveActionsSystem.Instance;

    #region GetAdaptiveActionByCommandPatternId — детерминизм 1:N

    [Fact]
    public void GetAdaptiveActionByCommandPatternId_OneToMany_ReturnsSmallestId()
    {
      // При 1:N (несколько действий ссылаются на один паттерн команды) выбор детерминирован —
      // берётся действие с минимальным ID, иначе зеркальный мотор «прыгал» бы между рецептами.
      var action = Sys.GetAdaptiveActionByCommandPatternId(_commandPatternId);

      Assert.NotNull(action);
      Assert.Equal(_linkedActionIdLow, action.Id);
      Assert.True(_linkedActionIdLow < _linkedActionIdHigh);
    }

    [Fact]
    public void GetAdaptiveActionByCommandPatternId_NonPositive_ReturnsNull()
    {
      // Граница: 0 и отрицательные — не валидный паттерн команды, связь не ищем (null, а не исключение).
      Assert.Null(Sys.GetAdaptiveActionByCommandPatternId(0));
      Assert.Null(Sys.GetAdaptiveActionByCommandPatternId(-1));
    }

    [Fact]
    public void GetAdaptiveActionByCommandPatternId_UnknownPattern_ReturnsNull()
    {
        Assert.Null(Sys.GetAdaptiveActionByCommandPatternId(Engine.NextSeed()));
    }

    #endregion

    #region ConvertCommandPatternIdsToAdaptiveActionIds — схлопывание дублей и пропуск

    [Fact]
    public void ConvertCommandPatternIds_SingleLinked_ReturnsSingleAdaptiveId()
    {
      var result = Sys.ConvertCommandPatternIdsToAdaptiveActionIds(new List<int> { _commandPatternId });

      Assert.Single(result);
      Assert.Equal(_linkedActionIdLow, result[0]);
    }

    [Fact]
    public void ConvertCommandPatternIds_DuplicatePatterns_CollapseToOne()
    {
      // Дубли паттернов в списке входа схлопываются в один ID мотора (result.Contains guard).
      var result = Sys.ConvertCommandPatternIdsToAdaptiveActionIds(
          new List<int> { _commandPatternId, _commandPatternId, _commandPatternId });

      Assert.Single(result);
      Assert.Equal(_linkedActionIdLow, result[0]);
    }

    [Fact]
    public void ConvertCommandPatternIds_UnlinkedPattern_IsSkipped()
    {
      // Паттерн без связи пропускается: в result попадает только связанный ID (никаких 0/мусора).
      int unknownPattern = Engine.NextSeed();
      var result = Sys.ConvertCommandPatternIdsToAdaptiveActionIds(
          new List<int> { unknownPattern, _commandPatternId });

      Assert.Single(result);
      Assert.Equal(_linkedActionIdLow, result[0]);
    }

    [Fact]
    public void ConvertCommandPatternIds_NullOrEmpty_ReturnsEmpty()
    {
      // Границы: null и пустой список → пустой результат (не null), чтобы зеркальный мотор
      // безопасно склеивал пустые части без проверок на null.
      Assert.Empty(Sys.ConvertCommandPatternIdsToAdaptiveActionIds(null));
      Assert.Empty(Sys.ConvertCommandPatternIdsToAdaptiveActionIds(new List<int>()));
    }

    [Fact]
    public void ConvertCommandPatternIds_Mixed_ReturnsOnlyLinkedDeterministic()
    {
      // Смешанный вход (связанный + несвязанный + 0) даёт ровно один детерминированный ID.
      var result = Sys.ConvertCommandPatternIdsToAdaptiveActionIds(
          new List<int> { 0, Engine.NextSeed(), _commandPatternId });

      Assert.Single(result);
      Assert.Equal(_linkedActionIdLow, result[0]);
    }

    #endregion

    #region IsCommandPatternIdUsedForMirroring

    [Fact]
    public void IsCommandPatternIdUsedForMirroring_Linked_ReturnsTrue()
    {
      Assert.True(Sys.IsCommandPatternIdUsedForMirroring(_commandPatternId));
    }

    [Fact]
    public void IsCommandPatternIdUsedForMirroring_NonPositive_ReturnsFalse()
    {
      Assert.False(Sys.IsCommandPatternIdUsedForMirroring(0));
      Assert.False(Sys.IsCommandPatternIdUsedForMirroring(-5));
    }

    [Fact]
    public void IsCommandPatternIdUsedForMirroring_UnknownPattern_ReturnsFalse()
    {
      Assert.False(Sys.IsCommandPatternIdUsedForMirroring(Engine.NextSeed()));
    }

    #endregion

    #region Контракт источника активации

    [Fact]
    public void ActionActivationSource_EchoCommentary_HasContractValue()
    {
      // Новый источник выделен отдельным значением (не переиспользует существующие), иначе
      // RecipeDispatcher не смог бы однозначно «проглотить» диспатч echo-комментария в VELUM.
      Assert.Equal(
          5,
          (int)AdaptiveActionsSystem.ActionActivationSource.AutomatizmEchoCommentary);
      Assert.NotEqual(
          AdaptiveActionsSystem.ActionActivationSource.AutomatizmVerbalResponse,
          AdaptiveActionsSystem.ActionActivationSource.AutomatizmEchoCommentary);
      Assert.NotEqual(
          AdaptiveActionsSystem.ActionActivationSource.Automatizm,
          AdaptiveActionsSystem.ActionActivationSource.AutomatizmEchoCommentary);
    }

    #endregion
  }
}
