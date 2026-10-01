using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты модели результата смены стадии эволюции.</summary>
  public class EvolutionStageChangeResultTests
  {
    [Fact]
    public void CreateSuccess_SetsFields()
    {
      var result = EvolutionStageChangeResult.CreateSuccess("ok", newStage: 2, previousStage: 1);

      Assert.True(result.Success);
      Assert.False(result.RequiresConfirmation);
      Assert.Equal("ok", result.Message);
      Assert.Equal(2, result.NewStage);
      Assert.Equal(1, result.PreviousStage);
    }

    [Fact]
    public void CreateFailure_SetsFields()
    {
      var result = EvolutionStageChangeResult.CreateFailure("bad");

      Assert.False(result.Success);
      Assert.False(result.RequiresConfirmation);
      Assert.Equal("bad", result.Message);
      Assert.Null(result.NewStage);
    }

    [Fact]
    public void CreateConfirmationRequired_SetsFields()
    {
      var result = EvolutionStageChangeResult.CreateConfirmationRequired("confirm?");

      Assert.False(result.Success);
      Assert.True(result.RequiresConfirmation);
      Assert.Equal("confirm?", result.Message);
    }
  }
}
