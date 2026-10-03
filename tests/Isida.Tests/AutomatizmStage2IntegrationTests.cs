using System;
using System.Collections.Generic;
using System.Linq;
using ISIDA.Common;
using ISIDA.Psychic;
using ISIDA.Psychic.Automatism;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Интеграционные тесты 2 стадии эволюции: реестр автоматизмов, базовое дерево
  /// автоматизмов, инвариант штатного автоматизма (Belief=2) и трекер результатов.
  /// <para>
  /// Automatism-подсистемы (<see cref="ActionsImagesSystem"/>,
  /// <see cref="InfluenceActionsImagesSystem"/>, <see cref="AutomatizmTreeSystem"/>,
  /// <see cref="AutomatizmSystem"/>, <see cref="AutomatismResultTracker"/>) не входят в
  /// базовую <see cref="EngineFixture"/>, поэтому поднимаются здесь идемпотентно
  /// (<c>IsInitialized</c>-гарды) поверх того же временного каталога данных. Синглтоны
  /// статические, коллекция прогоняется последовательно (<see cref="EngineIntegrationCollection"/>),
  /// поэтому изоляция обеспечивается сбросом состояния реестра в <see cref="ResetStage2State"/>.
  /// </para>
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class AutomatizmStage2IntegrationTests : EngineIntegrationTestBase
  {
    public AutomatizmStage2IntegrationTests(EngineFixture engine) : base(engine)
    {
      EnsureAutomatismSystems();
      ResetStage2State();
    }

    private static AutomatizmSystem Autom => AutomatizmSystem.Instance;
    private static AutomatizmTreeSystem AutomatizmTree => AutomatizmTreeSystem.Instance;
    private static AutomatismResultTracker Tracker => AutomatismResultTracker.Instance;

    /// <summary>
    /// Идемпотентно поднимает Automatism-подсистемы поверх временного каталога фикстуры
    /// и гарантирует наличие базового дерева (3 корневых узла базовых состояний).
    /// </summary>
    private void EnsureAutomatismSystems()
    {
      if (!ActionsImagesSystem.IsInitialized)
        ActionsImagesSystem.InitializeInstance(Engine.DataDir);
      if (!InfluenceActionsImagesSystem.IsInitialized)
        InfluenceActionsImagesSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatizmTreeSystem.IsInitialized)
        AutomatizmTreeSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatizmSystem.IsInitialized)
        AutomatizmSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatismResultTracker.IsInitialized)
        AutomatismResultTracker.InitializeInstance(AutomatizmSystem.Instance);

      // CreateBasicAutomatizmTree() создаёт узлы с checkUnicum:false, поэтому повторный вызов
      // дублирует базовые ветки. Гарантируем создание только для пустого дерева.
      if (AutomatizmTree.Tree.Children.Count == 0)
        AutomatizmTree.CreateBasicAutomatizmTree();
    }

    /// <summary>
    /// Готовит «чистый лист» 2 стадии перед тестом: переход на стадию 2,
    /// удаление всех автоматизмов и очистка истории трекера.
    /// </summary>
    private void ResetStage2State()
    {
      AppGlobalState.EvolutionStage = 2;
      if (AutomatizmSystem.IsInitialized)
        AutomatizmSystem.Instance.DeleteAllAutomatizm();
      if (AutomatismResultTracker.IsInitialized)
        AutomatismResultTracker.Instance.ClearHistory();
    }

    /// <summary>Создаёт автоматизм с уникальной веткой и ненулевым образом действий.</summary>
    private (int Id, Automatizm Automatizm) NewAutomatizm(
        int? branchId = null,
        AutomatizmConsolidationService.AutomatizmCreationRole role =
            AutomatizmConsolidationService.AutomatizmCreationRole.Default)
    {
      return Autom.CreateNewAutomatizm(
          branchId ?? Engine.NextSeed(),
          Engine.NextSeed(),
          checkUnicum: false,
          role);
    }

    #region Стадийность

    [Fact]
    public void CreateNewAutomatizm_BelowStage2_Throws()
    {
      AppGlobalState.EvolutionStage = 1;
      try
      {
        Assert.Throws<InvalidOperationException>(
            () => Autom.CreateNewAutomatizm(Engine.NextSeed(), Engine.NextSeed(), false));
      }
      finally
      {
        AppGlobalState.EvolutionStage = 2;
      }
    }

    [Fact]
    public void SetAutomatizmBelief_BelowStage2_Throws()
    {
      // Автоматизм создаём на стадии 2, затем опускаем стадию и пробуем назначить штатность.
      var (_, automatizm) = NewAutomatizm();
      AppGlobalState.EvolutionStage = 1;
      try
      {
        Assert.Throws<InvalidOperationException>(
            () => Autom.SetAutomatizmBelief(automatizm, 2));
      }
      finally
      {
        AppGlobalState.EvolutionStage = 2;
      }
    }

    #endregion

    #region Стартовая полезность по роли

    [Fact]
    public void CreateNewAutomatizm_Stage2_DefaultRole_UsefulnessIsThree()
    {
      var (id, automatizm) = NewAutomatizm();

      Assert.True(id > 0, "автоматизм не создан");
      Assert.Equal(3, automatizm.Usefulness);
    }

    [Fact]
    public void CreateNewAutomatizm_Stage2_ShiftRole_UsefulnessIsThree()
    {
      var (id, automatizm) = NewAutomatizm(
          role: AutomatizmConsolidationService.AutomatizmCreationRole.Shift);

      Assert.True(id > 0);
      Assert.Equal(3, automatizm.Usefulness);
    }

    [Fact]
    public void CreateNewAutomatizm_EchoRole_UsefulnessIsZero()
    {
      var (id, automatizm) = NewAutomatizm(
          role: AutomatizmConsolidationService.AutomatizmCreationRole.Echo);

      Assert.True(id > 0);
      Assert.Equal(0, automatizm.Usefulness);
    }

    [Fact]
    public void CreateNewAutomatizm_ZeroActionsImage_ReturnsEmpty()
    {
      var (id, automatizm) = Autom.CreateNewAutomatizm(
          Engine.NextSeed(), actionsImageId: 0, checkUnicum: false);

      Assert.Equal(0, id);
      Assert.Null(automatizm);
    }

    [Fact]
    public void CreateNewAutomatizm_CheckUnicum_ReturnsSameAutomatizm()
    {
      int branch = Engine.NextSeed();
      int actionsImage = Engine.NextSeed();

      var (id1, a1) = Autom.CreateNewAutomatizm(branch, actionsImage, checkUnicum: true);
      var (id2, a2) = Autom.CreateNewAutomatizm(branch, actionsImage, checkUnicum: true);

      Assert.True(id1 > 0);
      Assert.Equal(id1, id2);
      Assert.Same(a1, a2);
    }

    #endregion

    #region Инвариант штатного автоматизма (Belief = 2)

    [Fact]
    public void SetAutomatizmBelief_KeepsSingleStaffPerBranch()
    {
      int branch = Engine.NextSeed();
      var (_, first) = NewAutomatizm(branch);
      var (_, second) = NewAutomatizm(branch);

      Autom.SetAutomatizmBelief(first, 2);
      Assert.Equal(2, first.Belief);

      // Назначение штатности второму снимает её с первого (инвариант: ≤1 Belief=2 на ветку).
      Autom.SetAutomatizmBelief(second, 2);
      Assert.Equal(2, second.Belief);
      Assert.Equal(0, first.Belief);
    }

    [Fact]
    public void SetAutomatizmBelief_DoesNotTouchBeliefOneNeighbors()
    {
      int branch = Engine.NextSeed();
      var (_, neighbor) = NewAutomatizm(branch);
      var (_, staff) = NewAutomatizm(branch);

      Autom.SetAutomatizmBelief(neighbor, 1);
      Autom.SetAutomatizmBelief(staff, 2);

      // Сосед с Belief=1 не является штатным (2), поэтому при назначении штатности не меняется.
      Assert.Equal(1, neighbor.Belief);
      Assert.Equal(2, staff.Belief);
    }

    #endregion

    #region Отрицательная полезность → удаление на стадии 2

    [Fact]
    public void AfterUsefulnessUpdated_NegativeUsefulness_DeletesAtStage2()
    {
      var (id, automatizm) = NewAutomatizm();

      automatizm.Usefulness = -1;
      Autom.AfterAutomatizmUsefulnessUpdated(id);

      Assert.Null(Autom.GetAutomatizmById(id));
    }

    [Fact]
    public void AfterUsefulnessUpdated_NonNegativeUsefulness_KeepsAutomatizm()
    {
      var (id, automatizm) = NewAutomatizm();

      automatizm.Usefulness = 0;
      Autom.AfterAutomatizmUsefulnessUpdated(id);

      Assert.NotNull(Autom.GetAutomatizmById(id));
    }

    #endregion

    #region Трекер результатов

    [Fact]
    public void Tracker_StartTracking_ExistingAutomatizm_ReturnsResult()
    {
      var (id, automatizm) = NewAutomatizm();

      var result = Tracker.StartTracking(id, automatizm.BranchID, automatizm.ActionsImageID);

      Assert.NotNull(result);
      Assert.Equal(id, result.AutomatizmId);
      Assert.True(result.StartPulse >= 0);
    }

    [Fact]
    public void Tracker_StartTracking_UnknownAutomatizm_ReturnsNull()
    {
      var result = Tracker.StartTracking(999_999, branchId: 1, actionsImageId: 1);

      Assert.Null(result);
    }

    [Fact]
    public void Tracker_FinishTracking_SetsEndPulse()
    {
      var (id, automatizm) = NewAutomatizm();
      var result = Tracker.StartTracking(id, automatizm.BranchID, automatizm.ActionsImageID);

      Tracker.FinishTracking(result);

      Assert.True(result.EndPulse >= result.StartPulse);
    }

    #endregion

    #region Базовое дерево автоматизмов

    [Fact]
    public void BasicTree_HasThreeRootChildren()
    {
      // Базовое дерево гарантировано конструктором (EnsureAutomatismSystems).
      // Повторный CreateBasicAutomatizmTree() создаёт дубликаты (checkUnicum:false), поэтому не вызываем его здесь.
      Assert.Equal(3, AutomatizmTree.Tree.Children.Count);
    }

    [Fact]
    public void CreateNewAutomatizmNode_UniqueCondition_AddsNode()
    {
      int baseId = Engine.NextSeed();

      var (id, node) = AutomatizmTree.CreateNewAutomatizmNode(
          AutomatizmTree.Tree, id: 0, baseId: baseId, emotionId: 1,
          activityId: 0, toneMoodId: 0, simbolId: 0, verbID: 0,
          commandID: 0, visualID: 0, checkUnicum: true);

      Assert.True(id > 0, "узел дерева не создан");
      Assert.NotNull(node);
      Assert.Equal(baseId, node.BaseID);
    }

    [Fact]
    public void FindAutomatizmTreeNodeFromCondition_ReturnsCreatedNode()
    {
      int baseId = Engine.NextSeed();
      var (createdId, _) = AutomatizmTree.CreateNewAutomatizmNode(
          AutomatizmTree.Tree, id: 0, baseId: baseId, emotionId: 1,
          activityId: 0, toneMoodId: 0, simbolId: 0, verbID: 0,
          commandID: 0, visualID: 0, checkUnicum: true);

      var (foundId, foundNode) = AutomatizmTree.FindAutomatizmTreeNodeFromCondition(
          baseId, emotionId: 1, activityId: 0, toneMoodId: 0,
          simbolId: 0, verbID: 0, commandID: 0, visualID: 0);

      Assert.Equal(createdId, foundId);
      Assert.NotNull(foundNode);
    }

    [Fact]
    public void CreateNewAutomatizmNode_CheckUnicum_ReturnsSameNode()
    {
      int baseId = Engine.NextSeed();

      var (id1, _) = AutomatizmTree.CreateNewAutomatizmNode(
          AutomatizmTree.Tree, id: 0, baseId: baseId, emotionId: 1,
          activityId: 0, toneMoodId: 0, simbolId: 0, verbID: 0,
          commandID: 0, visualID: 0, checkUnicum: true);
      var (id2, _) = AutomatizmTree.CreateNewAutomatizmNode(
          AutomatizmTree.Tree, id: 0, baseId: baseId, emotionId: 1,
          activityId: 0, toneMoodId: 0, simbolId: 0, verbID: 0,
          commandID: 0, visualID: 0, checkUnicum: true);

      Assert.Equal(id1, id2);
    }

    #endregion
  }
}
