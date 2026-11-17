using System.Collections.Generic;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Psychic.Automatism;
using ISIDA.Reflexes;
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
      // Селективный клон (путь A) читает Level3 сработавшего б/у рефлекса через
      // GeneticReflexesSystem — в обход IsidaEngine ссылку подключаем явно.
      PurposeGeneticImageSystem.Instance.SetGeneticReflexesSystem(GeneticReflexesSystem.Instance);
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
    public void GetAutomatizmByGeneticPurpose_Stage3_PathAClosed_DoesNotCreateClone()
    {
      // Селективный клон (путь A) живёт ТОЛЬКО на 2-й стадии: на 3-й путь A закрыт гейтом
      // EvolutionStage == 2, даже при наличии повода (здесь — признак условного). Ниже блока A
      // идёт «if (EvolutionStage != 2) return null», поэтому на 3-й возвращается null.
      AppGlobalState.EvolutionStage = 3;
      AppGlobalState.FlgConditionReflexes = true;
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int> { _actionId }, geneticReflexId: Engine.SeedGeneticReflexId);

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.Null(atmz);
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

    #region Путь A: клон по безусловному рефлексу только при наличии триггера Level3 (EA/Command)

    /// <summary>Создаёт на стадии 0 безусловный рефлекс (с Level3=EA либо без него).</summary>
    private int CreateGeneticReflex(int influenceActionId)
    {
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        // Уникальный стиль: ключ дубля б/у рефлекса (Level1|Level2|EA|Command|VisualColor) не
        // учитывает мотор, поэтому без своего стиля рефлекс без EA столкнулся бы с эталонным.
        var (styleId, _) = GomeostasSystem.Instance.AddBehaviorStyle(
            "Тест-стиль-клон-" + Engine.NextSeed(), "");
        var influenceIds = influenceActionId > 0
            ? new List<int> { influenceActionId }
            : null;
        var (reflexId, _) = GeneticReflexesSystem.Instance.AddGeneticReflex(
            1, new List<int> { styleId }, influenceIds, null,
            new List<int> { _actionId });
        Assert.True(reflexId > 0, "Б/у рефлекс не создан");
        return reflexId;
      }
      finally
      {
        AppGlobalState.EvolutionStage = 2;
      }
    }

    /// <summary>Создаёт внешнее воздействие (EA) на стадии 0.</summary>
    private int CreateInfluenceAction()
    {
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        var (id, _) = InfluenceActionSystem.Instance.AddInfluenceAction(
            "Тест-EA-клон-" + Engine.NextSeed(), "Воздействие для теста Level3", null, null);
        Assert.True(id > 0, "EA не создан");
        return id;
      }
      finally
      {
        AppGlobalState.EvolutionStage = stage;
      }
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_Stage2_GeneticReflexWithoutLevel3_DoesNotCreateClone()
    {
      // Регресс мусорных автоматизмов: б/у рефлекс без триггера Level3 (ни EA, ни Command) отобран
      // только по состоянию/стилям — клонировать его в автоматизм нельзя.
      int reflexNoLevel3 = CreateGeneticReflex(influenceActionId: 0);

      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.CurActiveVerbalId = 0;
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int>());
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int> { _actionId }, geneticReflexId: reflexNoLevel3);
      // Отключаем случайную пробу, чтобы изолированно проверить именно путь A.
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
    public void GetAutomatizmByGeneticPurpose_Stage2_GeneticReflexWithLevel3_CreatesClone()
    {
      // Б/у рефлекс с Level3 (EA) — валидный повод: клон создаётся с мотором рефлекса (имеет recipe).
      int ea = CreateInfluenceAction();
      int reflexWithLevel3 = CreateGeneticReflex(influenceActionId: ea);

      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.CurActiveVerbalId = 0;
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int>());
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int> { _actionId }, geneticReflexId: reflexWithLevel3);

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.NotNull(atmz);
      Assert.NotNull(Autom.GetAutomatizmById(atmz.ID));
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_Stage2_VeryActual_CreatesCloneEvenWithoutReflexLevel3()
    {
      // Опасная ситуация — самостоятельный повод: Level3 у б/у рефлекса не требуется
      // (иначе сломается реакция на опасность). Рефлекс намеренно без EA/Command.
      int reflexNoLevel3 = CreateGeneticReflex(influenceActionId: 0);

      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.CurActiveVerbalId = 0;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int>());
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int> { _actionId }, geneticReflexId: reflexNoLevel3);
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(true);
      try
      {
        var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

        Assert.NotNull(atmz);
      }
      finally
      {
        InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);
      }
    }

    #endregion

    #region Стадия 3: условный рефлекс обязан дать клон с мотором (регресс бага «только вербальная часть»)

    /// <summary>
    /// Создаёт на стадии 0 действие G_AD с целевым параметром (имеет recipe-слот).
    /// </summary>
    private int CreateActionWithRecipe()
    {
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        var (paramId, _) = GomeostasSystem.Instance.AddParameter(
            "Тест-параметр-ст3-" + Engine.NextSeed(), "", 50f, 50, 50, 1);
        var (actionId, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Тест-G_AD-ст3-" + Engine.NextSeed(), "", targetGomeoParamIdArr: new List<int> { paramId }, Vigor: 5);
        return actionId;
      }
      finally
      {
        AppGlobalState.EvolutionStage = stage;
      }
    }

    /// <summary>
    /// Создаёт на стадии 0 действие G_AD без целевого параметра (нет recipe-слота).
    /// </summary>
    private int CreateActionWithoutRecipe()
    {
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        var (actionId, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Тест-G_AD-без-рецепта-ст3-" + Engine.NextSeed(), "", targetGomeoParamIdArr: new List<int>(), Vigor: 5);
        return actionId;
      }
      finally
      {
        AppGlobalState.EvolutionStage = stage;
      }
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_Stage2_ConditionalReflex_CreatesCloneWithConditionalMotor()
    {
      // Регресс случая 46 (перенесён на 2-ю стадию, т.к. путь A теперь только на 2-й):
      // при сработавшем условном клон обязан взять мотор условного (с рецептом), а не дефолт.
      // Дефолт намеренно БЕЗ рецепта — иначе путь A резолвил бы дефолт и отбраковывал клон
      // («no recipe slots») → Assert.NotNull падал. С корректным резолвом берётся мотор условного.
      int motorWithRecipe = CreateActionWithRecipe();
      int defaultNoRecipe = CreateActionWithoutRecipe();

      AppGlobalState.EvolutionStage = 2;
      AppGlobalState.DefaultAdaptiveActionId = defaultNoRecipe;
      AppGlobalState.FlgConditionReflexes = true;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int> { motorWithRecipe });

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.NotNull(atmz);
      Assert.NotNull(Autom.GetAutomatizmById(atmz.ID));
    }

    [Fact]
    public void GetActiveAdaptiveActionsOfReflexes_ConditionalMotor_IsReturned()
    {
      // Корень случая 46: GetActiveAdaptiveActionsOfReflexes обязан вернуть мотор условного,
      // иначе путь A подставит DefaultAdaptiveActionId (в Velum — G_AD без рецепта).
      // Читается на любой стадии, поэтому проверяем независимо от гейта клона.
      AppGlobalState.EvolutionStage = 2;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int> { _actionId });

      var actions = PurposeGeneticImageSystem.Instance.GetActiveAdaptiveActionsOfReflexes();

      Assert.Contains(_actionId, actions);
    }

    [Fact]
    public void OrientationReflex1_Stage2_WithConditionalFlag_CreatesClone()
    {
      // Гейт ОР1 (случай 46, перенесён на 2-ю стадию): при признаке условного ОР1 обязан вызвать
      // путь A (иначе создаётся вербальный попугай-эхо без мотора). Мотор условного — с рецептом.
      EnsureOrientationSystem();
      int motorWithRecipe = CreateActionWithRecipe();

      AppGlobalState.EvolutionStage = 2;
      AppGlobalState.FlgConditionReflexes = true;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int> { motorWithRecipe });

      var atmz = OrientationReflexSystem.Instance.OrientationReflex_1(0, 0);

      Assert.NotNull(atmz);
    }

    [Fact]
    public void OrientationReflex1_Stage3_CleanPhrase_WithoutConditionalFlag_ReturnsNull()
    {
      // Регресс: без условного на чистой фразе ОР1 на 3-й стадии НЕ должен вызывать путь A
      // (диалоговое зеркало/попугай обрабатывается ниже в PsychicSystem). Иначе ломается штатный диалог.
      EnsureOrientationSystem();

      AppGlobalState.EvolutionStage = 3;
      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int>());
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int>());
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);

      var atmz = OrientationReflexSystem.Instance.OrientationReflex_1(0, 0);

      Assert.Null(atmz);
    }

    #endregion

    #region Инфраструктура: ОР1

    /// <summary>Идемпотентно поднимает <see cref="OrientationReflexSystem"/> поверх фикстуры.</summary>
    private void EnsureOrientationSystem()
    {
      EnsureAutomatismSystems();
      EnsurePurposeSystem();
      if (!OrientationReflexSystem.IsInitialized)
      {
        OrientationReflexSystem.InitializeInstance(
            InformationEnvironmentSystem.Instance,
            PurposeGeneticImageSystem.Instance);
      }
      if (!OrientationReflexSystem.Instance.AreDependenciesSet)
        OrientationReflexSystem.Instance.SetDependencies(
            AutomatizmSystem.Instance,
            AutomatizmTreeSystem.Instance);
    }

    #endregion
  }
}
