using System;
using System.Collections.Generic;
using System.Linq;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Интеграционные тесты CRUD безусловных (генетических) рефлексов (стадия 0).</summary>
  [Collection("EngineIntegration")]
  public class GeneticReflexesSystemIntegrationTests
  {
    private readonly EngineFixture _engine;

    public GeneticReflexesSystemIntegrationTests(EngineFixture engine)
    {
      _engine = engine;
      AppGlobalState.EvolutionStage = 0;
    }

    private static GeneticReflexesSystem System => GeneticReflexesSystem.Instance;

    private static int NewStyle()
    {
      var (id, _) = GomeostasSystem.Instance.AddBehaviorStyle(
          "Стиль-" + Guid.NewGuid().ToString("N"), "");
      Assert.True(id > 0);
      return id;
    }

    private static int NewAction()
    {
      var (id, _) = AdaptiveActionsSystem.Instance.AddAction(
          "Действие-" + Guid.NewGuid().ToString("N"), "", Vigor: 5);
      Assert.True(id > 0);
      return id;
    }

    private static int AddReflex(int level1, int styleId, int actionId) =>
        System.AddGeneticReflex(level1, new List<int> { styleId }, null, null, new List<int> { actionId }).ActionId;

    private static GeneticReflexesSystem.GeneticReflex Find(int id) => System.GetGeneticReflex(id);

    [Fact]
    public void AddGeneticReflex_ReturnsIdAndStores()
    {
      int style = NewStyle();
      int action = NewAction();

      int id = AddReflex(1, style, action);

      Assert.True(id > 0);
      var reflex = Find(id);
      Assert.NotNull(reflex);
      Assert.Equal(1, reflex.Level1);
      Assert.Contains(style, reflex.Level2);
      Assert.Contains(action, reflex.AdaptiveActions);
    }

    [Fact]
    public void AddGeneticReflex_InvalidLevel1_Throws()
    {
      int style = NewStyle();
      int action = NewAction();

      Assert.Throws<ArgumentException>(() => System.AddGeneticReflex(
          5, new List<int> { style }, null, null, new List<int> { action }));
    }

    [Fact]
    public void AddGeneticReflex_EmptyLevel2_Throws()
    {
      int action = NewAction();

      Assert.Throws<ArgumentException>(() => System.AddGeneticReflex(
          0, new List<int>(), null, null, new List<int> { action }));
    }

    [Fact]
    public void AddGeneticReflex_UnknownStyle_Throws()
    {
      int action = NewAction();

      Assert.Throws<ArgumentException>(() => System.AddGeneticReflex(
          0, new List<int> { 999999 }, null, null, new List<int> { action }));
    }

    [Fact]
    public void AddGeneticReflex_EmptyAdaptiveActions_Throws()
    {
      int style = NewStyle();

      Assert.Throws<ArgumentException>(() => System.AddGeneticReflex(
          0, new List<int> { style }, null, null, new List<int>()));
    }

    [Fact]
    public void AddGeneticReflex_UnknownAdaptiveAction_Throws()
    {
      int style = NewStyle();

      Assert.Throws<ArgumentException>(() => System.AddGeneticReflex(
          0, new List<int> { style }, null, null, new List<int> { 999999 }));
    }

    [Fact]
    public void AddGeneticReflex_Duplicate_Throws()
    {
      int style = NewStyle();
      int action = NewAction();
      AddReflex(0, style, action);

      Assert.Throws<ArgumentException>(() => System.AddGeneticReflex(
          0, new List<int> { style }, null, null, new List<int> { action }));
    }

    [Fact]
    public void AddGeneticReflex_AntagonistStyles_Throws()
    {
      var (styleA, _) = GomeostasSystem.Instance.AddBehaviorStyle("Стиль-A-" + Guid.NewGuid().ToString("N"), "");
      var (styleB, _) = GomeostasSystem.Instance.AddBehaviorStyle(
          "Стиль-B-" + Guid.NewGuid().ToString("N"), "", antagonistStyles: new List<int> { styleA });
      int action = NewAction();

      Assert.Throws<ArgumentException>(() => System.AddGeneticReflex(
          0, new List<int> { styleA, styleB }, null, null, new List<int> { action }));
    }

    [Fact]
    public void RemoveGeneticReflex_Removes()
    {
      int style = NewStyle();
      int action = NewAction();
      int id = AddReflex(0, style, action);

      Assert.True(System.RemoveGeneticReflex(id));
      Assert.Null(Find(id));
    }

    [Fact]
    public void RemoveGeneticReflex_Unknown_Throws()
    {
      Assert.Throws<KeyNotFoundException>(() => System.RemoveGeneticReflex(999999));
    }

    [Fact]
    public void UpdateGeneticReflex_ChangesStoredCopy()
    {
      int style = NewStyle();
      int action = NewAction();
      int id = AddReflex(0, style, action);

      var reflex = Find(id);
      reflex.Level1 = 1;
      System.UpdateGeneticReflex(reflex);

      Assert.Equal(1, Find(id).Level1);
    }

    [Fact]
    public void GetGeneticReflex_Unknown_ReturnsNull()
    {
      Assert.Null(System.GetGeneticReflex(999999));
    }
  }
}
