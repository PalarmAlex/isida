using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static ISIDA.Actions.AdaptiveActionsSystem;
using static ISIDA.Common.FileValidator;
using static ISIDA.Psychic.InformationEnvironmentSystem;

namespace ISIDA.Gomeostas
{
  /// <summary>
  /// Система управления гомеостазом для одного агента
  /// </summary>
  public sealed class GomeostasSystem : IDisposable
  {
    private readonly StyleCombinationsManager _styleCombinationsManager;
    private PerceptionImagesSystem _perceptionImagesSystem;
    private ConditionedReflexesSystem _conditionedReflexesSystem;
    private ResearchLogger _researchLogger;
    private HomeostasisOverallState _currentOverallState = HomeostasisOverallState.Normal;

    #region Инициализация класса

    /// <summary>
    /// Устанавливает ссылку на систему образов после её инициализации
    /// </summary>
    public void SetPerceptionImagesSystem(PerceptionImagesSystem perceptionImagesSystem)
    {
      _perceptionImagesSystem = perceptionImagesSystem ??
          throw new ArgumentNullException(nameof(perceptionImagesSystem));
    }

    /// <summary>
    /// Инициализирует новый экземпляр системы гомеостаза с указанными или стандартными путями к данным.
    /// </summary>
    public GomeostasSystem(string gomeostasFolderPath = null)
    {
      try
      {
        // Используем переданные пути или вычисляем стандартные 
        GomeostasFolderPath = gomeostasFolderPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ISIDA", "Data", "Gomeostas");

        // Инициализируем словари
        _styleAntagonistsIndex = new Dictionary<int, List<BehaviorStyle>>();
        _styleActivationsIndex = new Dictionary<int, List<ParameterData>>();

        // Инициализация детектора новизны
        _previousOverallState = HomeostasisOverallState.Normal;
        _previousActiveStyleIds = new List<int>();
        _isNewConditions = false;

        EnsureDataDirectory();
        LoadAgentData();

        _calculator = new HomeostasisCalculator();
        _styleCombinationsManager = new StyleCombinationsManager(
          GomeostasFolderPath,
          () => InternalBehaviorStyles,
          () => GetAllParameters());
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        throw;
      }
    }

    /// <summary>
    /// Установка логгера
    /// </summary>
    public void SetResearchLogger(ResearchLogger logger)
    {
      _researchLogger = logger;
    }

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => Instance != null;

    /// <summary>
    /// Глобальный экземпляр системы гомеостаза
    /// </summary>
    public static GomeostasSystem Instance { get; private set; }

    /// <summary>
    /// Инициализирует глобальный экземпляр системы гомеостаза с указанными путями.
    /// Должен быть вызван один раз при старте приложения.
    /// </summary>
    /// <param name="gomeostasFolderPath">Путь к каталогу данных гомеостаза. Если null, используется путь по умолчанию.</param>
    public static void InitializeInstance(string gomeostasFolderPath = null)
    {
      if (Instance != null)
        throw new InvalidOperationException("Instance уже инициализирован.");

      Instance = new GomeostasSystem(gomeostasFolderPath);
    }

    #endregion

    #region Индексация для быстрого поиска ссылок при удалении стилей

    private readonly Dictionary<int, List<BehaviorStyle>> _styleAntagonistsIndex;
    private readonly Dictionary<int, List<ParameterData>> _styleActivationsIndex;

    /// <summary>
    /// Построение индексов для быстрого поиска зависимостей
    /// </summary>
    private void BuildStyleIndexes()
    {
      _styleAntagonistsIndex.Clear();
      _styleActivationsIndex.Clear();

      // Индекс антагонистов
      foreach (var style in _agentState.BehaviorStyles.Values)
      {
        foreach (var antagonistId in style.AntagonistStyles)
        {
          if (!_styleAntagonistsIndex.ContainsKey(antagonistId))
            _styleAntagonistsIndex[antagonistId] = new List<BehaviorStyle>();

          _styleAntagonistsIndex[antagonistId].Add(style);
        }
      }

      // Индекс активаций в параметрах
      foreach (var param in _agentState.Parameters)
      {
        foreach (var activation in param.StyleActivations)
        {
          foreach (var styleId in activation.Value)
          {
            int absStyleId = Math.Abs(styleId);
            if (!_styleActivationsIndex.ContainsKey(absStyleId))
              _styleActivationsIndex[absStyleId] = new List<ParameterData>();

            if (!_styleActivationsIndex[absStyleId].Contains(param))
              _styleActivationsIndex[absStyleId].Add(param);
          }
        }
      }
    }

    #endregion

    #region Обновление стилей реагирования

    private List<ParameterData> _previousParametersState = new List<ParameterData>();

    internal bool HasCriticalChanges = false;

