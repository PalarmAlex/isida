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
  /// Сервис отзеркаливания автоматизмов для 3-й стадии.
  /// Создает автоматизмы вида: триггер стимула с пульта → ответ оператора.
  /// В обычном режиме учитываются только вербальные стимулы; в режиме наблюдения — вербальные, невербальные (флажки воздействий) и смешанные.
  /// </summary>
  /// <remarks>
  /// Штатный автоматизм на ветке (<see cref="Automatizm.Belief"/> = 2) в системе ровно один на
  /// <see cref="Automatizm.BranchID"/>; назначение только через <see cref="AutomatizmSystem.SetAutomatizmBelief"/>.
  /// Сдвиг и эхо на разных узлах — два разных BranchID, у каждого свой единственный Belief=2.
  /// Если эхо и сдвиг совпали бы по BranchID, второму нельзя ставить Belief=2 (иначе нарушится инвариант).
  /// </remarks>
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
    /// <param name="hasCommandPart">Есть ли в стимуле командная часть (Solid-команда).</param>
    /// <param name="hasNonVerbalPart">Есть ли в стимуле невербальная часть (действия с пульта).</param>
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

      bool canMirror = hasVerbalPart || hasCommandPart || (AppGlobalState.ObservationMode && hasNonVerbalPart);
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
        ClearPendingOperatorResponse();

        var (id, created) = _automatizmSystem.CreateNewAutomatizm(detectedNodeId, responseActionsImageId, true);
        if (created == null)
          return 0;

        created.Count = 0;
        // Штатный Belief=2 только если на ветке ещё нет автоматизма: иначе эхо перезапишет уже выученный сдвиг при повторных прогонах.
        if (!_automatizmSystem.ExistsAutomatizmForThisNodeId(detectedNodeId))
          _automatizmSystem.SetAutomatizmBelief(created, 2);

        return id;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Снимок состояния зеркала без захвата блокировки — вызывать только под уже удерживаемым lock сервиса.
    /// </summary>
    private string MirrorStateInlineUnsynchronized()
    {
      return
          $"active={_dialogMirrorActive}, triggerNode={_dialogTriggerNodeId}, pendingImg={_pendingResponseActionsImageId}, pendingNode={_pendingResponseNodeId}, pendingVerbal={_pendingResponseHasVerbalPart}, pendingCommand={_pendingResponseHasCommandPart}, pendingNonVerbal={_pendingResponseHasNonVerbalPart}";
    }

    /// <summary>
    /// Запустить цикл зеркалирования для уже существующего автоматизма: следующий стимул оператора в окне ожидания будет считаться ответом и создаст пары эхо и сдвиг.
    /// Вызывается при выполнении найденного автоматизма (не попугайского), чтобы следующий стимул с пульта образовывал пары «новый стимул — новый стимул» и «предыдущий ответ симбионта — новый стимул».
    /// </summary>
    /// <param name="shiftAnchorTreeNodeId">
    /// Узел дерева — якорь следующего сдвига S_{n-1}→S_n: для выученного автоматизма передаётся узел фразы ответа симбионта
    /// (по образу действий выполняемого автоматизма), иначе якорь не совпадёт с цепочкой зеркала.
    /// </param>
    public void StartDialogMirrorForExistingAutomatizm(int shiftAnchorTreeNodeId)
    {
      if (AppGlobalState.EvolutionStage != 3 || shiftAnchorTreeNodeId <= 0)
        return;

      _lock.EnterWriteLock();
      try
      {
        _dialogMirrorActive = true;
        _dialogTriggerNodeId = shiftAnchorTreeNodeId;
        // На пульсе ответа оператора TrySchedule уже вызвал RegisterOperatorResponse — не сбрасывать pending до EvaluatePrevious / TryMirror.
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
    /// <param name="actionsImageId">ID образа действий (ответ оператора).</param>
    /// <param name="detectedNodeId">ID узла дерева автоматизмов, распознанного по ответу.</param>
    /// <param name="hasVerbalPart">Есть ли в ответе вербальная часть (фраза).</param>
    /// <param name="hasCommandPart">Есть ли в ответе командная часть.</param>
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
          return;
        }
        if (!_dialogMirrorActive)
        {
          return;
        }

        // При смешанном стимуле PhraseStimulusActivated вызывается первым (полный ответ),
        // затем TriggerStimulusActivated (только действие). Не перезаписывать полный ответ более бедным.
        if ((_pendingResponseHasVerbalPart || _pendingResponseHasCommandPart) && !hasVerbalPart && !hasCommandPart)
        {
          return;
        }

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

      var fullStimulusImg = ActionsImagesSystem.Instance.GetActionsImage(fullStimulusActionsImageId);
      int stimulusVisual = fullStimulusImg?.VisualColorId ?? 0;
      if (!AgentVisualColor.IsValidCode(stimulusVisual))
        stimulusVisual = AgentVisualColor.White;

      var actList = actionIdList ?? new List<int>();

      // Образ для первой части (первое слово + действие с пульта) и ответный образ для эхо
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
        var (echoId, created) = _automatizmSystem.CreateNewAutomatizm(detectedNodeId, responseFirstPartImageId, true);
        if (created == null)
          return 0;

        created.Count = 0;
        if (!_automatizmSystem.ExistsAutomatizmForThisNodeId(detectedNodeId))
          _automatizmSystem.SetAutomatizmBelief(created, 2);

        if (partPhraseIds.Count == 1)
          return echoId;

        // Цепочка: не более 2 звеньев. При 2 частях — 1 звено (вторая часть); при 3+ — 2 звена (вторая часть → последняя, промежуточные отбрасываются)
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
    /// Создать зеркальную пару на ответ оператора (стадия 3): сначала сдвиг S_{n-1}→S_n на якоре <see cref="_dialogTriggerNodeId"/>,
    /// затем при продолжении цикла — эхо S_n→S_n без Belief=2 (провокатор следующей пары; штатным остаётся сдвиг).
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

        // 1) Сдвиг: предыдущий якорь → ответ оператора (штатный Belief=2 на этой ветке).
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

        // 2) Эхо на ветке нового стимула: S_n→S_n. Belief=2 не ставим — иначе эхо перезапишет выученный сдвиг на этой ветке.
        bool continueCycle = _pendingResponseHasVerbalPart || _pendingResponseHasCommandPart ||
            (AppGlobalState.ObservationMode && _pendingResponseHasNonVerbalPart);
        if (continueCycle)
        {
          var (_, nextParrotAutomatizm) = _automatizmSystem.CreateNewAutomatizm(_pendingResponseNodeId, responseActionsImageId, true);
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
    /// Сбросить отложенный стимул оператора без создания эхо/сдвига (ст. 3: ответ в окне ожидания обработан только активацией штатного сдвига).
    /// </summary>
    public void DiscardPendingOperatorResponseWithoutMirror()
    {
      _lock.EnterWriteLock();
      try
      {
        ClearPendingOperatorResponse();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Задать ветку-триггер для следующего сдвига при активном зеркале (ст. 3: после штатного сдвига — узел фразы ответа симбионта для «доращивания» цепочки).
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

        int prev = _dialogTriggerNodeId;
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

    /// <summary>Узел дерева по отложенному ответу оператора (ст. 3, до <see cref="TryCreateMirrorFromPendingOperatorResponse"/>).</summary>
    public int GetPendingResponseTreeNodeId()
    {
      _lock.EnterReadLock();
      try { return _pendingResponseNodeId; }
      finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Возвращает ID образа действий для ответа автоматизма с AdaptiveAction ID вместо InfluenceAction ID.
    /// Стимул с пульта хранится с InfluenceAction ID (Развеселить и т.п.), ответ симбионта — с AdaptiveAction ID (Смеется и т.п.).
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
        checkUnicum: true,
        visualColorId: img.VisualColorId,
        commandPatternIdList: img.CommandPatternIdList?.ToList());
      return newId > 0 ? newId : stimulusActionsImageId;
    }
  }
}
