using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Проверка, что ConditionsDetection находит рефлекс даже когда узел дерева
  /// был создан на неправильном уровне вложенности.
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class ReflexTreeConditionsDetectionTests : IDisposable
  {
    private readonly EngineFixture _engine;

    public ReflexTreeConditionsDetectionTests(EngineFixture engine)
    {
      _engine = engine;
      AppGlobalState.EvolutionStage = 0;
    }

    public void Dispose()
    {
      AppGlobalState.EvolutionStage = 0;
    }

    private static GeneticReflexesSystem Genetic => GeneticReflexesSystem.Instance;
    private static ReflexTreeSystem Tree => ReflexTreeSystem.Instance;
    private static GomeostasSystem Gomeostas => GomeostasSystem.Instance;

    private int CreateTestStyle(string name)
    {
      var (id, _) = Gomeostas.AddBehaviorStyle(name, "Тестовый стиль для ConditionsDetection");
      Assert.True(id > 0, "Стиль не создан");
      return id;
    }

    private int CreateTestAction(string name)
    {
      var (id, _) = AdaptiveActionsSystem.Instance.AddAction(name, "Тестовое действие", Vigor: 5);
      Assert.True(id > 0, "Действие не создано");
      return id;
    }

    private int CreateTestInfluenceAction(string name)
    {
      var (id, _) = InfluenceActionSystem.Instance.AddInfluenceAction(
          name, "Тестовое воздействие", null, null);
      Assert.True(id > 0, "Воздействие не создано");
      return id;
    }

    private int CreateGeneticReflex(int level1, List<int> level2, List<int> influenceActionIds, List<int> adaptiveActions)
    {
      var (reflexId, _) = Genetic.AddGeneticReflex(level1, level2, influenceActionIds, null, adaptiveActions);
      Assert.True(reflexId > 0, "Рефлекс не создан");
      return reflexId;
    }

    /// <summary>
    /// Получает ID образа стиля поведения (не ID самого стиля).
    /// В дереве рефлексов StyleID — это ID образа стиля.
    /// </summary>
    private int GetStyleImageId(int styleId)
    {
      return PerceptionImagesSystem.Instance.AddBehaviorStyleImage(new List<int> { styleId });
    }

    [Fact]
    public void FindReflexTreeNodeFromCondition_FindsNodeWithCorrectParams()
    {
      // Тест: FindReflexTreeNodeFromCondition находит узел по точному совпадению
      // BaseID, StyleID (образ стиля), ActionID (образ пускового стимула) среди всех узлов дерева.

      int style = CreateTestStyle("Style_FindTest");
      int action = CreateTestAction("Action_FindTest");
      int influence = CreateTestInfluenceAction("Influence_FindTest");

      int reflexId = CreateGeneticReflex(-1, new List<int> { style }, new List<int> { influence }, new List<int> { action });

      // Получаем ID образа стиля и ID образа пускового стимула (то, что хранится в дереве)
      int styleImageId = GetStyleImageId(style);
      int actionImageId = PerceptionImagesSystem.Instance.AddPerceptionImage(
          new List<int> { influence }, new List<int>(), AgentVisualColor.White, null);
      Assert.True(styleImageId > 0, "Образ стиля не создан");
      Assert.True(actionImageId > 0, "Образ пускового стимула не создан");

      var (foundId, foundNode) = Tree.FindReflexTreeNodeFromCondition(-1, styleImageId, actionImageId);

      Assert.True(foundId > 0, "Узел не найден");
      Assert.Equal(-1, foundNode.BaseID);
      Assert.Equal(styleImageId, foundNode.StyleID);
      Assert.Equal(actionImageId, foundNode.ActionID);
      Assert.Equal(reflexId, foundNode.GeneticReflexID);
    }

    [Fact]
    public void FindReflexTreeNodeFromCondition_DoesNotReturnWrongActionId()
    {
      // Тест: FindReflexTreeNodeFromCondition возвращает null, если нет точного совпадения.

      int style = CreateTestStyle("Style_WrongAction");
      int action = CreateTestAction("Action_WrongAction");
      int influence = CreateTestInfluenceAction("Influence_WrongAction");

      CreateGeneticReflex(-1, new List<int> { style }, new List<int> { influence }, new List<int> { action });

      int styleImageId = GetStyleImageId(style);

      // Ищем с неправильным ActionID
      var (foundId, foundNode) = Tree.FindReflexTreeNodeFromCondition(-1, styleImageId, 999999);

      Assert.Equal(0, foundId);
      Assert.Null(foundNode);
    }
  }
}
