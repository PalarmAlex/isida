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
  /// Интеграционные тесты для пути M (зеркальный автоматизм по связи InfluenceActionId → AdaptiveAction)
  /// на 2-й стадии эволюции.
  /// <para>
  /// Путь M: когда стимул с пульта содержит InfluenceAction, но на него не сработал рефлекс,
  /// мотор берётся из справочника AdaptiveActions по полю InfluenceActionId. Цепочка не переносится.
  /// </para>
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class MirrorAutomatizmServiceTests : EngineIntegrationTestBase
  {
    private readonly int _actionId;
    private readonly int _influenceActionId;
    private readonly int _nonMirrorActionId;
    private readonly int _commandPatternId;
    private readonly int _commandMirrorActionId;
    private readonly int _nonCommandMirrorActionId;
    private readonly int _paramId;
    private readonly MirrorAutomatizmService _mirrorService;
    private readonly AutomatizmSystem _autom;

    public MirrorAutomatizmServiceTests(EngineFixture engine) : base(engine)
    {
      EnsureAutomatismSystems();
      _autom = AutomatizmSystem.Instance;
      // Создаём MirrorAutomatizmService напрямую (PsychicSystem не поднимается).
      _mirrorService = new MirrorAutomatizmService(_autom);
      EnsurePurposeSystem();

      // Действие с InfluenceActionId (зеркальное) — создаём на стадии 0.
      // Целевой параметр обязателен: путь R (клон) отбрасывает G_AD без recipe-слота.
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        (_paramId, _) = GomeostasSystem.Instance.AddParameter(
            "Тест-параметр-зеркало-" + Engine.NextSeed(), "", 50f, 50, 50, 1);
        (_actionId, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Зеркальное действие", "", targetGomeoParamIdArr: new List<int> { _paramId },
            Vigor: 5, influenceActionId: 0);
        // InfluenceAction с пульта — уникальное, не пересекается с seed.
        _influenceActionId = Engine.NextSeed();
        // Временное действие без InfluenceActionId (для проверки отсутствия связи).
        (_nonMirrorActionId, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Не зеркальное действие", "", Vigor: 5);

        // Командный канал: действие со связью CommandPatternId (зеркальное по команде).
        // Уникальный ID паттерна команды, не пересекается с seed.
        _commandPatternId = Engine.NextSeed();
        (_commandMirrorActionId, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Командное зеркальное действие", "", targetGomeoParamIdArr: new List<int> { _paramId },
            Vigor: 5, commandPatternId: _commandPatternId);
        // Действие без связи CommandPatternId (для проверки отсутствия командной связи).
        (_nonCommandMirrorActionId, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Не командное действие", "", Vigor: 5);
      }
      finally
      {
        AppGlobalState.EvolutionStage = stage;
      }

      // Связь: AdaptiveAction с InfluenceActionId = _influenceActionId.
      AdaptiveActionsSystem.Instance.GetAdaptiveAction(_actionId).InfluenceActionId = _influenceActionId;

      ResetStage2State();
    }

    #region Поднятие подсистем и подготовка

    /// <summary>
    /// Идемпотентно поднимает Automatism-подсистемы поверх временного каталога фикстуры.
    /// EngineFixture их не инициализирует — см. Stage2SelectiveCloneRegressionTests.
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

    /// <summary>
    /// Идемпотентно поднимает <see cref="PurposeGeneticImageSystem"/> и связывает с ним
    /// зеркальный сервис (путь M требует <c>_mirrorAutomatizmService</c>).
    /// </summary>
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
      // Путь M в GetAutomatizmByGeneticPurpose читает _mirrorAutomatizmService.
      PurposeGeneticImageSystem.Instance.SetStage2EchoDependencies(
          _mirrorService,
          VerbalBrocaImagesSystem.IsInitialized ? VerbalBrocaImagesSystem.Instance : null,
          null);
    }

    private void ResetStage2State()
    {
      AppGlobalState.EvolutionStage = 2;
      if (AutomatizmSystem.IsInitialized)
        AutomatizmSystem.Instance.DeleteAllAutomatizm();
      if (AutomatismResultTracker.IsInitialized)
        AutomatismResultTracker.Instance.ClearHistory();

      AppGlobalState.AutomatizmNodeId = Engine.NextSeed(); // любой > 0
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      AppGlobalState.CurActiveVerbalId = 0;
      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int>());
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int>());
      AppGlobalState.UpdateTeachingModeFromStimulusMood(ActionsImagesSystem.TeacherMoodId);
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);
      GlobalTimer.Reset();
    }

    #endregion

    #region TryCreateMirrorFromInfluenceActions

    [Fact]
    public void TryCreateMirrorFromInfluenceActions_NoInfluenceActionId_ReturnsZero()
    {
      int result = _mirrorService.TryCreateMirrorFromInfluenceActions(
          AppGlobalState.AutomatizmNodeId, Engine.NextSeed());
      Assert.Equal(0, result);
    }

    [Fact]
    public void TryCreateMirrorFromInfluenceActions_WithInfluenceActionId_CreatesAutomatizm()
    {
      var actionsImgSys = ActionsImagesSystem.Instance;
      var (stimulusImageId, _) = actionsImgSys.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _influenceActionId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);
      Assert.True(stimulusImageId > 0);

      int result = _mirrorService.TryCreateMirrorFromInfluenceActions(
          AppGlobalState.AutomatizmNodeId, stimulusImageId);

      Assert.True(result > 0);
      var atmz = _autom.GetAutomatizmById(result);
      Assert.NotNull(atmz);
      Assert.Equal(0, atmz.Usefulness);
      Assert.Equal(0, atmz.Count);
      Assert.Equal(0, atmz.NextID);
    }

    [Fact]
    public void TryCreateMirrorFromInfluenceActions_StageNot2_ReturnsZero()
    {
      AppGlobalState.EvolutionStage = 3;

      var actionsImgSys = ActionsImagesSystem.Instance;
      var (stimulusImageId, _) = actionsImgSys.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _influenceActionId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);

      int result = _mirrorService.TryCreateMirrorFromInfluenceActions(
          AppGlobalState.AutomatizmNodeId, stimulusImageId);

      Assert.Equal(0, result);
    }

    [Fact]
    public void TryCreateMirrorFromInfluenceActions_NodeIdZero_ReturnsZero()
    {
      var actionsImgSys = ActionsImagesSystem.Instance;
      var (stimulusImageId, _) = actionsImgSys.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _influenceActionId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);

      int result = _mirrorService.TryCreateMirrorFromInfluenceActions(0, stimulusImageId);
      Assert.Equal(0, result);
    }

    #endregion

    #region Путь M в GetAutomatizmByGeneticPurpose

    [Fact]
    public void GetAutomatizmByGeneticPurpose_PathM_CreatesAutomatizmFromMirror()
    {
      // Путь A: (VeryActual || FlgConditionReflexes || CurActiveVerbalId == 0)
      // Нужно, чтобы CurActiveVerbalId > 0, иначе путь A сработает по третьей ветке.
      AppGlobalState.CurActiveVerbalId = Engine.NextSeed();
      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _influenceActionId };

      // Путь M читает CurrentStimulusActionsImageId — создаём образ с InfluenceActionId.
      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _influenceActionId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);
      AppGlobalState.CurrentStimulusActionsImageId = stimulusImageId;

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.NotNull(atmz);
      Assert.Equal(0, atmz.Usefulness);
      Assert.Equal(0, atmz.NextID);
      var atmzImg = ActionsImagesSystem.Instance.GetActionsImage(atmz.ActionsImageID);
      Assert.NotNull(atmzImg);
      Assert.Contains(_actionId, atmzImg.ActIdList);
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_PathM_NoMirrorLink_ReturnsNull()
    {
      AppGlobalState.CurActiveVerbalId = Engine.NextSeed();
      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _nonMirrorActionId };

      // Создаём образ с действием, у которого нет связи в справочнике.
      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _nonMirrorActionId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);
      AppGlobalState.CurrentStimulusActionsImageId = stimulusImageId;

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.Null(atmz);
    }

    #endregion

    #region Приоритет R > M

    [Fact]
    public void GetAutomatizmByGeneticPurpose_PriorityR_OverM()
    {
      // Проверяем, что при VeryActual путь A срабатывает (путь M не проверяется).
      // При VeryActual путь A вызывает CreateAutomatizmByGeneticPurpose,
      // который берёт действие из ConditionedReflexesActions / GeneticReflexesActions.
      // На 2-й стадии GetActiveAdaptiveActionsOfReflexes читает ConditionedReflexesActions.
      AppGlobalState.FlgConditionReflexes = true;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int> { _actionId });
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _influenceActionId };

      // Очень важно: CurActiveVerbalId == 0, чтобы путь A сработал (VeryActual ||
      // FlgConditionReflexes || CurActiveVerbalId == 0).
      AppGlobalState.CurActiveVerbalId = 0;

      // Проверяем, что GetActiveAdaptiveActionsOfReflexes возвращает действие.
      var actionIds = PurposeGeneticImageSystem.Instance.GetActiveAdaptiveActionsOfReflexes();
      Assert.NotEmpty(actionIds);
      Assert.Contains(_actionId, actionIds);
    }

    #endregion

    #region Конвертация InfluenceAction → AdaptiveAction

    [Fact]
    public void ConvertInfluenceActionIdsToAdaptiveActionIds_WithLink_ReturnsCorrectIds()
    {
      var result = AdaptiveActionsSystem.Instance.ConvertInfluenceActionIdsToAdaptiveActionIds(
          new List<int> { _influenceActionId });

      Assert.Single(result);
      Assert.Contains(_actionId, result);
    }

    [Fact]
    public void ConvertInfluenceActionIdsToAdaptiveActionIds_NoLink_ReturnsEmpty()
    {
      var result = AdaptiveActionsSystem.Instance.ConvertInfluenceActionIdsToAdaptiveActionIds(
          new List<int> { _nonMirrorActionId });

      Assert.Empty(result);
    }

    [Fact]
    public void ConvertInfluenceActionIdsToAdaptiveActionIds_Mixed_ReturnsOnlyLinked()
    {
      var result = AdaptiveActionsSystem.Instance.ConvertInfluenceActionIdsToAdaptiveActionIds(
          new List<int> { _influenceActionId, _nonMirrorActionId });

      Assert.Single(result);
      Assert.Contains(_actionId, result);
    }

    #endregion

    #region Путь M-команда: TryCreateMirrorFromCommandPattern

    [Fact]
    public void TryCreateMirrorFromCommandPattern_NoCommandPatternId_ReturnsZero()
    {
      int result = _mirrorService.TryCreateMirrorFromCommandPattern(
          AppGlobalState.AutomatizmNodeId, Engine.NextSeed());
      Assert.Equal(0, result);
    }

    [Fact]
    public void TryCreateMirrorFromCommandPattern_WithCommandPatternId_CreatesAutomatizm()
    {
      var actionsImgSys = ActionsImagesSystem.Instance;
      var (stimulusImageId, _) = actionsImgSys.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int>(),
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true,
          commandPatternIdList: new List<int> { _commandPatternId });
      Assert.True(stimulusImageId > 0);

      int result = _mirrorService.TryCreateMirrorFromCommandPattern(
          AppGlobalState.AutomatizmNodeId, stimulusImageId);

      Assert.True(result > 0);
      var atmz = _autom.GetAutomatizmById(result);
      Assert.NotNull(atmz);
      Assert.Equal(0, atmz.Usefulness);
      Assert.Equal(0, atmz.Count);
      Assert.Equal(0, atmz.NextID);
      var atmzImg = ActionsImagesSystem.Instance.GetActionsImage(atmz.ActionsImageID);
      Assert.NotNull(atmzImg);
      Assert.Contains(_commandMirrorActionId, atmzImg.ActIdList);
      // commandPatternIdList ответа должен быть пустым — команда развёрнута в мотор.
      Assert.Empty(atmzImg.CommandPatternIdList);
    }

    [Fact]
    public void TryCreateMirrorFromCommandPattern_StageNot2_ReturnsZero()
    {
      AppGlobalState.EvolutionStage = 3;

      var actionsImgSys = ActionsImagesSystem.Instance;
      var (stimulusImageId, _) = actionsImgSys.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int>(),
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true,
          commandPatternIdList: new List<int> { _commandPatternId });

      int result = _mirrorService.TryCreateMirrorFromCommandPattern(
          AppGlobalState.AutomatizmNodeId, stimulusImageId);

      Assert.Equal(0, result);
    }

    [Fact]
    public void TryCreateMirrorFromCommandPattern_NoMirrorLink_ReturnsZero()
    {
      var actionsImgSys = ActionsImagesSystem.Instance;
      var (stimulusImageId, _) = actionsImgSys.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int>(),
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true,
          commandPatternIdList: new List<int> { _nonCommandMirrorActionId });

      int result = _mirrorService.TryCreateMirrorFromCommandPattern(
          AppGlobalState.AutomatizmNodeId, stimulusImageId);

      Assert.Equal(0, result);
    }

    #endregion

    #region Путь M-команда в GetAutomatizmByGeneticPurpose

    [Fact]
    public void GetAutomatizmByGeneticPurpose_PathCommand_CreatesAutomatizmFromCommandMirror()
    {
      // Путь A: (VeryActual || FlgConditionReflexes || CurActiveVerbalId == 0)
      // Нужно, чтобы CurActiveVerbalId > 0, иначе путь A сработает.
      AppGlobalState.CurActiveVerbalId = Engine.NextSeed();
      AppGlobalState.FlgConditionReflexes = false;

      // Путь M (InfluenceAction) не сработает: CurrentStimulusActionIdList пуст.
      AppGlobalState.CurrentStimulusActionIdList = new List<int>();

      // Путь M-команда: создаём образ с CommandPatternIdList.
      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int>(),
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true,
          commandPatternIdList: new List<int> { _commandPatternId });
      AppGlobalState.CurrentStimulusActionsImageId = stimulusImageId;

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.NotNull(atmz);
      Assert.Equal(0, atmz.Usefulness);
      Assert.Equal(0, atmz.NextID);
      var atmzImg = ActionsImagesSystem.Instance.GetActionsImage(atmz.ActionsImageID);
      Assert.NotNull(atmzImg);
      Assert.Contains(_commandMirrorActionId, atmzImg.ActIdList);
      // commandPatternIdList ответа пуст — команда развёрнута в мотор.
      Assert.Empty(atmzImg.CommandPatternIdList);
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_PathCommand_NoMirrorLink_ReturnsNull()
    {
      AppGlobalState.CurActiveVerbalId = Engine.NextSeed();
      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.CurrentStimulusActionIdList = new List<int>();

      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int>(),
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true,
          commandPatternIdList: new List<int> { _nonCommandMirrorActionId });
      AppGlobalState.CurrentStimulusActionsImageId = stimulusImageId;

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.Null(atmz);
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_PriorityInfluenceAction_OverCommand()
    {
      // Приоритет InfluenceActionId над CommandPatternId:
      // если есть и то, и другое — срабатывает путь M (InfluenceAction),
      // путь M-команда (else if) не проверяется.
      AppGlobalState.CurActiveVerbalId = Engine.NextSeed();
      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _influenceActionId };

      // Образ содержит и ActIdList, и CommandPatternIdList.
      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _influenceActionId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true,
          commandPatternIdList: new List<int> { _commandPatternId });
      AppGlobalState.CurrentStimulusActionsImageId = stimulusImageId;

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.NotNull(atmz);
      var atmzImg = ActionsImagesSystem.Instance.GetActionsImage(atmz.ActionsImageID);
      Assert.NotNull(atmzImg);
      // Должен сработать путь M (InfluenceAction), а не путь M-команда.
      Assert.Contains(_actionId, atmzImg.ActIdList);
    }

    #endregion

    #region Конвертация CommandPatternId → AdaptiveActionId

    [Fact]
    public void ConvertCommandPatternIdsToAdaptiveActionIds_WithLink_ReturnsCorrectIds()
    {
      var result = AdaptiveActionsSystem.Instance.ConvertCommandPatternIdsToAdaptiveActionIds(
          new List<int> { _commandPatternId });

      Assert.Single(result);
      Assert.Contains(_commandMirrorActionId, result);
    }

    [Fact]
    public void ConvertCommandPatternIdsToAdaptiveActionIds_NoLink_ReturnsEmpty()
    {
      var result = AdaptiveActionsSystem.Instance.ConvertCommandPatternIdsToAdaptiveActionIds(
          new List<int> { _nonCommandMirrorActionId });

      Assert.Empty(result);
    }

    [Fact]
    public void ConvertCommandPatternIdsToAdaptiveActionIds_Mixed_ReturnsOnlyLinked()
    {
      var result = AdaptiveActionsSystem.Instance.ConvertCommandPatternIdsToAdaptiveActionIds(
          new List<int> { _commandPatternId, _nonCommandMirrorActionId });

      Assert.Single(result);
      Assert.Contains(_commandMirrorActionId, result);
    }

    [Fact]
    public void ConvertCommandPatternIdsToAdaptiveActionIds_Duplicates_Squashed()
    {
      // Два одинаковых ID паттерна — результат должен содержать одно действие.
      var result = AdaptiveActionsSystem.Instance.ConvertCommandPatternIdsToAdaptiveActionIds(
          new List<int> { _commandPatternId, _commandPatternId });

      Assert.Single(result);
      Assert.Contains(_commandMirrorActionId, result);
    }

    #endregion
  }
}
