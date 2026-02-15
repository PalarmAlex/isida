using ISIDA.Common;
using System;
using System.Threading;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Сервис отзеркаливания автоматизмов для 3-й стадии.
  /// Создает автоматизмы вида: триггер первого вербального стимула -> ответ оператора.
  /// </summary>
  public sealed class MirrorAutomatizmService : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly AutomatizmSystem _automatizmSystem;
    private bool _disposed;

    private bool _dialogMirrorActive;
    private int _dialogTriggerNodeId;
    private int _pendingResponseActionsImageId;

    /// <summary>
    /// Создает сервис отзеркаливания автоматизмов.
    /// </summary>
    public MirrorAutomatizmService(AutomatizmSystem automatizmSystem)
    {
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
    }

    /// <summary>
    /// Создать и вернуть первый (попугайский) автоматизм для запуска цикла зеркалирования.
    /// </summary>
    public int TryCreateInitialParrotAutomatizm(
      int detectedNodeId,
      int actionsImageId,
      bool hasVerbalPart)
    {
      if (AppGlobalState.EvolutionStage != 3)
      {
        ResetDialogMirror();
        return 0;
      }

      if (detectedNodeId <= 0 || actionsImageId <= 0 || !hasVerbalPart)
        return 0;

      _lock.EnterWriteLock();
      try
      {
        _dialogMirrorActive = true;
        _dialogTriggerNodeId = detectedNodeId;
        _pendingResponseActionsImageId = 0;

        var (id, created) = _automatizmSystem.CreateNewAutomatizm(detectedNodeId, actionsImageId, true);
        if (created == null)
          return 0;

        // Первый шаг цикла не должен доминировать при выборе "лучшего" автоматизма.
        created.Usefulness = 0;
        created.Count = 0;

        Logger.Info($"MirrorAutomatizm: стартовый автоматизм ID={id}, TriggerNode={detectedNodeId}, ActionsImage={actionsImageId}");
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
    public void RegisterOperatorResponseActionsImage(int actionsImageId)
    {
      if (actionsImageId <= 0 || AppGlobalState.EvolutionStage != 3)
        return;

      _lock.EnterWriteLock();
      try
      {
        if (!_dialogMirrorActive)
          return;

        _pendingResponseActionsImageId = actionsImageId;
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
        if (!_dialogMirrorActive || _dialogTriggerNodeId <= 0 || _pendingResponseActionsImageId <= 0)
          return 0;

        var (id, created) = _automatizmSystem.CreateNewAutomatizm(_dialogTriggerNodeId, _pendingResponseActionsImageId, true);
        _pendingResponseActionsImageId = 0;
        if (created == null)
          return 0;

        // Второй шаг цикла является авторитетной демонстрацией правильного ответа оператора.
        if (created.Usefulness < 1)
          created.Usefulness = 1;
        created.Count = Math.Max(created.Count, 1);

        Logger.Info($"MirrorAutomatizm: зеркальный автоматизм ID={id}, TriggerNode={_dialogTriggerNodeId}, ActionsImage={created.ActionsImageID}");
        return id;
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
        _pendingResponseActionsImageId = 0;
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
  }
}
