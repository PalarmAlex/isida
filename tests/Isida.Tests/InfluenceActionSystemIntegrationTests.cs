using System;
using System.Collections.Generic;
using System.Linq;
using ISIDA.Actions;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Интеграционные тесты CRUD гомеостатических воздействий <see cref="InfluenceActionSystem"/> (стадия 0).</summary>
  [Collection("EngineIntegration")]
  public class InfluenceActionSystemIntegrationTests
  {
    private readonly EngineFixture _engine;

    public InfluenceActionSystemIntegrationTests(EngineFixture engine)
    {
      _engine = engine;
      AppGlobalState.EvolutionStage = 0;
    }

    private static InfluenceActionSystem System => InfluenceActionSystem.Instance;

    private static int AddAction(string name, Dictionary<int, int> influences, bool strict = false) =>
        System.AddInfluenceAction(name, "", influences, strictValidation: strict).ActionId;

    private static InfluenceActionSystem.GomeostasisInfluenceAction Find(int id) =>
        System.GetAllInfluenceActions().FirstOrDefault(a => a.Id == id);

    [Fact]
    public void AddInfluenceAction_EmptyName_Throws()
    {
      Assert.Throws<ArgumentException>(() => System.AddInfluenceAction("", "", new Dictionary<int, int>()));
    }

    [Fact]
    public void AddInfluenceAction_ReturnsIdAndStores()
    {
      int id = AddAction("Тест-воздействие", new Dictionary<int, int> { { 1, 5 } });

      Assert.True(id > 0);
      var action = Find(id);
      Assert.NotNull(action);
      Assert.Equal("Тест-воздействие", action.Name);
      Assert.Equal(5, action.Influences[1]);
    }

    [Fact]
    public void AddInfluenceAction_OutOfRange_WarnsAndClamps()
    {
      var (id, warnings) = System.AddInfluenceAction("Клэмп", "", new Dictionary<int, int> { { 1, 99 } });

      Assert.True(id > 0);
      Assert.NotEmpty(warnings);
      Assert.Equal(10, Find(id).Influences[1]);
    }

    [Fact]
    public void AddInfluenceAction_OutOfRange_Strict_Throws()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() =>
          System.AddInfluenceAction("Строго", "", new Dictionary<int, int> { { 1, 99 } }, strictValidation: true));
    }

    [Fact]
    public void UpdateAction_UnknownId_Throws()
    {
      var action = new InfluenceActionSystem.GomeostasisInfluenceAction
      {
        Id = 999999,
        Name = "Нет такого",
        Influences = new Dictionary<int, int>()
      };

      Assert.Throws<KeyNotFoundException>(() => System.UpdateAction(action));
    }

    [Fact]
    public void RemoveAction_RemovesFromAll()
    {
      int id = AddAction("Удаляемый", new Dictionary<int, int>());

      Assert.True(System.RemoveAction(id));
      Assert.Null(Find(id));
    }

    [Fact]
    public void InfluenceSums_AreComputed()
    {
      int id = AddAction("Суммы", new Dictionary<int, int> { { 1, 5 }, { 2, -3 } });

      Assert.Equal(8, System.GetInfluenceMagnitudeSum(id));   // |5| + |-3|
      Assert.Equal(2, System.GetSignedInfluenceSumForAction(id)); // 5 + (-3)
    }

    [Fact]
    public void SignedOperatorValence_DeficitParameter_PositiveEffectIsGood()
    {
      // дефицит-ориентированный параметр (Speed < 0)
      var (paramId, _) = ISIDA.Gomeostas.GomeostasSystem.Instance.AddParameter(
          "Валид-параметр", "", 50f, 50, 50, -1);

      int id = AddAction("Валентность", new Dictionary<int, int> { { paramId, 5 } });

      Assert.Equal(5, System.GetSignedOperatorValenceSumForActions(new[] { id }));
    }

    [Fact]
    public void SignedOperatorValence_ExcessParameter_PositiveEffectIsBad()
    {
      var (paramId, _) = ISIDA.Gomeostas.GomeostasSystem.Instance.AddParameter(
          "Избыток-параметр", "", 50f, 50, 50, 1);

      int id = AddAction("Валентность-избыток", new Dictionary<int, int> { { paramId, 5 } });

      Assert.Equal(-5, System.GetSignedOperatorValenceSumForActions(new[] { id }));
    }
  }
}
