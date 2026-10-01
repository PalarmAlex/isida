using System;
using System.Collections.Generic;
using System.Linq;
using ISIDA.Actions;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Интеграционные тесты CRUD моторных действий <see cref="AdaptiveActionsSystem"/> (стадия 0).</summary>
  [Collection("EngineIntegration")]
  public class AdaptiveActionsSystemIntegrationTests
  {
    private readonly EngineFixture _engine;

    public AdaptiveActionsSystemIntegrationTests(EngineFixture engine)
    {
      _engine = engine;
      AppGlobalState.EvolutionStage = 0;
    }

    private static AdaptiveActionsSystem System => AdaptiveActionsSystem.Instance;

    private static AdaptiveActionsSystem.AdaptiveAction Find(int id) =>
        System.GetAllAdaptiveActions().FirstOrDefault(a => a.Id == id);

    [Fact]
    public void AddAction_EmptyName_Throws()
    {
      Assert.Throws<ArgumentException>(() => System.AddAction("", ""));
    }

    [Fact]
    public void AddAction_ReturnsIdAndStores()
    {
      var (id, _) = System.AddAction("Тест-действие", "описание", Vigor: 7);

      Assert.True(id > 0);
      var action = Find(id);
      Assert.NotNull(action);
      Assert.Equal("Тест-действие", action.Name);
      Assert.Equal(7, action.Vigor);
    }

    [Fact]
    public void AddAction_InvalidVigor_ThrowsFromInvariant()
    {
      // Сеттер Vigor — инвариант [1..10]: недопустимое значение падает сразу,
      // независимо от strictValidation (до мягкой ветки валидации дело не доходит).
      Assert.Throws<ArgumentOutOfRangeException>(() => System.AddAction("Плохой Vigor", "", Vigor: 0));
    }

    [Fact]
    public void AddAction_UnknownTargetParameter_ReturnsZeroWithWarnings()
    {
      var (id, warnings) = System.AddAction(
          "Нет параметра", "", targetGomeoParamIdArr: new List<int> { 999999 });

      Assert.Equal(0, id);
      Assert.NotEmpty(warnings);
    }

    [Fact]
    public void RemoveAction_RemovesFromAll()
    {
      var (id, _) = System.AddAction("Удаляемое действие", "");

      Assert.True(System.RemoveAction(id));
      Assert.Null(Find(id));
    }

    [Fact]
    public void GetAdaptiveAction_Unknown_ReturnsNull()
    {
      Assert.Null(System.GetAdaptiveAction(999999));
    }

    [Fact]
    public void ClearAllActiveState_DoesNotRemoveDefinitions()
    {
      var (id, _) = System.AddAction("Активное действие", "");
      int countBefore = System.GetAllAdaptiveActions().Count;

      System.ClearAllActiveState();

      Assert.Equal(countBefore, System.GetAllAdaptiveActions().Count);
      Assert.NotNull(Find(id));
    }
  }
}
