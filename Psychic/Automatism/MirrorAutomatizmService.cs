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

        // Первый шаг цикла не должен доминировать при выборе "лучшего" автоматизма.
        created.Usefulness = 0;
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
            nextParrotAutomatizm.Usefulness = 0;
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
