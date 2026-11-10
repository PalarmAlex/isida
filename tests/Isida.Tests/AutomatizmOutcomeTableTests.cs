using System;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Таблица оценки автоматизма (<see cref="AppGlobalState.AssessAutomatizmOutcome"/>):
  /// 9 сочетаний «состояние до / после» × 3 знака дельты параметров = 27 комбинаций.
  /// <para>Правила (случай 42):</para>
  /// <list type="bullet">
  /// <item>состояние выросло — успех (+1) независимо от параметров;</item>
  /// <item>падение в «Плохо» — неуспех (−1) независимо от параметров;</item>
  /// <item>осталось «Плохо» — решает динамика параметров (лучше → +1, иначе −1);</item>
  /// <item>«Норма»/«Хорошо» без роста и «Хорошо»→«Норма» — оценка не меняется (0).</item>
  /// </list>
  /// </summary>
  public class AutomatizmOutcomeTableTests
  {
    private const int Worse = -1;
    private const int Same = 0;
    private const int Better = 1;

    [Theory]
    // ---------- было «Плохо» ----------
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Bad, Worse, -1)]
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Bad, Same, -1)]
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Bad, Better, 1)]
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Normal, Worse, 1)]
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Normal, Same, 1)]
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Normal, Better, 1)]
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Well, Worse, 1)]
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Well, Same, 1)]
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Well, Better, 1)]
    // ---------- было «Норма» ----------
    [InlineData(AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Bad, Worse, -1)]
    [InlineData(AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Bad, Same, -1)]
    [InlineData(AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Bad, Better, -1)]
    [InlineData(AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Normal, Worse, 0)]
    [InlineData(AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Normal, Same, 0)]
    [InlineData(AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Normal, Better, 0)]
    [InlineData(AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Well, Worse, 1)]
    [InlineData(AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Well, Same, 1)]
    [InlineData(AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Well, Better, 1)]
    // ---------- было «Хорошо» ----------
    [InlineData(AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Bad, Worse, -1)]
    [InlineData(AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Bad, Same, -1)]
    [InlineData(AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Bad, Better, -1)]
    [InlineData(AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Normal, Worse, 0)]
    [InlineData(AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Normal, Same, 0)]
    [InlineData(AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Normal, Better, 0)]
    [InlineData(AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Well, Worse, 0)]
    [InlineData(AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Well, Same, 0)]
    [InlineData(AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Well, Better, 0)]
    public void AssessAutomatizmOutcome_Table_ReturnsExpected(
        AppGlobalState.HomeostasisState before,
        AppGlobalState.HomeostasisState after,
        int parameterDelta,
        int expected)
    {
      Assert.Equal(expected, AppGlobalState.AssessAutomatizmOutcome(before, after, parameterDelta));
    }

    [Theory]
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Normal)]
    [InlineData(AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Well)]
    [InlineData(AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Well)]
    public void AssessAutomatizmOutcome_StateGrew_AlwaysSuccess(
        AppGlobalState.HomeostasisState before,
        AppGlobalState.HomeostasisState after)
    {
      foreach (int delta in new[] { -1, 0, 1 })
        Assert.Equal(1, AppGlobalState.AssessAutomatizmOutcome(before, after, delta));
    }

    [Theory]
    [InlineData(AppGlobalState.HomeostasisState.Normal)]
    [InlineData(AppGlobalState.HomeostasisState.Well)]
    public void AssessAutomatizmOutcome_FellToBad_AlwaysFailure(AppGlobalState.HomeostasisState before)
    {
      foreach (int delta in new[] { -1, 0, 1 })
        Assert.Equal(-1, AppGlobalState.AssessAutomatizmOutcome(before, AppGlobalState.HomeostasisState.Bad, delta));
    }

    /// <summary>
    /// Вчерашний баг (случай 42): возврат «Хорошо» → «Норма» — нормализация, а не ухудшение.
    /// Штраф за неё недопустим ни при какой дельте параметров.
    /// </summary>
    [Fact]
    public void AssessAutomatizmOutcome_WellToNormal_NoPenalty()
    {
      foreach (int delta in new[] { -1, 0, 1 })
        Assert.Equal(0, AppGlobalState.AssessAutomatizmOutcome(
            AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Normal, delta));
    }

    /// <summary>
    /// «Осталось Плохо» — решает динамика параметров: без улучшения это неуспех,
    /// даже если интегральное состояние формально не изменилось (случай 42, сценарий 10).
    /// </summary>
    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    public void AssessAutomatizmOutcome_StillBad_DependsOnParameterDelta(int parameterDelta, int expected)
    {
      Assert.Equal(expected, AppGlobalState.AssessAutomatizmOutcome(
          AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Bad, parameterDelta));
    }

    /// <summary>Оценка не убывает с ростом дельты параметров (нет противоречий в таблице).</summary>
    [Fact]
    public void AssessAutomatizmOutcome_MonotonicByParameterDelta()
    {
      foreach (AppGlobalState.HomeostasisState before in Enum.GetValues(typeof(AppGlobalState.HomeostasisState)))
        foreach (AppGlobalState.HomeostasisState after in Enum.GetValues(typeof(AppGlobalState.HomeostasisState)))
        {
          int worse = AppGlobalState.AssessAutomatizmOutcome(before, after, -1);
          int same = AppGlobalState.AssessAutomatizmOutcome(before, after, 0);
          int better = AppGlobalState.AssessAutomatizmOutcome(before, after, 1);

          Assert.True(worse <= same, $"{before}→{after}: −1 ({worse}) > 0 ({same})");
          Assert.True(same <= better, $"{before}→{after}: 0 ({same}) > +1 ({better})");
        }
    }

    /// <summary>Оценка принимает только −1 / 0 / +1 — никаких промежуточных значений.</summary>
    [Fact]
    public void AssessAutomatizmOutcome_ReturnsOnlyMinusOneZeroPlusOne()
    {
      foreach (AppGlobalState.HomeostasisState before in Enum.GetValues(typeof(AppGlobalState.HomeostasisState)))
        foreach (AppGlobalState.HomeostasisState after in Enum.GetValues(typeof(AppGlobalState.HomeostasisState)))
          foreach (int delta in new[] { -5, -1, 0, 1, 5 })
          {
            int outcome = AppGlobalState.AssessAutomatizmOutcome(before, after, delta);
            Assert.True(outcome == -1 || outcome == 0 || outcome == 1,
                $"{before}→{after} delta={delta} дал {outcome}");
          }
    }
  }
}
