using ISIDA.Actions;
using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Сервис отзеркаливания автоматизмов для 3-й стадии.
  /// Создает автоматизмы вида: триггер стимула с пульта -> ответ оператора.
  /// В обычном режиме учитываются только вербальные стимулы; в режиме наблюдения — вербальные, невербальные (флажки воздействий) и смешанные.
  /// </summary>
  public sealed class MirrorAutomatizmService : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly AutomatizmSystem _automatizmSystem;
    private bool _disposed;

    private bool _dialogMirrorActive;
    private int _dialogTriggerNodeId;
    private int _pendingResponseActionsImageId;
    private int _pendingResponseNodeId;
    private bool _pendingResponseHasVerbalPart;
    private bool _pendingResponseHasNonVerbalPart;

    /// <summary>
    /// Создает сервис отзеркаливания автоматизмов.
    /// </summary>
    public MirrorAutomatizmService(AutomatizmSystem automatizmSystem)
    {
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
    }

    /// <summary>
    /// Создать и вернуть первый (попугайский) автоматизм для запуска цикла зеркалирования.
    /// В режиме наблюдения учитываются и невербальные стимулы (флажки воздействий).
    /// </summary>
    /// <param name="detectedNodeId">ID узла дерева автоматизмов, распознанного по стимулу.</param>
    /// <param name="actionsImageId">ID образа действий (стимул оператора).</param>
    /// <param name="hasVerbalPart">Есть ли в стимуле вербальная часть (фраза).</param>
    /// <param name="hasNonVerbalPart">Есть ли в стимуле невербальная часть (действия с пульта).</param>
    public int TryCreateInitialParrotAutomatizm(
      int detectedNodeId,
      int actionsImageId,
      bool hasVerbalPart,
      bool hasNonVerbalPart = false)
    {
      if (AppGlobalState.EvolutionStage != 3)
      {
        ResetDialogMirror();
        return 0;
      }

      bool canMirror = hasVerbalPart || (AppGlobalState.ObservationMode && hasNonVerbalPart);
      if (detectedNodeId <= 0 || actionsImageId <= 0 || !canMirror)
        return 0;

      int responseActionsImageId = GetOrCreateResponseActionsImageWithAdaptiveIds(actionsImageId);
      if (responseActionsImageId <= 0)
        return 0;

      _lock.EnterWriteLock();
      try
      {
        _dialogMirrorActive = true;
        _dialogTriggerNodeId = detectedNodeId;
        _pendingResponseActionsImageId = 0;

        var (id, created) = _automatizmSystem.CreateNewAutomatizm(detectedNodeId, responseActionsImageId, true);
        if (created == null)
          return 0;

        created.Count = 0;
        // Не ставим Belief=2, если для этой ветки уже есть штатный автоматизм (сдвиг): иначе эхо перезапишет его при повторных прогонах.
        if (!_automatizmSystem.ExistsAutomatizmForThisNodeId(detectedNodeId))
          _automatizmSystem.SetAutomatizmBelief(created, 2);

        Logger.Info($"MirrorAutomatizm: стартовый автоматизм ID={id}, TriggerNode={detectedNodeId}, ActionsImage={responseActionsImageId}");
        return id;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сохранить образ ответа оператора в период ожидания оценки.
    /// </summary>
    /// <param name="actionsImageId">ID образа действий (ответ оператора).</param>
    /// <param name="detectedNodeId">ID узла дерева автоматизмов, распознанного по ответу.</param>
    /// <param name="hasVerbalPart">Есть ли в ответе вербальная часть (фраза).</param>
    /// <param name="hasNonVerbalPart">Есть ли в ответе невербальная часть (действия с пульта).</param>
    /// <remarks>
    /// При смешанном стимуле (фраза + действие) InfluenceActionSystem вызывает и PhraseStimulusActivated,
    /// и TriggerStimulusActivated. Второй вызов (только действие) перезаписывал первый (полный).
    /// Не перезаписываем полный ответ более бедным (action-only), чтобы сохранить правильные ID.
    /// </remarks>
    public void RegisterOperatorResponse(
      int actionsImageId,
      int detectedNodeId,
      bool hasVerbalPart,
      bool hasNonVerbalPart = false)
    {
      if (actionsImageId <= 0 || detectedNodeId <= 0 || AppGlobalState.EvolutionStage != 3)
        return;

      _lock.EnterWriteLock();
      try
      {
        if (!_dialogMirrorActive)
          return;

        // При смешанном стимуле PhraseStimulusActivated вызывается первым (полный ответ),
        // затем TriggerStimulusActivated (только действие). Не перезаписывать полный ответ более бедным.
        if (_pendingResponseHasVerbalPart && !hasVerbalPart)
          return;

        _pendingResponseActionsImageId = actionsImageId;
        _pendingResponseNodeId = detectedNodeId;
        _pendingResponseHasVerbalPart = hasVerbalPart;
        _pendingResponseHasNonVerbalPart = hasNonVerbalPart;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Создаёт на 2-й стадии эхо-автоматизм по вербальному стимулу с пульта; при нескольких частях (разбивка по пробелу или дефису) — эхо по первой части и цепочку из не более чем 2 звеньев (второе звено — последняя часть, промежуточные отбрасываются).
    /// Пусковые условия: текущее состояние + стили + образ действий с пульта (вербальная часть с тоном/настроением + невербальная; одна из частей может быть пустой, обе — нет).
    /// </summary>
    /// <param name="detectedNodeId">ID узла дерева автоматизмов (триггер)</param>
    /// <param name="fullStimulusActionsImageId">ID полного образа действий стимула (вся фраза + действие)</param>
    /// <param name="partPhraseIds">Список ID фраз по частям (по одному на слог после разбивки по пробелу или дефису)</param>
    /// <param name="actionIdList">Действия с пульта (могут быть пустыми)</param>
    /// <param name="toneId">Тон</param>
    /// <param name="moodId">Настроение</param>
    /// <returns>ID созданного эхо-автоматизма или 0</returns>
    public int TryCreateStage2EchoWithChain(
      int detectedNodeId,
      int fullStimulusActionsImageId,
      List<int> partPhraseIds,
      List<int> actionIdList,
      int toneId,
      int moodId)
    {
      if (AppGlobalState.EvolutionStage != 2)
        return 0;
      if (detectedNodeId <= 0 || fullStimulusActionsImageId <= 0)
        return 0;
      if (partPhraseIds == null || partPhraseIds.Count == 0)
        return 0;

      var actList = actionIdList ?? new List<int>();

      // Образ для первой части (первое слово + действие с пульта) и ответный образ для эхо
      var (firstPartImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: actList,
          phraseIdList: new List<int> { partPhraseIds[0] },
          toneId: toneId,
          moodId: moodId,
          checkUnicum: true);
      if (firstPartImageId <= 0)
        return 0;

      int responseFirstPartImageId = GetOrCreateResponseActionsImageWithAdaptiveIds(firstPartImageId);
      if (responseFirstPartImageId <= 0)
        return 0;

      _lock.EnterWriteLock();
      try
      {
        var (echoId, created) = _automatizmSystem.CreateNewAutomatizm(detectedNodeId, responseFirstPartImageId, true);
        if (created == null)
          return 0;

        created.Count = 0;
        if (!_automatizmSystem.ExistsAutomatizmForThisNodeId(detectedNodeId))
          _automatizmSystem.SetAutomatizmBelief(created, 2);

        if (partPhraseIds.Count == 1)
        {
          Logger.Info($"Stage2 echo automatism ID={echoId}, node={detectedNodeId}, one part");
          return echoId;
        }

        // Цепочка: не более 2 звеньев. При 2 частях — 1 звено (вторая часть); при 3+ — 2 звена (вторая часть → последняя, промежуточные отбрасываются)
        var links = new List<AutomatizmChainsSystem.ChainLink>();
        int partForLink1 = partPhraseIds[1];
        var (img1Id, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
            kind: 0,
            actIdList: new List<int>(),
            phraseIdList: new List<int> { partForLink1 },
            toneId: toneId,
            moodId: moodId,
            checkUnicum: true);
        if (img1Id <= 0)
          return echoId;
        int responseImg1 = GetOrCreateResponseActionsImageWithAdaptiveIds(img1Id);
        if (responseImg1 <= 0)
          return echoId;
        links.Add(new AutomatizmChainsSystem.ChainLink
        {
          ID = 0,
          ChainID = 0,
          ActionsImageId = responseImg1,
          SuccessNextLink = 0,
          FailureNextLink = 0,
          Description = "Звено 1 (вторая часть)",
          ChainUsefulness = 1
        });

        if (partPhraseIds.Count >= 3)
        {
          int partForLink2 = partPhraseIds[partPhraseIds.Count - 1];
          var (img2Id, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
              kind: 0,
              actIdList: new List<int>(),
              phraseIdList: new List<int> { partForLink2 },
              toneId: toneId,
              moodId: moodId,
              checkUnicum: true);
          if (img2Id <= 0)
            return echoId;
          int responseImg2 = GetOrCreateResponseActionsImageWithAdaptiveIds(img2Id);
          if (responseImg2 <= 0)
            return echoId;
          links.Add(new AutomatizmChainsSystem.ChainLink
          {
            ID = 0,
            ChainID = 0,
            ActionsImageId = responseImg2,
            SuccessNextLink = 0,
            FailureNextLink = 0,
            Description = "Звено 2 (последняя часть)",
            ChainUsefulness = 1
          });
        }

        if (!AutomatizmChainsSystem.IsInitialized)
          return echoId;

        var (chainId, warnings) = AutomatizmChainsSystem.Instance.AddAutomatizmChain(
            name: "Эхо-цепочка ст.2",
            description: $"Эхо + цепочка по частям (узёл {detectedNodeId})",
            links: links,
            treeNodeId: detectedNodeId,
            startAutomatizmId: echoId);

        if (chainId == 0)
          return echoId;

        if (links.Count >= 2)
          links[0].SuccessNextLink = links[1].ID;
        _automatizmSystem.AttachChainToAutomatizm(echoId, chainId);

        var saveResult = AutomatizmChainsSystem.Instance.SaveAutomatizmChains();
        if (!saveResult.Success)
          Logger.Warning($"Stage2 echo+chain: цепочка {chainId} создана, сохранение: {saveResult.ErrorMessage}");

        Logger.Info($"Stage2 echo+chain: automatism ID={echoId}, chain ID={chainId}, node={detectedNodeId}");
        return echoId;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Создать зеркальный автоматизм (второй шаг) на ответ оператора.
    /// </summary>
    public int TryCreateMirrorFromPendingOperatorResponse()
    {
      if (AppGlobalState.EvolutionStage != 3)
      {
        ResetDialogMirror();
        return 0;
      }

      _lock.EnterWriteLock();
      try
      {
        if (!_dialogMirrorActive || _dialogTriggerNodeId <= 0 || _pendingResponseActionsImageId <= 0 || _pendingResponseNodeId <= 0)
          return 0;

        int responseActionsImageId = GetOrCreateResponseActionsImageWithAdaptiveIds(_pendingResponseActionsImageId);
        if (responseActionsImageId <= 0)
          return 0;

        // 1) Учительский автоматизм: предыдущий триггер -> ответ оператора.
        var (_, teacherAutomatizm) = _automatizmSystem.CreateNewAutomatizm(_dialogTriggerNodeId, responseActionsImageId, true);
        if (teacherAutomatizm == null)
        {
          ClearPendingOperatorResponse();
          return 0;
        }

        if (teacherAutomatizm.Usefulness < 1)
          teacherAutomatizm.Usefulness = 1;
        teacherAutomatizm.Count = Math.Max(teacherAutomatizm.Count, 1);
        _automatizmSystem.SetAutomatizmBelief(teacherAutomatizm, 2);

        // 2) Прямой автоматизм для нового шага: новый триггер -> его же ответ.
        // Создается как "провокатор" следующей пары (эхо). Belief=2 НЕ ставим: иначе эхо перезапишет
        // уже выученный сдвиговый автоматизм для этой ветки при повторных запусках цепочки.
        bool continueCycle = _pendingResponseHasVerbalPart ||
            (AppGlobalState.ObservationMode && _pendingResponseHasNonVerbalPart);
        if (continueCycle)
        {
          var (_, nextParrotAutomatizm) = _automatizmSystem.CreateNewAutomatizm(_pendingResponseNodeId, responseActionsImageId, true);
          if (nextParrotAutomatizm != null)
          {
            nextParrotAutomatizm.Count = 0;
            // Не вызываем SetAutomatizmBelief(..., 2): штатным остаётся сдвиг (учительский), эхо — только запасной.
          }
        }

        // Переносим триггер диалога на последний стимул оператора.
        _dialogTriggerNodeId = _pendingResponseNodeId;
        ClearPendingOperatorResponse();

        Logger.Info($"MirrorAutomatizm: учительский автоматизм ID={teacherAutomatizm.ID}, TriggerNode={teacherAutomatizm.BranchID}, ActionsImage={teacherAutomatizm.ActionsImageID}");
        return teacherAutomatizm.ID;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сбросить состояние текущего зеркального диалогового цикла.
    /// </summary>
    public void ResetDialogMirror()
    {
      _lock.EnterWriteLock();
      try
      {
        _dialogMirrorActive = false;
        _dialogTriggerNodeId = 0;
        ClearPendingOperatorResponse();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Освобождает ресурсы сервиса.
    /// </summary>
    public void Dispose()
    {
      if (_disposed)
        return;

      _lock.Dispose();
      _disposed = true;
    }

    private void ClearPendingOperatorResponse()
    {
      _pendingResponseActionsImageId = 0;
      _pendingResponseNodeId = 0;
      _pendingResponseHasVerbalPart = false;
      _pendingResponseHasNonVerbalPart = false;
    }

    /// <summary>
    /// Возвращает ID образа действий для ответа автоматизма с AdaptiveAction ID вместо InfluenceAction ID.
    /// Стимул с пульта хранится с InfluenceAction ID (Развеселить и т.п.), ответ агента — с AdaptiveAction ID (Смеется и т.п.).
    /// </summary>
    private int GetOrCreateResponseActionsImageWithAdaptiveIds(int stimulusActionsImageId)
    {
      if (stimulusActionsImageId <= 0) return 0;
      if (!ActionsImagesSystem.IsInitialized) return stimulusActionsImageId;

      var img = ActionsImagesSystem.Instance.GetActionsImage(stimulusActionsImageId);
      if (img == null) return stimulusActionsImageId;

      if (img.ActIdList == null || !img.ActIdList.Any())
        return stimulusActionsImageId;

      if (!AdaptiveActionsSystem.IsInitialized)
        return stimulusActionsImageId;

      List<int> adaptiveIds = AdaptiveActionsSystem.Instance.ConvertInfluenceActionIdsToAdaptiveActionIds(img.ActIdList);
      if (adaptiveIds == null) adaptiveIds = new List<int>();

      var (newId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
        kind: 1,
        actIdList: adaptiveIds,
        phraseIdList: img.PhraseIdList,
        toneId: img.ToneId,
        moodId: img.MoodId,
        checkUnicum: true);
      return newId > 0 ? newId : stimulusActionsImageId;
    }
  }
}
