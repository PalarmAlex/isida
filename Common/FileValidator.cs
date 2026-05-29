using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ISIDA.Reflexes;

namespace ISIDA.Common
{
  /// <summary>
  /// Утилитный класс для проверки валидности файлов и безопасного сохранения
  /// </summary>
  public static class FileValidator
  {
    internal static class FileHeaders
    {
      // Условные рефлексы
      public const string ConditionedReflexesFormat = "# ID|Level1|Level2|Level3|AssociationStrength|LastActivation|BirthTime|SourceGeneticReflexId|ToneId|MoodId|SourceConditionedReflexId|Order";
      public const string ConditionedReflexesLevel1 = "# Level1: -1:Плохо, 0:Норма, 1:Хорошо";
      public const string ConditionedReflexesLevel2 = "# Level2: id1,id2,id3 (ID стилей поведения)";
      public const string ConditionedReflexesLevel3 = "# Level3: ID образа пускового стимула";
      public const string ConditionedReflexesActions = "# AssociationStrength: крепость связи C_ij ∈ [0,1]";
      public const string ConditionedReflexesToneId = "# ToneId: -1=Вялый, 0=Нормальный, 1=Повышенный";
      public const string ConditionedReflexesMoodId = "# MoodId: 0=Нормальное, 1=Хорошее, 2=Плохое, 3=Игривое, 4=Учитель, 5=Агрессивное, 6=Защитное, 7=Протест";
      public const string ConditionedReflexesSourceConditioned = "# SourceConditionedReflexId: ID родительского условного рефлекса (0 для первичных)";
      public const string ConditionedReflexesOrder = "# Order: порядок рефлекса (1=первичный, 2=вторичный, 3=третичный)";

      // Образы восприятия (пусковые стимулы для условных рефлексов)
      public const string PerceptionImagesFormat = "# ID|InfluenceActionsList|PhraseIdList|CommandPatternIdList|VisualColorId";
      public const string PerceptionImagesLists = "# Формат списков: id1,id2,id3";
      public const string PerceptionImagesCommandPatternIdList =
          "# CommandPatternIdList: ID паттернов CommandChannel PhraseTree (через запятую); столбец опционален в старых файлах (как пусто)";
      public const string PerceptionImagesVisualColor =
          "# VisualColorId: 0 белый, 1 чёрный, 2–8 спектр (см. AgentVisualColor); столбец опционален в старых файлах (как 0)";

      // Безусловные рефлексы
      public const string GeneticReflexesFormat = "# Формат: ID|Level1|Level2|Level3|Адаптивные действия|ReflexChainID";
      public const string GeneticReflexesLevel1 = "# Level1: Интегральное базовое состояние гомеостаза: -1: 0: 1";
      public const string GeneticReflexesLevel2 = "# Level2: Контексты реагирования: id1,id2,id3";
      public const string GeneticReflexesLevel3 = "# Level3: Гомеостатические воздействия: id1,id2,id3";
      public const string GeneticReflexesActions = "# Адаптивные действия: id1,id2,id3";
      public const string GeneticReflexesChain = "# ReflexChainID: ID цепочки рефлексов (0 если нет)";

      // Цепочки безусловных рефлексов
      public const string ReflexChainsFormat = "# Формат файла цепочек рефлексов";
      public const string ReflexChainsChain = "# CHAIN|ID|Name|Description";
      public const string ReflexChainsLink = "# LINK|LinkID|ActionID|SuccessNext|FailureNext|Description";
      public const string ReflexChainsChainDesc = "# ID: уникальный идентификатор цепочки";
      public const string ReflexChainsNameDesc = "# Name: наименование цепочки";
      public const string ReflexChainsLinkDesc = "# LinkID: уникальный идентификатор звена";
      public const string ReflexChainsReflexDesc = "# ActionID: ID действия для выполнения";
      public const string ReflexChainsSuccessDesc = "# SuccessNext: ID следующего звена при успехе";
      public const string ReflexChainsFailureDesc = "# FailureNext: ID следующего звена при неудаче";

      // Гомеостатические воздействия
      public const string InfluenceActionsFormat = "# Формат: ID|Имя|Описание|Воздействие|Антагонисты|EnvironmentMetricProbeKey";
      public const string InfluenceActionsBenefit = "# Воздействие: paramId1:effect1;paramId2:effect2";
      public const string InfluenceAntagonists = "# Антагонисты: id1,id2,id3";
      public const string InfluenceActionsEnvironmentProbeKey =
          "# EnvironmentMetricProbeKey: ключ пробы метрики среды для хоста; пусто — только оператор/Studio";

      // Адаптивные действия
      public const string ActionsFormat = "# Формат: ID|Имя|Описание|Интенсивность|Антагонисты|Target параметры|InfluenceActionId";
      public const string ActionsAntagonists = "# Антагонисты: id1,id2,id3";
      public const string TargetParameters = "# Target параметры: id1,id2,id3";
      public const string ActionsInfluenceActionId = "# InfluenceActionId: 0=нет связи, иначе ID действия с пульта для отзеркаливания";

      // Стили поведения
      public const string StylesFormat = "# Формат: ID|Имя|Описание|Антагонисты";
      public const string StylesAntagonis = "# Антагонисты: id1,id2,id3";

      // Параметры гомеостаза
      public const string ParametersFormat = "# Формат: ID|Название|Описание|Значение|Вес|Норма|Скорость|Активации стилей|Критический|Мин.значение|Макс.значение";
      public const string ParametersActivations = "# Активации стилей: id1,id2,id3";

      // Свойства симбионта
      public const string PropertiesFormat = "# Формат: Ключ|Значение";
      public const string PropertiesIsSleeping = "IsSleeping|";
      public const string PropertiesIsDead = "IsDead|";

      // Образы действий симбионта для психики
      public const string ActionsImagesFormat = "# ID|ActIdList|PhraseIdList|ToneID|MoodID|Kind|VisualColorID|CommandPatternIdList";
      public const string ActionsImagesActIdList = "# ActIdList: ID образа действий с Пульта (через запятую)";
      public const string ActionsImagesPhraseIdList = "# PhraseIdList: ID фраз (через запятую)";
      public const string ActionsImagesToneId = "# ToneID: ID тона сообщения: -1=Вялый, 0=Нормальный, 1=Повышенный";
      public const string ActionsImagesMoodId = "# MoodID: ID настроения: 0=Нормальное, 1=Хорошее, 2=Плохое, 3=Игривое, 4=Учитель, 5=Агрессивное, 6=Защитное, 7=Протест";
      public const string ActionsImagesKind = "# Kind: 0=объективное действие, 1=субъективное предположение";
      public const string ActionsImagesVisualColorId = "# VisualColorID: зрительный канал (AgentVisualColor): 0=белый, 1=чёрный, 2–8=спектр; при отсутствии столбца в старых файлах подразумевается 0";
      public const string ActionsImagesCommandPatternIdList = "# CommandPatternIdList: ID паттернов CommandChannel PhraseTree (через запятую); столбец опционален в старых файлах (как пусто)";

