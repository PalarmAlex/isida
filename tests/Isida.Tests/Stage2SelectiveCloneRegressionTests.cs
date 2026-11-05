using System.Collections.Generic;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Psychic.Automatism;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регрессионные тесты для Случая 36: селективный клон рефлекса в автоматизм (путь A)
  /// доступен начиная со 2-й стадии.
  /// <para>
  /// Коммит режима наблюдения (<c>65ca0ca</c>) добавил в путь A гейт <c>EvolutionStage &gt;= 3</c>
  /// («на стадии 2 клонирование запрещено»). После удаления сессии наблюдения (<c>9885359</c>)
  /// гейт остался хвостом и закрыл клон на 2-й стадии: в Velum (стадия 2) рефлекс срабатывал,
  /// но автоматизм не создавался, тогда как в студии (стадия 3+) — создавался при тех же условиях.
  /// </para>
  /// Проверяется поведение через <see cref="PurposeGeneticImageSystem.GetAutomatizmByGeneticPurpose"/>:
  /// на 2-й стадии при признаке сработавшего условного рефлекса возвращается клон-автоматизм.
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class Stage2SelectiveCloneRegressionTests : EngineIntegrationTestBase
  {
    private readonly int _actionId;
    private readonly int _paramId;

    public Stage2SelectiveCloneRegressionTests(EngineFixture engine) : base(engine)
    {
      EnsureAutomatismSystems();
      EnsurePurposeSystem();

      // Действие-клон создаётся только на стадии 0; оно обязано иметь целевой параметр
      // гомеостаза, иначе CreateAutomatizmByGeneticPurpose отбрасывает клон («no recipe slots»).
      (_paramId, _actionId) = CreateActionTargetingParameter();

      ResetStage2State();
    }

    private static AutomatizmSystem Autom => AutomatizmSystem.Instance;

    #region Поднятие подсистем и подготовка данных

    /// <summary>
    /// Идемпотентно поднимает Automatism-подсистемы поверх временного каталога фикстуры.
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

      if (AutomatizmTreeSystem.Instance.Tree.Children.Count == 0)
        AutomatizmTreeSystem.Instance.CreateBasicAutomatizmTree();
    }

    /// <summary>Идемпотентно поднимает <see cref="PurposeGeneticImageSystem"/>.</summary>
    private void EnsurePurposeSystem()
    {
      if (!PurposeGeneticImageSystem.IsInitialized)
      {
        PurposeGeneticImageSystem.InitializeInstance(
            InformationEnvironmentSystem.Instance,
            ActionsImagesSystem.Instance,
            AutomatizmSystem.Instance,
            AdaptiveActionsSystem.Instance);
      }
    }

    /// <summary>На стадии 0 заводит параметр гомеостаза и G_AD, нацеленный на него.</summary>
    private (int ParamId, int ActionId) CreateActionTargetingParameter()
    {
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        var (paramId, _) = GomeostasSystem.Instance.AddParameter(
            "Тест-параметр-клон-" + Engine.NextSeed(), "", 50f, 50, 50, 1);
        var (actionId, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Тест-G_AD-клон-" + Engine.NextSeed(), "", targetGomeoParamIdArr: new List<int> { paramId }, Vigor: 5);
        return (paramId, actionId);
      }
      finally
      {
        AppGlobalState.EvolutionStage = stage;
      }
    }

    /// <summary>Готовит «чистый лист» 2 стадии: стадия 2, пустой реестр автоматизмов, сброшенные флаги стимула.</summary>
    private void ResetStage2State()
    {
      AppGlobalState.EvolutionStage = 2;
      if (AutomatizmSystem.IsInitialized)
        AutomatizmSystem.Instance.DeleteAllAutomatizm();
      if (AutomatismResultTracker.IsInitialized)
        AutomatismResultTracker.Instance.ClearHistory();

      AppGlobalState.DominantParam = _paramId;
      AppGlobalState.AutomatizmNodeId = 0;
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      AppGlobalState.CurActiveVerbalId = 0;
      AppGlobalState.FlgConditionReflexes = false;
      // На стадии >= 3 список действий цели пуст (GetActiveAdaptiveActionsOfReflexes) и цель
      // строится из DefaultAdaptiveActionId — делаем его действием с рецептом, чтобы Stage3-тест
      // проверял именно проходимость клона по стадии, а не отсутствие рецепта у дефолта.
      AppGlobalState.DefaultAdaptiveActionId = _actionId;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int>());
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int>());
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);
      GlobalTimer.Reset();
    }

    #endregion

    #region Путь A на 2-й стадии

    [Fact]
    public void GetAutomatizmByGeneticPurpose_Stage2_ReflexFired_CreatesClone()
    {
      // Сработавший условный рефлекс: признак FlgConditionReflexes + активное действие УР.
      AppGlobalState.FlgConditionReflexes = true;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int> { _actionId });

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.NotNull(atmz);
      // Клон реально попал в реестр автоматизмов движка.
      Assert.NotNull(Autom.GetAutomatizmById(atmz.ID));
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_Stage2_NoReflex_DoesNotCreateClone()
    {
      // Регресс бага №1: без повода на создании клон не порождается. Повод — опасная ситуация,
      // рвущийся на выполнение безусловный рефлекс (CurrentGeneticReflexID > 0) либо признак
      // условного (FlgConditionReflexes). Ранее скобка пути A «|| CurActiveVerbalId == 0»
      // размыкала условие на любую голую смену контекста/метрики, порождая ложный автоматизм
      // (сценарий «-60»: чистое отпускание метрики создавало автоматизм id=2 без триггера).
      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.CurActiveVerbalId = 0;
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int>());
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int>());
      // Отключаем механизм 3 (случайную пробу) — у неё свои гейты (стили Поиск/Игра + пауза).
      // После GlobalTimer.Reset() пульс = 0, а waitPeriod > 0 ⇒ пауза активна ⇒ проба пропускается.
      // Иначе проба создала бы автоматизм и тест перестал бы изолированно проверять путь A.
      int savedWaitPeriod = AppGlobalState.WaitingPeriodForActionsVal;
      AppGlobalState.WaitingPeriodForActionsVal = 30;
      try
      {
        var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

        Assert.Null(atmz);
      }
      finally
      {
        AppGlobalState.WaitingPeriodForActionsVal = savedWaitPeriod;
      }
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_Stage3_StillCreatesClone()
    {
      // Регресс не должен сузить клон до ровно 2-й стадии: на 3-й и выше он работал и раньше.
      // На стадии >= 3 источник действий цели — генетические рефлексы (см. GetActiveAdaptiveActionsOfReflexes).
      AppGlobalState.EvolutionStage = 3;
      AppGlobalState.FlgConditionReflexes = true;
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int> { _actionId }, geneticReflexId: Engine.SeedGeneticReflexId);

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.NotNull(atmz);
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_Stage2_CloneSkippedWhenActionHasNoRecipe()
    {
      // Страховка, что фикс не отключил смысловую проверку клона: G_AD без целевого параметра
      // (нет recipe) не должен порождать автоматизм даже на 2-й стадии.
      int actionNoRecipe;
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        (actionNoRecipe, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Тест-G_AD-без-рецепта-" + Engine.NextSeed(), "", targetGomeoParamIdArr: new List<int>(), Vigor: 5);
      }
      finally
      {
        AppGlobalState.EvolutionStage = 2;
      }

      AppGlobalState.FlgConditionReflexes = true;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int> { actionNoRecipe });

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.Null(atmz);
    }

    #endregion
  }
}
