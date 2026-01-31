using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic.Automatism;
using ISIDA.Reflexes;
using ISIDA.Sensors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Конвертер условных рефлексов в автоматизмы
  /// </summary>
  public sealed class ConditionedReflexToAutomatizmConverter : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly ConditionedReflexesSystem _conditionedReflexesSystem;
    private readonly GeneticReflexesSystem _geneticReflexesSystem;
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private readonly EmotionsImageSystem _emotionsImageSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;
    private readonly AutomatizmTreeSystem _automatizmTreeSystem;
    private readonly AutomatizmSystem _automatizmSystem;
    private bool _disposed = false;

    #region Инициализация

    private static ConditionedReflexToAutomatizmConverter _instance;

    /// <summary>
    /// Глобальный экземпляр конвертера
    /// </summary>
    public static ConditionedReflexToAutomatizmConverter Instance => _instance ??
        throw new InvalidOperationException("ConditionedReflexToAutomatizmConverter не инициализирован.");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр конвертера
    /// </summary>
    public static void InitializeInstance(
        ConditionedReflexesSystem conditionedReflexesSystem,
        GeneticReflexesSystem geneticReflexesSystem,
        AdaptiveActionsSystem adaptiveActionsSystem,
        EmotionsImageSystem emotionsImageSystem,
        ActionsImagesSystem actionsImagesSystem,
        AutomatizmTreeSystem automatizmTreeSystem,
        AutomatizmSystem automatizmSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("ConditionedReflexToAutomatizmConverter уже инициализирован.");

      _instance = new ConditionedReflexToAutomatizmConverter(
          conditionedReflexesSystem,
          geneticReflexesSystem,
          adaptiveActionsSystem,
          emotionsImageSystem,
          actionsImagesSystem,
          automatizmTreeSystem,
          automatizmSystem);
    }

    private ConditionedReflexToAutomatizmConverter(
        ConditionedReflexesSystem conditionedReflexesSystem,
        GeneticReflexesSystem geneticReflexesSystem,
        AdaptiveActionsSystem adaptiveActionsSystem,
        EmotionsImageSystem emotionsImageSystem,
        ActionsImagesSystem actionsImagesSystem,
        AutomatizmTreeSystem automatizmTreeSystem,
        AutomatizmSystem automatizmSystem)
    {
      _conditionedReflexesSystem = conditionedReflexesSystem ??
          throw new ArgumentNullException(nameof(conditionedReflexesSystem));
      _geneticReflexesSystem = geneticReflexesSystem ??
          throw new ArgumentNullException(nameof(geneticReflexesSystem));
      _adaptiveActionsSystem = adaptiveActionsSystem ??
          throw new ArgumentNullException(nameof(adaptiveActionsSystem));
      _emotionsImageSystem = emotionsImageSystem ??
          throw new ArgumentNullException(nameof(emotionsImageSystem));
      _actionsImagesSystem = actionsImagesSystem ??
          throw new ArgumentNullException(nameof(actionsImagesSystem));
      _automatizmTreeSystem = automatizmTreeSystem ??
          throw new ArgumentNullException(nameof(automatizmTreeSystem));
      _automatizmSystem = automatizmSystem ??
          throw new ArgumentNullException(nameof(automatizmSystem));
    }

    #endregion

    #region Основные методы конвертации

    /// <summary>
    /// Клонирует все условные рефлексы в автоматизмы
    /// </summary>
    public (int SuccessCount, int TotalCount, List<string> Errors) CloneAllConditionedReflexesToAutomatisms()
    {
      var errors = new List<string>();
      int successCount = 0;
      int totalCount = 0;

      try
      {
        if (AppGlobalState.EvolutionStage < 2)
          return (0, 0, new List<string> { $"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для автоматизмов" });

        _lock.EnterWriteLock();
        try
        {
          var allConditionedReflexes = _conditionedReflexesSystem.GetAllConditionedReflexes();
          totalCount = allConditionedReflexes.Count;

          if (totalCount == 0)
            return (0, 0, new List<string> { "Нет условных рефлексов для клонирования" });

          foreach (var conditionedReflex in allConditionedReflexes)
          {
            try
            {
              var result = ConvertConditionedReflexToAutomatizm(conditionedReflex);
              if (result.Success)
                successCount++;
              else
                errors.Add($"Условный рефлекс ID={conditionedReflex.Id}: {result.Error}");
            }
            catch (Exception ex)
            {
              errors.Add($"Условный рефлекс ID={conditionedReflex.Id}: {ex.Message}");
              Logger.Error(ex.Message);
            }
          }
          return (successCount, totalCount, errors);
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }
      catch (Exception ex)
      {
        errors.Add($"Общая ошибка: {ex.Message}");
        Logger.Error(ex.Message);
        return (successCount, totalCount, errors);
      }
    }

    /// <summary>
    /// Конвертирует один условный рефлекс в автоматизм
    /// </summary>
    public (bool Success, int AutomatizmId, string Error) ConvertConditionedReflexToAutomatizm(
        ConditionedReflexesSystem.ConditionedReflex conditionedReflex)
    {
      try
      {
        var actionIds = GetActionsFromConditionedReflex(conditionedReflex);
        if (actionIds == null || !actionIds.Any())
          return (false, 0, $"Нет действий для условного рефлекса ID={conditionedReflex.Id}");

        var treeComponentsResult = ConvertReflexLevelsToTreeComponents(conditionedReflex, actionIds);
        if (!treeComponentsResult.IsValid)
          return (false, 0, treeComponentsResult.Error);

        var treeComponents = treeComponentsResult.Components;

        int nodeId = FindOrCreateAutomatizmTreeNode(treeComponents);
        if (nodeId == 0)
          return (false, 0, $"Не удалось найти или создать узел в дереве автоматизмов");


        var existingAutomatizm = _automatizmSystem.GetAutomatizmFromNodeIdNoLock(nodeId);
        if (existingAutomatizm != null)
          return (true, existingAutomatizm.ID, "Автоматизм уже существует");

        int actionsImageId = CreateActionsImageForReflex(actionIds);
        if (actionsImageId == 0)
          return (false, 0, $"Не удалось создать образ действий");

        Automatizm automatizm = null;
        (_, automatizm) = _automatizmSystem.CreateNewAutomatizm(
            branchId: nodeId,
            actionsImageId: actionsImageId,
            checkUnicum: true);

        if (automatizm == null)
          return (false, 0, $"Не удалось создать автоматизм");

        ConfigureAutomatizmFromReflex(automatizm, conditionedReflex, actionIds);

        return (true, automatizm.ID, "Успешно");
      }
      catch (Exception ex)
      {
        return (false, 0, $"Ошибка: {ex.Message}");
      }
    }

    /// <summary>
    /// Конвертирует конкретный условный рефлекс по ID
    /// </summary>
    public (bool Success, int AutomatizmId, string Error) ConvertConditionedReflexById(int conditionedReflexId)
    {
      try
      {
        var conditionedReflex = _conditionedReflexesSystem.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == conditionedReflexId);

        if (conditionedReflex == null)
          return (false, 0, $"Условный рефлекс с ID {conditionedReflexId} не найден");

        return ConvertConditionedReflexToAutomatizm(conditionedReflex);
      }
      catch (Exception ex)
      {
        return (false, 0, $"Ошибка: {ex.Message}");
      }
    }

    #endregion

    #region Вспомогательные методы

    /// <summary>
    /// Получает действия из условного рефлекса
    /// </summary>
    private List<int> GetActionsFromConditionedReflex(ConditionedReflexesSystem.ConditionedReflex conditionedReflex)
    {
      try
      {
        var geneticReflex = _geneticReflexesSystem.GetAllGeneticReflexesList()
            .FirstOrDefault(r => r.Id == conditionedReflex.SourceGeneticReflexId);

        if (geneticReflex == null)
        {
          Logger.Warning($"Не найден безусловный рефлекс ID={conditionedReflex.SourceGeneticReflexId} для условного рефлекса ID={conditionedReflex.Id}");
          return new List<int>();
        }

        if (geneticReflex.AdaptiveActions == null || !geneticReflex.AdaptiveActions.Any())
        {
          Logger.Warning($"Безусловный рефлекс ID={geneticReflex.Id} не содержит действий");
          return new List<int>();
        }

        return geneticReflex.AdaptiveActions.ToList();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return new List<int>();
      }
    }

    /// <summary>
    /// Преобразует уровни рефлекса в компоненты дерева автоматизмов
    /// </summary>
    private (bool IsValid, string Error, TreeComponents Components) ConvertReflexLevelsToTreeComponents(
        ConditionedReflexesSystem.ConditionedReflex reflex,
        List<int> actionIds)
    {
      try
      {
        var components = new TreeComponents();

        components.BaseID = reflex.Level1;

        if (reflex.Level2 != null && reflex.Level2.Any())
        {
          (int emotionId, var emotionImage) = _emotionsImageSystem.CreateNewEmotionsImage(reflex.Level2, true);
          components.EmotionID = emotionId;
        }

        (int activityId, var activityImage) = _actionsImagesSystem.CreateNewActionsImage(
            kind: 0,
            actIdList: actionIds,
            phraseIdList: null,
            toneId: 0,
            moodId: 0,
            checkUnicum: true);

        components.ActivityID = activityId;
        components.ToneMoodID = PsychicSystem.GetToneMoodID(0, 0);
        components.SimbolID = 0;
        components.VerbID = 0;

        return (true, string.Empty, components);
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка преобразования уровней: {ex.Message}", new TreeComponents());
      }
    }

    /// <summary>
    /// Находит или создает узел в дереве автоматизмов
    /// </summary>
    private int FindOrCreateAutomatizmTreeNode(TreeComponents components)
    {
      try
      {
        // если узла нет, он создается автоматом при активации дерева
        int newNodeId = _automatizmTreeSystem.AutomatizmTreeActivation(
            components.BaseID,
            components.EmotionID,
            components.ActivityID,
            components.ToneMoodID,
            components.SimbolID,
            components.VerbID,
            isUnrecognizedPhrase: false);

        if (newNodeId > 0)
          return newNodeId;

        return 0;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return 0;
      }
    }

    /// <summary>
    /// Создает образ действий для автоматизма
    /// </summary>
    private int CreateActionsImageForReflex(List<int> actionIds)
    {
      try
      {
        var (imageId, _) = _actionsImagesSystem.CreateNewActionsImage(
            kind: 0,
            actIdList: actionIds,
            phraseIdList: null,
            toneId: 0,
            moodId: 0,
            checkUnicum: true);

        return imageId;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return 0;
      }
    }

    /// <summary>
    /// Настраивает автоматизм на основе рефлекса
    /// </summary>
    private void ConfigureAutomatizmFromReflex(
        Automatizm automatizm,
        ConditionedReflexesSystem.ConditionedReflex reflex,
        List<int> actionIds)
    {
      try
      {
        if(AppGlobalState.EvolutionStage == 2)
          automatizm.Usefulness = 5;
        else
          automatizm.Usefulness = 3;

        automatizm.Count = 1;
        automatizm.Energy = 5;
        automatizm.Belief = 1;

        var targetParams = new List<int>();
        foreach (var actionId in actionIds)
        {
          var action = _adaptiveActionsSystem.GetAllAdaptiveActions()
              .FirstOrDefault(a => a.Id == actionId);

          if (action != null && action.TargetGomeoParamIdArr != null)
            targetParams.AddRange(action.TargetGomeoParamIdArr);
        }

        automatizm.GomeoIdSuccesArr = targetParams.Distinct().ToList();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    #endregion

    #region Вспомогательные классы

    /// <summary>
    /// Компоненты узла дерева автоматизмов
    /// </summary>
    private class TreeComponents
    {
      public int BaseID { get; set; }
      public int EmotionID { get; set; }
      public int ActivityID { get; set; }
      public int ToneMoodID { get; set; }
      public int SimbolID { get; set; }
      public int VerbID { get; set; }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы объекта
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      _lock?.Dispose();
      _disposed = true;
    }

    #endregion
  }
}