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
  /// <summary>
  /// Проверяет, что безусловный рефлекс находится в дереве рефлексов по НОВЫМ условиям
  /// после поочерёдного обновления каждого уровня его условий запуска:
  /// Level1 (базовое состояние), Level2 (стили/контексты реагирования),
  /// InfluenceActionIds (воздействия с пульта оператора).
  ///
  /// Проверяемый механизм: <c>GeneticReflexesSystem.UpdateGeneticReflex</c> при изменении
  /// условий удаляет и пересоздаёт рефлекс, порождая <c>GeneticReflexDeleted</c> →
  /// <c>GeneticReflexCreated</c>. Подписчик <c>ReflexTreeSystem</c> обязан пересоздать
  /// узел дерева с актуальными ID образов (StyleID/ActionID), иначе <c>FindReflexTreeNodeFromCondition</c>
  /// по новым условиям ничего не найдёт.
  ///
  /// Два пути обновления дают разный результат, и оба закреплены тестами:
  /// 1) «безопасный» — в <c>UpdateGeneticReflex</c> передаётся КОПИЯ с новыми значениями
  ///    (система сравнивает её с объектом из словаря) → узел пересоздаётся, рефлекс находится;
  /// 2) «мутация по ссылке» — условия меняются у объекта, возвращённого <c>GetGeneticReflex</c>,
  ///    и тот же объект передаётся в <c>UpdateGeneticReflex</c>. Словарь хранит тот же экземпляр,
  ///    поэтому «старая» копия копирует уже изменённые значения, <c>conditionsChanged == false</c>
  ///    и дерево остаётся с ПРЕЖНИМ узлом → по новым условиям рефлекс не находится.
  ///    Этот дефект закреплён skipped-тестом <see cref="UpdateConditions_DirectMutation_TreeNodeStaysOld"/>.
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class GeneticReflexLevelUpdateSearchTests : IDisposable
  {
    private readonly EngineFixture _engine;

    public GeneticReflexLevelUpdateSearchTests(EngineFixture engine)
    {
      _engine = engine;
      _engine.ResetToCleanState();
      AppGlobalState.EvolutionStage = 0;
    }

    public void Dispose()
    {
      AppGlobalState.EvolutionStage = 0;
    }

    private static GeneticReflexesSystem Genetic => GeneticReflexesSystem.Instance;
    private static ReflexTreeSystem Tree => ReflexTreeSystem.Instance;
    private static GomeostasSystem Gomeostas => GomeostasSystem.Instance;
    private static PerceptionImagesSystem Images => PerceptionImagesSystem.Instance;

    private int CreateStyle(string name)
    {
      var (id, _) = Gomeostas.AddBehaviorStyle(name, "Тест");
      Assert.True(id > 0, "Стиль не создан");
      return id;
    }

    private int CreateMotorAction(string name)
    {
      var (id, _) = AdaptiveActionsSystem.Instance.AddAction(name, "Тест", Vigor: 5);
      Assert.True(id > 0, "Действие не создано");
      return id;
    }

    private int CreateInfluenceAction(string name)
    {
      var (id, _) = InfluenceActionSystem.Instance.AddInfluenceAction(name, "Тест", null, null);
      Assert.True(id > 0, "Воздействие не создано");
      return id;
    }

    /// <summary>
    /// Копия рефлекса для «безопасного» обновления: сравнивая её с объектом из словаря,
    /// <c>UpdateGeneticReflex</c> корректно детектирует изменение условий.
    /// </summary>
    private static GeneticReflexesSystem.GeneticReflex Clone(GeneticReflexesSystem.GeneticReflex source) =>
        new GeneticReflexesSystem.GeneticReflex
        {
          Id = source.Id,
          Level1 = source.Level1,
          Level2 = new List<int>(source.Level2 ?? new List<int>()),
          InfluenceActionIds = new List<int>(source.InfluenceActionIds ?? new List<int>()),
          CommandPatternIds = new List<int>(source.CommandPatternIds ?? new List<int>()),
          AdaptiveActions = new List<int>(source.AdaptiveActions ?? new List<int>()),
          ReflexChainID = source.ReflexChainID
        };

    /// <summary>ID образа стиля (то, что хранится в <c>ReflexNode.StyleID</c>).</summary>
    private static int StyleImageOf(List<int> styleIds) => Images.AddBehaviorStyleImage(styleIds);

    /// <summary>ID образа пускового стимула (то, что хранится в <c>ReflexNode.ActionID</c>).</summary>
    private static int ActionImageOf(List<int> influenceIds)
    {
      if (influenceIds == null || !influenceIds.Any())
        return 0;

      var operatorIds = InfluenceActionSystem.Instance.FilterOperatorStimulusActionIds(influenceIds);
      return Images.AddPerceptionImage(operatorIds, new List<int>());
    }

    private static void AssertFound(int expectedReflexId, int baseId, int styleImageId, int actionImageId)
    {
      var (foundId, node) = Tree.FindReflexTreeNodeFromCondition(baseId, styleImageId, actionImageId);
      Assert.True(foundId > 0,
          $"Узел не найден для условий [Base={baseId}, Style={styleImageId}, Action={actionImageId}]");
      Assert.NotNull(node);
      Assert.Equal(expectedReflexId, node.GeneticReflexID);
    }

    private static void AssertNotBoundToReflex(int reflexId, int baseId, int styleImageId, int actionImageId)
    {
      var (foundId, node) = Tree.FindReflexTreeNodeFromCondition(baseId, styleImageId, actionImageId);
      Assert.True(foundId == 0 || node.GeneticReflexID != reflexId,
          $"По старым условиям [Base={baseId}, Style={styleImageId}, Action={actionImageId}] " +
          $"всё ещё привязан рефлекс {reflexId}");
    }

    // ======================================================================
    // Level1 — базовое состояние гомеостаза
    // ======================================================================

    [Fact]
    public void UpdateLevel1_ReflexFoundByNewBaseState()
    {
      int style = CreateStyle("Style_L1");
      int motor = CreateMotorAction("Action_L1");
      int influence = CreateInfluenceAction("Influence_L1");

      var (reflexId, _) = Genetic.AddGeneticReflex(0, new List<int> { style },
          new List<int> { influence }, null, new List<int> { motor });
      Assert.True(reflexId > 0);

      int styleImage = StyleImageOf(new List<int> { style });
      int actionImage = ActionImageOf(new List<int> { influence });

      AssertFound(reflexId, 0, styleImage, actionImage);

      var updated = Clone(Genetic.GetGeneticReflex(reflexId));
      updated.Level1 = 1;
      Genetic.UpdateGeneticReflex(updated);

      AssertFound(reflexId, 1, styleImage, actionImage);
      AssertNotBoundToReflex(reflexId, 0, styleImage, actionImage);
    }

    [Fact]
    public void UpdateLevel1_AcrossAllBaseStates()
    {
      int style = CreateStyle("Style_L1_All");
      int motor = CreateMotorAction("Action_L1_All");

      var (reflexId, _) = Genetic.AddGeneticReflex(-1, new List<int> { style },
          null, null, new List<int> { motor });

      int styleImage = StyleImageOf(new List<int> { style });
      AssertFound(reflexId, -1, styleImage, 0);

      foreach (int baseState in new[] { 0, 1, -1 })
      {
        var updated = Clone(Genetic.GetGeneticReflex(reflexId));
        updated.Level1 = baseState;
        Genetic.UpdateGeneticReflex(updated);

        AssertFound(reflexId, baseState, styleImage, 0);
      }
    }

    // ======================================================================
    // Level2 — контексты реагирования (стили поведения)
    // ======================================================================

    [Fact]
    public void UpdateLevel2_ReflexFoundByNewStyleImage()
    {
      int styleA = CreateStyle("Style2_A");
      int styleB = CreateStyle("Style2_B");
      int motor = CreateMotorAction("Action2_A");

      var (reflexId, _) = Genetic.AddGeneticReflex(0, new List<int> { styleA },
          null, null, new List<int> { motor });

      int imageA = StyleImageOf(new List<int> { styleA });
      AssertFound(reflexId, 0, imageA, 0);

      var updated = Clone(Genetic.GetGeneticReflex(reflexId));
      updated.Level2 = new List<int> { styleB };
      Genetic.UpdateGeneticReflex(updated);

      int imageB = StyleImageOf(new List<int> { styleB });
      AssertFound(reflexId, 0, imageB, 0);
      AssertNotBoundToReflex(reflexId, 0, imageA, 0);
    }

    [Fact]
    public void UpdateLevel2_AddSecondStyle_ReflexFoundByCombinationImage()
    {
      int styleA = CreateStyle("Style2_Add_A");
      int styleB = CreateStyle("Style2_Add_B");
      int motor = CreateMotorAction("Action2_Add_A");

      var (reflexId, _) = Genetic.AddGeneticReflex(1, new List<int> { styleA },
          null, null, new List<int> { motor });

      var updated = Clone(Genetic.GetGeneticReflex(reflexId));
      updated.Level2 = new List<int> { styleA, styleB };
      Genetic.UpdateGeneticReflex(updated);

      int combined = StyleImageOf(new List<int> { styleA, styleB });
      Assert.True(combined > 0, "Образ комбинации стилей не создан");
      AssertFound(reflexId, 1, combined, 0);
    }

    [Fact]
    public void UpdateLevel2_SameStylesDifferentOrder_StillFound()
    {
      int styleA = CreateStyle("Style2_Ord_A");
      int styleB = CreateStyle("Style2_Ord_B");
      int motor = CreateMotorAction("Action2_Ord_A");

      var (reflexId, _) = Genetic.AddGeneticReflex(0, new List<int> { styleA, styleB },
          null, null, new List<int> { motor });

      int combined = StyleImageOf(new List<int> { styleA, styleB });
      AssertFound(reflexId, 0, combined, 0);

      var updated = Clone(Genetic.GetGeneticReflex(reflexId));
      updated.Level2 = new List<int> { styleB, styleA };
      Genetic.UpdateGeneticReflex(updated);

      AssertFound(reflexId, 0, combined, 0);
    }

    // ======================================================================
    // Level3 — воздействия с пульта оператора
    // ======================================================================

    [Fact]
    public void UpdateInfluence_NoneToAtoB_ReflexFoundByNewActionImage()
    {
      int style = CreateStyle("Style_Inf");
      int motor = CreateMotorAction("Action_Inf");
      int influenceA = CreateInfluenceAction("Influence_Inf_A");
      int influenceB = CreateInfluenceAction("Influence_Inf_B");

      var (reflexId, _) = Genetic.AddGeneticReflex(0, new List<int> { style },
          null, null, new List<int> { motor });

      int styleImage = StyleImageOf(new List<int> { style });
      AssertFound(reflexId, 0, styleImage, 0);

      // Было: без воздействия → Стало: воздействие A
      var withA = Clone(Genetic.GetGeneticReflex(reflexId));
      withA.InfluenceActionIds = new List<int> { influenceA };
      Genetic.UpdateGeneticReflex(withA);

      int imageA = ActionImageOf(new List<int> { influenceA });
      Assert.True(imageA > 0, "Образ пускового стимула для A не создан");
      AssertFound(reflexId, 0, styleImage, imageA);

      // Было: A → Стало: B
      var withB = Clone(Genetic.GetGeneticReflex(reflexId));
      withB.InfluenceActionIds = new List<int> { influenceB };
      Genetic.UpdateGeneticReflex(withB);

      int imageB = ActionImageOf(new List<int> { influenceB });
      Assert.True(imageB > 0, "Образ пускового стимула для B не создан");
      AssertFound(reflexId, 0, styleImage, imageB);
      AssertNotBoundToReflex(reflexId, 0, styleImage, imageA);
    }

    [Fact]
    public void UpdateInfluence_RemoveAll_ReflexFoundWithoutTrigger()
    {
      int style = CreateStyle("Style_Inf_Del");
      int motor = CreateMotorAction("Action_Inf_Del");
      int influence = CreateInfluenceAction("Influence_Inf_Del");

      var (reflexId, _) = Genetic.AddGeneticReflex(0, new List<int> { style },
          new List<int> { influence }, null, new List<int> { motor });

      int styleImage = StyleImageOf(new List<int> { style });
      AssertFound(reflexId, 0, styleImage, ActionImageOf(new List<int> { influence }));

      var updated = Clone(Genetic.GetGeneticReflex(reflexId));
      updated.InfluenceActionIds = new List<int>();
      Genetic.UpdateGeneticReflex(updated);

      AssertFound(reflexId, 0, styleImage, 0);
    }

    [Fact]
    public void UpdateInfluence_AddTwo_ReflexFoundByCombinedActionImage()
    {
      int style = CreateStyle("Style_Inf_Two");
      int motor = CreateMotorAction("Action_Inf_Two");
      int infA = CreateInfluenceAction("Influence_Inf_Two_A");
      int infB = CreateInfluenceAction("Influence_Inf_Two_B");

      var (reflexId, _) = Genetic.AddGeneticReflex(1, new List<int> { style },
          new List<int> { infA }, null, new List<int> { motor });

      var updated = Clone(Genetic.GetGeneticReflex(reflexId));
      updated.InfluenceActionIds = new List<int> { infA, infB };
      Genetic.UpdateGeneticReflex(updated);

      AssertFound(reflexId, 1, StyleImageOf(new List<int> { style }),
          ActionImageOf(new List<int> { infA, infB }));
    }

    // ======================================================================
    // Последовательное обновление всех уровней условий запуска
    // ======================================================================

    [Fact]
    public void SequentialUpdateAllLevels_ReflexFoundAfterEachStep()
    {
      int styleA = CreateStyle("Seq_Style_A");
      int styleB = CreateStyle("Seq_Style_B");
      int motor = CreateMotorAction("Seq_Action");
      int infA = CreateInfluenceAction("Seq_Inf_A");
      int infB = CreateInfluenceAction("Seq_Inf_B");

      var (reflexId, _) = Genetic.AddGeneticReflex(-1, new List<int> { styleA },
          new List<int> { infA }, null, new List<int> { motor });

      AssertFound(reflexId, -1, StyleImageOf(new List<int> { styleA }),
          ActionImageOf(new List<int> { infA }));

      // Шаг 1: Level1.
      var step1 = Clone(Genetic.GetGeneticReflex(reflexId));
      step1.Level1 = 1;
      Genetic.UpdateGeneticReflex(step1);
      AssertFound(reflexId, 1, StyleImageOf(new List<int> { styleA }),
          ActionImageOf(new List<int> { infA }));

      // Шаг 2: Level2.
      var step2 = Clone(Genetic.GetGeneticReflex(reflexId));
      step2.Level2 = new List<int> { styleB };
      Genetic.UpdateGeneticReflex(step2);
      AssertFound(reflexId, 1, StyleImageOf(new List<int> { styleB }),
          ActionImageOf(new List<int> { infA }));

      // Шаг 3: воздействие.
      var step3 = Clone(Genetic.GetGeneticReflex(reflexId));
      step3.InfluenceActionIds = new List<int> { infB };
      Genetic.UpdateGeneticReflex(step3);
      AssertFound(reflexId, 1, StyleImageOf(new List<int> { styleB }),
          ActionImageOf(new List<int> { infB }));

      // Шаг 4:回到 состояние «Норма» без внешнего триггера.
      var step4 = Clone(Genetic.GetGeneticReflex(reflexId));
      step4.Level1 = 0;
      step4.InfluenceActionIds = new List<int>();
      Genetic.UpdateGeneticReflex(step4);
      AssertFound(reflexId, 0, StyleImageOf(new List<int> { styleB }), 0);
    }

    // ======================================================================
    // Поля, не участвующие в условиях запуска — поиск не должен ломаться
    // ======================================================================

    [Fact]
    public void UpdateAdaptiveActions_NodeKeepsReflex()
    {
      int style = CreateStyle("Style_Act");
      int motorA = CreateMotorAction("Action_Act_A");
      int motorB = CreateMotorAction("Action_Act_B");

      var (reflexId, _) = Genetic.AddGeneticReflex(0, new List<int> { style },
          null, null, new List<int> { motorA });

      int styleImage = StyleImageOf(new List<int> { style });
      AssertFound(reflexId, 0, styleImage, 0);

      var updated = Clone(Genetic.GetGeneticReflex(reflexId));
      updated.AdaptiveActions = new List<int> { motorB };
      Genetic.UpdateGeneticReflex(updated);

      AssertFound(reflexId, 0, styleImage, 0);
    }

    [Fact]
    public void UpdateReflexChainId_NodeKeepsReflex()
    {
      int style = CreateStyle("Style_Chain");
      int motor = CreateMotorAction("Action_Chain");

      var (reflexId, _) = Genetic.AddGeneticReflex(0, new List<int> { style },
          null, null, new List<int> { motor });

      int styleImage = StyleImageOf(new List<int> { style });

      var updated = Clone(Genetic.GetGeneticReflex(reflexId));
      updated.ReflexChainID = 7;
      Genetic.UpdateGeneticReflex(updated);

      AssertFound(reflexId, 0, styleImage, 0);
    }

    // ======================================================================
    // Мутация объекта из GetGeneticReflex (баг исправлен)
    // ======================================================================

    /// <summary>
    /// Регресс на дефект: <c>GetGeneticReflex</c> возвращает ссылку на экземпляр из словаря
    /// <c>_geneticReflexes</c>. При изменении условий запуска у этого же экземпляра и передаче
    /// его в <c>UpdateGeneticReflex</c> (путь UI) «старая» копия внутри метода совпадала
    /// с «новой», <c>conditionsChanged == false</c>, ветка «удалить и пересоздать» не выполнялась,
    /// события <c>GeneticReflexDeleted</c>/<c>GeneticReflexCreated</c> не порождались, и в дереве
    /// оставался ПРЕЖНИЙ узел с прежними BaseID/StyleID/ActionID → рефлекс не находился по новым
    /// условиям. Исправлено: <c>UpdateGeneticReflex</c> сравнивает условия с замороженным снимком
    /// (<c>_geneticReflexTriggerKeys</c>), который меняет только сама система.
    /// </summary>
    [Fact]
    public void UpdateConditions_DirectMutation_ReflexFoundByNewConditions()
    {
      int styleA = CreateStyle("Bug_Style_A");
      int styleB = CreateStyle("Bug_Style_B");
      int motor = CreateMotorAction("Bug_Action");

      var (reflexId, _) = Genetic.AddGeneticReflex(0, new List<int> { styleA },
          null, null, new List<int> { motor });

      int oldStyleImage = StyleImageOf(new List<int> { styleA });
      AssertFound(reflexId, 0, oldStyleImage, 0);

      var stored = Genetic.GetGeneticReflex(reflexId);
      stored.Level1 = 1;
      stored.Level2 = new List<int> { styleB };
      Genetic.UpdateGeneticReflex(stored);

      int newStyleImage = StyleImageOf(new List<int> { styleB });

      // Узел пересоздан и рефлекс находится по новым условиям…
      var (foundId, foundNode) = Tree.FindReflexTreeNodeFromCondition(1, newStyleImage, 0);
      Assert.True(foundId > 0, "По новым условиям [Base=1] рефлекс не найден — узел остался прежним");
      Assert.Equal(reflexId, foundNode.GeneticReflexID);

      // …прежний узел его больше не держит.
      AssertNotBoundToReflex(reflexId, 0, oldStyleImage, 0);
    }

    /// <summary>
    /// Мутация по ссылке + только внешнее воздействие (Level3): образ пускового стимула меняется,
    /// рефлекс должен найтись по новому ActionID, а по старому — нет.
    /// </summary>
    [Fact]
    public void UpdateInfluence_DirectMutation_ReflexFoundByNewActionImage()
    {
      int style = CreateStyle("BugInf_Style");
      int motor = CreateMotorAction("BugInf_Action");
      int infA = CreateInfluenceAction("BugInf_A");
      int infB = CreateInfluenceAction("BugInf_B");

      var (reflexId, _) = Genetic.AddGeneticReflex(0, new List<int> { style },
          new List<int> { infA }, null, new List<int> { motor });

      int styleImage = StyleImageOf(new List<int> { style });
      AssertFound(reflexId, 0, styleImage, ActionImageOf(new List<int> { infA }));

      var stored = Genetic.GetGeneticReflex(reflexId);
      stored.InfluenceActionIds = new List<int> { infB };
      Genetic.UpdateGeneticReflex(stored);

      AssertFound(reflexId, 0, styleImage, ActionImageOf(new List<int> { infB }));
      AssertNotBoundToReflex(reflexId, 0, styleImage, ActionImageOf(new List<int> { infA }));
    }

    /// <summary>
    /// Мутация по ссылке, НЕ затрагивающая условия запуска (только моторные действия):
    /// <c>conditionsChanged</c> остаётся false, прежний узел должен сохранить привязку к рефлексу.
    /// </summary>
    [Fact]
    public void UpdateAdaptiveActions_DirectMutation_NodeKeepsReflex()
    {
      int style = CreateStyle("BugAct_Style");
      int motorA = CreateMotorAction("BugAct_A");
      int motorB = CreateMotorAction("BugAct_B");

      var (reflexId, _) = Genetic.AddGeneticReflex(0, new List<int> { style },
          null, null, new List<int> { motorA });

      int styleImage = StyleImageOf(new List<int> { style });
      AssertFound(reflexId, 0, styleImage, 0);

      var stored = Genetic.GetGeneticReflex(reflexId);
      stored.AdaptiveActions = new List<int> { motorB };
      Genetic.UpdateGeneticReflex(stored);

      AssertFound(reflexId, 0, styleImage, 0);
    }
  }
}