      // Образы действий с пульта для психики
      public const string InfluenceActionsImagesFormat = "# ID|ActIdList";
      public const string InfluenceActionsImagesActIdList = "# ActIdList: ID действий с Пульта (через запятую)";

      // Образы эмоций для психики
      public const string EmotionsImagesFormat = "# ID|BaseStyleIdList";
      public const string EmotionsImagesBaseIdList = "# BaseStyleIdList: ID стилей (через запятую)";

      // Вербальные образы для психики
      public const string VerbalBrocaFileNameImagesFormat = "# ID|SimbolID|PhraseIdList|ToneId|MoodId";
      public const string VerbalBrocaSimbolID = "# SimbolID: ID первого символа фразы (0 если нет фразы)";
      public const string VerbalBrocaPhraseIdList = "# PhraseIdList: Массив ID фраз (через запятую)";
      public const string VerbalBrocaToneId = "# ToneId: ID тона сообщения с Пульта или Ответного действия";
      public const string VerbalBrocaMoodId = "# MoodId: ID настроения при передаче фразы с Пульта или Ответного действия";

      // Образы команды для психики
      public const string CommandBrocaImagesFormat = "# ID|PatternIdList";
      public const string CommandBrocaPatternIdList = "# PatternIdList: ID паттернов CommandChannel PhraseTree (через запятую)";

      // Legacy alias: старые файлы могут содержать CadPatternIdList в шапке (столбец данных тот же).
      public const string PerceptionImagesLegacyCommandPatternIdListHeader = "CadPatternIdList";
      public const string ActionsImagesLegacyCommandPatternIdListHeader = "CadPatternIdList";

      // Дерево автоматизмов
      public const string AutomatizmTreeFormat = "# Формат записи: ID|ParentID|BaseID|EmotionID|ActivityID|ToneMoodID|SimbolID|VerbID|CommandID|VisualID";
      public const string AutomatizmTreeFields1 = "# ID: уникальный идентификатор узла дерева";
      public const string AutomatizmTreeFields2 = "# ParentID: ID родительского узла (0 для корневых веток)";
      public const string AutomatizmTreeFields3 = "# BaseID: базовое состояние: -1=Плохо, 0=Норма, 1=Хорошо";
      public const string AutomatizmTreeFields4 = "# EmotionID: ID эмоции (0 если нет эмоции)";
      public const string AutomatizmTreeFields5 = "# ActivityID: ID образа сочетания действий с Пульта (0 если нет действия)";
      public const string AutomatizmTreeFields6 = "# ToneMoodID: ID образа контекста сообщения";
      public const string AutomatizmTreeFields7 = "# SimbolID: ID первого символа фразы (0 если нет фразы)";
      public const string AutomatizmTreeFields8 = "# VerbID: ID вербального образа (0 если нет фразы)";
      public const string AutomatizmTreeFields10 = "# CommandID: ID образа команды (0 если нет команды)";
      public const string AutomatizmTreeFields11 = "# VisualID: код зрительного канала (AgentVisualColor), 0=нейтральный; при отсутствии столбца CommandID/VisualID в старых файлах подразумевается 0";

      // Автоматизмы
      public const string AutomatizmFormat = "# Формат записи: ID|BranchID|Usefulness|ActionsImageID|NextID|Energy|Belief|Count|GomeoIdSuccesArr";
      public const string AutomatizmFields1 = "# ID: уникальный идентификатор автоматизма";
      public const string AutomatizmFields2 = "# BranchID: ID объекта привязки: 0=дерево, >1000000=действия, >2000000=фразы";
      public const string AutomatizmFields3 = "# Usefulness: (БЕС)ПОЛЕЗНОСТЬ: -10=вред, 0=нейтрально, +10=польза";
      public const string AutomatizmFields4 = "# ActionsImageID: ID образа действий (ActionsImage.ID)";
      public const string AutomatizmFields5 = "# NextID: ID следующей цепочки действий (0 если нет цепочки)";
      public const string AutomatizmFields6 = "# Energy: энергичность действия (1-10, по умолчанию=5)";
      public const string AutomatizmFields7 = "# Belief: уверенность: 0=предположение, 1=чужие сведения, 2=проверенное собственное знание";
      public const string AutomatizmFields8 = "# Count: надежность - число использований с подтверждением (бес)полезности";
      public const string AutomatizmFields9 = "# GomeoIdSuccesArr: ID гомео-параметров, которые улучшает это действие (через запятую)";

      // Цепочки автоматизмов
      public const string AutomatizmChainsFormat = "# Формат файла цепочек автоматизмов";
      public const string AutomatizmChainsChain = "# CHAIN|ID|Name|Description|TreeNodeId|StartAutomatizmId";
      public const string AutomatizmChainsLink = "# LINK|LinkID|ActionsImageID|SuccessNext|FailureNext|Description|ChainUsefulness";
      public const string AutomatizmChainsChainDesc = "# ID: уникальный идентификатор цепочки";
      public const string AutomatizmChainsNameDesc = "# Name: наименование цепочки";
      public const string AutomatizmChainsTreeNodeDesc = "# TreeNodeId: ID узла дерева автоматизмов (0 если нет)";
      public const string AutomatizmChainsStartAutomatizmDesc = "# StartAutomatizmId: ID автоматизма, который запускает цепочку (0 если нет)";
      public const string AutomatizmChainsLinkDesc = "# LinkID: уникальный идентификатор звена";
      public const string AutomatizmChainsAutomatizmDesc = "# ActionsImageID: ID образа действий";
      public const string AutomatizmChainsSuccessDesc = "# SuccessNext: ID следующего звена при успехе";
      public const string AutomatizmChainsFailureDesc = "# FailureNext: ID следующего звена при неудаче";
      public const string AutomatizmChainsThresholdDesc = "# ChainUsefulness: оценка полезности звена цепочки";

