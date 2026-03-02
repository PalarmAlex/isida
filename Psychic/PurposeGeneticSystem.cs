using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Psychic.Automatism;
using ISIDA.Sensors;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using static ISIDA.Actions.AdaptiveActionsSystem;
using static ISIDA.Psychic.Automatism.ActionsImagesSystem;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Система управления гомеостатическими целями агента
  /// </summary>
  public sealed class PurposeGeneticImageSystem: IDisposable
  {
    private readonly InformationEnvironmentSystem _informationEnvironmentSystem;
    private readonly AutomatizmSystem _automatizmSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private ConditionedReflexToAutomatizmConverter _conditionedReflexToAutomatizm;
    private AutomatizmChainsSystem _automatizmChainsSystem;
    private MirrorAutomatizmService _mirrorAutomatizmService;
    private VerbalBrocaImagesSystem _verbalBrocaImagesSystem;
    private SensorySystem _sensorySystem;

    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private int oldAutomatizmId = 0;

    #region Инициализация

    private static PurposeGeneticImageSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static PurposeGeneticImageSystem Instance => _instance ??
        throw new InvalidOperationException("PurposeGeneticSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы
    /// </summary>
    /// <exception cref="InvalidOperationException">Выбрасывается, если система уже была инициализирована ранее</exception>
    public static void InitializeInstance(
      InformationEnvironmentSystem informationEnvironmentSystem,
      ActionsImagesSystem actionsImagesSystem,
      AutomatizmSystem automatizmSystem,
      AdaptiveActionsSystem adaptiveActionsSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("PurposeGeneticSystem уже инициализирован.");

      _instance = new PurposeGeneticImageSystem(
        informationEnvironmentSystem, 
        actionsImagesSystem, 
        automatizmSystem, 
        adaptiveActionsSystem);
    }

    private PurposeGeneticImageSystem(
      InformationEnvironmentSystem informationEnvironmentSystem,
      ActionsImagesSystem actionsImagesSystem,
      AutomatizmSystem automatizmSystem,
      AdaptiveActionsSystem adaptiveActionsSystem)
    {
      try
      {
        _informationEnvironmentSystem = informationEnvironmentSystem ?? throw new ArgumentNullException(nameof(informationEnvironmentSystem));
        _actionsImagesSystem = actionsImagesSystem ?? throw new ArgumentNullException(nameof(actionsImagesSystem));
        _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
        _adaptiveActionsSystem = adaptiveActionsSystem ?? throw new ArgumentNullException(nameof(adaptiveActionsSystem));
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    /// <summary>
    /// Вторичная инициализация
    /// </summary>
    public void SetDopPurposeGeneticImageSystem(
      ConditionedReflexToAutomatizmConverter conditionedReflexToAutomatizm,
      AutomatizmChainsSystem automatizmChainsSystem)
    {
      _conditionedReflexToAutomatizm = conditionedReflexToAutomatizm ?? throw new ArgumentNullException(nameof(conditionedReflexToAutomatizm));
      _automatizmChainsSystem = automatizmChainsSystem ?? throw new ArgumentNullException(nameof(automatizmChainsSystem));
    }

    /// <summary>
    /// Зависимости для создания эхо-автоматизма с цепочкой на 2-й стадии (при отсутствии автоматизма и !VeryActual).
    /// </summary>
    public void SetStage2EchoDependencies(
      MirrorAutomatizmService mirrorAutomatizmService,
      VerbalBrocaImagesSystem verbalBrocaImagesSystem,
      SensorySystem sensorySystem)
    {
      _mirrorAutomatizmService = mirrorAutomatizmService;
      _verbalBrocaImagesSystem = verbalBrocaImagesSystem;
      _sensorySystem = sensorySystem;
    }

    #endregion

    #region Константы и структуры

    /// <summary>
    /// Образ гомеостатической целм
    /// </summary>
    public class PurposeGeneticImage
    {
      /// <summary>
      /// Номер пульса
      /// </summary>
      public int Puls { get; set; }

      /// <summary>
      /// Флаг актуальности цели (True - очень актуальна)
      /// </summary>
      public bool VeryActual { get; set; }

      /// <summary>
      /// ID параметра гомеостаза как цели для улучшения в данных условиях - текущий приоритет по функции потребности
      /// </summary>
      public int TargetId { get; set; }

      /// <summary>
      /// Выбранный образ действия для данной цели
      /// </summary>
      public ActionsImage ActionImage { get; set; }
    }

    #endregion

    #region Статические поля

    /// <summary>
    /// Объекты PurposeGeneticObject накапливаются в оперативке и удаляются во сне
    /// </summary>
    private static List<PurposeGeneticImage> PurposeGeneticObject = new List<PurposeGeneticImage>();

    /// <summary>
    /// Текущая цель сохраняется до перекрытия следующим orientation_N()
    /// </summary>
    private static PurposeGeneticImage CurrentPurposeGenetic { get; set; }

    /// <summary>
    /// Предыдущая цель
    /// </summary>
    private static PurposeGeneticImage OldPurposeGenetic { get; set; }

    #endregion

    #region Управление образами целей

    /// <summary>
    /// Получить текущий гомеостатический образ
    /// </summary>
    public PurposeGeneticImage GetPurposeGeneticImage()
    {
      _lock.EnterReadLock();
      try
      {
        var purposeGenetic = new PurposeGeneticImage
        {
          Puls = GlobalTimer.GlobalPulsCount,
          VeryActual = _informationEnvironmentSystem.VeryActualSituation,
          TargetId = AppGlobalState.DominantParam
        };

        var actionIdList = GetActiveAdaptiveActionsOfReflexes();
        ActionsImage actionImage = null;
        if (actionIdList.Count == 0)
          actionIdList = new List<int> { AppGlobalState.DefaultAdaptiveActionId };

        (_, actionImage) = _actionsImagesSystem.CreateNewActionsImageWithIdNoLock(0, 0, actionIdList, null, 0, 0, true);
        purposeGenetic.ActionImage = actionImage;

        PurposeGeneticObject.Add(purposeGenetic);
        OldPurposeGenetic = CurrentPurposeGenetic;
        CurrentPurposeGenetic = purposeGenetic;

        return purposeGenetic;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает список активных адаптивных действий
    /// </summary>
    public List<int> GetActiveAdaptiveActionsOfReflexes()
    {
      if (AppGlobalState.EvolutionStage < 3)
      {
        var conditionActionsIdArr = (List<int>)AppGlobalState.ConditionedReflexesActions;
        if (conditionActionsIdArr != null && conditionActionsIdArr.Count > 0)
          return conditionActionsIdArr;
        else
        {
          var geneticActionsIdArr = (List<int>)AppGlobalState.GeneticReflexesActions;
          if (geneticActionsIdArr != null && geneticActionsIdArr.Count > 0)
            return geneticActionsIdArr;
        }
      }

      return new List<int>();
    }

    /// <summary>
    /// Получить автоматизм по гомеостатической цели.
    /// При VeryActual / FlgConditionReflexes / отсутствии вербального стимула — создаётся автоматизм по генетической цели.
    /// Иначе на 2-й стадии при наличии вербального стимула создаётся эхо-автоматизм с цепочкой (только если VeryActual == false).
    /// </summary>
    public Automatizm GetAutomatizmByGeneticPurpose()
    {
      try
      {
        var purposeGenetic = GetPurposeGeneticImage();
        Automatizm atmz = null;
        
        if (purposeGenetic.VeryActual || AppGlobalState.FlgConditionReflexes || AppGlobalState.CurActiveVerbalId == 0)
        {
          if (purposeGenetic.ActionImage != null)
            atmz = CreateAutomatizmByGeneticPurpose(purposeGenetic);
        }
        else if (!purposeGenetic.VeryActual &&
                 AppGlobalState.EvolutionStage == 2 &&
                 AppGlobalState.CurActiveVerbalId != 0 &&
                 _mirrorAutomatizmService != null &&
                 _verbalBrocaImagesSystem != null &&
                 _sensorySystem?.VerbalChannel != null)
        {
          atmz = TryCreateStage2EchoWithChainFromStimulusContext();
        }

        return atmz;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }
    }

    /// <summary>
    /// Создаёт эхо-автоматизм с цепочкой на 2-й стадии по контексту стимула с пульта (AppGlobalState + CurActiveVerbalId).
    /// </summary>
    private Automatizm TryCreateStage2EchoWithChainFromStimulusContext()
    {
      int nodeId = AppGlobalState.AutomatizmNodeId;
      int actionsImageId = AppGlobalState.CurrentStimulusActionsImageId;
      var actionIdList = AppGlobalState.CurrentStimulusActionIdList ?? new List<int>();
      int toneId = AppGlobalState.CurrentStimulusToneId;
      int moodId = AppGlobalState.CurrentStimulusMoodId;

      if (nodeId <= 0 || actionsImageId <= 0)
        return null;

      var verbalImage = _verbalBrocaImagesSystem.GetVerbalBrocaImage(AppGlobalState.CurActiveVerbalId);
      var phraseIdList = verbalImage?.PhraseIdList;
      if (phraseIdList == null || phraseIdList.Count == 0)
        return null;

      List<int> parts = phraseIdList.Count == 1
          ? _sensorySystem.VerbalChannel.GetPartPhraseIdsFromPhraseId(phraseIdList[0])
          : phraseIdList.ToList();
      if (parts == null || parts.Count == 0)
        return null;

      int echoId = _mirrorAutomatizmService.TryCreateStage2EchoWithChain(
          nodeId,
          actionsImageId,
          parts,
          actionIdList,
          toneId,
          moodId);
      if (echoId <= 0)
        return null;

      return _automatizmSystem.GetAutomatizmById(echoId);
    }

    /// <summary>
    /// Обработка автоматизма, рвущегося на выполнение - простейший вариант
    /// </summary>
    public Automatizm GetBasicAutomatizmByPurpose(int atmtzmID)
    {
      try
      {
        var atmz = _automatizmSystem.GetAutomatizmById(atmtzmID);
        if(atmz == null)
        {
          Logger.Info($"Нет автоматизма ID={atmtzmID}");
          return null;
        }

        var purposeGenetic = GetPurposeGeneticImage();
        ActionsImage actImg = null;
        actImg = _actionsImagesSystem.GetActionsImage(atmz.ActionsImageID);
        bool IsHasThreat = HasThreat(actImg.ToneId, actImg.MoodId);

        // значимая новизна: не полное распознавание + опасные признаки в Tone и/или Mood
        if (IsHasThreat && AppGlobalState.CurrentFindAtmzStepCount == 3)
          return atmz;

        // опасная ситуация
        if (purposeGenetic.VeryActual)
        {
          if (oldAutomatizmId == atmz.ID)
            return null;  // чтобы не долбить одно и тоже постоянно     
          else
            oldAutomatizmId = atmz.ID;
        }
        else
        {
          // если автоматизм протух, и состояние агента Bad, создаем новый на базе гомеостатических целей
          if(atmz.Usefulness < 0)
          {
            if(AppGlobalState.CurrentOverallState == AppGlobalState.HomeostasisState.Bad)
            {
              int dominantParamId = AppGlobalState.DominantParam;
              var activeActions = _adaptiveActionsSystem.GetActiveAdaptiveActionsList();
              var actionsForDominantParam = activeActions
                  .Where(action => action.TargetGomeoParamIdArr != null &&
                                   action.TargetGomeoParamIdArr.Contains(dominantParamId))
                  .ToList();

              // Выбираем 1 действие с максимальным Vigor
              AdaptiveAction bestAction = null;
              var actionIdList = new List<int>();
              if (actionsForDominantParam.Count > 0)
              {
                bestAction = actionsForDominantParam
                    .OrderByDescending(a => a.Vigor)
                    .FirstOrDefault();

                actionIdList = new List<int> { bestAction.Id };
              }
              else
                actionIdList = new List<int> { AppGlobalState.DefaultAdaptiveActionId };

              ActionsImage actionImage = null;
              (_, actionImage) = _actionsImagesSystem.CreateNewActionsImageWithIdNoLock(0, 0, actionIdList, null, 0, 0, true);
              purposeGenetic.ActionImage = actionImage;
              atmz = CreateAutomatizmByGeneticPurpose(purposeGenetic);
            }
          }
        }
        return atmz;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }     
    }

    /// <summary>
    /// Создать и запустить автоматизм по гомеостатической цели
    /// </summary>
    public Automatizm CreateAutomatizmByGeneticPurpose(PurposeGeneticImage purposeGenetic)
    {
      if(purposeGenetic == null || purposeGenetic.ActionImage == null)
        return null;

      try
      {
        int branchID = AppGlobalState.AutomatizmNodeId;
        var aArr = purposeGenetic.ActionImage.ActIdList;
        var sArr = purposeGenetic.ActionImage.PhraseIdList;
        int toneId = purposeGenetic.ActionImage.ToneId;
        int moodId = purposeGenetic.ActionImage.MoodId;

        // На стадии 2 при действиях от безусловного рефлекса клонируем цепочку, если есть
        int automatizmChainId = 0;
        if (AppGlobalState.EvolutionStage == 2 && AppGlobalState.CurrentGeneticReflexID > 0)
        {
          var chainResult = _conditionedReflexToAutomatizm.CreateAutomatizmChainFromGeneticReflex(
              AppGlobalState.CurrentGeneticReflexID, branchID);
          if (chainResult.Success && chainResult.ChainId > 0)
            automatizmChainId = chainResult.ChainId;
        }

        int actionImageId = 0;
        (actionImageId, _) = _actionsImagesSystem.CreateNewActionsImageWithIdNoLock(0, 0, aArr, sArr, toneId, moodId, true);
        Automatizm atmz = null;
        (_, atmz) = _automatizmSystem.CreateNewAutomatizm(branchID, actionImageId);

        if (atmz != null && automatizmChainId > 0)
        {
          atmz.NextID = automatizmChainId;
          var chain = _automatizmChainsSystem.GetChain(automatizmChainId);
          if (chain != null)
            chain.StartAutomatizmId = atmz.ID;
        }

        return atmz;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        _lock?.Dispose();
        _disposed = true;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    #endregion

  }
}
