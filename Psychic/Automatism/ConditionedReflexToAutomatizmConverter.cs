using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic.Automatism;
using ISIDA.Reflexes;
using ISIDA.Sensors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Threading;
using static ISIDA.Psychic.Automatism.InfluenceActionsImagesSystem;
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
    private readonly ReflexChainsSystem _reflexChainsSystem;
    private readonly InfluenceActionsImagesSystem _influenceActionsImages;
    private readonly AutomatizmChainsSystem _automatizmChains;
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
        VerbalBrocaImagesSystem verbalBrocaImagesSystem,
        ReflexChainsSystem reflexChainsSystem,
        InfluenceActionsImagesSystem influenceActionsImages,
        AutomatizmChainsSystem automatizmChains)
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
          verbalBrocaImagesSystem,
          reflexChainsSystem,
          influenceActionsImages,
          automatizmChains);
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
        VerbalBrocaImagesSystem verbalBrocaImagesSystem,
        ReflexChainsSystem reflexChainsSystem,
        InfluenceActionsImagesSystem influenceActionsImages,
        AutomatizmChainsSystem automatizmChains)
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
      _reflexChainsSystem = reflexChainsSystem ??
          throw new ArgumentNullException(nameof(reflexChainsSystem));
      _influenceActionsImages = influenceActionsImages ??
          throw new ArgumentNullException(nameof(influenceActionsImages));
      _automatizmChains = automatizmChains ??
          throw new ArgumentNullException(nameof(automatizmChains));
    }

    #endregion

    #region Основные методы конвертации

    /// <summary>
    /// Клонирует все условные рефлексы в автоматизмы (с поддержкой цепочек)
    /// </summary>
    public (int NewCount, int ExistingCount, int TotalCount, int DuplicateCount, int ChainsCreated, List<string> Errors)
        CloneAllConditionedReflexesToAutomatisms()
    {
      var errors = new List<string>();
      int newCount = 0;
      int existingCount = 0;
      int duplicateCount = 0;
      int chainsCreated = 0;
      int totalCount = 0;

      try
      {
        if (AppGlobalState.EvolutionStage < 2)
          return (0, 0, 0, 0, 0, new List<string> { $"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для автоматизмов" });

        _lock.EnterWriteLock();
        try
        {
          var allConditionedReflexes = _conditionedReflexesSystem.GetAllConditionedReflexes();
          totalCount = allConditionedReflexes.Count;

          if (totalCount == 0)
            return (0, 0, 0, 0, 0, new List<string> { "Нет условных рефлексов для клонирования" });

          var processedImageIds = new HashSet<int>();
          float actReflexTreshold = _conditionedReflexesSystem.Settings.ActivationThreshold;

          foreach (var conditionedReflex in allConditionedReflexes)
          {
            try
            {
              if (AddUtils.FloatLessOrEqual(conditionedReflex.AssociationStrength, actReflexTreshold))
                continue; // пропускаем рефлекс с крепостью <= пороговой

              var (actions, phrases, toneId, moodId, visualColorId) = GetActionPhrasesFromConditionedReflex(conditionedReflex);
              var imageHash = CalculateImageHash(conditionedReflex.Level1, conditionedReflex.Level2, actions, phrases, toneId, moodId, visualColorId);

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

                    // Проверяем, была ли создана цепочка
                    if (result.Error?.Contains("с цепочкой") == true)
                      chainsCreated++;

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

          Logger.Info($"Конвертация завершена: {newCount} новых, {existingCount} существующих, " +
                     $"{duplicateCount} дубликатов, {chainsCreated} цепочек создано, {errors.Count} ошибок");

          return (newCount, existingCount, totalCount, duplicateCount, chainsCreated, errors);
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
        return (newCount, existingCount, totalCount, duplicateCount, chainsCreated, errors);
      }
    }

    /// <summary>
    /// Конвертирует один условный рефлекс в автоматизм (с поддержкой цепочек)
    /// </summary>
    public (bool Success, int AutomatizmId, ConversionStatus Status, string Error) ConvertConditionedReflexToAutomatizm(
        ConditionedReflexesSystem.ConditionedReflex conditionedReflex)
    {
      try
      {
        // Получаем действия из исходного безусловного рефлекса
        var actionIds = GetActionsFromConditionedReflex(conditionedReflex);
        if (actionIds == null || !actionIds.Any())
          return (false, 0, ConversionStatus.Failed, $"Нет действий для условного рефлекса ID={conditionedReflex.Id}");

        // получаем пусковые действия и фразу для триггера автоматизма из Level 3 условного рефлекса
        var (actionsTrigger, phrases, toneId, moodId, triggerVisualColorId) = GetActionPhrasesFromConditionedReflex(conditionedReflex);
        int symbolId = 0;
        int verbId = 0;

        if (phrases?.Any() == true)
        {
          int phraseId0 = phrases[0];
          symbolId = _sensorySystem.VerbalChannel.GetFirstSymbolFromPhraseId(phraseId0);
          if (symbolId == 0)
          {
            var phraseText = _sensorySystem.VerbalChannel.GetPhraseFromPhraseId(phraseId0);
            char firstChar = '\0';
            if (!string.IsNullOrEmpty(phraseText))
            {
              var trimmed = phraseText.TrimStart();
              if (trimmed.Length > 0) firstChar = trimmed[0];
            }
          }
          (verbId, _) = _verbalBrocaImages.CreateNewVerbalBrocaImage(symbolId, phrases, toneId, moodId, true);
        }

        var treeComponentsResult = ConvertReflexLevelsToTreeComponents(
            conditionedReflex,
            actionsTrigger,
            phrases,
            toneId,
            moodId,
            symbolId,
            verbId,
            triggerVisualColorId);

        if (!treeComponentsResult.IsValid)
          return (false, 0, ConversionStatus.Failed, treeComponentsResult.Error);

        var treeComponents = treeComponentsResult.Components;

        int nodeId = FindOrCreateAutomatizmTreeNode(treeComponents);
        if (nodeId == 0)
          return (false, 0, ConversionStatus.Failed, $"Не удалось найти или создать узел в дереве автоматизмов");

        // Проверяем существующий автоматизм
        var existingAutomatizm = _automatizmSystem.GetAutomatizmFromNodeId(nodeId);
        if (existingAutomatizm != null)
          return (true, existingAutomatizm.ID, ConversionStatus.AlreadyExists, "Автоматизм уже существует");

        // Проверяем наличие цепочки в исходном безусловном рефлексе
        var geneticReflex = _geneticReflexesSystem.GetGeneticReflex(conditionedReflex.SourceGeneticReflexId);
        int automatizmChainId = 0;
        ReflexChainInfo reflexChainInfo = null;

        if (geneticReflex?.ReflexChainID > 0)
        {
          // Получаем информацию о цепочке рефлексов
          reflexChainInfo = GetChainInfoFromGeneticReflex(conditionedReflex.SourceGeneticReflexId);
          if (reflexChainInfo != null)
          {
            // Создаем цепочку автоматизмов на основе цепочки рефлексов
            var chainResult = CreateAutomatizmChainFromReflexChain(reflexChainInfo, nodeId);
            if (chainResult.Success)
            {
              automatizmChainId = chainResult.ChainId;
              Logger.Info($"Создана цепочка автоматизмов {automatizmChainId} для условного рефлекса {conditionedReflex.Id}");
            }
            else
              Logger.Warning($"Не удалось создать цепочку автоматизмов: {chainResult.Error}");
          }
        }

        // Создаем образ действий для автоматизма
        int actionsImageId = 0;
        (actionsImageId, _) = _actionsImagesSystem.CreateNewActionsImage(
            kind: 0,
            actIdList: new List<int> { actionIds.First() },
            phraseIdList: null, // условный рефлекс это только действия безусловного
            toneId: toneId,
            moodId: moodId,
            checkUnicum: true,
            visualColorId: triggerVisualColorId);

        if (actionsImageId == 0)
          return (false, 0, ConversionStatus.Failed, $"Не удалось создать образ действий");

        // Создаем автоматизм
        var (automatizmId, automatizm) = _automatizmSystem.CreateNewAutomatizm(nodeId, actionsImageId, true);
        if (automatizm == null)
          return (false, 0, ConversionStatus.Failed, $"Не удалось создать автоматизм");

        // Настраиваем автоматизм
        ConfigureAutomatizmFromReflex(automatizm, conditionedReflex, actionIds);

        // Если создана цепочка, связываем ее с автоматизмом
        if (automatizmChainId > 0)
        {
          automatizm.NextID = automatizmChainId;
          // обновляем цепочку - указываем стартовый автоматизм
          var chain = _automatizmChains.GetChain(automatizmChainId);
          chain.StartAutomatizmId = automatizm.ID;

          Logger.Info($"Автоматизм {automatizmId} связан с цепочкой {automatizmChainId}");
        }

        return (true, automatizmId, ConversionStatus.Created,
                automatizmChainId > 0 ? "Автоматизм с цепочкой успешно создан" : "Автоматизм успешно создан");
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
    /// Вычисляет хэш образа для обнаружения дубликатов.
    /// Учитывает: Level1, Level2 (эмоции), пусковые действия, фразы, тон, настроение.
    /// </summary>
    private int CalculateImageHash(int baseId, List<int> level2, List<int> actions, List<int> phrases, int toneId, int moodId, int visualColorId)
    {
      unchecked
      {
        int hash = 17;
        hash = hash * 31 + baseId.GetHashCode();

        if (level2 != null)
        {
          foreach (var emotionId in level2.OrderBy(x => x))
            hash = hash * 31 + emotionId.GetHashCode();
        }

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

        hash = hash * 31 + toneId.GetHashCode();
        hash = hash * 31 + moodId.GetHashCode();
        hash = hash * 31 + visualColorId.GetHashCode();

        return hash;
      }
    }

    /// <summary>
    /// Получает фразы и действия из пускового стимула условного рефлекса
    /// </summary>
    private (List<int> Action, List<int> Phrases, int ToneId, int MoodId, int VisualColorId) GetActionPhrasesFromConditionedReflex(
        ConditionedReflexesSystem.ConditionedReflex conditionedReflex)
    {
      try
      {
        if (_perceptionImagesSystem == null)
          return (new List<int>(), new List<int>(), 0, 0, AgentVisualColor.White);

        var perceptionImage = _perceptionImagesSystem.GetAllPerceptionImagesList()
            .FirstOrDefault(img => img.Id == conditionedReflex.Level3);

        if (perceptionImage == null)
          return (new List<int>(), new List<int>(), 0, 0, AgentVisualColor.White);

        var phrases = perceptionImage.PhraseIdList ?? new List<int>();
        var actions = perceptionImage.InfluenceActionsList ?? new List<int>();
        int toneId = conditionedReflex.ToneId;
        int moodId = conditionedReflex.MoodId;
        int visualColorId = perceptionImage.VisualColorId;
        if (!AgentVisualColor.IsValidCode(visualColorId))
          visualColorId = AgentVisualColor.White;

        return (actions, phrases, toneId, moodId, visualColorId);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return (new List<int>(), new List<int>(), 0, 0, AgentVisualColor.White);
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
        int verbId,
        int triggerVisualColorId)
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

        var (activityId, _) = _influenceActionsImages.CreateNewInfluenceActionsImage(actionIds, true);

        components.ActivityID = activityId;
        components.ToneMoodID = PsychicSystem.GetToneMoodID(toneId, moodId);
        components.SimbolID = symbolId;
        components.VerbID = verbId;
        components.VisualID = AgentVisualColor.IsValidCode(triggerVisualColorId)
            ? triggerVisualColorId
            : AgentVisualColor.White;

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
            components.VisualID,
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
      public int VisualID { get; set; }
    }

    #endregion

    #region Вспомогательные классы и методы для цепочек

    /// <summary>
    /// Информация о цепочке рефлексов
    /// </summary>
    private class ReflexChainInfo
    {
      public int ReflexId { get; set; }
      public int ChainId { get; set; }
      public List<ReflexChainsSystem.ChainLink> Links { get; set; } = new List<ReflexChainsSystem.ChainLink>();
      public int StartLinkId { get; set; }
      public string Description { get; set; }
    }

    /// <summary>
    /// Получает информацию о цепочке из безусловного рефлекса
    /// </summary>
    private ReflexChainInfo GetChainInfoFromGeneticReflex(int geneticReflexId)
    {
      try
      {
        var geneticReflex = _geneticReflexesSystem.GetGeneticReflex(geneticReflexId);
        if (geneticReflex == null || geneticReflex.ReflexChainID == 0)
          return null;

        var chainId = geneticReflex.ReflexChainID;
        var chain = _reflexChainsSystem?.GetChain(chainId);
        if (chain == null)
          return null;

        return new ReflexChainInfo
        {
          ReflexId = geneticReflexId,
          ChainId = chainId,
          Links = chain.Links?.ToList() ?? new List<ReflexChainsSystem.ChainLink>(),
          StartLinkId = chain.Links?.FirstOrDefault()?.ID ?? 0,
          Description = chain.Description ?? ""
        };
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }
    }

    /// <summary>
    /// Создает цепочку автоматизмов на основе цепочки рефлексов
    /// </summary>
    private (bool Success, int ChainId, string Error) CreateAutomatizmChainFromReflexChain(
        ReflexChainInfo reflexChainInfo,
        int treeNodeId)
    {
      try
      {
        if (reflexChainInfo == null || !reflexChainInfo.Links.Any())
          return (false, 0, "Информация о цепочке рефлексов отсутствует или пуста");

        if (!AutomatizmChainsSystem.IsInitialized)
          return (false, 0, "Система цепочек автоматизмов не инициализирована");

        // Создаем словарь для сопоставления ID звеньев рефлексов и ID звеньев автоматизмов
        var linkIdMap = new Dictionary<int, int>();
        var automatizmLinks = new List<AutomatizmChainsSystem.ChainLink>();

        // Сначала создаем все звенья (без ссылок)
        foreach (var reflexLink in reflexChainInfo.Links.OrderBy(l => l.ID))
        {
          // Создаем образ действий для звена
          var actionId = reflexLink.ActionId;
          var (actionsImageId, _) = _actionsImagesSystem.CreateNewActionsImage(
              kind: 0,
              actIdList: new List<int> { actionId },
              phraseIdList: null,
              toneId: 0,
              moodId: 0,
              checkUnicum: true);

          if (actionsImageId == 0)
            return (false, 0, $"Не удалось создать образ действий для действия {actionId}");

          // Создаем звено с уникальным ID
          var automatizmLink = new AutomatizmChainsSystem.ChainLink
          {
            ID = reflexLink.ID,
            ActionsImageId = actionsImageId,
            SuccessNextLink = 0, // Пока временно 0
            FailureNextLink = 0, // Пока временно 0
            Description = reflexLink.Description,
            ChainUsefulness = 1
          };

          automatizmLinks.Add(automatizmLink);
          linkIdMap[reflexLink.ID] = automatizmLink.ID;
        }

        // Теперь устанавливаем правильные ссылки между звеньями
        for (int i = 0; i < reflexChainInfo.Links.Count; i++)
        {
          var reflexLink = reflexChainInfo.Links[i];
          var automatizmLink = automatizmLinks[i];

          // Находим ID следующего звена для успеха
          if (reflexLink.SuccessNextLink > 0 && linkIdMap.ContainsKey(reflexLink.SuccessNextLink))
            automatizmLink.SuccessNextLink = linkIdMap[reflexLink.SuccessNextLink];

          // Находим ID следующего звена для неудачи
          if (reflexLink.FailureNextLink > 0 && linkIdMap.ContainsKey(reflexLink.FailureNextLink))
            automatizmLink.FailureNextLink = linkIdMap[reflexLink.FailureNextLink];
        }

        // Создаем цепочку
        var chainName = $"Цепочка из рефлекса {reflexChainInfo.ReflexId}";
        var chainDescription = reflexChainInfo.Description;

        var (chainId, warnings) = _automatizmChains.AddAutomatizmChain(
            chainName,
            chainDescription,
            automatizmLinks,
            treeNodeId);

        if (chainId == 0)
          return (false, 0, "Не удалось создать цепочку автоматизмов");

        Logger.Info($"Создана цепочка автоматизмов ID={chainId} с {automatizmLinks.Count} звеньями");
        return (true, chainId, "Цепочка автоматизмов успешно создана");
      }
      catch (Exception ex)
      {
        return (false, 0, $"Ошибка создания цепочки автоматизмов: {ex.Message}");
      }
    }

    /// <summary>
    /// Конвертирует цепочку рефлексов в цепочку автоматизмов для конкретного условного рефлекса
    /// </summary>
    public (bool Success, int ChainId, string Error) ConvertReflexChainToAutomatizmChain(
        int conditionedReflexId)
    {
      try
      {
        var conditionedReflex = _conditionedReflexesSystem.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == conditionedReflexId);

        if (conditionedReflex == null)
          return (false, 0, $"Условный рефлекс с ID {conditionedReflexId} не найден");

        var geneticReflex = _geneticReflexesSystem.GetGeneticReflex(conditionedReflex.SourceGeneticReflexId);
        if (geneticReflex?.ReflexChainID == 0)
          return (false, 0, $"Исходный безусловный рефлекс не имеет цепочки");

        var reflexChainInfo = GetChainInfoFromGeneticReflex(conditionedReflex.SourceGeneticReflexId);
        if (reflexChainInfo == null)
          return (false, 0, $"Не удалось получить информацию о цепочке рефлексов");

        // Создаем дерево автоматизмов для этого условного рефлекса
        var (actionsTrigger, phrases, toneId, moodId, chainVisualColorId) = GetActionPhrasesFromConditionedReflex(conditionedReflex);

        int symbolId = 0;
        int verbId = 0;
        if (phrases?.Any() == true)
        {
          int phraseId0 = phrases[0];
          symbolId = _sensorySystem.VerbalChannel.GetFirstSymbolFromPhraseId(phraseId0);
          if (symbolId == 0)
          {
            var phraseText = _sensorySystem.VerbalChannel.GetPhraseFromPhraseId(phraseId0);
            char firstChar = '\0';
            if (!string.IsNullOrEmpty(phraseText))
            {
              var trimmed = phraseText.TrimStart();
              if (trimmed.Length > 0) firstChar = trimmed[0];
            }
          }
          (verbId, _) = _verbalBrocaImages.CreateNewVerbalBrocaImage(symbolId, phrases, toneId, moodId, true);
        }

        var treeComponentsResult = ConvertReflexLevelsToTreeComponents(
            conditionedReflex,
            actionsTrigger,
            phrases,
            toneId,
            moodId,
            symbolId,
            verbId,
            chainVisualColorId);

        if (!treeComponentsResult.IsValid)
          return (false, 0, treeComponentsResult.Error);

        var treeComponents = treeComponentsResult.Components;
        int nodeId = FindOrCreateAutomatizmTreeNode(treeComponents);

        if (nodeId == 0)
          return (false, 0, "Не удалось создать узел дерева автоматизмов");

        // Создаем цепочку автоматизмов
        var chainResult = CreateAutomatizmChainFromReflexChain(reflexChainInfo, nodeId);

        return chainResult;
      }
      catch (Exception ex)
      {
        return (false, 0, $"Ошибка: {ex.Message}");
      }
    }

    /// <summary>
    /// Создаёт цепочку автоматизмов из безусловного рефлекса (для ситуативного клонирования на стадии 2).
    /// </summary>
    /// <param name="geneticReflexId">ID безусловного рефлекса</param>
    /// <param name="treeNodeId">ID узла дерева автоматизмов</param>
    /// <returns>Success, ChainId, Error</returns>
    public (bool Success, int ChainId, string Error) CreateAutomatizmChainFromGeneticReflex(int geneticReflexId, int treeNodeId)
    {
      try
      {
        var reflexChainInfo = GetChainInfoFromGeneticReflex(geneticReflexId);
        if (reflexChainInfo == null)
          return (false, 0, "У безусловного рефлекса нет цепочки или рефлекс не найден");
        return CreateAutomatizmChainFromReflexChain(reflexChainInfo, treeNodeId);
      }
      catch (Exception ex)
      {
        return (false, 0, ex.Message);
      }
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