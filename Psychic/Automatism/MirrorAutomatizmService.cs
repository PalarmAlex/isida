using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Сервис отзеркаливания автоматизмов (стадии 2–4+).
  /// Стадия 3: диалоговое зеркало без флага Teacher; стадии 2 и 4+: запись только при <see cref="AppGlobalState.TeachingMode"/>.
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
    private bool _pendingResponseHasCommandPart;
    private bool _pendingResponseHasNonVerbalPart;

    /// <summary>Активен ли цикл зеркального диалога (стадия 3).</summary>
    public bool IsDialogMirrorActive
    {
      get
      {
        _lock.EnterReadLock();
        try { return _dialogMirrorActive; }
        finally { _lock.ExitReadLock(); }
      }
    }

    /// <summary>
    /// Создает сервис отзеркаливания автоматизмов.
    /// </summary>
    public MirrorAutomatizmService(AutomatizmSystem automatizmSystem)
    {
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
    }

    /// <summary>
    /// Можно ли записывать отзеркаливание на текущей стадии (Teacher на 2/4+, ст.3 всегда).
    /// </summary>
    public static bool IsMirrorRecordingAllowedForStage()
    {
      int stage = AppGlobalState.EvolutionStage;
      if (stage == 3)
        return true;
      if (stage == 2 || stage >= 4)
        return AppGlobalState.TeachingMode;
      return false;
    }

    /// <summary>
    /// Teacher &gt; Observation: невербальное зеркалирование при Teacher или Observation.
    /// </summary>
    public static bool CanMirrorStimulusParts(bool hasVerbalPart, bool hasCommandPart, bool hasNonVerbalPart)
    {
      if (hasVerbalPart || hasCommandPart)
        return true;
      if (hasNonVerbalPart && (AppGlobalState.TeachingMode || AppGlobalState.ObservationMode))
        return true;
      return false;
    }

    /// <summary>
    /// Создать и вернуть первый (попугайский) автоматизм для запуска цикла зеркалирования (стадия 3).
    /// </summary>
    public int TryCreateInitialParrotAutomatizm(
      int detectedNodeId,
      int actionsImageId,
      bool hasVerbalPart,
      bool hasCommandPart = false,
      bool hasNonVerbalPart = false)
    {
      if (AppGlobalState.EvolutionStage != 3)
      {
        ResetDialogMirror();
        return 0;
      }

      if (!CanMirrorStimulusParts(hasVerbalPart, hasCommandPart, hasNonVerbalPart))
        return 0;
      if (detectedNodeId <= 0 || actionsImageId <= 0)
        return 0;

      int responseActionsImageId = GetOrCreateResponseActionsImageWithAdaptiveIds(actionsImageId);
      if (responseActionsImageId <= 0)
        return 0;

      _lock.EnterWriteLock();
      try
      {
        _dialogMirrorActive = true;
        _dialogTriggerNodeId = detectedNodeId;
        ClearPendingOperatorResponse();

        var (id, created) = _automatizmSystem.CreateNewAutomatizm(
            detectedNodeId, responseActionsImageId, true, AutomatizmConsolidationService.AutomatizmCreationRole.Echo);
        if (created == null)
          return 0;

        created.Count = 0;

        return id;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Запустить цикл зеркалирования для уже существующего автоматизма (стадия 3).
    /// </summary>
    public void StartDialogMirrorForExistingAutomatizm(int shiftAnchorTreeNodeId)
    {
      if (AppGlobalState.EvolutionStage != 3 || shiftAnchorTreeNodeId <= 0)
        return;

      _lock.EnterWriteLock();
      try
      {
        _dialogMirrorActive = true;
        _dialogTriggerNodeId = shiftAnchorTreeNodeId;
        if (!AppGlobalState.WaitingForOperatorEvaluation)
          ClearPendingOperatorResponse();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сохранить образ ответа оператора в период ожидания оценки.
    /// </summary>
    public void RegisterOperatorResponse(
      int actionsImageId,
      int detectedNodeId,
      bool hasVerbalPart,
      bool hasCommandPart = false,
      bool hasNonVerbalPart = false)
    {
      if (actionsImageId <= 0 || detectedNodeId <= 0)
        return;
      if (AppGlobalState.EvolutionStage < 2)
        return;

      _lock.EnterWriteLock();
      try
      {
        if (AppGlobalState.EvolutionStage >= 4 || AppGlobalState.EvolutionStage == 2)
        {
          _pendingResponseActionsImageId = actionsImageId;
          _pendingResponseNodeId = detectedNodeId;
          _pendingResponseHasVerbalPart = hasVerbalPart;
          _pendingResponseHasCommandPart = hasCommandPart;
          _pendingResponseHasNonVerbalPart = hasNonVerbalPart;
          return;
        }

        if (!_dialogMirrorActive)
          return;

        if ((_pendingResponseHasVerbalPart || _pendingResponseHasCommandPart) && !hasVerbalPart && !hasCommandPart)
          return;

        _pendingResponseActionsImageId = actionsImageId;
        _pendingResponseNodeId = detectedNodeId;
        _pendingResponseHasVerbalPart = hasVerbalPart;
        _pendingResponseHasCommandPart = hasCommandPart;
        _pendingResponseHasNonVerbalPart = hasNonVerbalPart;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Эхо-автоматизм на 2-й стадии (только при TeachingMode, кроме bootstrap).
    /// </summary>
    public int TryCreateStage2EchoWithChain(
      int detectedNodeId,
      int fullStimulusActionsImageId,
      List<int> partPhraseIds,
      List<int> actionIdList,
      int toneId,
      int moodId,
      bool bypassTeachingGate = false)
    {
      if (AppGlobalState.EvolutionStage != 2)
        return 0;
      if (!bypassTeachingGate && !AppGlobalState.TeachingMode)
        return 0;
      if (detectedNodeId <= 0 || fullStimulusActionsImageId <= 0)
        return 0;
      if (partPhraseIds == null || partPhraseIds.Count == 0)
        return 0;

      var fullStimulusImg = ActionsImagesSystem.Instance.GetActionsImage(fullStimulusActionsImageId);
      int stimulusVisual = fullStimulusImg?.VisualColorId ?? 0;
      if (!AgentVisualColor.IsValidCode(stimulusVisual))
        stimulusVisual = AgentVisualColor.White;

      var actList = actionIdList ?? new List<int>();

      var (firstPartImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: actList,
          phraseIdList: new List<int> { partPhraseIds[0] },
          toneId: toneId,
          moodId: moodId,
          checkUnicum: true,
          visualColorId: stimulusVisual);
      if (firstPartImageId <= 0)
        return 0;

      int responseFirstPartImageId = GetOrCreateResponseActionsImageWithAdaptiveIds(firstPartImageId);
      if (responseFirstPartImageId <= 0)
        return 0;

      _lock.EnterWriteLock();
      try
      {
        var (echoId, created) = _automatizmSystem.CreateNewAutomatizm(
            detectedNodeId, responseFirstPartImageId, true, AutomatizmConsolidationService.AutomatizmCreationRole.Echo);
        if (created == null)
          return 0;

        created.Count = 0;

        if (partPhraseIds.Count == 1)
          return echoId;

        var links = new List<AutomatizmChainsSystem.ChainLink>();
        int partForLink1 = partPhraseIds[1];
        var (img1Id, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
            kind: 0,
            actIdList: new List<int>(),
            phraseIdList: new List<int> { partForLink1 },
            toneId: toneId,
            moodId: moodId,
            checkUnicum: true,
            visualColorId: stimulusVisual);
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
              checkUnicum: true,
              visualColorId: stimulusVisual);
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

        return echoId;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сдвиг + эхо на стадии 3 по ответу оператора в окне ожидания.
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

        var (_, teacherAutomatizm) = _automatizmSystem.CreateNewAutomatizm(
            _dialogTriggerNodeId,
            responseActionsImageId,
            true,
            AutomatizmConsolidationService.AutomatizmCreationRole.Shift);
        if (teacherAutomatizm == null)
        {
          ClearPendingOperatorResponse();
          return 0;
        }

        teacherAutomatizm.Count = Math.Max(teacherAutomatizm.Count, 0);
        _automatizmSystem.SetAutomatizmBelief(teacherAutomatizm, 2);

        bool continueCycle = CanMirrorStimulusParts(
            _pendingResponseHasVerbalPart,
            _pendingResponseHasCommandPart,
            _pendingResponseHasNonVerbalPart);
        if (continueCycle)
        {
          var (_, nextParrotAutomatizm) = _automatizmSystem.CreateNewAutomatizm(
              _pendingResponseNodeId,
              responseActionsImageId,
              true,
              AutomatizmConsolidationService.AutomatizmCreationRole.Echo);
          if (nextParrotAutomatizm != null)
            nextParrotAutomatizm.Count = 0;
        }

        _dialogTriggerNodeId = _pendingResponseNodeId;
        ClearPendingOperatorResponse();

        return teacherAutomatizm.ID;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сдвиг на стадиях 4+ при TeachingMode (без эхо). Якорь — узел ответа симбионта.
    /// </summary>
    public int TryCreateTeachingShiftAutomatizm(int shiftAnchorNodeId, int operatorStimulusActionsImageId)
    {
      if (AppGlobalState.EvolutionStage < 4 || !AppGlobalState.TeachingMode)
        return 0;
      if (shiftAnchorNodeId <= 0 || operatorStimulusActionsImageId <= 0)
        return 0;

      int responseActionsImageId = GetOrCreateResponseActionsImageWithAdaptiveIds(operatorStimulusActionsImageId);
      if (responseActionsImageId <= 0)
        return 0;

      var (_, shift) = _automatizmSystem.CreateNewAutomatizm(
          shiftAnchorNodeId,
          responseActionsImageId,
          true,
          AutomatizmConsolidationService.AutomatizmCreationRole.Shift);
      if (shift == null)
        return 0;

      _automatizmSystem.SetAutomatizmBelief(shift, 2);
      return shift.ID;
    }

    /// <summary>
    /// Сбросить отложенный стимул оператора без создания эхо/сдвига.
    /// </summary>
    public void DiscardPendingOperatorResponseWithoutMirror()
    {
      _lock.EnterWriteLock();
      try { ClearPendingOperatorResponse(); }
      finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Задать ветку-триггер для следующего сдвига при активном зеркале.
    /// </summary>
    public void SetDialogTriggerNodeIdForActiveMirror(int treeNodeId)
    {
      if (treeNodeId <= 0)
        return;
      _lock.EnterWriteLock();
      try
      {
        if (!_dialogMirrorActive)
          return;
        _dialogTriggerNodeId = treeNodeId;
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
      _pendingResponseHasCommandPart = false;
      _pendingResponseHasNonVerbalPart = false;
    }

    /// <summary>ID образа действий ответа оператора (для FixTeacherRule, stage 4)</summary>
    public int GetPendingOperatorResponseActionsImageId()
    {
      _lock.EnterReadLock();
      try { return _pendingResponseActionsImageId; }
      finally { _lock.ExitReadLock(); }
    }

    /// <summary>Узел дерева по отложенному ответу оператора.</summary>
    public int GetPendingResponseTreeNodeId()
    {
      _lock.EnterReadLock();
      try { return _pendingResponseNodeId; }
      finally { _lock.ExitReadLock(); }
    }

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
        checkUnicum: true,
        visualColorId: img.VisualColorId,
        commandPatternIdList: img.CommandPatternIdList?.ToList());
      return newId > 0 ? newId : stimulusActionsImageId;
    }
  }
}
