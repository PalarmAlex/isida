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
using static ISIDA.Psychic.VerbalBrocaImagesSystem;

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
    private readonly PerceptionImagesSystem _perceptionImagesSystem;
    private readonly SensorySystem _sensorySystem;
    private readonly VerbalBrocaImagesSystem _verbalBrocaImages;
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
        AutomatizmSystem automatizmSystem,
        PerceptionImagesSystem perceptionImagesSystem,
        SensorySystem sensorySystem,
        VerbalBrocaImagesSystem verbalBrocaImagesSystem)
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
          automatizmSystem,
          perceptionImagesSystem,
          sensorySystem,
          verbalBrocaImagesSystem);
    }

    private ConditionedReflexToAutomatizmConverter(
        ConditionedReflexesSystem conditionedReflexesSystem,
        GeneticReflexesSystem geneticReflexesSystem,
        AdaptiveActionsSystem adaptiveActionsSystem,
        EmotionsImageSystem emotionsImageSystem,
        ActionsImagesSystem actionsImagesSystem,
        AutomatizmTreeSystem automatizmTreeSystem,
        AutomatizmSystem automatizmSystem,
        PerceptionImagesSystem perceptionImagesSystem,
        SensorySystem sensorySystem,
        VerbalBrocaImagesSystem verbalBrocaImagesSystem)
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
      _perceptionImagesSystem = perceptionImagesSystem ??
          throw new ArgumentNullException(nameof(perceptionImagesSystem));
      _sensorySystem = sensorySystem ??
          throw new ArgumentNullException(nameof(sensorySystem));
      _verbalBrocaImages = verbalBrocaImagesSystem ??
          throw new ArgumentNullException(nameof(verbalBrocaImagesSystem));
    }

    #endregion

    #region Основные методы конвертации

    /// <summary>
    /// Клонирует все условные рефлексы в автоматизмы
    /// </summary>
    public (int NewCount, int ExistingCount, int TotalCount, int DuplicateCount, List<string> Errors) CloneAllConditionedReflexesToAutomatisms()
    {
      var errors = new List<string>();
      int newCount = 0;
      int existingCount = 0;
      int duplicateCount = 0;
      int totalCount = 0;

      try
      {
        if (AppGlobalState.EvolutionStage < 2)
          return (0, 0, 0, 0, new List<string> { $"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для автоматизмов" });

        _lock.EnterWriteLock();
        try
        {
          var allConditionedReflexes = _conditionedReflexesSystem.GetAllConditionedReflexes();
          totalCount = allConditionedReflexes.Count;

          if (totalCount == 0)
            return (0, 0, 0, 0, new List<string> { "Нет условных рефлексов для клонирования" });

          var processedImageIds = new HashSet<int>();

          foreach (var conditionedReflex in allConditionedReflexes)
          {
            try
            {
              var (actions, phrases) = GetStimulusDetailsFromConditionedReflex(conditionedReflex);
              var imageHash = CalculateImageHash(conditionedReflex.Level1, actions, phrases);

              if (processedImageIds.Contains(imageHash))
              {
                duplicateCount++;
                continue;
              }

              processedImageIds.Add(imageHash);

              var result = ConvertConditionedReflexToAutomatizm(conditionedReflex);
              if (result.Success)
              {
                switch (result.Status)
                {
                  case ConversionStatus.Created:
                    newCount++;
                    break;
                  case ConversionStatus.AlreadyExists:
                    existingCount++;
                    break;
                }
              }
              else
              {
                errors.Add($"Условный рефлекс ID={conditionedReflex.Id}: {result.Error}");
              }
            }
            catch (Exception ex)
            {
              errors.Add($"Условный рефлекс ID={conditionedReflex.Id}: {ex.Message}");
              Logger.Error(ex.Message);
            }
          }

          Logger.Info($"Конвертация завершена: {newCount} новых, {existingCount} существующих, {duplicateCount} дубликатов, {errors.Count} ошибок");

          return (newCount, existingCount, totalCount, duplicateCount, errors);
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
        return (newCount, existingCount, totalCount, duplicateCount, errors);
      }
    }

    /// <summary>
    /// Конвертирует один условный рефлекс в автоматизм
    /// </summary>
    public (bool Success, int AutomatizmId, ConversionStatus Status, string Error) ConvertConditionedReflexToAutomatizm(
        ConditionedReflexesSystem.ConditionedReflex conditionedReflex)
    {
      try
      {
        var actionIds = GetActionsFromConditionedReflex(conditionedReflex);
        if (actionIds == null || !actionIds.Any())
          return (false, 0, ConversionStatus.Failed, $"Нет действий для условного рефлекса ID={conditionedReflex.Id}");

        var (phrases, toneId, moodId) = GetPhrasesFromConditionedReflex(conditionedReflex);
        int symbolId = 0;
        int verbId = 0;

        if (phrases?.Any() == true)
        {
          symbolId = _sensorySystem.VerbalChannel.GetFirstSymbolFromPhraseId(phrases[0]);
          (verbId, _) = _verbalBrocaImages.CreateNewVerbalBrocaImage(symbolId, phrases, toneId, moodId, true);
        }

        var treeComponentsResult = ConvertReflexLevelsToTreeComponents(
            conditionedReflex,
            actionIds,
            phrases,
            toneId,
            moodId,
            symbolId,
            verbId);

        if (!treeComponentsResult.IsValid)
          return (false, 0, ConversionStatus.Failed, treeComponentsResult.Error);

        var treeComponents = treeComponentsResult.Components;

        int nodeId = FindOrCreateAutomatizmTreeNode(treeComponents);
        if (nodeId == 0)
          return (false, 0, ConversionStatus.Failed, $"Не удалось найти или создать узел в дереве автоматизмов");

        // Проверяем существующий автоматизм
        var existingAutomatizm = _automatizmSystem.GetAutomatizmFromNodeIdNoLock(nodeId);
        if (existingAutomatizm != null)
          return (true, existingAutomatizm.ID, ConversionStatus.AlreadyExists, "Автоматизм уже существует");

        // для автоматизма по условному рефлексу только действия
        int actionsImageId = CreateActionsImageForReflex(actionIds, null, 0, 0);
        if (actionsImageId == 0)
          return (false, 0, ConversionStatus.Failed, $"Не удалось создать образ действий");

        Automatizm automatizm = null;
        (_, automatizm) = _automatizmSystem.CreateNewAutomatizm(nodeId, actionsImageId, true);

        if (automatizm == null)
          return (false, 0, ConversionStatus.Failed, $"Не удалось создать автоматизм");

        ConfigureAutomatizmFromReflex(automatizm, conditionedReflex, actionIds);

        return (true, automatizm.ID, ConversionStatus.Created, "Успешно создан");
      }
      catch (Exception ex)
      {
        return (false, 0, ConversionStatus.Failed, $"Ошибка: {ex.Message}");
      }
    }

    /// <summary>
    /// Конвертирует конкретный условный рефлекс по ID
    /// </summary>
    public (bool Success, int AutomatizmId, ConversionStatus Status, string Error) ConvertConditionedReflexById(int conditionedReflexId)
    {
      try
      {
        var conditionedReflex = _conditionedReflexesSystem.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == conditionedReflexId);

        if (conditionedReflex == null)
          return (false, 0, ConversionStatus.Failed, $"Условный рефлекс с ID {conditionedReflexId} не найден");

        return ConvertConditionedReflexToAutomatizm(conditionedReflex);
      }
      catch (Exception ex)
      {
        return (false, 0, ConversionStatus.Failed, $"Ошибка: {ex.Message}");
      }
    }
    #endregion

    #region Вспомогательные методы

    /// <summary>
    /// Вычисляет хэш образа для обнаружения дубликатов
    /// </summary>
    private int CalculateImageHash(int baseId, List<int> actions, List<int> phrases)
    {
      unchecked
      {
        int hash = 17;
        hash = hash * 31 + baseId.GetHashCode();

        if (actions != null)
        {
          foreach (var actionId in actions.OrderBy(x => x))
            hash = hash * 31 + actionId.GetHashCode();
        }

        if (phrases != null)
        {
          foreach (var phraseId in phrases.OrderBy(x => x))
            hash = hash * 31 + phraseId.GetHashCode();
        }

        return hash;
      }
    }

    /// <summary>
    /// Получает детали стимула из условного рефлекса (для отладки)
    /// </summary>
    private (List<int> Actions, List<int> Phrases) GetStimulusDetailsFromConditionedReflex(
        ConditionedReflexesSystem.ConditionedReflex reflex)
    {
      var actions = GetActionsFromConditionedReflex(reflex);
      var (phrases, _, _) = GetPhrasesFromConditionedReflex(reflex);
      return (actions ?? new List<int>(), phrases);
    }

    /// <summary>
    /// Получает фразы из пускового стимула условного рефлекса
    /// </summary>
    private (List<int> Phrases, int ToneId, int MoodId) GetPhrasesFromConditionedReflex(
        ConditionedReflexesSystem.ConditionedReflex conditionedReflex)
    {
      try
      {
        if (_perceptionImagesSystem == null)
          return (new List<int>(), 0, 0);

        var perceptionImage = _perceptionImagesSystem.GetAllPerceptionImagesList()
            .FirstOrDefault(img => img.Id == conditionedReflex.Level3);

        if (perceptionImage == null)
          return (new List<int>(), 0, 0);

        var phrases = perceptionImage.PhraseIdList ?? new List<int>();

        return (phrases, 0, 0);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return (new List<int>(), 0, 0);
      }
    }

    /// <summary>
    /// Преобразует уровни рефлекса в компоненты дерева автоматизмов с учетом фраз
    /// </summary>
    private (bool IsValid, string Error, TreeComponents Components) ConvertReflexLevelsToTreeComponents(
        ConditionedReflexesSystem.ConditionedReflex reflex,
        List<int> actionIds,
        List<int> phraseIds,
        int toneId,
        int moodId,
        int symbolId,
        int verbId)
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
        else
          components.EmotionID = 0;

        (int activityId, var activityImage) = _actionsImagesSystem.CreateNewActionsImage(
            kind: 0,
            actIdList: actionIds,
            phraseIdList: phraseIds,
            toneId: toneId,
            moodId: moodId,
            checkUnicum: true);

        components.ActivityID = activityId;
        components.ToneMoodID = PsychicSystem.GetToneMoodID(toneId, moodId);
        components.SimbolID = symbolId;
        components.VerbID = verbId;

        return (true, string.Empty, components);
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка преобразования уровней: {ex.Message}", new TreeComponents());
      }
    }

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
    private int CreateActionsImageForReflex(
        List<int> actionIds,
        List<int> phraseIds,
        int toneId,
        int moodId)
    {
      try
      {
        var (imageId, _) = _actionsImagesSystem.CreateNewActionsImage(
            kind: 0,
            actIdList: actionIds,
            phraseIdList: phraseIds,
            toneId: toneId,
            moodId: moodId,
            checkUnicum: true);

        if (imageId == 0)
          Logger.Warning($"Не удалось создать образ действий для actions={string.Join(",", actionIds)}, phrases={string.Join(",", phraseIds)}");

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
          automatizm.Usefulness = 3;
        else
          automatizm.Usefulness = 2;

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

    #region Вспомогательные классы и перечисления

    /// <summary>
    /// Статус конвертации условного рефлекса в автоматизм
    /// </summary>
    public enum ConversionStatus
    {
      /// <summary>
      /// Конвертация не удалась из-за ошибки или некорректных данных
      /// </summary>
      Failed = 0,

      /// <summary>
      /// Новый автоматизм успешно создан
      /// </summary>
      Created = 1,

      /// <summary>
      /// Автоматизм уже существует (дубликат)
      /// </summary>
      AlreadyExists = 2
    }

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