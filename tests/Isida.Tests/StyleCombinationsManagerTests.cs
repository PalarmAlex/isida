using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using ISIDA.Gomeostas;
using Xunit;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты внутреннего <c>StyleCombinationsManager</c> (доступен благодаря
  /// <c>[InternalsVisibleTo("Isida.Tests")]</c>): генерация, сохранение и загрузка комбинаций стилей.
  /// </summary>
  public class StyleCombinationsManagerTests : IDisposable
  {
    private readonly string _dir;

    public StyleCombinationsManagerTests()
    {
      _dir = Path.Combine(Path.GetTempPath(), "isida_stylecomb_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
      try
      {
        if (Directory.Exists(_dir))
          Directory.Delete(_dir, recursive: true);
      }
      catch
      {
        // временный каталог — зачистка best-effort
      }
    }

    private static ReadOnlyDictionary<int, BehaviorStyle> Styles(params BehaviorStyle[] styles)
    {
      var dict = new Dictionary<int, BehaviorStyle>();
      foreach (var s in styles)
        dict[s.Id] = s;
      return new ReadOnlyDictionary<int, BehaviorStyle>(dict);
    }

    private static BehaviorStyle Style(int id, string name)
        => new BehaviorStyle { Id = id, Name = name, Description = string.Empty };

    private StyleCombinationsManager Create(
        ReadOnlyDictionary<int, BehaviorStyle> styles,
        Func<List<ParameterData>> parameters)
    {
      return new StyleCombinationsManager(_dir, () => styles, parameters);
    }

    [Fact]
    public void Ctor_NullPath_Throws()
    {
      Assert.Throws<ArgumentNullException>(() => new StyleCombinationsManager(
          null, () => Styles(), () => new List<ParameterData>()));
    }

    [Fact]
    public void Ctor_NullStylesFunc_Throws()
    {
      Assert.Throws<ArgumentNullException>(() => new StyleCombinationsManager(
          _dir, null, () => new List<ParameterData>()));
    }

    [Fact]
    public void Ctor_NullParametersFunc_Throws()
    {
      Assert.Throws<ArgumentNullException>(() => new StyleCombinationsManager(
          _dir, () => Styles(), null));
    }

    [Fact]
    public void LoadStyleCombinations_NoFile_ReturnsEmpty()
    {
      using (var manager = Create(Styles(), () => new List<ParameterData>()))
      {
        Assert.Empty(manager.LoadStyleCombinations());
      }
    }

    [Fact]
    public void GenerateStyleCombinations_FromParameterBindings()
    {
      var styles = Styles(Style(1, "A"), Style(2, "B"), Style(3, "C"));

      var param = new ParameterData(1, "P", string.Empty, 50f, 50, 50, -5);
      param.StyleActivations = new Dictionary<int, List<int>>
      {
        { 2, new List<int> { 1, 2 } },
        { 3, new List<int> { 3 } }
      };

      using (var manager = Create(styles, () => new List<ParameterData> { param }))
      {
        var combinations = manager.GenerateStyleCombinations(forceRegenerate: true);

        Assert.Equal(2, combinations.Count);
        Assert.Single(combinations[0]);
        Assert.Equal(3, combinations[0][0].Id);
        Assert.Equal(new[] { 1, 2 }, new[] { combinations[1][0].Id, combinations[1][1].Id });
      }
    }

    [Fact]
    public void SaveAndReload_RoundTrip()
    {
      var styles = Styles(Style(1, "A"), Style(2, "B"));

      var combinations = new List<List<BehaviorStyle>>
      {
        new List<BehaviorStyle> { Style(1, "A"), Style(2, "B") }
      };

      using (var manager = Create(styles, () => new List<ParameterData>()))
      {
        var save = manager.SaveStyleCombinations(combinations);
        Assert.True(save.Success);

        Assert.True(File.Exists(Path.Combine(_dir, "StyleCombinations.comb")));
      }

      using (var manager = Create(styles, () => new List<ParameterData>()))
      {
        var loaded = manager.LoadStyleCombinations();

        Assert.Single(loaded);
        Assert.Equal(2, loaded[0].Count);
        Assert.Equal(1, loaded[0][0].Id);
        Assert.Equal(2, loaded[0][1].Id);
      }
    }

    [Fact]
    public void GenerateStyleCombinations_NotForced_LoadsFromFileWhenPresent()
    {
      var styles = Styles(Style(1, "A"));

      using (var manager = Create(styles, () => new List<ParameterData>()))
      {
        manager.SaveStyleCombinations(new List<List<BehaviorStyle>>
        {
          new List<BehaviorStyle> { Style(1, "A") }
        });
      }

      // parameters пусты, но файл есть — комбинация должна загрузиться, а не сгенерироваться.
      using (var manager = Create(styles, () => new List<ParameterData>()))
      {
        var combinations = manager.GenerateStyleCombinations();

        Assert.Single(combinations);
        Assert.Equal(1, combinations[0][0].Id);
      }
    }

    [Fact]
    public void SaveStyleCombinations_EmptyInput_SuccessAndEmptyFile()
    {
      using (var manager = Create(Styles(), () => new List<ParameterData>()))
      {
        var save = manager.SaveStyleCombinations(new List<List<BehaviorStyle>>());

        Assert.True(save.Success);
        var loaded = manager.LoadStyleCombinations();
        Assert.Empty(loaded);
      }
    }
  }
}
