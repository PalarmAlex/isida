using ISIDA.Gomeostas;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты простых моделей стилей: условия активации и антагонизмы.</summary>
  public class StyleModelsTests
  {
    [Fact]
    public void StyleActivationCondition_Defaults()
    {
      var condition = new StyleActivationCondition();

      Assert.Equal(0, condition.ParamId);
      Assert.NotNull(condition.StateStyles);
      Assert.Empty(condition.StateStyles);
    }

    [Fact]
    public void StyleActivationCondition_StateStylesAreMutable()
    {
      var condition = new StyleActivationCondition { ParamId = 5 };
      condition.StateStyles[3] = new System.Collections.Generic.List<int> { 1, 2 };

      Assert.Equal(5, condition.ParamId);
      Assert.Equal(new[] { 1, 2 }, condition.StateStyles[3]);
    }

    [Fact]
    public void StyleAntagonism_Defaults()
    {
      var antagonism = new StyleAntagonism();

      Assert.Equal(0, antagonism.StyleId);
      Assert.NotNull(antagonism.AntagonistIds);
      Assert.Empty(antagonism.AntagonistIds);
    }

    [Fact]
    public void StyleAntagonism_StoresValues()
    {
      var antagonism = new StyleAntagonism { StyleId = 7 };
      antagonism.AntagonistIds.Add(8);

      Assert.Equal(7, antagonism.StyleId);
      Assert.Equal(new[] { 8 }, antagonism.AntagonistIds);
    }
  }
}