      // Дерево проблем
      public const string ProblemTreeFormat = "# Формат записи: ID|ParentID|AutTreeID|SituationTreeID|ThemeID|PurposeID";
      public const string ProblemTreeFields1 = "# ID: уникальный идентификатор узла дерева проблем";
      public const string ProblemTreeFields2 = "# AutTreeID: ID узла дерева автоматизмов";
      public const string ProblemTreeFields3 = "# SituationTreeID: ID образа ситуации";
      public const string ProblemTreeFields4 = "# ThemeID: ID образа темы мышления";
      public const string ProblemTreeFields5 = "# PurposeID: ID образа цели";

      // Образы тем мышления
      public const string ThemeImagesFormat = "# Формат: ID|Weight|Type|PulsCount";
      public const string ThemeImagesDesc = "# Weight: вес (1-10), Type: тип темы (ThemeTypeStr), PulsCount: время актуализации";

      // Справочник типов тем
      public const string ThemeTypesFormat = "# Формат: Id|Description|DefaultWeight|AllowedInfoFuncIds";
      public const string ThemeTypesDesc = "# Id: идентификатор типа темы (1–20), Description: описание, DefaultWeight: вес по умолчанию (обязательно >0), AllowedInfoFuncIds: список Id через запятую (пусто = без ограничений)";

      // Образы целей
      public const string PurposeImagesFormat = "# Формат: ID|Target|MoodId|EmotionId|SituationId";
      public const string PurposeImagesDesc = "# Target: 1=повторение, 2=улучшение; MoodId/EmotionId/SituationId: параметры цели";

      // Дерево понимания ситуации
      public const string UnderstandingTreeFormat = "# ID|ParentID|Mood|EmotionID|SituationID";
      public const string UnderstandingTreeDesc = "# Дерево понимания ситуации";

      // Ментальная эпизодическая память (дерево контекстов + листья-цепочки ИФ)
      public const string MentalEpisodicTreeFormat = "# Формат: Id|ParentID|NodePID|ThemeID|PurposeID|info1,info2#Effect|Count";
      public const string MentalEpisodicTreeDesc = "# ParentID=0 — узел под корнем; папка контекста: пустой info, Effect=0, Count=0; лист: список Id инфо-функций; после # — Effect и Count";

      public const string MentalEpisodicHistoryFormat = "# Формат: MentalRuleNodeId|LifeTime|LastEpisodicNodeId";
      public const string MentalEpisodicHistoryDesc = "# История кадров ментальной эпизодики: узел правила в дереве, пульс жизни, последний узел моторной эпизодики (0 — нет)";

      // Справочник типов ситуаций
      public const string SituationTypesFormat = "# Id|MoodId|InfluenceId|ThemeTypeId|EventAgentCode";
      public const string SituationTypesDesc = "# Id 1-20: события (ThemeTypeId, EventAgentCode). Id 21-40: настроение (MoodId, ThemeTypeId). Id 41-60: воздействие (InfluenceId, ThemeTypeId). ThemeTypeId: уникален внутри каждого из трёх диапазонов; между диапазонами одна тема может повторяться. EventAgentCode: -1=нет; для 1-20 — код из AgentEventsCatalog.";

      // Образы ситуаций
      public const string SituationImagesFormat = "# Id|AutomatizmTreeNodeId|SituationTypeId";
      public const string SituationImagesDesc = "# Образы ситуаций";

      // Эпизодическая память
      public const string EpisodicTreeFormat = "# Формат: ID|ParentID|BaseID|EmotionID|UnderstandingNodeId|NodePID|TriggerId|ActionId#Effect|Count|StimulsEffect|IsTeacher (после # 4 поля; порядок: обход в глубину, родитель перед детьми)";
      public const string EpisodicTreeId = "# ID: уникальный идентификатор узла";
      public const string EpisodicTreeParentId = "# ParentID: ID родительского узла (0 для корня)";
      public const string EpisodicTreeBaseId = "# BaseID: Базовое состояние. -1: Плохо 0: Норма 1: Хорошо";
      public const string EpisodicTreeEmotionId = "# EmotionID: Образ эмоции";
      public const string EpisodicTreeUnderstandingNodeId = "# UnderstandingNodeId: ID активного узла дерева понимания ситуации";
      public const string EpisodicTreeNodePid = "# NodePID: ID узла дерева проблем";
      public const string EpisodicTreeTriggerId = "# TriggerId: ID образа стимула";
      public const string EpisodicTreeActionId = "# ActionId: ID образа ответа";
      public const string EpisodicTreeEffect = "# Effect: изменение полезности прямого правила (-10..+10); для учителя 0";
      public const string EpisodicTreeCount = "# Count: число подтверждений применения";
      public const string EpisodicTreeStimulsEffect = "# StimulsEffect: для прямого — значимость стимула; для учителя — оценка с пульта (-10..10)";
      public const string EpisodicTreeIsTeacher = "# IsTeacher: 1 — учительское правило (оценка в StimulsEffect), 0 — прямое";
      public const string EpisodicHistoryFormat = "# Формат записи: ID,LifeTime|ID,LifeTime|...";

      // Первичные сенсоры
      public const string VerbalPrimariesFormat = "# Формат: Символ|#|ID";

      // Сценарии оператора
      public const string ScenarioRegistryColumns =
          "# Реестр: SCENARIO_REGISTRY_FORMAT|версия; строки данных — Id|Title|Description|PreRunTargetStage";
      public const string ScenarioLinesMeta =
          "# SCENARIO_META: Title|Description|InitialHomeostasisValues|PreRunTargetStage|Clear|Obs|Auth|Norm|PulseStep|RunPulseCoeff";
      public const string ScenarioGroupRegistryColumns =
          "# Реестр групп: SCENARIO_GROUP_REGISTRY_FORMAT|версия; строки — Id|Title|Description";
      public const string ScenarioGroupMeta =
          "# SCENARIO_GROUP_META: Title|Description|RunPulseTimingCoefficient|ReportFormat";
    }

    private static string _logFilePath;

    /// <summary>
    /// Путь к каталогу логов
    /// </summary>
    public static string LogFilePath
    {
      get
      {
        if (_logFilePath == null)
        {
          // Путь по умолчанию
          _logFilePath = Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Logs", "SaveErrors.log");
        }
        return _logFilePath;
      }
    }

