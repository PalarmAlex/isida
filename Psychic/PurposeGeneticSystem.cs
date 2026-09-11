﻿﻿﻿using ISIDA.Actions;
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
  /// Система управления гомеостатическими целями симбионта
  /// </summary>
  public sealed class PurposeGeneticImageSystem: IDisposable
  {
    private readonly InformationEnvironmentSystem _informationEnvironmentSystem;
    private readonly AutomatizmSystem _automatizmSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private InfluenceActionSystem _influenceActionSystem;
    private ConditionedReflexToAutomatizmConverter _conditionedReflexToAutomatizm;
    private AutomatizmChainsSystem _automatizmChainsSystem;
    private MirrorAutomatizmService _mirrorAutomatizmService;
    private VerbalBrocaImagesSystem _verbalBrocaImagesSystem;
    private SensorySystem _sensorySystem;
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private int oldAutomatizmId = 0;
    private IReadOnlyList<int> _stage2SearchPlayStyleIds = new List<int>();

    /// <summary>
    /// Хост-зависимый делегат для проверки наличия рецепта для G_AD.
    /// Устанавливается адаптером через SetRecipeChecker.
    /// Возвращает true, если для заданного actionId найден рецепт в каталоге.
    /// </summary>
    private Func<int, bool> _recipeChecker;

    /// <summary>
    /// Устанавливает хост-зависимый делегат для проверки наличия рецепта.
    /// </summary>
    /// <param name="checker">Функция проверки: actionId → true, если рецепт найден.</param>
    public void SetRecipeChecker(Func<int, bool> checker)
    {
      _recipeChecker = checker ?? throw new ArgumentNullException(nameof(checker));
    }

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
    /// Зависимости для создания эхо-автоматизмов с цепочкой на 2-й стадии (при отсутствии автоматизма и !VeryActual).
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

    /// <summary>
    /// Устанавливает InfluenceActionSystem для проверки активных probe-EA.
    /// </summary>
    public void SetInfluenceActionSystem(InfluenceActionSystem influenceActionSystem)
    {
      _influenceActionSystem = influenceActionSystem ?? throw new ArgumentNullException(nameof(influenceActionSystem));
    }

    /// <summary>
    /// Задаёт список кодов стилей Поиск/Игра для механизма 3 стадии 2 (случайная проба).
    /// Передаётся адаптером из настроек через <see cref="ISIDA.Common.IsidaConfig.Stage2SearchPlayStyleIds"/>.
    /// Пустой список означает, что проверка стилей не ограничивается.
    /// </summary>
    /// <param name="styleIds">Список ID стилей (например, 3,5,7).</param>
    public void SetStage2SearchPlayStyleIds(IReadOnlyList<int> styleIds)
    {
      _stage2SearchPlayStyleIds = styleIds ?? new List<int>();
      //Logger.Info(
      //    "SetStage2SearchPlayStyleIds: " +
      //    (string.Join(",", _stage2SearchPlayStyleIds) ?? string.Empty));
    }

    /// <summary>
    /// Парсит строку кодов стилей через запятую и задаёт список.
    /// </summary>
    /// <param name="commaSeparatedIds">Строка вида "3,5,7" или пустая строка.</param>
    public void SetStage2SearchPlayStyleIds(string commaSeparatedIds)
    {
      var ids = (commaSeparatedIds ?? string.Empty)
          .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
          .Select(s =>
          {
            int id;
            return int.TryParse(s.Trim(), out id) ? id : 0;
          })
          .Where(id => id > 0)
          .ToList();
      SetStage2SearchPlayStyleIds((IReadOnlyList<int>)ids);
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
    /// Получает список активных моторных действий
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
        
        // Селективный клон (путь A) — только со стадии 3.
        // На стадии 2 клонирование рефлексов в автоматизмы запрещено:
        // рефлексный и наблюдательный контуры — разные зоны реагирования.
        // ObservationSession откроется при Bad + нет usable atmz и создаст atmz по мотору оператора.
        if ((purposeGenetic.VeryActual || AppGlobalState.FlgConditionReflexes || AppGlobalState.CurActiveVerbalId == 0)
            && AppGlobalState.EvolutionStage >= 3)
        {
          if (purposeGenetic.ActionImage != null)
            atmz = CreateAutomatizmByGeneticPurpose(purposeGenetic);
        }
        else if (!purposeGenetic.VeryActual &&
                 AppGlobalState.TeachingMode &&
                 AppGlobalState.EvolutionStage == 2 &&
                 AppGlobalState.CurActiveVerbalId != 0 &&
                 _mirrorAutomatizmService != null &&
                 _verbalBrocaImagesSystem != null &&
                 _sensorySystem?.VerbalChannel != null)
        {
          // Path B: эхо-автоматизм по вербальному стимулу.
          // Различаем вербальный echo без метрик (нормально — "привет - привет")
          // и повтор действия оператора в среде с метриками (не нормально).
          // Если есть активные probe-EA — это повтор действия оператора → пропускаем.
          bool hasActiveProbeActions = HasActiveProbeActions();
          if (!hasActiveProbeActions)
          {
            atmz = TryCreateStage2EchoWithChainFromStimulusContext();
          }
          else
          {
            Logger.Info("PurposeGeneticSystem: skipping path B echo — active probe actions detected (operator action repetition)");
          }
        }
        else if (AppGlobalState.EvolutionStage == 2 && !purposeGenetic.VeryActual)
        {
          // Механизм 3 — случайная проба: Search/Play стили активны, нет usable atmz
          atmz = TryCreateRandomProbeAutomatizm(purposeGenetic);
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

      // Для одной фразы с дефисом («кора-мора») включаем авторитарный режим, чтобы части добавились в канал и нашлись для цепочки
      var verbal = _sensorySystem?.VerbalChannel;
      bool wasAuthoritative = verbal?.AuthoritativeMode ?? false;
      if (verbal != null && phraseIdList.Count == 1)
        verbal.AuthoritativeMode = true;
      try
      {
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
      finally
      {
        if (verbal != null && phraseIdList.Count == 1)
          verbal.AuthoritativeMode = wasAuthoritative;
      }
    }

    /// <summary>
    /// Механизм 3 — случайная проба на стадии 2.
    /// Когда: EvolutionStage == 2, !VeryActual, активны стили Поиск/Игра, нет usable atmz.
    /// Как: один случайный релевантный G_AD -> create-one atmz тем же путём, что genetic purpose.
    /// Не использовать RandomBranchAutomatizmStrategy (infoFunc_30) — это ст. 4+.
    /// Приоритет: ожидание оператора (сессия наблюдения) > пауза > случайная проба.
    /// </summary>
    private static readonly Random _random = new Random();

    private Automatizm TryCreateRandomProbeAutomatizm(PurposeGeneticImage purposeGenetic)
    {
      try
      {
        if (AppGlobalState.EvolutionStage != 2)
          return null;

        if (purposeGenetic.VeryActual)
          return null;

        // Приоритет ожидания: если сессия наблюдения активна — пропускаем случайную пробу.
        // Оператор может показать полезное действие, нужно подождать.
        if (OperatorMotorObservationSession.IsInitialized && OperatorMotorObservationSession.Instance.IsActive)
        {
          Logger.Info("TryCreateRandomProbeAutomatizm: session active — skipping random probe (priority to operator observation)");
          return null;
        }

        // Пауза перед случайной пробой: ждём, пока истечёт WaitingPeriodForActionsVal.
        // Это время ожидания ответа оператора — по сути пауза перед своими случайными действиями.
        int waitPeriod = AppGlobalState.WaitingPeriodForActionsVal;
        if (waitPeriod > 0 && GlobalTimer.GlobalPulsCount < waitPeriod)
        {
          Logger.Info($"TryCreateRandomProbeAutomatizm: pause active (waitPeriod={waitPeriod}, pulse={GlobalTimer.GlobalPulsCount}) — skipping random probe");
          return null;
        }

        // Проверяем, активны ли стили Поиск и/или Игра
        // Стили хранятся в AppGlobalState — проверяем через текущее настроение/эмоции
        bool hasSearchOrPlayStyle = CheckSearchOrPlayStyle();
        if (!hasSearchOrPlayStyle)
          return null;

        // Находим кандидаты G_AD под целевой параметр
        int targetParamId = purposeGenetic.TargetId;
        if (targetParamId <= 0)
          targetParamId = AppGlobalState.DominantParam;

        if (targetParamId <= 0)
          return null;

        var allActions = _adaptiveActionsSystem.GetAllAdaptiveActions();
        var candidates = allActions
            .Where(a => a.TargetGomeoParamIdArr != null && a.TargetGomeoParamIdArr.Contains(targetParamId))
            .ToList();

        if (candidates.Count == 0)
          return null;

        // Выбираем один случайный G_AD
        var selectedAction = candidates[_random.Next(candidates.Count)];
        int selectedActionId = selectedAction.Id;

        Logger.Info(
            $"TryCreateRandomProbeAutomatizm: selected actionId={selectedActionId} " +
            $"targetParam={targetParamId} candidates={candidates.Count}");

        // Создаём ActionsImage с одним G_AD
        int actionsImageId;
        (actionsImageId, _) = _actionsImagesSystem.CreateNewActionsImageWithIdNoLock(
            0, 0, new List<int> { selectedActionId }, null, 0, 0, true);

        if (actionsImageId <= 0)
          return null;

        // Создаём автоматизм на текущем узле дерева
        int branchId = AppGlobalState.AutomatizmNodeId;
        int atmzId;
        Automatizm atmz = null;
        (atmzId, atmz) = _automatizmSystem.CreateNewAutomatizm(branchId, actionsImageId);

        if (atmz == null)
          return null;

        // Начальная Usefulness = 0 (нейтрально, будет оценена после исполнения)
        atmz.Usefulness = 0;

        Logger.Info(
            $"TryCreateRandomProbeAutomatizm: created atmzId={atmzId} " +
            $"branchId={branchId} actionsImageId={actionsImageId} usefulness=0");

        return atmz;
      }
      catch (Exception ex)
      {
        Logger.Error($"TryCreateRandomProbeAutomatizm: {ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Проверяет, активны ли стили Поиск и/или Игра.
    /// Использует список кодов стилей, заданный через
    /// <see cref="SetStage2SearchPlayStyleIds(IReadOnlyList{int})"/>.
    /// Если список пуст — проверка стилей не ограничивается.
    /// </summary>
    private bool CheckSearchOrPlayStyle()
    {
      try
      {
        // Список стилей передаётся адаптером через IsidaConfig.Stage2SearchPlayStyleIds
        if (_stage2SearchPlayStyleIds != null && _stage2SearchPlayStyleIds.Count > 0)
        {
          // Проверяем, есть ли активные стили из списка
          var currentStyles = AppGlobalState.ActiveStyles;
          if (currentStyles != null)
          {
            bool hasMatchingStyle = currentStyles.Any(s => s != null && _stage2SearchPlayStyleIds.Contains(s.Id));
            if (hasMatchingStyle)
            {
              Logger.Info($"CheckSearchOrPlayStyle: active search/play style found (ids={string.Join(",", _stage2SearchPlayStyleIds)})");
              return true;
            }
          }
        }
        else
        {
          // Список пуст — не ограничиваем (разрешены все стили)
          return true;
        }

        // Fallback: если OverallState не Bad и нет вербального стимула — стили Search/Play могут быть активны
        bool overallNotBad = AppGlobalState.CurrentOverallState != AppGlobalState.HomeostasisState.Bad;
        bool hasVerbalStimulus = AppGlobalState.CurActiveVerbalId > 0;
        bool fallback = overallNotBad && !hasVerbalStimulus;

        Logger.Info($"CheckSearchOrPlayStyle: fallback result={fallback} overallNotBad={overallNotBad} hasVerbalStimulus={hasVerbalStimulus}");
        return fallback;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет, есть ли активные probe-EA (InfluenceAction с IsActive).
    /// Используется для различения вербального echo (без метрик) и повторения действия оператора (с метриками).
    /// </summary>
    private bool HasActiveProbeActions()
    {
      try
      {
        if (_influenceActionSystem == null)
          return false;

        // Проверяем, есть ли активные InfluenceAction (probe-EA)
        // Если есть — значит оператор действует в среде с метриками → не создаём echo
        var allInfluenceActions = _influenceActionSystem.GetAllInfluenceActions();
        if (allInfluenceActions != null)
        {
          foreach (var ia in allInfluenceActions)
          {
            if (ia.IsActive)
              return true;
          }
        }

        return false;
      }
      catch
      {
        return false;
      }
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
          // если автоматизм протух, и состояние симбионта Bad, создаем новый на базе гомеостатических целей
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
              // Селективный клон — только со стадии 3.
              if (AppGlobalState.EvolutionStage >= 3)
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
        // Проверка: G_AD должен иметь recipe slots (настраиваемые параметры).
        // Если для G_AD нет рецепта — не клонируем, так как это без адаптивной ценности.
        var aArr = purposeGenetic.ActionImage.ActIdList;
        if (aArr != null && aArr.Count > 0)
        {
          bool hasRecipe = false;
          foreach (int actId in aArr)
          {
            var action = _adaptiveActionsSystem.GetAdaptiveAction(actId);
            if (action != null)
            {
              // Если хост установил делегат проверки рецепта — используем его.
              if (_recipeChecker != null)
              {
                if (_recipeChecker(actId))
                {
                  hasRecipe = true;
                  break;
                }
              }
              else
              {
                // Fallback: упрощённая проверка через TargetGomeoParamIdArr.
                if (action.TargetGomeoParamIdArr != null && action.TargetGomeoParamIdArr.Count > 0)
                {
                  hasRecipe = true;
                  break;
                }
              }
            }
          }

          if (!hasRecipe)
          {
            Logger.Info($"Skipping clone: no recipe slots for action image Id={purposeGenetic.ActionImage.Id}");
            return null;
          }
        }

        int branchID = AppGlobalState.AutomatizmNodeId;
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
        _instance = null;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    #endregion

  }
}
