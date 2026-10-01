using System;
using ISIDA.Reflexes;
using Xunit;
using static ISIDA.Reflexes.PerceptionImagesSystem;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты чистых статических функций сопоставления образов восприятия
  /// <see cref="PerceptionImagesSystem"/> (иерархия «часть — целое», подмножества, равенство).
  /// </summary>
  public class PerceptionImageMatchingTests
  {
    private static PerceptionImage Image(
        int id = 0, int[] actions = null, int[] phrases = null, int color = 0)
    {
      return new PerceptionImage
      {
        Id = id,
        InfluenceActionsList = new System.Collections.Generic.List<int>(actions ?? new int[0]),
        PhraseIdList = new System.Collections.Generic.List<int>(phrases ?? new int[0]),
        VisualColorId = color
      };
    }

    // ---------- IsIntListSubset ----------

    [Fact]
    public void IsIntListSubset_EmptySmall_True()
    {
      Assert.True(IsIntListSubset(null, new System.Collections.Generic.List<int> { 1 }));
      Assert.True(IsIntListSubset(new System.Collections.Generic.List<int>(), new System.Collections.Generic.List<int> { 1 }));
    }

    [Fact]
    public void IsIntListSubset_NullLarge_False()
    {
      Assert.False(IsIntListSubset(new System.Collections.Generic.List<int> { 1 }, null));
    }

    [Fact]
    public void IsIntListSubset_ProperSubset_True()
    {
      Assert.True(IsIntListSubset(
          new System.Collections.Generic.List<int> { 1, 2 },
          new System.Collections.Generic.List<int> { 1, 2, 3 }));
    }

    [Fact]
    public void IsIntListSubset_NotSubset_False()
    {
      Assert.False(IsIntListSubset(
          new System.Collections.Generic.List<int> { 1, 4 },
          new System.Collections.Generic.List<int> { 1, 2, 3 }));
    }

    [Fact]
    public void IsIntListSubset_RespectsMultiplicity()
    {
      // {1,1} не является подмножеством {1}
      Assert.False(IsIntListSubset(
          new System.Collections.Generic.List<int> { 1, 1 },
          new System.Collections.Generic.List<int> { 1 }));
      // {1,1} является подмножеством {1,1,2}
      Assert.True(IsIntListSubset(
          new System.Collections.Generic.List<int> { 1, 1 },
          new System.Collections.Generic.List<int> { 1, 1, 2 }));
    }

    // ---------- StimulusImagesHierarchyCompatible ----------

    [Fact]
    public void Hierarchy_Null_False()
    {
      Assert.False(StimulusImagesHierarchyCompatible(null, Image()));
      Assert.False(StimulusImagesHierarchyCompatible(Image(), null));
    }

    [Fact]
    public void Hierarchy_TriggerSubsetOfStimulus_True()
    {
      var stimulus = Image(actions: new[] { 1, 2, 3 });
      var trigger = Image(actions: new[] { 1, 2 });

      Assert.True(StimulusImagesHierarchyCompatible(stimulus, trigger));
    }

    [Fact]
    public void Hierarchy_StimulusSubsetOfTrigger_True()
    {
      var stimulus = Image(actions: new[] { 1 });
      var trigger = Image(actions: new[] { 1, 2 });

      Assert.True(StimulusImagesHierarchyCompatible(stimulus, trigger));
    }

    [Fact]
    public void Hierarchy_Disjoint_False()
    {
      var stimulus = Image(actions: new[] { 1, 2 });
      var trigger = Image(actions: new[] { 3, 4 });

      Assert.False(StimulusImagesHierarchyCompatible(stimulus, trigger));
    }

    [Fact]
    public void Hierarchy_WhiteColorIsWildcard()
    {
      var stimulus = Image(actions: new[] { 1 }, color: AgentVisualColor.White);
      var trigger = Image(actions: new[] { 1 }, color: AgentVisualColor.Black);

      Assert.True(StimulusImagesHierarchyCompatible(stimulus, trigger));
    }

    [Fact]
    public void Hierarchy_TwoDifferentNonWhiteColors_Conflict()
    {
      var stimulus = Image(actions: new[] { 1 }, color: AgentVisualColor.Black);
      var trigger = Image(actions: new[] { 1 }, color: 2);

      Assert.False(StimulusImagesHierarchyCompatible(stimulus, trigger));
    }

    [Fact]
    public void Hierarchy_SameNonWhiteColor_Compatible()
    {
      var stimulus = Image(actions: new[] { 1 }, color: 3);
      var trigger = Image(actions: new[] { 1 }, color: 3);

      Assert.True(StimulusImagesHierarchyCompatible(stimulus, trigger));
    }

    // ---------- PerceptionImagesEqual ----------

    [Fact]
    public void Equal_SameContent_OrderIndependent_True()
    {
      var a = Image(actions: new[] { 1, 2 }, phrases: new[] { 5 }, color: 1);
      var b = Image(actions: new[] { 2, 1 }, phrases: new[] { 5 }, color: 1);

      Assert.True(PerceptionImagesEqual(a, b));
    }

    [Fact]
    public void Equal_DifferentColor_False()
    {
      var a = Image(actions: new[] { 1 }, color: 1);
      var b = Image(actions: new[] { 1 }, color: 2);

      Assert.False(PerceptionImagesEqual(a, b));
    }

    [Fact]
    public void Equal_DifferentActions_False()
    {
      Assert.False(PerceptionImagesEqual(Image(actions: new[] { 1 }), Image(actions: new[] { 2 })));
    }

    [Fact]
    public void Equal_Null_False()
    {
      Assert.False(PerceptionImagesEqual(null, Image()));
      Assert.False(PerceptionImagesEqual(Image(), null));
    }

    // ---------- GetTriggerSpecificityTier ----------

    [Fact]
    public void Specificity_BothModalities_Three()
    {
      Assert.Equal(3, GetTriggerSpecificityTier(Image(actions: new[] { 1 }, phrases: new[] { 2 })));
    }

    [Fact]
    public void Specificity_OneModality_Two()
    {
      Assert.Equal(2, GetTriggerSpecificityTier(Image(actions: new[] { 1 })));
      Assert.Equal(2, GetTriggerSpecificityTier(Image(phrases: new[] { 1 })));
    }

    [Fact]
    public void Specificity_ColorOnly_One()
    {
      Assert.Equal(1, GetTriggerSpecificityTier(Image(color: 4)));
    }

    [Fact]
    public void Specificity_Null_Zero()
    {
      Assert.Equal(0, GetTriggerSpecificityTier(null));
    }

    // ---------- CompoundModalityCount ----------

    [Fact]
    public void CompoundModality_CountsActionsPhrasesAndColor()
    {
      Assert.Equal(0, CompoundModalityCount(Image()));
      Assert.Equal(1, CompoundModalityCount(Image(actions: new[] { 1 })));
      Assert.Equal(2, CompoundModalityCount(Image(actions: new[] { 1 }, phrases: new[] { 2 })));
      Assert.Equal(3, CompoundModalityCount(Image(actions: new[] { 1 }, phrases: new[] { 2 }, color: 5)));
    }

    [Fact]
    public void CompoundModality_WhiteColorNotCounted()
    {
      Assert.Equal(1, CompoundModalityCount(Image(actions: new[] { 1 }, color: AgentVisualColor.White)));
    }

    [Fact]
    public void CompoundModality_Null_Zero()
    {
      Assert.Equal(0, CompoundModalityCount(null));
    }
  }
}