    /// <summary>
    /// Установка пути к каталогу логов
    /// </summary>
    public static void SetLogsPath(string logsDirectory)
    {
      if (!string.IsNullOrEmpty(logsDirectory))
      {
        _logFilePath = Path.Combine(logsDirectory, "SaveErrors.log");

        try
        {
          // Создаем директорию, если её нет
          var directory = Path.GetDirectoryName(_logFilePath);
          if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
          {
            Directory.CreateDirectory(directory);
          }
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
        }
      }
    }

    /// <summary>
    /// Логирование ошибок в файл
    /// </summary>
    public static void LogError(string message)
    {
      try
      {
        if(message != "")
          File.AppendAllText(_logFilePath,
              $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
      }
      catch
      {
        // Игнорируем ошибки логирования
      }
    }

    // ======== ПЕРЕГРУЗКИ ВАЛИДАЦИЙ: по пути и по содержимому ========

    #region IsValidPerceptionImagesFile

    /// <summary>
    /// Проверяет валидность файла образов восприятия по пути
    /// </summary>
    public static bool IsValidPerceptionImagesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidPerceptionImagesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла образов восприятия.
    /// Строка данных: ID|список воздействий|список фраз|опционально CommandPatternIdList|опционально VisualColorId (0–8).
    /// </summary>
    public static bool IsValidPerceptionImagesFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 3 || parts.Length > 5)
          return false;

        if (!int.TryParse(parts[0], out int id) || id <= 0)
          return false;

        int colorPartIndex = parts.Length == 4 ? 3 : (parts.Length == 5 ? 4 : -1);
        if (colorPartIndex >= 0)
        {
          if (string.IsNullOrWhiteSpace(parts[colorPartIndex]) ||
              !int.TryParse(parts[colorPartIndex].Trim(), out int colorId) ||
              !AgentVisualColor.IsValidCode(colorId))
            return false;
        }
      }