    /// <summary>
    /// Обновляет на каждом пульсе состояние агента (параметры, стили, время и т.п.) без активации реакций.
    /// </summary>
    internal void UpdateStateOnly()
    {
      _lock.EnterWriteLock();
      try
      {
        if (_agentState.IsDead)
        {
          Logger.Warning("Агент уже мертв, обновление невозможно");
          return;
        }

        var previousOverallState = _previousOverallState; 
        var previousActiveStyleIds = new List<int>(_previousActiveStyleIds);

        if (_agentState.IsFirstPulse)
          _agentState.IsFirstPulse = false;

        // Сохраняем предыдущее состояние ДО обновления
        SaveParametersState();
        HasCriticalChanges = _calculator.HasCriticalParameterChanges(
            _agentState.Parameters, _previousParametersState);

        // ритмичное убывание/нарастание параметров в зависимости от типа: дефицит/избыток ориентированные
        foreach (var param in _agentState.Parameters)
        {
          try
          {
            float delta100 = param.Speed / 100f;
            if (_agentState.IsSleeping) delta100 /= 10f;

            var newValue = ClampFloat(param.Value + delta100, 0f, 100f);
            param.Value = newValue;

            param.UpdateState(_dynamicTime, _difSensorPar, _agentState.IsFirstPulse);
          }
          catch (Exception paramEx)
          {
            Logger.Error($"{paramEx.Message}");
            throw;
          }
        }

        var lastWellStatePulse = _agentState.LastWellStatePulse;
        var currentAgentState = _calculator.CalculateAgentState(_agentState.Parameters, _dynamicTime, _difSensorPar, ref lastWellStatePulse, _compareLevel);
        _agentState.LastWellStatePulse = lastWellStatePulse;
        _currentOverallState = currentAgentState.OverallState;

        UpdateNoveltyDetector(previousOverallState, previousActiveStyleIds);
        UpdateActiveStyles();

        // Проверяем критическое состояние ПОСЛЕ обновления
        CheckForCriticalState();

        _agentState.LastUpdated = DateTime.UtcNow;
        _agentState.Lifetime++;
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        throw;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    private void SaveParametersState()
    {
      _previousParametersState.Clear();
      foreach (var param in _agentState.Parameters)
      {
        _previousParametersState.Add(new ParameterData(
            param.Id, param.Name, param.Description, param.Value,
            param.Weight, param.NormaWell, param.Speed,
            param.IsVital, param.CriticalMinValue, param.CriticalMaxValue));
      }
    }

    internal void OnExternalInfluenceApplied(bool isCriticalImpact = false)
    {
      UpdateActiveStyles(true);
    }

    #endregion

    #region Валидация параметров гомеостаза и стилей реагирования

    /// <summary>
    /// Валидация стилей реагирования перед обновлением и удалением
    /// </summary>
    /// <param name="styles">Список стилей</param>
    /// <param name="errorMessage">Строка сообщения об ошибке валидации</param>
    /// <param name="isForDeletion">При установке True валидация удаления, по умолчанию False - валидация обновления</param>
    /// <returns></returns>
    public bool ValidateAgentBehaviorStyles(IEnumerable<BehaviorStyle> styles, out string errorMessage, bool isForDeletion = false)
    {
      errorMessage = string.Empty;
      var existingIds = styles.Select(p => p.Id).ToHashSet();
      var styleList = styles.ToList();

      foreach (var style in styles)
      {
        if (isForDeletion)
        {
          if (!existingIds.Contains(style.Id))
          {
            errorMessage = $"Стиль c ID: {style.Id} не найден";
            return false;
          }

          if (style.Id == _defaultStileId)
          {
            errorMessage = $"Стиль {_agentState.BehaviorStyles[style.Id].Name} задан стилем по умолчанию и запрещён для удаления";
            return false;
          }

          if (IsStyleUsedInBehaviorStyleImages(style.Id))
          {
            errorMessage = $"Стиль {_agentState.BehaviorStyles[style.Id].Name} (ID: {style.Id}) используется в образах стилей поведения и не может быть удален";
            return false;
          }
        }
        else
        {
          if (style.AntagonistStyles?.Contains(style.Id) == true)
          {
            errorMessage = $"Стиль {style.Name} (ID: {style.Id}) блокирует сам себя";
            return false;
          }

          // Проверка существования ID антагонистов
          foreach (var antId in style.AntagonistStyles)
          {
            if (!existingIds.Contains(antId))
            {
              errorMessage = $"Стиль {style.Name} (ID: {style.Id}) ссылается на несуществующий антагонист ID={antId}";
              return false;
            }
          }
        }
      }

      // Проверка антагонистических пар (только если не удаление)
      if (!isForDeletion)
      {
        var unpairedStyles = FindUnpairedStylesForValidation(styleList);
        if (unpairedStyles.Any())
        {
          var unpairedList = string.Join(", ", unpairedStyles.Select(s => $"{s.Name} (ID:{s.Id})"));
          errorMessage = $"AsymmetricStyles: Обнаружены несимметричные антагонистические связи:\n{unpairedList}\n\n";
          return false;
        }
      }

      if (!ValidateParameterStyleConflicts(_agentState.Parameters, out string errorMsg))
      {
        errorMessage = "Изменения антагонистов нарушило существующую матрицу связей параметры - стили. Возникли конфликты антагонистов:\n\n" + errorMsg;
        return false;
      }

      return true;
    }
    
    private List<BehaviorStyle> FindUnpairedStylesForValidation(List<BehaviorStyle> styles)
    {
      var unpaired = new List<BehaviorStyle>();
      var styleDict = styles.ToDictionary(s => s.Id, s => s);

      foreach (var style in styles)
      {
        foreach (var antagonistId in style.AntagonistStyles)
        {
          if (styleDict.ContainsKey(antagonistId))
          {
            var antagonist = styleDict[antagonistId];
            if (!antagonist.AntagonistStyles.Contains(style.Id))
            {
              if (!unpaired.Contains(style))
                unpaired.Add(style);

              break;
            }
          }
        }
      }

      return unpaired;
    }

    /// <summary>
    /// Автоматически исправляет асимметричные антагонистические связи в переданной коллекции стилей
    /// </summary>
    /// <param name="styles">Коллекция стилей для исправления</param>
    /// <returns>Количество исправленных связей</returns>
    public int FixAntagonistSymmetry(IEnumerable<BehaviorStyle> styles)
    {
      int fixesCount = 0;
      var styleList = styles.ToList();
      var styleDict = styleList.ToDictionary(s => s.Id, s => s);

      foreach (var style in styleList)
      {
        foreach (var antagonistId in style.AntagonistStyles.ToList())
        {
          if (styleDict.ContainsKey(antagonistId))
          {
            var antagonist = styleDict[antagonistId];

            if (!antagonist.AntagonistStyles.Contains(style.Id))
            {
              antagonist.AntagonistStyles.Add(style.Id);
              fixesCount++;
            }
          }
        }
      }

      return fixesCount;
    }

    /// <summary>
    /// Автоматически исправляет асимметричные антагонистические связи в текущих данных гомеостаза
    /// </summary>
    /// <returns>Количество исправленных связей</returns>
    public int FixAntagonistSymmetry()
    {
      _lock.EnterWriteLock();
      try
      {
        var styles = _agentState.BehaviorStyles.Values.ToList();
        int fixesCount = FixAntagonistSymmetry(styles);

        if (fixesCount > 0)
        {
          // Обновляем индексы после исправления связей
          BuildStyleIndexes();
        }

        return fixesCount;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Находит стили с асимметричными антагонистическими связями
    /// </summary>
    /// <returns>Список проблемных стилей</returns>
    public List<BehaviorStyle> FindAsymmetricStyles(IEnumerable<BehaviorStyle> styles)
    {
      return FindUnpairedStylesForValidation(styles.ToList());
    }

    /// <summary>
    /// Проверяет, используется ли стиль в каких-либо образах BehaviorStyleImage
    /// </summary>
    /// <param name="styleId">ID проверяемого стиля</param>
    /// <returns>True если стиль используется, иначе False</returns>
    private bool IsStyleUsedInBehaviorStyleImages(int styleId)
    {
      try
      {
        if (_perceptionImagesSystem == null || !PerceptionImagesSystem.IsInitialized)
          return false;

        var behaviorStyleImages = _perceptionImagesSystem.GetAllBehaviorStyleImagesList();

        foreach (var image in behaviorStyleImages)
        {
          if (image.BehaviorStylesList != null && image.BehaviorStylesList.Contains(styleId))
            return true;
        }

        return false;
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        return true;
      }
    }

    /// <summary>
    /// Валидация параметров гомеостаза перед обновлением
    /// </summary>
    public bool ValidateParameterIds(IEnumerable<ParameterData> parameters, out string errorMessage)
    {
      errorMessage = string.Empty;
      var existingIds = parameters.Select(p => p.Id).ToHashSet();
      var errors = new List<string>();

      // Проверка наличия хотя бы одного жизненно важного параметра (IsVital)
      if (!parameters.Any(p => p.IsVital))
        errors.Add("Должен быть хотя бы один жизненно важный параметр (IsVital == true)");

      foreach (var param in parameters)
      {
        // Проверка наличия стилей активации
        if (!param.StyleActivations.Any(kv => kv.Value.Any()))
          errors.Add($"Параметр '{param.Name}': не заданы активации стилей реагирования для действий");

        // Проверка StyleActivations - все ID стилей должны существовать
        foreach (var activation in param.StyleActivations)
        {
          foreach (var styleId in activation.Value)
          {
            int absStyleId = Math.Abs(styleId); // учитываем отрицательные ID для деактиваций
            if (!_agentState.BehaviorStyles.ContainsKey(absStyleId))
              errors.Add($"Параметр '{param.Name}' (ID:{param.Id}) содержит ссылку на несуществующий стиль (ID:{absStyleId}) в активациях состояния {activation.Key}");
          }
        }

        var validation = SettingsValidator.ValidateCriticalMinMaxValueParamValue(param.Value, param.CriticalMinValue, param.CriticalMaxValue, param.Speed, true);
        if (!validation.isValid)
          errors.Add(validation.errorMessage);
      }

      if (errors.Any())
      {
        errorMessage = "Обнаружены ссылки на несуществующие элементы:\n" + string.Join("\n", errors);
        return false;
      }

      if (!ValidateParameterStyleConflicts(_agentState.Parameters, out errorMessage))
        return false;

      return true;
    }

    /// <summary>
    /// Проверяет параметры гомеостаза на наличие конфликтующих стилей в активациях
    /// </summary>
    /// <param name="parameters">Список параметров для проверки</param>
    /// <param name="errorMessage">Сообщение об ошибке с детализацией конфликтов</param>
    /// <returns>True если конфликтов нет, иначе False</returns>
    public bool ValidateParameterStyleConflicts(IEnumerable<ParameterData> parameters, out string errorMessage)
    {
      errorMessage = string.Empty;
      var conflicts = new List<string>();

      //_lock.EnterReadLock();
      try
      {
        var allStyles = GetAllBehaviorStyles();

        foreach (var param in parameters)
        {
          foreach (var activation in param.StyleActivations)
          {
            int zoneId = activation.Key;
            var styleIds = activation.Value.Where(id => id > 0).ToList();

            if (styleIds.Count < 2) continue; // Нет смысла проверять одиночные стили

            var conflictResult = CheckStyleCombinationForConflicts(styleIds, allStyles);
            if (!conflictResult.IsValid)
            {
              conflicts.Add($"Параметр '{param.Name}' (ID:{param.Id}), зона {zoneId}: {conflictResult.ErrorMessage}");
            }
          }
        }

        if (conflicts.Any())
        {
          errorMessage = "Обнаружены конфликты стилей в активациях параметров:\n" +
                        string.Join("\n", conflicts);
          return false;
        }

        return true;
      }
      finally
      {
        //_lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Проверяет комбинацию стилей на конфликты антагонистов
    /// </summary>
    private (bool IsValid, string ErrorMessage) CheckStyleCombinationForConflicts(List<int> styleIds, ReadOnlyDictionary<int, BehaviorStyle> allStyles)
    {
      var conflictingPairs = new List<string>();

      // Проверяем все возможные пары стилей в комбинации
      for (int i = 0; i < styleIds.Count; i++)
      {
        for (int j = i + 1; j < styleIds.Count; j++)
        {
          int styleId1 = styleIds[i];
          int styleId2 = styleIds[j];

          // Проверяем, являются ли стили антагонистами
          if (AreStylesAntagonists(styleId1, styleId2, allStyles))
          {
            var style1 = allStyles[styleId1];
            var style2 = allStyles[styleId2];
            conflictingPairs.Add($"{style1.Name} (ID:{style1.Id}) ↔ {style2.Name} (ID:{style2.Id})");
          }
        }
      }

      if (conflictingPairs.Any())
      {
        return (false, $"Конфликтующие стили: {string.Join("; ", conflictingPairs)}");
      }

      return (true, string.Empty);
    }

    /// <summary>
    /// Проверяет, являются ли два стиля антагонистами
    /// </summary>
    private bool AreStylesAntagonists(int styleId1, int styleId2, ReadOnlyDictionary<int, BehaviorStyle> allStyles)
    {
      if (!allStyles.ContainsKey(styleId1) || !allStyles.ContainsKey(styleId2))
        return false;

      var style1 = allStyles[styleId1];
      var style2 = allStyles[styleId2];

      // Проверяем двусторонние антагонистические связи
      return (style1.AntagonistStyles.Contains(styleId2) ||
              style2.AntagonistStyles.Contains(styleId1));
    }

    #endregion

    #region Константы и структуры

    private const string StylesFileName = "BehaviorStyles";
    private const string AgentParametersFileName = "VitalParameters";
    private const string AgentPropertiesFileName = "AgentProperties";
    private const string DefaultAgentName = "Агент";
    private const string DefaultAgentDescription = "Простой агент";
    internal string GomeostasFolderPath;
    private string GetAgentStylesFilePath() =>
      Path.Combine(GomeostasFolderPath, $"{StylesFileName}.dat");
    private string GetAgentParametersPath() =>
    Path.Combine(GomeostasFolderPath, $"{AgentParametersFileName}.dat");
    private string GetAgentPropertiesPath() =>
        Path.Combine(GomeostasFolderPath, $"{AgentPropertiesFileName}.dat");

    /// <summary>
    /// Получает словарь всех поведенческих стилей агента (для внутреннего использования)
    /// </summary>
    internal ReadOnlyDictionary<int, BehaviorStyle> InternalBehaviorStyles
    {
      get
      {
        _lock.EnterReadLock();
        try
        {
          return new ReadOnlyDictionary<int, BehaviorStyle>(_agentState.BehaviorStyles);
        }
        finally
        {
          _lock.ExitReadLock();
        }
      }
    }

    /// <summary>
    /// Возвращает калькулятор для определения состояний гомеостаза
    /// </summary>
    public HomeostasisCalculator Calculator => _calculator;

    /// <summary>
    /// Текущее значение счетчика пульсов
    /// </summary>
    public int PulseCount { get; set; }

    /// <summary>
    /// Стиль поведения агента
    /// </summary>
    public class BehaviorStyle
    {
      /// <summary>
      /// Уникальный идентификатор стиля поведения
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Наименование стиля поведения (например, "Пищевой", "Защита")
      /// </summary>
      public string Name { get; set; }

      /// <summary>
      /// Подробное описание стиля поведения и его характеристик
      /// </summary>
      public string Description { get; set; }

      /// <summary>
      /// Список ID стилей-антагонистов, которые несовместимы с данным стилем
      /// </summary>
      /// <remarks>
      /// При активации данного стиля, все указанные здесь стили будут деактивированы.
      /// </remarks>
      public List<int> AntagonistStyles { get; set; } = new List<int>();
    }

    /// <summary>
    /// Данные параметра гомеостаза
    /// </summary>
    public class ParameterData : INotifyPropertyChanged
    {
      private int _id = 0; // временное значение, будет перезаписано в AddParameter
      private string _name;
      private string _description;
      private float _value;
      private float _criticalMinValue;
      private float _criticalMaxValue;
      private int _weight;
      private int _normaWell;
      private int _speed;
      private ParameterState _previousState;
      private float _previousValue;
      private bool _isDominant;

      /// <summary>
      /// Конструктор по умолчанию
      /// </summary>
      public ParameterData()
      {
        // Установка значений по умолчанию
        Name = "Новый параметр";
        Description = string.Empty;
        Value = 50f;
        Weight = 50;
        NormaWell = 50;
        Speed = -1;
        IsVital = false;
        CriticalMinValue = 0f;
        CriticalMaxValue = 100f;
        _previousState = ParameterState.Normal;
        _previousValue = 50f;
        LastState = ParameterState.Normal;
        LastStateChangePulse = null;
        _isDominant = false;
      }

      /// <summary>
      /// Инициализирует новый экземпляр класса ParameterData
      /// </summary>
      public ParameterData(int id, string name, string description, float value,
                           int weight, int normaWell, int speed,
                           bool isVital = false, float criticalMinValue = 0f, float criticalMaxValue = 100f,
                           ParameterState initialState = ParameterState.Normal)
      {
        // строго в таком порядке! иначе ValidateCriticalMinMaxValueParamValue() не пропустит при загрузке
        Id = id;
        Name = name;
        Description = description;
        Value = value;
        Weight = weight;
        NormaWell = normaWell;
        Speed = speed;
        IsVital = isVital;
        CriticalMaxValue = criticalMaxValue;
        CriticalMinValue = criticalMinValue;
        _previousState = initialState;
        _previousValue = value;
        LastState = ParameterState.Normal;
        LastStateChangePulse = null;
      }

      /// <summary>
      /// Предыдущее значение параметра (только для чтения)
      /// </summary>
      public float PreviousValue => _previousValue;

      /// <summary>
      /// Предыдущее состояние параметра (только для чтения)
      /// </summary>
      public ParameterState PreviousState => _previousState;

      /// <summary>
      /// Время последнего изменения состояния параметра
      /// </summary>
      /// <remarks>
      /// Используется для временного удержания состояний Well и Bad.
      /// Устанавливается в момент перехода между состояниями и сбрасывается
      /// при возврате в состояние Normal или при истечении времени удержания.
      /// </remarks>
      public int? LastStateChangePulse { get; set; } = null;

      /// <summary>
      /// Последнее зафиксированное состояние параметра
      /// </summary>
      /// <remarks>
      /// Хранит предыдущее состояние для отслеживания переходов между состояниями
      /// и определения необходимости запуска таймера временного удержания.
      /// </remarks>
      public ParameterState LastState { get; set; } = ParameterState.Normal;

      /// <summary>
      /// Уникальный идентификатор параметра
      /// </summary>
      public int Id
      {
        get => _id;
        set { _id = value; OnPropertyChanged(nameof(Id)); }
      }

      /// <summary>
      /// Наименование параметра
      /// </summary>
      public string Name
      {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
      }

      /// <summary>
      /// Подробное описание параметра
      /// </summary>
      public string Description
      {
        get => _description;
        set { _description = value; OnPropertyChanged(nameof(Description)); }
      }

      /// <summary>
      /// Текущее значение параметра (в диапазоне от 0 до 100)
      /// </summary>
      public float Value
      {
        get => _value;
        set
        {
          var validation = SettingsValidator.ValidateCriticalMinMaxValueParamValue(value, _criticalMinValue, CriticalMaxValue, _speed);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);

          if (Math.Abs(_value - value) < float.Epsilon)
            return;

          _previousValue = _value;
          _value = value;
          OnPropertyChanged(nameof(Value));
        }
      }

      /// <summary>
      /// Вес параметра (в диапазоне от 0 до 100)
      /// </summary>
      public int Weight
      {
        get => _weight;
        set
        {
          var validation = SettingsValidator.ValidateWeightParam(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);

          _weight = value;
          OnPropertyChanged(nameof(Weight));
        }
      }

      /// <summary>
      /// Пороговое значение нормы (в диапазоне от 1 до 99)
      /// </summary>
      public int NormaWell
      {
        get => _normaWell;
        set
        {
          var validation = SettingsValidator.ValidateNormaWellParam(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);

          _normaWell = value;
          OnPropertyChanged(nameof(NormaWell));
        }
      }

      /// <summary>
      /// Скорость изменения параметра (% в час)
      /// </summary>
      public int Speed
      {
        get => _speed;
        set
        {
          if(value == 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Значение не может быть равно 0");

          var validation = SettingsValidator.ValidateSpeedParam(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);

          _speed = value;
          OnPropertyChanged(nameof(Speed));
        }
      }

      /// <summary>
      /// Флаг, указывающий что параметр является жизненно важным
      /// Критические мин/макс значения только таких параметров могут вызвать "смерть" агента
      /// </summary>
      public bool IsVital { get; set; } = false;

      /// <summary>
      /// Минимальное критическое значение для жизненно важных параметров
      /// </summary>
      public float CriticalMinValue
      {
        get => _criticalMinValue;
        set
        {
          var validation = SettingsValidator.ValidateCriticalMinMaxValueParamValue(_value, value, CriticalMaxValue, _speed);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);

          _criticalMinValue = value;
          OnPropertyChanged(nameof(CriticalMinValue));
        }
      }

      /// <summary>
      /// Максимальное критическое значение для жизненно важных параметров  
      /// </summary>
      public float CriticalMaxValue
      {
        get => _criticalMaxValue;
        set
        {
          var validation = SettingsValidator.ValidateCriticalMinMaxValueParamValue(_value, _criticalMinValue, value, _speed);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);

          _criticalMaxValue = value;
          OnPropertyChanged(nameof(CriticalMaxValue));
        }
      }

      /// <summary>
      /// Флаг, указывающий что параметр является доминирующим в текущий момент
      /// </summary>
      public bool IsDominant
      {
        get => _isDominant;
        set
        {
          if (_isDominant != value)
          {
            _isDominant = value;
            OnPropertyChanged(nameof(IsDominant));
          }
        }
      }

      /// <summary>
      /// Условия активации стилей в зависимости от состояния параметра
      /// </summary>
      public Dictionary<int, List<int>> StyleActivations { get; set; } = new Dictionary<int, List<int>>()
        {
            {0, new List<int>()},  // Выход из нормы
            {1, new List<int>()},  // Возврат в норму
            {2, new List<int>()},  // Норма
            {3, new List<int>()},  // Слабое отклонение
            {4, new List<int>()},  // Значительное отклонение
            {5, new List<int>()},  // Сильное отклонение
            {6, new List<int>()}   // Критическое отклонение
        };

      /// <summary>
      /// Текущее состояние параметра
      /// </summary>
      public ParameterStateInfo GetCurrentState(int dynamicTime, int difSensorPar)
      {
        return Instance.Calculator.CalculateParameterState(this, dynamicTime, difSensorPar);
      }

      /// <summary>
      /// Событие, возникающее при изменении свойств объекта
      /// </summary>
      public event PropertyChangedEventHandler PropertyChanged;

      /// <summary>
      /// Вызывает событие PropertyChanged при изменении свойства
      /// </summary>
      /// <param name="propertyName">Имя изменившегося свойства</param>
      protected virtual void OnPropertyChanged(string propertyName)
      {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
      }

      /// <summary>
      /// Обновляет состояние параметра с учетом пульса
      /// </summary>
      public void UpdateState(int dynamicTime, float difSensorPar, bool isFirstPulse = false)
      {
        if (Math.Abs(_value - _previousValue) < float.Epsilon && !isFirstPulse)
          return;

        var newState = Instance.Calculator.CalculateParameterState(
            this, dynamicTime, difSensorPar);

        _previousState = CurrentState;
        CurrentState = newState.State;
      }

      private ParameterState _currentState;
      /// <summary>
      /// Состояние параметра
      /// </summary>
      public ParameterState CurrentState
      {
        get => _currentState;
        set
        {
          if (_currentState != value)
          {
            _currentState = value;
            OnPropertyChanged(nameof(CurrentState));
          }
        }
      }
    }

    /// <summary>
    /// Состояние агента
    /// </summary>
    private class AgentState
    {
      /// <summary>
      /// Счетчик для генерации новых ID параметров
      /// </summary>
      public int LastParameterId { get; set; }
      public int LastBehaviorStylesId { get; set; }

      /// <summary>
      /// Жизненные параметры агента
      /// </summary>
      public List<ParameterData> Parameters = new List<ParameterData>();

      /// <summary>
      /// Стили поведения агента
      /// </summary>
      public Dictionary<int, BehaviorStyle> BehaviorStyles { get; } = new Dictionary<int, BehaviorStyle>();

      /// <summary>
      /// Время последнего обновления
      /// </summary>
      public DateTime LastUpdated;

      /// <summary>
      ///  Имя агента
      /// </summary>
      public string Name { get; set; }

      /// <summary>
      /// Описание агента
      /// </summary>
      public string Description { get; set; }

      /// <summary>
      /// ID параметра с наивысшим приоритетом
      /// </summary>
      public int PriorityParameterId { get; set; }

      /// <summary>
      /// Время жизни агента в пульсах
      /// </summary>
      public int Lifetime { get; set; }

      private int _evolutionStage;
      /// <summary>
      /// Текущая стадия развития (от 0 до 5)
      /// </summary>
      public int EvolutionStage
      {
        get => _evolutionStage;
        set
        {
          if (value < 0 || value > 5)
            throw new ArgumentOutOfRangeException(nameof(EvolutionStage),
                "Стадия может быть только от 0 до 5");
          _evolutionStage = value;
        }
      }

      /// <summary>
      /// Флаг фазы сна
      /// </summary>
      public bool IsSleeping { get; set; }

      /// <summary>
      /// Текущий уровень боли
      /// </summary>
      public int PainValue { get; set; }

      /// <summary>
      /// Текущий уровень радости
      /// </summary>
      public int JoyValue { get; set; }

      /// <summary>
      /// Флаг смерти агента (повреждения > 99%)
      /// </summary>
      public bool IsDead { get; set; }

      /// <summary>
      /// Получить параметр по ID
      /// </summary>
      public ParameterData GetParameter(int paramId)
      {
        return Parameters.FirstOrDefault(p => p.Id == paramId);
      }

      /// <summary>
      /// Обновить параметр
      /// </summary>
      public void UpdateParameter(ParameterData parameter)
      {
        var index = Parameters.FindIndex(p => p.Id == parameter.Id);
        if (index >= 0)
          Parameters[index] = parameter;
      }

      /// <summary>
      /// Удалить параметр по ID
      /// </summary>
      public bool RemoveParameter(int paramId)
      {
        var index = Parameters.FindIndex(p => p.Id == paramId);
        if (index >= 0)
        {
          Parameters.RemoveAt(index);
          return true;
        }
        return false;
      }

      /// <summary>
      /// Время последнего перехода в состояние Хорошо
      /// </summary>
      public int? LastWellStatePulse { get; set; } = null;

      /// <summary>
      /// Флаг первого пульса агента
      /// </summary>
      public bool IsFirstPulse { get; set; } = true;
    }

    /// <summary>
    /// Представляет интегральное состояние гомеостаза агента
    /// </summary>
    public class AgentHomeostasisState
    {
      /// <summary>
      ///  Интегральное состояние агента
      /// </summary>
      public HomeostasisOverallState OverallState { get; set; }
      /// <summary>
      ///  Сумма значений параметров в состоянии Плохо
      /// </summary>
      public float BadSum { get; set; }
      /// <summary>
      ///  Сумма значений параметров в состоянии Хорошо
      /// </summary>
      public float WellSum { get; set; }
      /// <summary>
      ///  Список состояний всех параметров агента
      /// </summary>
      public List<ParameterStateInfo> ParametersState { get; set; }
    }

    /// <summary>
    /// Представляет состояние отдельного параметра гомеостаза
    /// </summary>
    public class ParameterStateInfo
    {
      /// <summary>
      /// Состояние параметра гомеостаза агента
      /// </summary>
      public ParameterState State { get; set; }
      /// <summary>
      /// Значение параметра гомеостаза агента
      /// </summary>
      public float Value { get; set; }
      /// <summary>
      /// ID параметра гомеостаза агента
      /// </summary>
      public int ParameterId { get; set; }
      /// <summary>
      /// Имя параметра гомеостаза агента
      /// </summary>
      public string ParameterName { get; set; }
    }

    /// <summary>
    /// Общее состояние гомеостаза агента
    /// </summary>
    public enum HomeostasisOverallState
    {
      /// <summary>
      /// Плохо
      /// </summary>
      Bad = -1,
      /// <summary>
      /// Норма
      /// </summary>
      Normal = 0,
      /// <summary>
      /// Хорошо
      /// </summary>
      Well = 1
    }

    /// <summary>
    /// Состояние отдельного параметра гомеостаза
    /// </summary>
    public enum ParameterState
    {
      /// <summary>
      /// Плохо
      /// </summary>
      Bad = -1,
      /// <summary>
      /// Норма
      /// </summary>
      Normal = 0,
      /// <summary>
      /// Хорошо
      /// </summary>
      Well = 1
    }

    #endregion

    #region Поля и свойства

    private int _dynamicTime = 50;
    private float _difSensorPar = 0.5f;
    private int _compareLevel = 100;
    private int _defaultStileId = 0;

    /// <summary>
    /// Время в пульсах удержания состояния для возврата в норму после активации состояния ХОРОШО
    /// </summary>
    public int DynamicTime
    {
      get => _dynamicTime;
      set
      {
        var validation = SettingsValidator.ValidateDynamicTime(value);
        if (!validation.isValid)
          throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);
        
        _dynamicTime = value;
      }
    }

    /// <summary>
    /// Минимальная величина изменения параметров для детектирования
    /// </summary>
    public float DifSensorPar
    {
      get => _difSensorPar;
      set
      {
        var validation = SettingsValidator.ValidateDifSensorPar(value);
        if (!validation.isValid)
          throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);
        
        _difSensorPar = value;
      }
    }

    /// <summary>
    /// Порог начала изменения глобального состояния агента
    /// </summary>
    public int CompareLevel
    {
      get => _compareLevel;
      set
      {
        var validation = SettingsValidator.ValidateCompareLevel(value);
        if (!validation.isValid)
          throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);

        _compareLevel = value;
      }
    }

    /// <summary>
    /// ID существующего стиля по умолчанию
    /// </summary>
    /// 
    public int DefaultStileId
    {
      get => _defaultStileId;
      set
      {
        _defaultStileId = value;
      }
    }

    /// <summary>
    /// Текущий образ активных стилей (не более 3)
    /// </summary>
    public BehaviorStyle[] ActiveStyles { get; private set; } = new BehaviorStyle[3];

    /// <summary>
    /// Событие удаления стиля поведения
    /// </summary>
    public event Action<int> StyleDeleted;

    private readonly HomeostasisCalculator _calculator;
    private readonly AgentState _agentState = new AgentState();
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    #region Детектор гомео-новизны

    // детектор новизны гомео-состояния
    private HomeostasisOverallState _previousOverallState;
    private List<int> _previousActiveStyleIds = new List<int>();
    private bool _isNewConditions = false;

    /// <summary>
    /// Флаг новых гомео-условий: изменилось интегральное состояние или активные стили
    /// </summary>
    internal bool IsNewConditions
    {
      get => _isNewConditions;
      set => _isNewConditions = value;
    }

    /// <summary>
    /// Обновляет детектор новизны на основе изменений гомео-состояния
    /// </summary>
    private void UpdateNoveltyDetector(HomeostasisOverallState previousOverallState, List<int> previousActiveStyleIds)
    {
      var currentOverallState = _currentOverallState;
      var currentActiveStyleIds = ActiveStyles
          .Where(style => style != null)
          .Select(style => style.Id)
          .OrderBy(id => id)
          .ToList();

      bool overallStateChanged = currentOverallState != previousOverallState;
      bool activeStylesChanged = !currentActiveStyleIds.SequenceEqual(previousActiveStyleIds);
      bool holdingEnded = false;

      // Проверяем все параметры на окончание удержания
      foreach (var param in _agentState.Parameters)
      {
        if (param.LastState != ParameterState.Normal &&
            param.LastStateChangePulse.HasValue)
        {
          int pulsesSinceChange = GlobalTimer.GlobalPulsCount - param.LastStateChangePulse.Value;
          if (pulsesSinceChange >= _dynamicTime && !_calculator.IsChainActive)
          {
            holdingEnded = true;
            break;
          }
        }
      }

      IsNewConditions = overallStateChanged || activeStylesChanged || holdingEnded;

      _previousOverallState = currentOverallState;
      _previousActiveStyleIds = currentActiveStyleIds;
    }

    #endregion

    /// <summary>
    /// Информация о состоянии агента
    /// </summary>
    public class AgentStateInfo
    {
      /// <summary>
      ///  Имя агента
      /// </summary>
      public string Name { get; set; }

      /// <summary>
      /// Описание агента
      /// </summary>
      public string Description { get; set; }

      /// <summary>
      /// Флаг, указывающий находится ли агент в состоянии сна
      /// </summary>
      public bool IsSleeping { get; set; }

      /// <summary>
      /// Флаг, указывающий является ли агент мертвым
      /// </summary>
      public bool IsDead { get; set; }

      /// <summary>
      /// Время жизни агента в пульсах
      /// </summary>
      public int Lifetime { get; set; }

      /// <summary>
      /// Текущая стадия эволюции агента (от 0 до 5)
      /// </summary>
      public int EvolutionStage { get; set; }

      /// <summary>
      /// Текущий уровень боли агента (0-100)
      /// </summary>
      public int PainValue { get; set; }

      /// <summary>
      /// Текущий уровень радости агента (0-100)
      /// </summary>
      public int JoyValue { get; set; }

      /// <summary>
      /// Флаг первого пульса агента
      /// </summary>
      public bool IsFirstPulse { get; set; }

      /// <summary>
      ///  Интегральное состояние агента
      /// </summary>
      public HomeostasisOverallState OverallState { get; set; }

      /// <summary>
      /// Полный словарь всех поведенческих стилей агента
      /// </summary>
      /// <remarks>
      /// Ключ - ID стиля, значение - информация о стиле поведения
      /// </remarks>
      public ReadOnlyDictionary<int, BehaviorStyle> AllBehaviorStyles { get; set; }

      /// <summary>
      /// Список текущих активных стилей поведения (максимум 3)
      /// </summary>
      public IReadOnlyList<BehaviorStyle> ActiveStyles { get; set; }
    }

    /// <summary>
    /// Получает полный словарь всех поведенческих стилей агента
    /// </summary>
    /// <returns>
    /// ReadOnlyDictionary где ключ - ID стиля, значение - информация о стиле поведения.
    /// Возвращает только для чтения коллекцию всех стилей.
    /// </returns>
    public ReadOnlyDictionary<int, BehaviorStyle> GetAllBehaviorStyles()
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyDictionary<int, BehaviorStyle>(_agentState.BehaviorStyles);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает список текущих активных стилей поведения агента
    /// </summary>
    /// <returns>
    /// ReadOnlyCollection содержащий до 3 активных стилей поведения. 
    /// </returns>
    public IReadOnlyList<BehaviorStyle> GetActiveStyles()
    {
      _lock.EnterReadLock();
      try
      {
        return ActiveStyles.Where(s => s != null).ToList().AsReadOnly();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Инициализация параметров гомеостаза

    /// <summary>
    /// каталог параметров гомеостаза
    /// </summary>
    private void EnsureDataDirectory()
    {
      if (!Directory.Exists(GomeostasFolderPath))
      {
        Directory.CreateDirectory(GomeostasFolderPath);
      }
    }

    #endregion

    #region Управление параметрами

    private void CheckForCriticalState()
    {
      try
      {
        foreach (var param in _agentState.Parameters)
        {
          if (!param.IsVital) continue;

          if (param.Value <= param.CriticalMinValue || param.Value >= param.CriticalMaxValue)
          {
            Logger.Warning($"КРИТИЧЕСКОЕ СОСТОЯНИЕ - параметр '{param.Name}' = {param.Value} (min={param.CriticalMinValue}, max={param.CriticalMaxValue})");
            _agentState.IsDead = true;
            OnAgentDeath(param);
            return;
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        throw;
      }
    }

    private void OnAgentDeath(ParameterData criticalParameter)
    {
      // Логируем причину смерти
      Logger.Info($"OnAgentDeath: Агент умер: параметр '{criticalParameter.Name}' " +
                            $"достиг критического значения {criticalParameter.Value}");

      // Можно добавить событие для внешних систем
      // AgentDied?.Invoke(this, criticalParameter);
    }

    /// <summary>
    /// Флаг статуса агента
    /// </summary>
    [Flags]
    public enum AgentCheck
    {
      /// <summary>
      /// Не определен
      /// </summary>
      None = 0,
      /// <summary>
      /// Существует
      /// </summary>
      Exists = 1,
      /// <summary>
      /// Жив
      /// </summary>
      NotDead = 2,
      /// <summary>
      /// Активен (не спит)
      /// </summary>
      NotSleeping = 4,
      /// <summary>
      /// Активен (пульсация включена)
      /// </summary>
      IsActive = 8
    }

    /// <summary>
    /// Проверка статуса агента без выброса исключений: существует, активен, жив
    /// Проверка параметра агента: существует ли параметр с таким ID
    /// </summary>
    public bool TryEnsureAgentState(AgentCheck checks, int? paramId = null, bool silent = false)
    {
      try
      {
        EnsureAgentState(checks, paramId);
        return true;
      }
      catch (InvalidOperationException ex) when (ex.Message.Contains("Агент мертв") || ex.Message.Contains("Агент спит"))
      {
        if (!silent)
          Logger.Error($"{ex.Message}");

        return false;
      }
      catch (Exception ex)
      {
        if (!silent)
          Logger.Error($"{ex.Message}");

        return false;
      }
    }

    /// <summary>
    /// Проверка статуса агента: существует, активен, жив
    /// Проверка параметра агента: существует ли параметр с таким ID
    /// </summary>
    public void EnsureAgentState(AgentCheck checks, int? paramId = null)
    {
      if ((checks & AgentCheck.NotDead) != 0 && _agentState.IsDead)
        throw new InvalidOperationException("Агент мертв");

      if ((checks & AgentCheck.NotSleeping) != 0 && _agentState.IsSleeping)
        throw new InvalidOperationException("Агент спит");

      if ((checks & AgentCheck.IsActive) != 0 && _agentState.IsFirstPulse)
        throw new InvalidOperationException("Агент неактивен");

      if ((checks & AgentCheck.Exists) != 0)
      {
        if (paramId == null)
          throw new ArgumentNullException(nameof(paramId));
        var param = _agentState.GetParameter(paramId.Value);
        if (param == null)
          throw new KeyNotFoundException($"Параметр с ID {paramId} не найден");
      }
    }

    /// <summary>
    /// Получить свойства агентов
    /// </summary>
    /// <summary>
    /// Получить свойства агента
    /// </summary>
    public AgentStateInfo GetAgentState()
    {
      AgentHomeostasisState homeostasisState = null;

      // Сначала получаем состояние гомеостаза
      _lock.EnterReadLock();
      try
      {
        var lastWellStatePulse = _agentState.LastWellStatePulse;

        homeostasisState = _calculator.CalculateAgentState(
            _agentState.Parameters,
            _dynamicTime,
            _difSensorPar,
            ref lastWellStatePulse,
            _compareLevel);

        _agentState.LastWellStatePulse = lastWellStatePulse;
      }
      finally
      {
        _lock.ExitReadLock();
      }

      // Теперь получаем остальные данные агента
      _lock.EnterReadLock();
      try
      {
        return new AgentStateInfo
        {
          Name = _agentState.Name,
          Description = _agentState.Description,
          IsSleeping = _agentState.IsSleeping,
          IsDead = _agentState.IsDead,
          Lifetime = _agentState.Lifetime,
          EvolutionStage = _agentState.EvolutionStage,
          PainValue = _agentState.PainValue,
          JoyValue = _agentState.JoyValue,
          IsFirstPulse = _agentState.IsFirstPulse,
          OverallState = homeostasisState.OverallState,
          AllBehaviorStyles = new ReadOnlyDictionary<int, BehaviorStyle>(_agentState.BehaviorStyles),
          ActiveStyles = ActiveStyles.Where(s => s != null).ToList().AsReadOnly()
        };
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }
    
    /// <summary>
    /// Добавляет новый параметр гомеостаза
    /// </summary>
    public (int ParamId, string[] Warnings) AddParameter(
        string name, string description,
        float initialValue, int weight, 
        int normaWell, int speed,
        bool _isVital = false,
        float _criticalMinValue = 0f,
        float _criticalMaxValue = 100f,
        bool strictValidation = false)
    {
      if (_agentState.EvolutionStage > 0)
        throw new InvalidOperationException("Работа с параметрами разрешена только в стадии 0");

      if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Наименование параметра не может быть пустым", nameof(name));

      var warnings = new List<string>();

      ValidateParameter("Начальное значение", initialValue, 0, 100, warnings, strictValidation);
      ValidateParameter("Вес параметра", weight, 0, 100, warnings, strictValidation);
      ValidateParameter("Порог нормы", normaWell, 0, 100, warnings, strictValidation);
      ValidateParameter("Скорость изменения", speed, -10, 10, warnings, strictValidation);

      _lock.EnterWriteLock();
      try
      {
        EnsureAgentState(AgentCheck.NotDead);

        int newId = (_agentState.Parameters.Count == 0)
            ? 1
            : _agentState.Parameters.Max(p => p.Id) + 1;

        var param = new ParameterData(
            id: newId,
            name: name,
            description: description,
            value: ClampFloat(initialValue, 0, 100),
            weight: ClampInt(weight, 0, 100),
            normaWell: ClampInt(normaWell, 0, 100),
            speed: ClampInt(speed, -10, 10),
            isVital: _isVital,
            criticalMinValue: _criticalMinValue,
            criticalMaxValue: _criticalMaxValue);

        _agentState.Parameters.Add(param);
        _agentState.LastParameterId = newId;

        return (newId, warnings.ToArray());
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Удаление параметра
    /// </summary>
    public void RemoveParameter(int paramId)
    {
      if (_agentState.EvolutionStage > 0)
        throw new InvalidOperationException("Работа с параметрами разрешена только в стадии 0");

      _lock.EnterWriteLock();
      try
      {
        EnsureAgentState(AgentCheck.Exists, paramId);

        var parameter = _agentState.GetParameter(paramId);
        if (parameter != null && parameter.IsVital)
          throw new InvalidOperationException($"Параметр '{parameter.Name}' является системным, его нельзя удалять");
        
        _agentState.RemoveParameter(paramId);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обновление параметра
    /// </summary>
    public void UpdateParameter(ParameterData parameter)
    {
      _lock.EnterWriteLock();
      try
      {
        EnsureAgentState(AgentCheck.NotDead | AgentCheck.Exists, parameter.Id);
        _agentState.UpdateParameter(parameter);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    internal ParameterData GetParameter(int paramId)
    {
      try
      {
        return _agentState.GetParameter(paramId);
      }
      catch
      {
        return null;
      }
    }

    /// <summary>
    /// Получение всех параметров
    /// </summary>
    public List<ParameterData> GetAllParameters()
    {
      _lock.EnterReadLock();
      try
      {
        return new List<ParameterData>(_agentState.Parameters);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Управление состоянием агента

    /// <summary>
    /// Установить флаг сна
    /// </summary>
    public void SetSleepState(bool isSleeping)
    {
      _lock.EnterWriteLock();
      try
      {
        EnsureAgentState(AgentCheck.NotDead | AgentCheck.NotSleeping);
        _agentState.IsSleeping = isSleeping;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Установить время жизни агента
    /// </summary>
    public void SetLifeTime(int lifeTime)
    {
      _lock.EnterWriteLock();
      try
      {
        EnsureAgentState(AgentCheck.NotDead);
        _agentState.Lifetime = lifeTime;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Устанавливает имя агента
    /// </summary>
    public void SetAgentName(string name)
    {
      _lock.EnterWriteLock();
      try
      {
        EnsureAgentState(AgentCheck.NotDead);
        _agentState.Name = name;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Устанавливает описание агента
    /// </summary>
    public void SetAgentDescription(string description)
    {
      _lock.EnterWriteLock();
      try
      {
        EnsureAgentState(AgentCheck.NotDead);
        _agentState.Description = description;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Установить стадию развития агента
    /// </summary>
    public void SetEvolutionStage(int stage)
    {
      var result = SetEvolutionStageWithValidation(stage);
      if (!result.Success)
        throw new InvalidOperationException(result.Message);
    }

    /// <summary>
    /// Установить стадию развития агента с проверкой последовательности
    /// </summary>
    public (bool Success, string Message) SetEvolutionStageWithValidation(int newStage, bool force = false)
    {
      _lock.EnterWriteLock();
      try
      {
        EnsureAgentState(AgentCheck.NotDead);

        int currentStage = _agentState.EvolutionStage;

        // Проверка на попытку перепрыгнуть через стадию вперед
        if (newStage > currentStage + 1)
          return (false, $"Недопустимый переход! Можно переходить только на следующую стадию (с {currentStage} на {currentStage + 1}).");

        // Проверка на возврат на предыдущую стадию
        if (newStage < currentStage && !force)
          return (false, $"Внимание! Возврат на предыдущую стадию ({newStage}) приведет к очистке данных всех последующих стадий (с {newStage + 1} по 5). Продолжить?");

        // Если force = true или это обычный переход - выполняем очистку и установку
        if (newStage < currentStage)
          ClearSubsequentStagesData(newStage);

        _agentState.EvolutionStage = newStage;
        return (true, "Стадия успешно изменена");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Очистка данных последующих стадий
    /// </summary>
    private void ClearSubsequentStagesData(int currentStage)
    {
      try
      {
        for (int stage = currentStage + 1; stage <= 5; stage++)
        {
          ClearStageData(stage);
        }
      }
      catch (Exception ex)
      {
        throw new InvalidOperationException("Ошибка очистки данных стадий", ex);
      }
    }

    /// <summary>
    /// Очистка данных конкретной стадии
    /// </summary>
    private void ClearStageData(int stage)
    {
      switch (stage)
      {
        case 1:
          ClearConditionedReflexes();
          break;
        case 2:

          break;
        case 3:

          break;
        case 4:

          break;
        case 5:

          break;
      }
    }

    /// <summary>
    /// Очищает все условные рефлексы
    /// </summary>
    private void ClearConditionedReflexes()
    {
      try
      {
        // TODO: Убрать Instance. Очистка только  после проверки, что у-рефлексы есть, чтобы не плодить пустых сообщений
        if (ConditionedReflexesSystem.IsInitialized)
        {
          _conditionedReflexesSystem = ConditionedReflexesSystem.Instance;
          if (_conditionedReflexesSystem != null)
          {
            _conditionedReflexesSystem.removeAllConditionedReflexes = true;
            _conditionedReflexesSystem.RemoveAllConditionedReflexes();
            _conditionedReflexesSystem.removeAllConditionedReflexes = false;
            var result = _conditionedReflexesSystem.SaveConditionedReflexes();
            if (!result.Success)
              Logger.Warning($"{result.ErrorMessage}");
            else
              Logger.Info("Условные рефлексы успешно очищены");
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
      }
    }

    internal AgentHomeostasisState GetHomeostasisStateInternal()
    {
      var lastWellStatePulse = _agentState.LastWellStatePulse;
      var result = _calculator.CalculateAgentState(
          _agentState.Parameters,
          _dynamicTime,
          _difSensorPar,
          ref lastWellStatePulse,
          _compareLevel);

      return result;
    }

    /// <summary>
    /// Определяет интегральное состояние агента на основе его параметров гомеостаза
    /// </summary>
    /// <summary>
    /// Определяет интегральное состояние агента на основе его параметров гомеостаза
    /// </summary>
    public AgentHomeostasisState GetHomeostasisState()
    {
      _lock.EnterReadLock();
      try
      {
        return GetHomeostasisStateInternal();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Управление стилями реагирования

    /// <summary>
    /// Добавляет новый стиль поведения агента
    /// </summary>
    /// <param name="name">Наименование стиля</param>
    /// <param name="description">Описание стиля</param>
    /// <param name="antagonistStyles">Список ID стилей-антагонистов</param>
    /// <param name="strictValidation">Флаг строгой проверки параметров</param>
    /// <returns>ID созданного стиля и предупреждения (если есть)</returns>
    public (int StyleId, string[] Warnings) AddBehaviorStyle(
        string name,
        string description,
        List<int> antagonistStyles = null,
        bool strictValidation = false)
    {
      if (_agentState.EvolutionStage > 0)
        throw new InvalidOperationException("Работа со стилями реагирования разрешена только в стадии 0");

      if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Наименование стиля не может быть пустым", nameof(name));

      var warnings = new List<string>();

      _lock.EnterWriteLock();
      try
      {
        EnsureAgentState(AgentCheck.NotDead);
        int newId = _agentState.BehaviorStyles.Count == 0
          ? 1
          : _agentState.BehaviorStyles.Keys.Max() + 1;

        var style = new BehaviorStyle
        {
          Id = newId,
          Name = name,
          Description = description,
          AntagonistStyles = antagonistStyles ?? new List<int>()
        };

        _agentState.BehaviorStyles.Add(newId, style);
        _agentState.LastBehaviorStylesId = newId;

        BuildStyleIndexes();

        return (newId, warnings.ToArray());
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Удаляет стиль поведения агента по указанному ID
    /// </summary>
    /// <param name="styleId">ID стиля для удаления</param>
    /// <returns>True, если стиль был успешно удален, иначе False</returns>
    public bool RemoveBehaviorStyle(int styleId)
    {  
      if (_agentState.EvolutionStage > 0)
        throw new InvalidOperationException("Работа со стилями реагирования разрешена только в стадии 0");

      if (!_agentState.BehaviorStyles.ContainsKey(styleId))
        throw new InvalidOperationException($"Стиль c ID: {styleId} не найден.");

      if (styleId == _defaultStileId)
        throw new InvalidOperationException($"Стиль {_agentState.BehaviorStyles[styleId].Name} задан стилем по умолчанию и запрещён для удаления.");

      if (IsStyleUsedInBehaviorStyleImages(styleId))
        throw new InvalidOperationException($"Стиль {_agentState.BehaviorStyles[styleId].Name} (ID: {styleId}) используется в образах стилей поведения и не может быть удален");

      string errorMessage = string.Empty;

      if (!ValidateAgentBehaviorStyles(new[] { _agentState.BehaviorStyles[styleId] }, out errorMessage, true))
        throw new InvalidOperationException(errorMessage);

      EnsureAgentState(AgentCheck.NotDead);

      _lock.EnterWriteLock();
      try
      {
        // Удаляем стиль из коллекции
        bool removed = _agentState.BehaviorStyles.Remove(styleId);

        // Если стиль был активным, удаляем его из активных стилей
        for (int i = 0; i < ActiveStyles.Length; i++)
        {
          if (ActiveStyles[i] != null && ActiveStyles[i].Id == styleId)
            ActiveStyles[i] = null;
        }

        // БЫСТРОЕ УДАЛЕНИЕ ЧЕРЕЗ ИНДЕКСЫ:
        // Удаляем из антагонистов других стилей
        if (_styleAntagonistsIndex.ContainsKey(styleId))
        {
          foreach (var style in _styleAntagonistsIndex[styleId])
          {
            if (style.AntagonistStyles.Contains(styleId))
              style.AntagonistStyles.Remove(styleId);
          }
          _styleAntagonistsIndex.Remove(styleId);
        }

        // Удаляем активации из параметров
        if (_styleActivationsIndex.ContainsKey(styleId))
        {
          foreach (var param in _styleActivationsIndex[styleId])
          {
            foreach (var activation in param.StyleActivations.Values)
            {
              activation.Remove(styleId); // Удаляем прямые активации
              activation.Remove(-styleId); // Удаляем деактивации
            }
          }
          _styleActivationsIndex.Remove(styleId);
        }

        // Вызываем событие удаления стиля для подписчиков - для каскадного удаления в них зависимостей
        StyleDeleted?.Invoke(styleId);

        return removed;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обновляет активные стили поведения на основе текущего состояния параметров
    /// </summary>
   internal void UpdateActiveStyles(bool pulsAdd = false)
    {
      var (dominant_param, dominantZone, dominanceScore) = _calculator.FindDominantParameter(
        _agentState.Parameters, _dynamicTime, _difSensorPar);

      var baseStyles = GetBaseActiveStyles(dominant_param, dominantZone);

      var (finalStylesWithWeights, activations, parameterActivations, dominantParam) =
              _calculator.GetFinalActiveStyles(baseStyles, _agentState.Parameters, _dynamicTime, _difSensorPar);

      UpdateDominantParameter(dominantParam);

      // Преобразуем обратно в обычные стили для ActiveStyles
      var finalStyles = finalStylesWithWeights.Select(sw => sw.Style).ToList();

      if (finalStyles == null || finalStyles.Count == 0)
      {
        if (_agentState.BehaviorStyles.TryGetValue(_defaultStileId, out var stuporStyle))
          finalStyles = new List<BehaviorStyle> { stuporStyle };
      }

      Array.Clear(ActiveStyles, 0, ActiveStyles.Length);
      int i = 0;
      foreach (var style in finalStyles)
      {
        if (i >= ActiveStyles.Length) break;
        ActiveStyles[i++] = style;
      }

      var finalStylesForLogs = finalStylesWithWeights.Select(sw => new BehaviorStyle
      {
        Id = sw.Style.Id,
        Name = sw.Style.Name,
      }).ToList();

      _researchLogger?.LogStylesActivationProcess(pulsAdd ? PulseCount + 1 : PulseCount, finalStylesForLogs, activations, parameterActivations);
      CreateBehaviorStyleImageFromActiveStyles();
    }

    /// <summary>
    /// Обновляет флаги доминирующих параметров
    /// </summary>
    /// <param name="dominantParam">Текущий доминирующий параметр</param>
    private void UpdateDominantParameter(ParameterData dominantParam)
    {
      foreach (var param in _agentState.Parameters)
      {
        param.IsDominant = false;
      }

      if (dominantParam != null)
        dominantParam.IsDominant = true;
    }

    /// <summary>
    /// ID созданного нового или найденного образа стилей реагирования
    /// </summary>
    internal int ActiveBehaviorStyleImageId = 0;

    /// <summary>
    /// Создает образ стилей поведения из текущих активных стилей и возвращает его ID
    /// </summary>
    /// <returns>ID образа стилей поведения (0 в случае ошибки)</returns>
    private int CreateBehaviorStyleImageFromActiveStyles()
    {
      if (_perceptionImagesSystem == null)
        return 0;

      var activeStyleIds = ActiveStyles
          .Where(style => style != null)
          .Select(style => style.Id)
          .ToList();

      if (!activeStyleIds.Any())
        return 0;

      try
      {
        int imageId = _perceptionImagesSystem.AddBehaviorStyleImage(activeStyleIds);
        if (imageId != ActiveBehaviorStyleImageId)
        {
          ActiveBehaviorStyleImageId = imageId;
          SaveBehaviorStyleImagesWithRetry();
        }

        return imageId;
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        return 0;
      }
    }

    /// <summary>
    /// Сохранение образов стилей с повторными попытками при ошибках
    /// </summary>
    private void SaveBehaviorStyleImagesWithRetry(int maxRetries = 2)
    {
      for (int attempt = 1; attempt <= maxRetries; attempt++)
      {
        try
        {
          var result = _perceptionImagesSystem.SaveBehaviorStyleImages();
          if (result.Success)
            break;

          if (attempt == maxRetries)
            Logger.Warning($"{result.ErrorMessage}");
          else
            Thread.Sleep(100 * attempt); // Увеличивающаяся задержка
        }
        catch (Exception ex)
        {
          if (attempt == maxRetries)
            Logger.Error($"{ex.Message}");
        }
      }
    }

    private List<BehaviorStyle> GetBaseActiveStyles(ParameterData dominantParam, int dominantZone)
    {
      var activeStyles = new List<BehaviorStyle>();
      var styleIds = new List<int>();

      if (dominantParam != null)
      {
        // Получаем стили только от доминирующего параметра
        styleIds = GetActivationRule(dominantParam.Id, dominantZone);
        if (styleIds.Any())
          ApplyActivationRule(styleIds, activeStyles);
      }

      if (dominantParam == null || !styleIds.Any())
      {
        if (_agentState.BehaviorStyles.TryGetValue(_defaultStileId, out var defaultStyle))
          activeStyles.Add(defaultStyle);
      }

      return activeStyles;
    }

    private List<int> GetActivationRule(int paramId, int stateId)
    {
      // Находим параметр по ID
      var param = _agentState.Parameters.FirstOrDefault(p => p.Id == paramId);
      if (param != null && param.StyleActivations.TryGetValue(stateId, out var styleIds))
      {
        return styleIds;
      }

      return new List<int>();
    }

    private void ApplyActivationRule(List<int> styleIds, List<BehaviorStyle> activeStyles)
    {
      if (styleIds == null || !styleIds.Any()) return;

      foreach (var styleId in styleIds)
      {
        if (styleId > 0) // Активация стиля
        {
          if (_agentState.BehaviorStyles.TryGetValue(styleId, out var style) && !activeStyles.Any(s => s.Id == styleId))
            activeStyles.Add(style);
        }
        else // Деактивация стиля (отрицательный ID)
        {
          var styleToRemove = activeStyles.FirstOrDefault(s => s.Id == Math.Abs(styleId));
          if (styleToRemove != null)
            activeStyles.Remove(styleToRemove);
        }
      }
    }

    #endregion

    #region Управление комбинациями стилей

    /// <summary>
    /// Генерирует все возможные комбинации стилей реагирования с учетом антагонистов и латерального торможения
    /// </summary>
    /// <param name="forceRegenerate">Принудительная генерация новых комбинаций</param>
    /// <returns>Список валидных комбинаций стилей</returns>
    public List<List<BehaviorStyle>> GenerateStyleCombinations(bool forceRegenerate = false)
    {
      return _styleCombinationsManager.GenerateStyleCombinations(forceRegenerate);
    }

    /// <summary>
    /// Загружает комбинации стилей из файла
    /// </summary>
    /// <returns>Список загруженных комбинаций стилей</returns>
    public List<List<BehaviorStyle>> LoadStyleCombinations()
    {
      return _styleCombinationsManager.LoadStyleCombinations();
    }

    /// <summary>
    /// Сохраняет комбинации стилей в файл
    /// </summary>
    /// <param name="combinations">Список комбинаций для сохранения</param>
    /// <returns>Результат операции сохранения</returns>
    public (bool Success, string ErrorMessage) SaveStyleCombinations(List<List<BehaviorStyle>> combinations)
    {
      return _styleCombinationsManager.SaveStyleCombinations(combinations);
    }

    #endregion

    #region Управление адаптивными действиями

    /// <summary>
    /// Возвращает список текущих активных адаптивных действий.
    /// </summary>
    /// <returns>Список действий</returns>
    public List<AdaptiveAction> GetActiveAdaptiveActionsList()
    {
      return AdaptiveActionsSystem.Instance.GetActiveAdaptiveActionsList();
    }

    /// <summary>
    /// Возвращает список всех адаптивных действий.
    /// </summary>
    /// <returns>Список действий</returns>
    public List<AdaptiveAction> GetAllActions()
    {
      return AdaptiveActionsSystem.Instance.GetAllAdaptiveActionsList();
    }

    #endregion

    #region Работа с файлами

    private Dictionary<int, List<int>> ParseStyleActivations(string data)
    {
      var result = new Dictionary<int, List<int>>()
    {
        {0, new List<int>()}, {1, new List<int>()}, {2, new List<int>()},
        {3, new List<int>()}, {4, new List<int>()}, {5, new List<int>()}, {6, new List<int>()}
    };

      if (string.IsNullOrWhiteSpace(data)) return result;

      var stateParts = data.Split(';');
      foreach (var statePart in stateParts)
      {
        var keyValue = statePart.Split(':');
        if (keyValue.Length == 2 && int.TryParse(keyValue[0], out int stateId))
        {
          var styleIds = keyValue[1].Split(',')
              .Where(s => !string.IsNullOrWhiteSpace(s))
              .Select(int.Parse)
              .ToList();

          if (result.ContainsKey(stateId))
            result[stateId] = styleIds;
        }
      }

      return result;
    }

    private string StyleActivationsToStr(Dictionary<int, List<int>> activations)
    {
      return string.Join(";", activations
          .Where(kv => kv.Value.Any())
          .Select(kv => $"{kv.Key}:{string.Join(",", kv.Value)}"));
    }

    /// <summary>
    /// Загружает стили из файла в указанный словарь
    /// </summary>
    private void LoadStylesFromFile(string filePath, Dictionary<int, BehaviorStyle> targetDictionary)
    {
      foreach (var line in File.ReadLines(filePath))
      {
        if (string.IsNullOrWhiteSpace(line)) continue;
        if (line.StartsWith("#")) continue;

        var parts = line.Split('|');
        if (parts.Length >= 3)
        {
          var style = new BehaviorStyle
          {
            Id = int.Parse(parts[0]),
            Name = parts[1],
            Description = parts[2],
          };

          // Загрузка антагонистов
          if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
          {
            style.AntagonistStyles = parts[3].Split(',')
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(int.Parse)
                .ToList();
          }

          targetDictionary[style.Id] = style;
        }
      }
    }

    private void LoadAgentData()
    {
      LoadAgentProperties();
      LoadAgentParameters();
      LoadAgentBehaviorStyles();
    }

    private void LoadAgentProperties()
    {
      var path = GetAgentPropertiesPath();

      try
      {
        if (!IsValidAgentPropertiesFile(path))
        {
          // Устанавливаем значения по умолчанию
          _agentState.Name = DefaultAgentName;
          _agentState.Description = DefaultAgentDescription;
          _agentState.IsSleeping = false;
          _agentState.IsDead = false;
          _agentState.Lifetime = 0;
          _agentState.EvolutionStage = 0;
          _agentState.PainValue = 0;
          _agentState.JoyValue = 0;

          _difSensorPar = 2;
          _compareLevel = 100;
          _dynamicTime = 50;

          SaveAgentProperties();
          return;
        }

        var lines = File.ReadAllLines(path);
        StringBuilder descriptionBuilder = null;

        for (int i = 0; i < lines.Length; i++)
        {
          var line = lines[i];
          if (string.IsNullOrWhiteSpace(line)) continue;
          if (line.StartsWith("#")) continue;

          var parts = line.Split('|');
          if (parts.Length >= 2)
          {
            if (parts[0] == "Description" && !string.IsNullOrEmpty(parts[1]))
            {
              // Начинаем сбор многострочного описания
              descriptionBuilder = new StringBuilder();
              descriptionBuilder.Append(parts[1]);

              // Проверяем следующие строки на продолжение описания
              for (int j = i + 1; j < lines.Length; j++)
              {
                var nextLine = lines[j];
                if (string.IsNullOrWhiteSpace(nextLine))
                {
                  descriptionBuilder.AppendLine();
                  continue;
                }
                if (nextLine.StartsWith("#") || nextLine.Contains("|"))
                  break; // Новое свойство или комментарий
                descriptionBuilder.AppendLine().Append(nextLine.Trim());
              }
              _agentState.Description = descriptionBuilder.ToString();
              continue;
            }

            if (parts[0] == "Name" && !string.IsNullOrEmpty(parts[1]))
              _agentState.Name = parts[1];
            else if (parts[0] == "IsSleeping" && bool.TryParse(parts[1], out bool isSleeping))
              _agentState.IsSleeping = isSleeping;
            else if (parts[0] == "IsDead" && bool.TryParse(parts[1], out bool isDead))
              _agentState.IsDead = isDead;
            else if (parts[0] == "Lifetime" && int.TryParse(parts[1], out int lifetime))
              _agentState.Lifetime = lifetime;
            else if (parts[0] == "EvolutionStage" && int.TryParse(parts[1], out int stage))
              _agentState.EvolutionStage = stage;
            else if (parts[0] == "PainValue" && int.TryParse(parts[1], out int pain))
              _agentState.PainValue = pain;
            else if (parts[0] == "JoyValue" && int.TryParse(parts[1], out int joy))
              _agentState.JoyValue = joy;
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
      }
    }

    private void LoadAgentParameters()
    {
      var path = GetAgentParametersPath();

      try
      {
        _agentState.Parameters.Clear();

        if (!IsValidAgentParametersFile(path))
        {
          EnsureDataDirectory();
          var lines = new List<string>
            {
              FileHeaders.ParametersFormat,
              FileHeaders.ParametersActivations
            };

          File.WriteAllLines(path, lines);
          return;
        }

        foreach (var line in File.ReadLines(path))
        {
          if (string.IsNullOrWhiteSpace(line)) continue;
          if (line.StartsWith("#") && !line.StartsWith("# Формат:")) continue;

          var parts = line.Split('|');
          if (parts.Length >= 11 && int.TryParse(parts[0], out int paramId))
          {
            var param = new ParameterData(
                id: paramId,
                name: parts[1],
                description: parts[2],
                value: float.Parse(parts[3], CultureInfo.InvariantCulture),
                weight: int.Parse(parts[4]),
                normaWell: int.Parse(parts[5]),
                speed: int.Parse(parts[6]),
                isVital: bool.Parse(parts[8].Trim()),
                criticalMinValue: float.Parse(parts[9].Trim(), CultureInfo.InvariantCulture),
                criticalMaxValue: float.Parse(parts[10].Trim(), CultureInfo.InvariantCulture)
            );

            // Загрузка активаций стилей
            if (parts.Length >= 8 && !string.IsNullOrWhiteSpace(parts[7]))
              param.StyleActivations = ParseStyleActivations(parts[7]);

            _agentState.Parameters.Add(param);

            if (paramId > _agentState.LastParameterId)
              _agentState.LastParameterId = paramId;
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
      }
    }

    /// <summary>
    /// Загружает стили поведения агента из файла данных.
    /// Если файл отсутствует или повреждён — инициализирует стили из шаблона.
    /// </summary>
    private void LoadAgentBehaviorStyles()
    {
      var path = GetAgentStylesFilePath();
      try
      {
        if (File.Exists(path))
        {
          var lines = File.ReadAllLines(path);
          if (IsValidStyleFile(path))
          {
            _agentState.BehaviorStyles.Clear();
            LoadStylesFromFile(path, _agentState.BehaviorStyles);
            BuildStyleIndexes();
            return;
          }
        }
        else
        {
          EnsureDataDirectory();
          var lines = new List<string>
            {
              FileHeaders.StylesFormat,
              FileHeaders.StylesAntagonis
            };

          File.WriteAllLines(path, lines);
          _agentState.BehaviorStyles.Clear();
          BuildStyleIndexes();
          return;
        }
      }
      catch (Exception initEx)
      {
        throw new InvalidOperationException("Ошибка при загрузке стилей реагирования агента", initEx);
      }
    }

    /// <summary>
    /// Сохранить свойства агента
    /// </summary>
    public (bool Success, string ErrorMessage) SaveAgentProperties()
    {
      try
      {
        EnsureDataDirectory();

        var lines = new List<string>
        {
          FileHeaders.PropertiesFormat,
            $"Name|{_agentState.Name}",
            $"Description|{_agentState.Description}",
            $"IsSleeping|{_agentState.IsSleeping}",
            $"IsDead|{_agentState.IsDead}",
            $"Lifetime|{_agentState.Lifetime}",
            $"EvolutionStage|{_agentState.EvolutionStage}",
            $"PainValue|{_agentState.PainValue}",
            $"JoyValue|{_agentState.JoyValue}"
        };

        var result = FileValidator.SafeSaveFile(
            GetAgentPropertiesPath(),
            lines,
            FileValidator.IsValidAgentPropertiesFile,
            minLinesCount: 3,
            fileDescription: "свойств агента");

        if (!result.Success)
        {
          Logger.Error($"{result.ErrorMessage}");
        }

        return result;
      }
      catch (Exception ex)
      {
        string error = $"{ex.Message}";
        Logger.Error(error);
        return (false, error);
      }
    }

    /// <summary>
    /// Сохранение жизненных параметров
    /// </summary>
    public (bool Success, string ErrorMessage) SaveAgentParameters(bool IsValidate = true)
    {
      // сохранение доступно на любой стадии - нужно ведь сохранять текущие значения
      try
      {
        string errorMessage = string.Empty;

        if (IsValidate)
        {
          if (!ValidateParameterIds(_agentState.Parameters, out errorMessage))
            return (false, errorMessage);
        }

        EnsureDataDirectory();

        var lines = new List<string>
        {
          FileHeaders.ParametersFormat,
          FileHeaders.ParametersActivations
        };

        foreach (var param in _agentState.Parameters.OrderBy(p => p.Id))
        {
          var activationsStr = StyleActivationsToStr(param.StyleActivations);

          lines.Add($"{param.Id}|{param.Name}|{param.Description}|" +
                   $"{param.Value.ToString(CultureInfo.InvariantCulture)}|{param.Weight}|" +
                   $"{param.NormaWell}|{param.Speed}|" +
                   $"{activationsStr}|" +
                   $"{param.IsVital}|" +
                   $"{param.CriticalMinValue.ToString(CultureInfo.InvariantCulture)}|" +
                   $"{param.CriticalMaxValue.ToString(CultureInfo.InvariantCulture)}");
        }

        var linCount = 4;
        if (lines.Count == 3)
          linCount = 3; // для случая очистки всего кроме шапки

        var result = SafeSaveFile(
            GetAgentParametersPath(),
            lines,
            IsValidAgentParametersFile,
            minLinesCount: linCount,
            fileDescription: "параметров агента");

        if (!result.Success)
          errorMessage = $"Ошибка сохранения параметров агента: {result.ErrorMessage}";

        return result;
      }
      catch (Exception ex)
      {
        string error = $"Критическая ошибка при сохранении параметров агента: {ex.Message}";
        return (false, error);
      }
    }

    /// <summary>
    /// Сохранение стилей реагирования
    /// </summary>
    public (bool Success, string ErrorMessage) SaveAgentBehaviorStyles(bool IsValidate = true)
    {
      if (_agentState.EvolutionStage > 0)
        throw new InvalidOperationException("Работа с параметрами разрешена только в стадии 0");

      try
      {
        string errorMessage = string.Empty;

        if (IsValidate)
        {
          if (!ValidateAgentBehaviorStyles(_agentState.BehaviorStyles.Values, out errorMessage))
            return (false, errorMessage);
        }

        EnsureDataDirectory();
        var lines = new List<string>
        {
          FileHeaders.StylesFormat,
          FileHeaders.StylesAntagonis
        };

        foreach (var style in _agentState.BehaviorStyles.Values.OrderBy(s => s.Id))
        {
          lines.Add($"{style.Id}|{style.Name}|{style.Description}|" +
                  $"{string.Join(",", style.AntagonistStyles)}");
        }

        var linCount = 4;
        if (lines.Count == 3)
          linCount = 3; // для случая очистки всего кроме шапки

        var result = SafeSaveFile(
            GetAgentStylesFilePath(),
            lines,
            IsValidStyleFile,
            minLinesCount: linCount,
            fileDescription: "стилей поведения агента");

        if (!result.Success)
          errorMessage = $"Ошибка сохранения стилей реагирования агента: {result.ErrorMessage}";

        return result;
      }
      catch (Exception ex)
      {
        return (false, $"Системная ошибка: {ex.Message}");
      }
    }

    /// <summary>
    /// Сохранение всех данных
    /// </summary>
    public (bool Success, string ErrorMessage) SaveAllData()
    {
      if (_agentState.EvolutionStage > 0)
        throw new InvalidOperationException("Работа с параметрами разрешена только в стадии 0");

      var errors = new List<string>();

      var (propsSuccess, propsError) = SaveAgentProperties();
      if (!propsSuccess) errors.Add($"Свойства агента: {propsError}");

      var (paramsSuccess, paramsError) = SaveAgentParameters();
      if (!paramsSuccess) errors.Add($"Параметры: {paramsError}");

      var (stylesSuccess, stylesError) = SaveAgentBehaviorStyles();
      if (!stylesSuccess) errors.Add($"Стили: {stylesError}");

      return (errors.Count == 0, errors.Count == 0 ? string.Empty : string.Join("\n", errors));
    }

    #endregion

    #region Вспомогательные методы

    /// <summary>
    /// приведение значения параметра в границы заданного диапазона
    /// </summary>
    private void ValidateParameter(string paramName, float value, float min, float max,
        List<string> warnings, bool strictValidation)
    {
      if (value < min || value > max)
      {
        string message = $"{paramName} скорректировано с {value} до {ClampFloat(value, min, max)} " +
                        $"(допустимый диапазон: {min}-{max})";

        if (strictValidation)
        {
          throw new ArgumentOutOfRangeException(paramName, value, message);
        }

        warnings.Add(message);
      }
    }

    /// <summary>
    /// приведение вещественного числа в границы заданного диапазона
    /// </summary>
    public static float ClampFloat(float value, float min, float max)
    {
      return value < min ? min : (value > max ? max : value);
    }

    /// <summary>
    /// приведение целого числа в границы заданного диапазона
    /// </summary>
    public static int ClampInt(int value, int min, int max)
    {
      return value < min ? min : (value > max ? max : value);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом GomeostasSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        SaveAllData();
        _styleCombinationsManager?.Dispose();
        _conditionedReflexesSystem?.Dispose();
        _perceptionImagesSystem?.Dispose();
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
      }
    }

    #endregion
  }
}