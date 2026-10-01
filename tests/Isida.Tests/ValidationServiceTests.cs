using System.Collections.Generic;
using ISIDA.Gomeostas;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты внутреннего <c>ValidationService</c> (доступны благодаря
  /// <c>[InternalsVisibleTo("Isida.Tests")]</c>): валидация активаций и поиск циклов.
  /// </summary>
  public class ValidationServiceTests
  {
    // ---------- GetParameterName ----------

    [Fact]
    public void GetParameterName_KnownId_ReturnsNameWithId()
    {
      var parameters = new List<GomeostasSystem.ParameterData>
      {
        new GomeostasSystem.ParameterData(7, "Голод", string.Empty, 50f, 50, 50, -5)
      };

      Assert.Equal("Голод (№7)", ValidationService.GetParameterName(7, parameters));
    }

    [Fact]
    public void GetParameterName_UnknownId_ReturnsIdOnly()
    {
      var parameters = new List<GomeostasSystem.ParameterData>();

      Assert.Equal("№42", ValidationService.GetParameterName(42, parameters));
    }

    // ---------- ValidateActivation ----------

    [Fact]
    public void ValidateActivation_NullItems_False()
    {
      var map = new List<int[]> { new int[0] };

      bool result = ValidationService.ValidateActivation(null, map, out string message);

      Assert.False(result);
      Assert.False(string.IsNullOrEmpty(message));
    }

    [Fact]
    public void ValidateActivation_NullMap_False()
    {
      bool result = ValidationService.ValidateActivation(
          new List<int> { 0 }, null, out string message);

      Assert.False(result);
      Assert.False(string.IsNullOrEmpty(message));
    }

    [Fact]
    public void ValidateActivation_ItemOutOfRange_False()
    {
      var map = new List<int[]> { new int[0] };

      bool result = ValidationService.ValidateActivation(
          new List<int> { 5 }, map, out string message);

      Assert.False(result);
      Assert.Contains("выходит за пределы", message);
    }

    [Fact]
    public void ValidateActivation_SelfAntagonist_False()
    {
      var map = new List<int[]> { new[] { 0 } };

      bool result = ValidationService.ValidateActivation(
          new List<int> { 0 }, map, out string message);

      Assert.False(result);
      Assert.Contains("самому себе", message);
    }

    [Fact]
    public void ValidateActivation_EmptyItems_True()
    {
      var map = new List<int[]>();

      bool result = ValidationService.ValidateActivation(
          new List<int>(), map, out string message);

      Assert.True(result);
      Assert.Equal(string.Empty, message);
    }

    [Fact]
    public void ValidateActivation_NoAntagonistsAmongActivated_True()
    {
      var map = new List<int[]>
      {
        new int[0],            // 0
        new[] { 2 }            // 1 — антагонист 2, но 2 не активируется
      };

      bool result = ValidationService.ValidateActivation(
          new List<int> { 0, 1 }, map, out string message);

      Assert.True(result);
      Assert.Equal(string.Empty, message);
    }

    [Fact]
    public void ValidateActivation_MutualAnnihilation_False()
    {
      var map = new List<int[]>
      {
        new[] { 1 },           // 0 ↔ 1
        new[] { 0 }
      };

      bool result = ValidationService.ValidateActivation(
          new List<int> { 0, 1 }, map, out string message);

      Assert.False(result);
      Assert.Contains("взаимно деактивированы", message);
    }

    [Fact]
    public void ValidateActivation_NullAntagonistArrayEntry_Skipped_True()
    {
      var map = new List<int[]> { null };

      bool result = ValidationService.ValidateActivation(
          new List<int> { 0 }, map, out string message);

      Assert.True(result);
    }
  }
}