      return true;
    }

    #endregion

    #region IsValidReflexChainsFile

    /// <summary>
    /// Проверяет валидность файла цепочек рефлексов по пути
    /// </summary>
    public static bool IsValidReflexChainsFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidReflexChainsFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла цепочек рефлексов
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidReflexChainsFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      // Проверяем, что файл содержит только комментарии/пустые строки
      bool hasOnlyComments = lineList.All(line =>
          string.IsNullOrWhiteSpace(line) ||
          line.Trim().StartsWith("#", StringComparison.Ordinal));

      if (hasOnlyComments)
        return true;

      // Проверяем все строки с данными
      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');

        if (parts.Length >= 2 && parts[0] == "CHAIN")
        {
          // CHAIN|ID
          if (!int.TryParse(parts[1], out int chainId) || chainId <= 0)
            return false;

          // Должно быть хотя бы ID после CHAIN
          if (parts.Length < 2)
            return false;
        }
        else if (parts.Length >= 5 && parts[0] == "LINK")
        {
          if (!int.TryParse(parts[1], out int linkId) || linkId <= 0 ||
              !int.TryParse(parts[2], out int actionId) || actionId <= 0 ||
              !int.TryParse(parts[3], out int successNext) ||
              !int.TryParse(parts[4], out int failureNext))
            return false;
        }
        else
        {
          return false; // Неизвестный формат строки
        }
      }

      return true; // Все строки прошли проверку
    }

    #endregion

    #region IsValidGeneticReflexesFile

    /// <summary>
    /// Проверяет валидность файла безусловных рефлексов по пути
    /// </summary>
    public static bool IsValidGeneticReflexesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidGeneticReflexesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла безусловных рефлексов
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidGeneticReflexesFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 5)
          return false;

        if (!int.TryParse(parts[0], out _))
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidActionsFile

    /// <summary>
    /// Проверяет валидность файла адаптивных действий по пути
    /// </summary>
    public static bool IsValidActionsFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidActionsFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла адаптивных действий
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidActionsFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 6)
          return false;

        if (!int.TryParse(parts[0], out _))
          return false;

        if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
        {
          if (!int.TryParse(parts[3], out int vigor) || vigor < 1 || vigor > 10)
            return false;
        }

        if (parts.Length >= 7 && !string.IsNullOrWhiteSpace(parts[6]))
        {
          if (!int.TryParse(parts[6], out int influenceActionId) || influenceActionId < 0)
            return false;
        }

        return true;
      }

      return true;
    }

    #endregion

    #region IsInfluenceValidActionsFile

    /// <summary>
    /// Проверяет валидность файла гомеостатических воздействий по пути
    /// </summary>
    public static bool IsInfluenceValidActionsFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsInfluenceValidActionsFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла гомеостатических воздействий
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsInfluenceValidActionsFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 3)
          return false;

        if (!int.TryParse(parts[0], out _))
          return false;

        if (parts.Length < 5)
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsEmotionsImagesFile

    /// <summary>
    /// Проверяет валидность файла эмоций по пути
    /// </summary>
    public static bool IsValidEmotionsImagesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidEmotionsImagesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла эмоций
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidEmotionsImagesFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 2)
          return false;

        if (!int.TryParse(parts[0], out _))
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidStyleFile

    /// <summary>
    /// Проверяет валидность файла стилей по пути
    /// </summary>
    public static bool IsValidStyleFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidStyleFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла стилей (по строкам)
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidStyleFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 4)
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidAgentParametersFile

    /// <summary>
    /// Проверяет валидность файла параметров симбионта по пути
    /// </summary>
    public static bool IsValidAgentParametersFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidAgentParametersFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла параметров симбионта
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidAgentParametersFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 11)
          return false;

        if (!int.TryParse(parts[0], out _) ||
            !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value < 0 || value > 100 ||
            !int.TryParse(parts[4], out int weight) || weight < 0 || weight > 100 ||
            !int.TryParse(parts[5], out int norma) || norma < 0 || norma > 100)
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidAgentPropertiesFile

    /// <summary>
    /// Проверяет валидность файла свойств симбионта по пути
    /// </summary>
    public static bool IsValidAgentPropertiesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidAgentPropertiesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла свойств симбионта
    /// Разрешает файлы, содержащие только шапку (комментарии #), если нет данных
    /// Если есть данные — требует обязательные ключи
    /// </summary>
    public static bool IsValidAgentPropertiesFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      // Проверяем наличие шапки
      bool hasHeader = lineList.Any(l => l?.Contains(FileHeaders.PropertiesFormat) == true);
      if (!hasHeader)
        return false;

      // Проверяем ключи
      bool hasIsSleeping = lineList.Any(l => l?.Contains(FileHeaders.PropertiesIsSleeping) == true);
      bool hasIsDead = lineList.Any(l => l?.Contains(FileHeaders.PropertiesIsDead) == true);
      bool hasName = lineList.Any(l => l?.StartsWith("Name|") == true);
      bool hasEvolutionStage = lineList.Any(l => l?.StartsWith("EvolutionStage|") == true);

      // Если есть хотя бы один ключ — требуем все
      if (hasIsSleeping || hasIsDead || hasName || hasEvolutionStage)
        return hasIsSleeping && hasIsDead && hasName && hasEvolutionStage;

      // Если нет данных — достаточно шапки
      return true;
    }

    #endregion

    #region IsValidActionsImagesFile

    /// <summary>
    /// Проверяет валидность файла образов действий по пути
    /// </summary>
    public static bool IsValidActionsImagesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidActionsImagesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла образов действий
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidActionsImagesFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      // Проверяем, что файл содержит только комментарии/пустые строки (шапку)
      bool hasOnlyComments = lineList.All(line =>
          string.IsNullOrWhiteSpace(line) ||
          line.Trim().StartsWith("#", StringComparison.Ordinal));

      if (hasOnlyComments)
        return true;

      // Проверяем все строки с данными
      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');

        // Проверяем минимальное количество полей
        if (parts.Length < 6)
          return false;

        // Проверяем ID (первое поле должно быть числом)
        if (!int.TryParse(parts[0], out int id) || id <= 0)
          return false;

        // ToneId должен быть валидным (-1, 0, 1)
        if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
        {
          if (int.TryParse(parts[3], out int toneId))
          {
            if (toneId < -1 || toneId > 1)
              return false;
          }
        }

        // Kind должен быть 0 или 1
        if (parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5]))
        {
          if (int.TryParse(parts[5], out int kind))
          {
            if (kind < 0 || kind > 1)
              return false;
          }
        }

        // VisualColorID (опционально для старых файлов): диапазон как у AgentVisualColor 0–8
        if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]))
        {
          if (int.TryParse(parts[6], out int visualColorId))
          {
            if (visualColorId < 0 || visualColorId > 8)
              return false;
          }
        }

        return true; // Достаточно одной валидной строки данных
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidVerbalBrocaImagesFile

    /// <summary>
    /// Проверяет валидность файла вербальных образов по пути
    /// </summary>
    public static bool IsValidVerbalBrocaImagesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidVerbalBrocaImagesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла вербальных образов
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidVerbalBrocaImagesFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 5)
        return false;

      // Проверяем, что файл содержит только комментарии/пустые строки (шапку)
      bool hasOnlyComments = lineList.All(line =>
          string.IsNullOrWhiteSpace(line) ||
          line.Trim().StartsWith("#", StringComparison.Ordinal));

      if (hasOnlyComments)
        return true;

      // Проверяем все строки с данными
      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');

        // Проверяем минимальное количество полей
        if (parts.Length < 5)
          return false;

        if (!int.TryParse(parts[0], out int id) || id <= 0)
          return false;

        // SimbolID может быть 0 (формат: "0 если нет фразы")
        if (!int.TryParse(parts[1], out int simbolID) || simbolID < 0)
          return false;

        // ToneId должен быть валидным (-1, 0, 1)
        if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
        {
          if (int.TryParse(parts[3], out int toneId))
          {
            if (toneId < -1 || toneId > 1)
              return false;
          }
        }

        return true; // Достаточно одной валидной строки данных
      }

      return true; // только шапка — допустимо
    }

    #endregion

    /// <summary>
    /// Проверяет, что строка шапки описывает столбец паттернов команд (CommandPatternIdList или legacy CadPatternIdList).
    /// </summary>
    internal static bool IsCommandPatternIdListHeader(string line)
    {
      if (string.IsNullOrWhiteSpace(line))
        return false;

      var trimmed = line.Trim();
      return trimmed.IndexOf("CommandPatternIdList", StringComparison.Ordinal) >= 0 ||
             trimmed.IndexOf(FileHeaders.PerceptionImagesLegacyCommandPatternIdListHeader, StringComparison.Ordinal) >= 0;
    }

    #region IsValidCommandBrocaImagesFile

    /// <summary>
    /// Проверяет валидность файла образов команды по пути
    /// </summary>
    public static bool IsValidCommandBrocaImagesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidCommandBrocaImagesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла образов команды
    /// </summary>
    public static bool IsValidCommandBrocaImagesFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 2)
        return false;

      bool hasOnlyComments = lineList.All(line =>
          string.IsNullOrWhiteSpace(line) ||
          line.Trim().StartsWith("#", StringComparison.Ordinal));

      if (hasOnlyComments)
        return true;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 2)
          return false;

        if (!int.TryParse(parts[0], out int id) || id <= 0)
          return false;

        return true;
      }

      return true;
    }

    #endregion

    #region IsValidInfluenceActionsImagesFile

    /// <summary>
    /// Проверяет валидность файла образов действий по пути
    /// </summary>
    public static bool IsValidInfluenceActionsImagesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidInfluenceActionsImagesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла образов действий
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidInfluenceActionsImagesFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      // Проверяем, что файл содержит только комментарии/пустые строки (шапку)
      bool hasOnlyComments = lineList.All(line =>
          string.IsNullOrWhiteSpace(line) ||
          line.Trim().StartsWith("#", StringComparison.Ordinal));

      if (hasOnlyComments)
        return true;

      // Проверяем все строки с данными
      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');

        // Проверяем минимальное количество полей
        if (parts.Length < 1)
          return false;

        // Проверяем ID (первое поле должно быть числом)
        if (!int.TryParse(parts[0], out int id) || id <= 0)
          return false;

        return true; // Достаточно одной валидной строки данных
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidAutomatizmTreeFile

    /// <summary>
    /// Проверяет валидность файла дерева автоматизмов по пути
    /// </summary>
    public static bool IsValidAutomatizmTreeFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidAutomatizmTreeFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла дерева автоматизмов
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidAutomatizmTreeFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      // Проверяем, что файл содержит только комментарии/пустые строки (шапку)
      bool hasOnlyComments = true;
      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#", StringComparison.Ordinal))
        {
          hasOnlyComments = false;
          break;
        }
      }

      if (hasOnlyComments)
        return true;

      // Проверяем все строки с данными
      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');

        // Проверяем минимальное количество полей
        if (parts.Length < 8)
          return false;

        // Проверяем ID (первое поле должно быть числом)
        if (!int.TryParse(parts[0], out int id) || id < 0)
          return false;

        // Проверяем ParentID
        if (!int.TryParse(parts[1], out int parentId) || parentId < 0)
          return false;

        // Проверяем BaseID (1-3)
        if (!int.TryParse(parts[2], out int baseId) || baseId < -1 || baseId > 1)
          return false;

        // Проверяем EmotionID (может быть 0)
        if (!int.TryParse(parts[3], out int emotionId) || emotionId < 0)
          return false;

        // Проверяем ActivityID (может быть 0)
        if (!int.TryParse(parts[4], out int activityId) || activityId < 0)
          return false;

        // Проверяем ToneMoodID (может быть 0 или 90)
        if (!int.TryParse(parts[5], out int toneMoodId) || (toneMoodId != 0 && toneMoodId < 1))
          return false;

        // Проверяем SimbolID (может быть 0)
        if (!int.TryParse(parts[6], out int simbolId) || simbolId < 0)
          return false;

        // Проверяем VerbID (может быть 0)
        if (!int.TryParse(parts[7], out int verbId) || verbId < 0)
          return false;

        // VisualID (опционально в старых файлах): 0–8 как у AgentVisualColor
        int visualPartIndex = parts.Length == 9 ? 8 : (parts.Length >= 10 ? 9 : -1);
        if (visualPartIndex >= 0 && !string.IsNullOrWhiteSpace(parts[visualPartIndex]))
        {
          if (int.TryParse(parts[visualPartIndex], out int visualId))
          {
            if (visualId < 0 || visualId > 8)
              return false;
          }
        }

        // CommandID (опционально в старых файлах): parts[8] при 10+ столбцах
        if (parts.Length >= 10 && !string.IsNullOrWhiteSpace(parts[8]))
        {
          if (!int.TryParse(parts[8], out int commandId) || commandId < 0)
            return false;
        }

        return true; // Достаточно одной валидной строки данных
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidAutomatizmFile

    /// <summary>
    /// Проверяет валидность файла автоматизмов по пути
    /// </summary>
    public static bool IsValidAutomatizmFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidAutomatizmFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла автоматизмов
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidAutomatizmFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      // Проверяем, что файл содержит только комментарии/пустые строки (шапку)
      bool hasOnlyComments = lineList.All(line =>
          string.IsNullOrWhiteSpace(line) ||
          line.Trim().StartsWith("#", StringComparison.Ordinal));

      if (hasOnlyComments)
        return true;

      // Проверяем все строки с данными
      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');

        // Проверяем минимальное количество полей
        if (parts.Length < 8)
          return false;

        // Проверяем ID (первое поле должно быть числом)
        if (!int.TryParse(parts[0], out int id) || id <= 0)
          return false;

        // Проверяем BranchID
        if (!int.TryParse(parts[1], out int branchId) || branchId < 0)
          return false;

        // Проверяем Usefulness
        if (!int.TryParse(parts[2], out int usefulness))
          return false;

        // Проверяем ActionsImageID
        if (!int.TryParse(parts[3], out int actionsImageId) || actionsImageId < 0)
          return false;

        // Проверяем NextID (может быть 0)
        if (!int.TryParse(parts[4], out int nextId) || nextId < 0)
          return false;

        // Проверяем Energy (1-10)
        if (!int.TryParse(parts[5], out int energy) || energy < 1 || energy > 10)
          return false;

        // Проверяем Belief (0-2)
        if (!int.TryParse(parts[6], out int belief) || belief < 0 || belief > 2)
          return false;

        // Проверяем Count
        if (!int.TryParse(parts[7], out int count) || count < 0)
          return false;

        return true; // Достаточно одной валидной строки данных
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidAutomatizmChainsFile

    /// <summary>
    /// Проверяет валидность файла цепочек автоматизмов по пути
    /// </summary>
    public static bool IsValidAutomatizmChainsFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidAutomatizmChainsFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла цепочек автоматизмов
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidAutomatizmChainsFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      // Проверяем, что файл содержит только комментарии/пустые строки
      bool hasOnlyComments = lineList.All(line =>
          string.IsNullOrWhiteSpace(line) ||
          line.Trim().StartsWith("#", StringComparison.Ordinal));

      if (hasOnlyComments)
        return true;

      // Проверяем все строки с данными
      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');

        if (parts.Length >= 4 && parts[0] == "CHAIN")
        {
          // CHAIN|ID|Name|Description|TreeNodeId|StartAutomatizmId
          if (!int.TryParse(parts[1], out int chainId) || chainId <= 0)
            return false;

          if (parts.Length < 2)
            return false;

          // Опциональные поля
          if (parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]))
          {
            if (!int.TryParse(parts[4], out int treeNodeId) || treeNodeId < 0)
              return false;
          }

          if (parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5]))
          {
            if (!int.TryParse(parts[5], out int startAutomatizmId) || startAutomatizmId < 0)
              return false;
          }
        }
        else if (parts.Length >= 5 && parts[0] == "LINK")
        {
          // LINK|LinkID|ActionsImageID|SuccessNext|FailureNext|Description|ChainUsefulness
          if (!int.TryParse(parts[1], out int linkId) || linkId <= 0 ||
              !int.TryParse(parts[2], out int actionsImageId) || actionsImageId <= 0 || // Изменено: ActionsImageID
              !int.TryParse(parts[3], out int successNext) || successNext < 0 ||
              !int.TryParse(parts[4], out int failureNext) || failureNext < 0)
            return false;

          if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]))
          {
            if (!int.TryParse(parts[6], out int threshold) || threshold < 0)
              return false;
          }
        }
        else
        {
          return false; // Неизвестный формат строки
        }
      }

      return true; // Все строки прошли проверку
    }

    #endregion

    #region IsValidProblemTreeFile

    /// <summary>Проверяет валидность файла дерева проблем по пути</summary>
    public static bool IsValidProblemTreeFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;
      try
      {
        return IsValidProblemTreeFile(File.ReadLines(filePath).ToList());
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого файла дерева проблем</summary>
    public static bool IsValidProblemTreeFile(IEnumerable<string> lines)
    {
      if (lines == null) return false;
      var list = lines.ToList();
      if (list.Count < 1) return false;

      foreach (var line in list)
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 6) return false;
        if (!int.TryParse(p[0], out int id) || id <= 0) return false;
        if (!int.TryParse(p[1], out _)) return false;
        return true;
      }
      return true;
    }

    #endregion

    #region IsValidEpisodicTreeFile

    /// <summary>Проверяет валидность файла дерева эпизодической памяти</summary>
    public static bool IsValidEpisodicTreeFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;
      try
      {
        return IsValidEpisodicTreeFile(File.ReadLines(filePath).ToList());
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого файла дерева эпизодической памяти</summary>
    public static bool IsValidEpisodicTreeFile(IEnumerable<string> lines)
    {
      if (lines == null) return false;
      var list = lines.ToList();
      if (list.Count < 1) return false;

      foreach (var line in list)
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var main = t.Split('#');
        var p = main[0].Split('|');
        if (p.Length < 8) return false;
        if (!int.TryParse(p[0], out int id) || id <= 0) return false;
        if (!int.TryParse(p[1], out _)) return false;
        return true;
      }
      return true;
    }

    #endregion

    #region IsValidEpisodicHistoryFile

    /// <summary>Проверяет валидность файла истории эпизодической памяти</summary>
    public static bool IsValidEpisodicHistoryFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath))
        return true; // пустой путь — допустимо для новой системы
      if (!File.Exists(filePath))
        return true; // файла нет — создастся при сохранении
      try
      {
        return IsValidEpisodicHistoryContent(File.ReadAllText(filePath));
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого истории (ID,LifeTime|...)</summary>
    public static bool IsValidEpisodicHistoryContent(string content)
    {
      if (string.IsNullOrWhiteSpace(content)) return true;
      var parts = content.Split('|');
      foreach (var part in parts)
      {
        var s = part?.Trim();
        if (string.IsNullOrWhiteSpace(s)) continue;
        var p = s.Split(',');
        if (p.Length < 2) continue; // пропускаем части без формата id,time (напр. "..." в шапке)
        if (!int.TryParse(p[0], out _)) continue; // шапка или нечисловое — пропустить
        if (!int.TryParse(p[1], out _)) return false; // число,xxx но xxx не число — ошибка
      }
      return true;
    }

    #endregion

    #region IsValidMentalEpisodicTreeFile

    /// <summary>Проверяет валидность файла ментальной эпизодической памяти по пути.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>True, если файл проходит базовую проверку формата.</returns>
    public static bool IsValidMentalEpisodicTreeFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;
      try
      {
        return IsValidMentalEpisodicTreeFile(File.ReadLines(filePath).ToList());
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого файла ментальной эпизодической памяти.</summary>
    /// <param name="lines">Строки файла.</param>
    /// <returns>True, если формат строк данных допустим.</returns>
    public static bool IsValidMentalEpisodicTreeFile(IEnumerable<string> lines)
    {
      if (lines == null) return false;
      var list = lines.ToList();
      if (list.Count < 1) return false;

      foreach (var line in list)
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var hashIdx = t.IndexOf("#", StringComparison.Ordinal);
        if (hashIdx < 0) return false;
        var head = t.Substring(0, hashIdx);
        var tail = t.Substring(hashIdx + 1);
        var hp = head.Split('|');
        if (hp.Length < 6) return false;
        if (!int.TryParse(hp[0], out int id) || id < 0) return false;
        if (!int.TryParse(hp[1], out _)) return false;
        if (!int.TryParse(hp[2], out _)) return false;
        if (!int.TryParse(hp[3], out _)) return false;
        if (!int.TryParse(hp[4], out _)) return false;
        var tp = tail.Split('|');
        if (tp.Length < 2) return false;
        if (!int.TryParse(tp[0], out _)) return false;
        if (!int.TryParse(tp[1], out _)) return false;
      }
      return true;
    }

    #endregion

    #region IsValidMentalEpisodicHistoryFile

    /// <summary>Проверяет валидность файла истории ментальной эпизодики по пути.</summary>
    public static bool IsValidMentalEpisodicHistoryFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;
      try
      {
        return IsValidMentalEpisodicHistoryFile(File.ReadLines(filePath).ToList());
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого файла истории ментальной эпизодики.</summary>
    public static bool IsValidMentalEpisodicHistoryFile(IEnumerable<string> lines)
    {
      if (lines == null) return false;
      var list = lines.ToList();
      if (list.Count < 1) return false;

      foreach (var line in list)
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 3) return false;
        if (!int.TryParse(p[0], out int mentalId) || mentalId < 0) return false;
        if (!int.TryParse(p[1], out _)) return false;
        if (!int.TryParse(p[2], out int motorId) || motorId < 0) return false;
      }
      return true;
    }

    #endregion

    #region IsValidUnderstandingTreeFile

    /// <summary>Проверяет валидность файла дерева понимания ситуации по пути</summary>
    public static bool IsValidUnderstandingTreeFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;
      try
      {
        return IsValidUnderstandingTreeFile(File.ReadLines(filePath).ToList());
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого файла дерева понимания ситуации</summary>
    public static bool IsValidUnderstandingTreeFile(IEnumerable<string> lines)
    {
      if (lines == null) return false;
      var list = lines.ToList();
      if (list.Count < 1) return false;

      foreach (var line in list)
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 5) return false;
        if (!int.TryParse(p[0], out int id) || id < 0) return false;
        if (!int.TryParse(p[1], out _)) return false;
        return true;
      }
      return true;
    }

    #endregion

    #region IsValidSituationTypeFile

    /// <summary>Проверяет валидность файла справочника типов ситуаций по пути</summary>
    public static bool IsValidSituationTypeFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;
      try
      {
        return IsValidSituationTypeFile(File.ReadLines(filePath).ToList());
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого файла справочника типов ситуаций (формат: Id|MoodId|InfluenceId|ThemeTypeId|EventAgentCode; 5-й столбец опционален для старых файлов)</summary>
    public static bool IsValidSituationTypeFile(IEnumerable<string> lines)
    {
      if (lines == null) return false;
      var list = lines.ToList();
      if (list.Count < 1) return false;

      foreach (var line in list)
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 4) return false;
        if (!int.TryParse(p[0], out int id) || id <= 0 || id > 60) return false;
        if (!int.TryParse(p[1], out _)) return false;
        if (!int.TryParse(p[2], out _)) return false;
        if (!int.TryParse(p[3], out int themeTypeId) || themeTypeId < -1 || themeTypeId > 100) return false;
        if (p.Length >= 5 && (!int.TryParse(p[4], out int evCode) || evCode < -1 || evCode > 100)) return false;
      }
      return true;
    }

    #endregion

    #region IsValidSituationImageFile

    /// <summary>Проверяет валидность файла образов ситуаций по пути</summary>
    public static bool IsValidSituationImageFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;
      try
      {
        return IsValidSituationImageFile(File.ReadLines(filePath).ToList());
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого файла образов ситуаций</summary>
    public static bool IsValidSituationImageFile(IEnumerable<string> lines)
    {
      if (lines == null) return false;
      var list = lines.ToList();
      if (list.Count < 1) return false;

      foreach (var line in list)
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 3) return false;
        if (!int.TryParse(p[0], out int id) || id <= 0) return false;
        if (!int.TryParse(p[1], out _)) return false;
        if (!int.TryParse(p[2], out _)) return false;
        return true;
      }
      return true;
    }

    #endregion

    #region IsValidThemeImagesFile

    /// <summary>Проверяет валидность файла образов тем по пути</summary>
    public static bool IsValidThemeImagesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;
      try
      {
        return IsValidThemeImagesFile(File.ReadLines(filePath).ToList());
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого файла образов тем</summary>
    public static bool IsValidThemeImagesFile(IEnumerable<string> lines)
    {
      if (lines == null) return false;
      var list = lines.ToList();
      if (list.Count < 1) return false;

      foreach (var line in list)
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 4) return false;
        if (!int.TryParse(p[0], out int id) || id <= 0) return false;
        if (!int.TryParse(p[1], out int weight) || weight < 1 || weight > 10) return false;
        if (!int.TryParse(p[2], out _)) return false;
        if (!int.TryParse(p[3], out _)) return false;
        return true;
      }
      return true;
    }

    #endregion

    #region IsValidThemeTypesFile

    /// <summary>Проверяет валидность файла справочника типов тем по пути</summary>
    public static bool IsValidThemeTypesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;
      try
      {
        return IsValidThemeTypesFile(File.ReadLines(filePath).ToList());
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого файла справочника типов тем. Формат: Id|Description|DefaultWeight|AllowedInfoFuncIds (4-е поле опционально).</summary>
    public static bool IsValidThemeTypesFile(IEnumerable<string> lines)
    {
      if (lines == null) return false;
      var list = lines.ToList();
      if (list.Count < 1) return false;

      foreach (var line in list)
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 3) return false;
        if (!int.TryParse(p[0], out int id) || id < 1) return false;
        if (string.IsNullOrWhiteSpace(p[1])) return false;
        if (!int.TryParse(p[2], out int defaultWeight) || defaultWeight < 1) return false;
      }
      return true;
    }

    #endregion

    #region IsValidPurposeImagesFile

    /// <summary>Проверяет валидность файла образов целей по пути</summary>
    public static bool IsValidPurposeImagesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;
      try
      {
        return IsValidPurposeImagesFile(File.ReadLines(filePath).ToList());
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Проверяет валидность содержимого файла образов целей</summary>
    public static bool IsValidPurposeImagesFile(IEnumerable<string> lines)
    {
      if (lines == null) return false;
      var list = lines.ToList();
      if (list.Count < 1) return false;

      foreach (var line in list)
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 5) return false;
        if (!int.TryParse(p[0], out int id) || id <= 0) return false;
        if (!int.TryParse(p[1], out int target) || target < 1 || target > 2) return false;
        if (!int.TryParse(p[2], out _)) return false;
        if (!int.TryParse(p[3], out _)) return false;
        if (!int.TryParse(p[4], out _)) return false;
        return true;
      }
      return true;
    }

    #endregion

    // ======== БЕЗОПАСНОЕ СОХРАНЕНИЕ С РЕЗЕРВНОЙ КОПИЕЙ ========

    /// <summary>
    /// Безопасное сохранение файла с проверкой валидности
    /// </summary>
    public static (bool Success, string ErrorMessage) SafeSaveFile(
        string filePath,
        IEnumerable<string> content,
        Func<string, bool> validationFunc,
        int minLinesCount = 1,
        string fileDescription = "файл")
    {
      var (success, error) = SafeSaveFileDetailed(
          filePath, content, validationFunc, minLinesCount, fileDescription);

      if (!success)
        LogError($"SafeSaveFile: Ошибка сохранения {fileDescription} ({filePath}): {error}");

      return (success, error);
    }

    /// <summary>
    /// Подробная реализация безопасного сохранения с .tmp и .bak
    /// </summary>
    public static (bool Success, string ErrorMessage) SafeSaveFileDetailed(
        string filePath,
        IEnumerable<string> content,
        Func<string, bool> validationFunc,
        int minLinesCount,
        string fileDescription)
    {
      if (content == null)
      {
        return (false, $"Нет данных для сохранения {fileDescription}");
      }

      var contentList = content.ToList();
      if (contentList.Count < minLinesCount)
      {
        return (false, $"Недостаточно данных (требуется минимум {minLinesCount} строк)");
      }

      string tempPath = filePath + ".tmp";

      try
      {
        File.WriteAllLines(tempPath, contentList);

        // Валидация через функцию
        if (!validationFunc(tempPath))
        {
          File.Delete(tempPath);
          return (false, "Данные не прошли проверку на корректность");
        }

        if (File.Exists(filePath))
        {
          string backupPath = filePath + ".bak";
          File.Replace(tempPath, filePath, backupPath);
        }
        else
        {
          File.Move(tempPath, filePath);
        }

        return (true, string.Empty);
      }
      catch (UnauthorizedAccessException ex)
      {
        string dir = Path.GetDirectoryName(filePath);
        return (false,
            "Отказ в доступе при записи файла данных (это тот же каталог, что и целевой файл; создаются .tmp и .bak).\n" +
            "Файл: " + filePath + "\n" +
            "Временный: " + tempPath + "\n" +
            (string.IsNullOrEmpty(dir) ? string.Empty : "Каталог: " + dir + "\n") +
            "Частые причины: атрибут «только чтение» на файле или каталоге; права NTFS (не та учётная запись); конфиг указывает не на ProgramData.\n" +
            "Для записи в %ProgramData% обычно не нужен запуск от администратора — проверьте вкладку «Безопасность» у папки.\n" +
            "Детали: " + ex.Message);
      }
      catch (IOException ex)
      {
        return (false, $"Ошибка записи файла: {ex.Message}");
      }
      catch (Exception ex)
      {
        return (false, $"Неожиданная ошибка: {ex.Message}");
      }
      finally
      {
        if (File.Exists(tempPath))
        {
          try { File.Delete(tempPath); } catch { }
        }
      }
    }
  }
}