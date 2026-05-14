using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ISIDA.Common
{
  /// <summary>
  /// Класс валидации настроек полей классов
  /// </summary>
  public static class SettingsValidator
  {
     /// <summary>
    /// Валидация значений параметров
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateSetting(string settingName, object value)
    {
      switch (settingName)
      {
        case "RecognitionThreshold":
          return ValidateRecognitionThreshold((int)value);

        case "CompareLevel":
          return ValidateCompareLevel((int)value);

        case "DifSensorPar":
          return ValidateDifSensorPar((float)value);

        case "DynamicTime":
          return ValidateDynamicTime((int)value);

        case "LearningRate":
          return ValidateLearningRate((float)value);

        case "DecayRate":
          return ValidateDecayRate((float)value);

        case "ActivationThreshold":
          return ValidateActivationThreshold((float)value);

        case "TimeWindowPulses":
          return ValidateTimeWindowPulses((int)value);

        case "MinAssociationStrength":
          return ValidateMinAssociationStrength((float)value);

        case "HigherOrderStrengthReductionCoefficient":
          return ValidateHigherOrderStrengthReductionCoefficient((float)value);

        default:
          return (true, string.Empty);
      }
    }   
      
    #region Универсальные методы валидации

    /// <summary>
    /// Универсальная валидация числовых значений
    /// </summary>
    private static (bool isValid, string errorMessage) ValidateValue<T>(
        T? value, string paramName, T min, T max, string rangeDescription = "")
        where T : struct, IComparable<T>
    {
      if (value == null)
        return (false, $"{paramName} не может быть null. Допустимый диапазон: {rangeDescription}");

      if (value.Value.CompareTo(min) >= 0 && value.Value.CompareTo(max) <= 0)
        return (true, string.Empty);
      else
        return (false, $"{paramName} должна быть в диапазоне {rangeDescription}. Получено значение: {value}");
    }

    /// <summary>
    /// Универсальная валидация с пользовательским сообщением
    /// </summary>
    private static (bool isValid, string errorMessage) ValidateValueCustom<T>(
        T? value, string paramName, T min, T max, string range)
        where T : struct, IComparable<T>
    {
      return ValidateValue(value, paramName, min, max, range);
    }

    #endregion

    #region Условные рефлексы

    /// <summary>
    /// Валидация базового времени жизни без активации
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateBaseInactivationTime(int? value)
    {
      const string paramName = "Базовое время жизни без активации";
      const string range = "[100:10000] пульсов";
      return ValidateValueCustom(value, paramName, 100, 10000, range);
    }

    /// <summary>
    /// Валидация коэффициента обучения α
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateLearningRate(float? value)
    {
      const string paramName = "Коэффициент обучения";
      const string range = "[0.1:0.3]";
      return ValidateValueCustom(value, paramName, 0.1f, 0.3f, range);
    }

    /// <summary>
    /// Валидация коэффициента затухания η
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateDecayRate(float? value)
    {
      const string paramName = "Коэффициент затухания";
      const string range = "[0.95:0.99]";
      return ValidateValueCustom(value, paramName, 0.95f, 0.99f, range);
    }

    /// <summary>
    /// Валидация порога активации γ
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateActivationThreshold(float? value)
    {
      const string paramName = "Порог активации";
      const string range = "[0.5:0.7]";
      return ValidateValueCustom(value, paramName, 0.5f, 0.7f, range);
    }

    /// <summary>
    /// Валидация временного окна корреляции τ
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateTimeWindowPulses(int? value)
    {
      const string paramName = "Временное окно корреляции";
      const string range = "[1:10] пульсов";
      return ValidateValueCustom(value, paramName, 1, 10, range);
    }

    /// <summary>
    /// Валидация минимальной крепости связи C_min
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateMinAssociationStrength(float? value)
    {
      const string paramName = "Минимальная крепость связи";
      const string range = "[0.01:0.3]";
      return ValidateValueCustom(value, paramName, 0.01f, 0.3f, range);
    }

    /// <summary>
    /// Валидация коэффициента понижения крепости вторичных условных рефлексов
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateHigherOrderStrengthReductionCoefficient(float? value)
    {
      const string paramName = "Коэффициент понижения крепости вторичных";
      const string range = "[1.2:3.0]";
      return ValidateValueCustom(value, paramName, 1.2f, 3.0f, range);
    }

    #endregion

    #region Сенсорная система

    /// <summary>
    /// Валидация числа повторов сенсора для записи из песочницы в основную базу
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateRecognitionThreshold(int? value)
    {
      const string paramName = "Число повторов для записи сенсора";
      const string range = "[1:10]";
      return ValidateValueCustom(value, paramName, 1, 10, range);
    }

    #endregion

    #region Параметры гомеостаза

    /// <summary>
    /// Валидация интегрального порога состояния агента
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateCompareLevel(int? value)
    {
      const string paramName = "Интегральный порог состояния";
      const string range = "[1:99]%";
      return ValidateValueCustom(value, paramName, 1, 99, range);
    }

    /// <summary>
    /// Валидация минимального детектирования параметра гомеостаза
    /// </summary>
    /// <param name="value">Значение для валидации (float)</param>
    public static (bool isValid, string errorMessage) ValidateDifSensorPar(float? value)
    {
      const string paramName = "Величина детектирования параметра";
      const string range = "[0.01:2]";
      return ValidateValueCustom(value, paramName, 0.01f, 2.0f, range);
    }

    /// <summary>
    /// Валидация времени удержания состояний в сек
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateDynamicTime(int? value)
    {
      const string paramName = "Время удержания состояний";
      const string range = "[5:100] сек";
      return ValidateValueCustom(value, paramName, 5, 100, range);
    }

    /// <summary>
    /// Валидация значения влияния параметра на другие параметры
    /// </summary>
    /// <param name="value">Значение для валидации (float)</param>
    public static (bool isValid, string errorMessage) ValidateBadWellStateInfluence(float? value)
    {
      const string paramName = "Величина влияния на параметры";
      const string range = "[-1.0:1.0]";
      return ValidateValueCustom(value, paramName, -1.0f, 1.0f, range);
    }

    /// <summary>
    /// Валидация значений веса параметров гомеостаза
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateWeightParam(int? value)
    {
      const string paramName = "Величина значения веса параметра гомеостаза";
      const string range = "[0:100]";
      return ValidateValueCustom(value, paramName, 0, 100, range);
    }

    /// <summary>
    /// Валидация значений порога параметров гомеостаза
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateNormaWellParam(int? value)
    {
      const string paramName = "Величина порогового значения параметра гомеостаза";
      const string range = "[1:99]";
      return ValidateValueCustom(value, paramName, 1, 99, range);
    }

    /// <summary>
    /// Валидация скорости изменения параметров гомеостаза
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateSpeedParam(int? value)
    {
      const string paramName = "Величина скорости изменения параметра гомеостаза";
      const string range = "[-20:-1] или [1:20] (знак задаёт тип: дефицит / избыток; 0 недопустим)";
      var bounds = ValidateValueCustom(value, paramName, -20, 20, range);
      if (!bounds.isValid)
        return bounds;
      if (value.Value == 0)
        return (false, $"{paramName} не может быть 0: теряется тип параметра (дефицит vs избыток) и логика дрейфа/зон. Используйте отрицательное значение для дефицит-ориентированных или положительное для избыток-ориентированных параметров.");
      return (true, string.Empty);
    }

    /// <summary>
    /// Валидация значения параметра гомеостаза, а так же его минимальных и максимальных критических значений
    /// </summary>
    /// <param name="value">Значение параметра (float)</param>
    /// <param name="min_value">Значение критического минимума (float)</param>
    /// <param name="max_value">Значение критического максимума (float)</param>
    /// <param name="speed">Скорость убывания/нарастания параметра (int)</param>
    /// <param name="isSaveValidate">Флаг дополнительных проверок при попытке сохранить параметры через интерфейс. Не ставить для валидации в полях класса ParameterData. По умолчанию False</param>
    public static (bool isValid, string errorMessage) ValidateCriticalMinMaxValueParamValue(float? value, float? min_value, float? max_value, int? speed, bool isSaveValidate = false)
    {
      var errors = new List<string>();
      const string range = "[0.0:100.0]";

      var valueResult = ValidateValueCustom(value, "Текущее значение параметра", 0.0f, 100.0f, range);
      if (!valueResult.isValid)
        errors.Add(valueResult.errorMessage);

      var minResult = ValidateValueCustom(min_value, "Минимальное критическое значение", 0.0f, 100.0f, range);
      if (!minResult.isValid)
        errors.Add(minResult.errorMessage);

      var maxResult = ValidateValueCustom(max_value, "Максимальное критическое значение", 0.0f, 100.0f, range);
      if (!maxResult.isValid)
        errors.Add(maxResult.errorMessage);

      if (isSaveValidate)
      {
        if (speed.HasValue)
        {
          var speedResult = ValidateSpeedParam(speed);
          if (!speedResult.isValid)
            errors.Add(speedResult.errorMessage);
        }
        else
          errors.Add("Скорость изменения параметра не может быть null");

        if (value.HasValue && min_value.HasValue && max_value.HasValue && speed.HasValue && !errors.Any())
        {
          if (min_value.Value > max_value.Value)
            errors.Add($"Минимальное значение ({min_value}) не может быть больше максимального ({max_value})");
          else
          {
            if (speed.Value < 0)
            {
              // Дефицит-ориентированный параметр: value не может быть меньше min_value, но может быть <= max_value
              if (value.Value < min_value.Value)
                errors.Add($"Для параметра с отрицательной скоростью (дефицит-ориентированный) значение ({value}) не может быть меньше минимального критического ({min_value}). Допустимый диапазон: [{min_value}:{max_value}]");
              // value может быть любым вплоть до max_value включительно
            }
            else if (speed.Value > 0)
            {
              // Избыток-ориентированный параметр: value не может быть больше max_value, но может быть >= min_value
              if (value.Value > max_value.Value)
                errors.Add($"Для параметра с положительной скоростью (избыток-ориентированный) значение ({value}) не может быть больше максимального критического ({max_value}). Допустимый диапазон: [{min_value}:{max_value}]");
              // value может быть любым начиная от min_value включительно
            }
          }
        }
      }
      if (errors.Any())
        return (false, string.Join("; ", errors));

      return (true, string.Empty);
    }
    #endregion

    #region Адаптивные действия
    
    /// <summary>
    /// Валидация значения интенсивности действия
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateVigorAction(int? value)
    {
      const string paramName = "Величина интенсивности действие";
      const string range = "[1:10]";
      return ValidateValueCustom(value, paramName, 1, 10, range);
    }

    /// <summary>
    /// Валидация текущего значения интенсивности действия
    /// </summary>
    /// <param name="value1">Значение для валидации (int)</param>
    /// <param name="value2">Граничное значение для валидации (int)</param> 
    public static (bool isValid, string errorMessage) ValidateCurrentVigorAction(float? value1, int? value2)
    {
      const string paramName1 = "Величина текущей интенсивности действия";
      const string paramName2 = "Величина предела текущей интенсивности действия";
      string range1 = $"[0:{value2}]";
      string range2 = $"[{value1}:{value2}]";

      if (value1 == null)
        return (false, $"{paramName1} не может быть null. Допустимый диапазон: {range1}");

      if (value2 == null)
        return (false, $"{paramName2} не может быть null. Допустимый диапазон: {range2}");

      if (value1 >= 0 && value1 <= value2)
        return (true, string.Empty);
      else
        return (false, $"{paramName1} должна быть в диапазоне {range1}. Получено значение: {value1}");
    }

    /// <summary>
    /// Валидация коэффициента усталости действия
    /// </summary>
    /// <param name="value">Значение для валидации (float)</param>
    public static (bool isValid, string errorMessage) ValidateFatigueCoefficient(float? value)
    {
      const string paramName = "Величина коэффициента усталости";
      const string range = "[0.0:0.8]";
      return ValidateValueCustom(value, paramName, 0.0f, 0.8f, range);
    }

    /// <summary>
    /// Валидация коэффициента восстановления действия
    /// </summary>
    /// <param name="value">Значение для валидации (float)</param>
    public static (bool isValid, string errorMessage) ValidateRecoveryCoefficient(float? value)
    {
      const string paramName = "Величина коэффициента восстановления";
      const string range = "[0.01:1]";
      return ValidateValueCustom(value, paramName, 0.01f, 1f, range);
    }

    #endregion

    #region Внешнее воздействие

    /// <summary>
    /// Валидация значения влияния внешнего воздействия на параметры
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateInfluencesParametr(int? value)
    {
      const string paramName = "Величина влияния на параметры";
      const string range = "[-10:10]";
      return ValidateValueCustom(value, paramName, -10, 10, range);
    }

    /// <summary>
    /// Ключ пробы метрики среды в строке InfluenceActions (пустая строка допустима).
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateEnvironmentMetricProbeKey(string key)
    {
      if (string.IsNullOrWhiteSpace(key))
        return (true, string.Empty);

      string t = key.Trim();
      if (t.Length > 128)
        return (false, "EnvironmentMetricProbeKey: длина не более 128 символов после обрезки пробелов.");

      if (t.IndexOf('|') >= 0)
        return (false, "EnvironmentMetricProbeKey: символ «|» запрещён (разделитель полей файла).");

      foreach (char c in t)
      {
        if (char.IsControl(c))
          return (false, "EnvironmentMetricProbeKey: управляющие символы запрещены.");
      }

      return (true, string.Empty);
    }

    #endregion

    #region Цепочки автоматизмов и рефлексов

    /// <summary>
    /// Валидация и ограничение значения полезности звена цепочки
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    /// <returns>Ограниченное значение в диапазоне [-10:10]</returns>
    public static int ClampChainLinkUsefulness(int value)
    {
      return AddUtils.Clamp(value, -10, 10);
    }

    #endregion

    #region Шаблон каталогов проекта данных ISIDA

    /// <summary>
    /// Обязательные каталоги непосредственно в корне проекта данных (имена папок без разделителей).
    /// </summary>
    public static readonly string[] RequiredProjectRootFolderNames =
    {
      "Logs",
      "Data",
      "BootData",
      "Settings"
    };

    /// <summary>
    /// Возвращает текстовое описание дерева каталогов по умолчанию для документации и просмотра пользователем.
    /// </summary>
    /// <returns>Многострочное описание структуры.</returns>
    public static string GetProjectDirectoryTemplateText()
    {
      var sb = new StringBuilder();
      sb.AppendLine("Корень проекта данных ISIDA (шаблон каталогов):");
      sb.AppendLine("");
      sb.AppendLine("Logs");
      sb.AppendLine("BootData");
      sb.AppendLine("Settings");
      sb.AppendLine("  (рекомендуется размещать копию Settings.xml при переносе настроек между машинами)");
      sb.AppendLine("Data");
      sb.AppendLine("  Gomeostas");
      sb.AppendLine("  Actions");
      sb.AppendLine("  Sensors");
      sb.AppendLine("  Reflexes");
      sb.AppendLine("  Psychic");
      sb.AppendLine("  Scenarios");
      sb.AppendLine("    Reports");
      sb.AppendLine("");
      sb.AppendLine("Ключи путей в конфигурации студии: SettingsPath, LogsFolderPath, BootDataFolderPath,");
      sb.AppendLine("DataGomeostasFolderPath, DataActionsFolderPath, SensorsFolderPath, ReflexesFolderPath,");
      sb.AppendLine("PsychicDataFolderPath, ScenarioReportsFolderPath (относительно корня: Data\\Scenarios\\Reports).");
      return sb.ToString();
    }

    /// <summary>
    /// Возвращает дерево каталогов по умолчанию для отображения в интерфейсе (шаблон задаётся в коде, только просмотр).
    /// </summary>
    /// <returns>Корневой узел с заполненными дочерними элементами.</returns>
    public static ProjectDirectoryTemplateNode GetProjectDirectoryTemplateRoot()
    {
      var reports = new ProjectDirectoryTemplateNode("Reports");
      var scenarios = new ProjectDirectoryTemplateNode("Scenarios", new List<ProjectDirectoryTemplateNode> { reports });
      var dataChildren = new List<ProjectDirectoryTemplateNode>
      {
        new ProjectDirectoryTemplateNode("Gomeostas"),
        new ProjectDirectoryTemplateNode("Actions"),
        new ProjectDirectoryTemplateNode("Sensors"),
        new ProjectDirectoryTemplateNode("Reflexes"),
        new ProjectDirectoryTemplateNode("Psychic"),
        scenarios
      };
      var data = new ProjectDirectoryTemplateNode("Data", dataChildren);
      var settingsChildren = new List<ProjectDirectoryTemplateNode>
      {
        new ProjectDirectoryTemplateNode("Settings.xml")
      };
      var settings = new ProjectDirectoryTemplateNode("Settings", settingsChildren);
      var rootChildren = new List<ProjectDirectoryTemplateNode>
      {
        new ProjectDirectoryTemplateNode("Logs"),
        new ProjectDirectoryTemplateNode("BootData"),
        settings,
        data
      };
      return new ProjectDirectoryTemplateNode("Корень проекта данных", rootChildren);
    }

    /// <summary>
    /// Определяет корень проекта по пути к каталогу настроек: последний сегмент пути должен называться «Settings».
    /// </summary>
    /// <param name="settingsFolderPath">Полный путь к каталогу настроек проекта.</param>
    /// <param name="projectRoot">При успехе — родительский каталог (корень проекта данных).</param>
    /// <returns>True, если корень определён.</returns>
    public static bool TryGetProjectRootFromSettingsPath(string settingsFolderPath, out string projectRoot)
    {
      projectRoot = null;
      if (string.IsNullOrWhiteSpace(settingsFolderPath))
        return false;

      try
      {
        string trimmed = settingsFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(trimmed);
        string name = Path.GetFileName(full);
        if (!string.Equals(name, "Settings", StringComparison.OrdinalIgnoreCase))
          return false;

        projectRoot = Path.GetDirectoryName(full);
        return !string.IsNullOrEmpty(projectRoot);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Определяет корень проекта по пути к каталогу данных гомеостаза: ожидается «...\Data\Gomeostas».
    /// </summary>
    /// <param name="dataGomeostasFolderPath">Полный путь к каталогу Gomeostas.</param>
    /// <param name="projectRoot">При успехе — корень проекта данных.</param>
    /// <returns>True, если корень определён.</returns>
    public static bool TryGetProjectRootFromDataGomeostasPath(string dataGomeostasFolderPath, out string projectRoot)
    {
      projectRoot = null;
      if (string.IsNullOrWhiteSpace(dataGomeostasFolderPath))
        return false;

      try
      {
        string trimmed = dataGomeostasFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(trimmed);
        if (!string.Equals(Path.GetFileName(full), "Gomeostas", StringComparison.OrdinalIgnoreCase))
          return false;

        string dataDir = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(dataDir))
          return false;

        if (!string.Equals(Path.GetFileName(dataDir), "Data", StringComparison.OrdinalIgnoreCase))
          return false;

        projectRoot = Path.GetDirectoryName(dataDir);
        return !string.IsNullOrEmpty(projectRoot);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Пытается определить корень проекта сначала по каталогу настроек, затем по пути гомеостаза.
    /// </summary>
    /// <param name="settingsFolderPath">Путь к каталогу Settings.</param>
    /// <param name="dataGomeostasFolderPath">Путь к каталогу Data\Gomeostas.</param>
    /// <param name="projectRoot">Корень проекта данных.</param>
    /// <returns>True, если удалось определить корень.</returns>
    public static bool TryInferProjectRoot(string settingsFolderPath, string dataGomeostasFolderPath, out string projectRoot)
    {
      if (TryGetProjectRootFromSettingsPath(settingsFolderPath, out projectRoot))
        return true;
      return TryGetProjectRootFromDataGomeostasPath(dataGomeostasFolderPath, out projectRoot);
    }

    /// <summary>
    /// Проверяет наличие обязательных каталогов Logs, Data, BootData, Settings в указанном корне.
    /// </summary>
    /// <param name="projectRoot">Корень проекта данных.</param>
    /// <param name="missingFolderNames">Имена отсутствующих каталогов из шаблона.</param>
    /// <returns>True, если все обязательные каталоги существуют.</returns>
    public static bool MandatoryProjectRootFoldersExist(string projectRoot, out List<string> missingFolderNames)
    {
      missingFolderNames = new List<string>();
      if (string.IsNullOrWhiteSpace(projectRoot))
        return false;

      try
      {
        string rootFull = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        for (int i = 0; i < RequiredProjectRootFolderNames.Length; i++)
        {
          string name = RequiredProjectRootFolderNames[i];
          string path = Path.Combine(rootFull, name);
          if (!Directory.Exists(path))
            missingFolderNames.Add(name);
        }

        return missingFolderNames.Count == 0;
      }
      catch
      {
        missingFolderNames.Add("(ошибка доступа к пути)");
        return false;
      }
    }

    /// <summary>
    /// Возвращает относительные сегменты пути от корня проекта для ключа каталога из конфигурации студии.
    /// </summary>
    /// <param name="pathSettingKey">Имя элемента настроек (например SettingsPath).</param>
    /// <returns>Массив сегментов или null, если ключ не относится к каталогу из шаблона.</returns>
    public static string[] GetFolderSegmentsForPathSettingKey(string pathSettingKey)
    {
      if (pathSettingKey == null)
        return null;

      switch (pathSettingKey)
      {
        case "SettingsPath":
          return new[] { "Settings" };
        case "LogsFolderPath":
          return new[] { "Logs" };
        case "BootDataFolderPath":
          return new[] { "BootData" };
        case "DataGomeostasFolderPath":
          return new[] { "Data", "Gomeostas" };
        case "DataActionsFolderPath":
          return new[] { "Data", "Actions" };
        case "SensorsFolderPath":
          return new[] { "Data", "Sensors" };
        case "ReflexesFolderPath":
          return new[] { "Data", "Reflexes" };
        case "PsychicDataFolderPath":
          return new[] { "Data", "Psychic" };
        case "ScenarioReportsFolderPath":
          return new[] { "Data", "Scenarios", "Reports" };
        default:
          return null;
      }
    }

    /// <summary>
    /// Собирает полный ожидаемый путь к каталогу по ключу настройки и корню проекта.
    /// </summary>
    /// <param name="projectRoot">Корень проекта данных.</param>
    /// <param name="pathSettingKey">Ключ настройки каталога.</param>
    /// <returns>Полный путь.</returns>
    /// <exception cref="ArgumentException">Неизвестный ключ или пустой корень.</exception>
    public static string GetExpectedFolderPathForSetting(string projectRoot, string pathSettingKey)
    {
      if (string.IsNullOrWhiteSpace(projectRoot))
        throw new ArgumentException("Корень проекта не задан.", nameof(projectRoot));

      string[] segments = GetFolderSegmentsForPathSettingKey(pathSettingKey);
      if (segments == null)
        throw new ArgumentException("Ключ не соответствует каталогу из шаблона проекта.", nameof(pathSettingKey));

      string root = Path.GetFullPath(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
      string path = root;
      for (int i = 0; i < segments.Length; i++)
        path = Path.Combine(path, segments[i]);

      return Path.GetFullPath(path);
    }

    /// <summary>
    /// Проверяет, что абсолютный путь каталога совпадает с шаблонным путём для данного ключа относительно корня проекта.
    /// </summary>
    /// <param name="projectRoot">Корень проекта данных.</param>
    /// <param name="absoluteFolderPath">Текущий абсолютный путь из настроек.</param>
    /// <param name="pathSettingKey">Ключ настройки каталога.</param>
    /// <returns>True, если путь соответствует шаблону.</returns>
    public static bool IsFolderPathMatchingProjectTemplate(string projectRoot, string absoluteFolderPath, string pathSettingKey)
    {
      if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(absoluteFolderPath))
        return false;

      try
      {
        string expected = GetExpectedFolderPathForSetting(projectRoot, pathSettingKey);
        string actual = Path.GetFullPath(absoluteFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    #endregion
  }
}
