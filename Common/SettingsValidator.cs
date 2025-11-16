using System;
using System.Collections.Generic;
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

        case "TimeWindowMs":
          return ValidateTimeWindowMs((int)value);

        case "MinAssociationStrength":
          return ValidateMinAssociationStrength((float)value);

        case "MaxRank":
          return ValidateMaxRank((int)value);

        case "MaxInactivationTime":
          return ValidateMaxInactivationTime((int)value);

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
    public static (bool isValid, string errorMessage) ValidateTimeWindowMs(int? value)
    {
      const string paramName = "Временное окно корреляции";
      const string range = "[100:2000] мс";
      return ValidateValueCustom(value, paramName, 100, 2000, range);
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
    /// Валидация максимального ранга рефлекса
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateMaxRank(int? value)
    {
      const string paramName = "Максимальный ранг рефлекса";
      const string range = "[1:50]";
      return ValidateValueCustom(value, paramName, 1, 50, range);
    }

    /// <summary>
    /// Валидация времени жизни без активации
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateMaxInactivationTime(int? value)
    {
      const string paramName = "Время жизни без активации";
      const string range = "[100:10000] пульсов";
      return ValidateValueCustom(value, paramName, 100, 10000, range);
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
      const string range = "[-20:20] за исключением 0";
      return ValidateValueCustom(value, paramName, -20, 20, range);
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
    /// Валидация влияние действия на параметры гомеостаза
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateInfluencesAction(int? value)
    {
      const string paramName = "Величина влияния на действие";
      const string range = "[-10:10]";
      return ValidateValueCustom(value, paramName, -10, 10, range);
    }

    /// <summary>
    /// Валидация влияние затрат на параметры гомеостаза
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateCostsAction(int? value)
    {
      const string paramName = "Величина затраты на действие";
      const string range = "[-10:10]";
      return ValidateValueCustom(value, paramName, -10, 10, range);
    }
    
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

    #region Стили реагирования

    /// <summary>
    /// Валидация порога гистерезиса для переключения активных стилей (в % шкал)
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateHysteresisLimit(int? value)
    {
      const string paramName = "Порог гистерезиса";
      const string range = "[1:99]";
      return ValidateValueCustom(value, paramName, 1, 99, range);
    }

    /// <summary>
    /// Валидация модуляции действия стилями реагирования
    /// </summary>
    /// <param name="value">Значение для валидации (int)</param>
    public static (bool isValid, string errorMessage) ValidateStileActionInfluencet(int? value)
    {
      const string paramName = "Модуляция действия";
      const string range = "[-5:5]";
      return ValidateValueCustom(value, paramName, -5, 5, range);
    }

    /// <summary>
    /// Валидация базового коэффициента конкурентного подавления для латерального торможения
    /// </summary>
    /// <param name="value">Значение для валидации (float)</param>
    public static (bool isValid, string errorMessage) ValidateDefaultKCompetition(float? value)
    {
      const string paramName = "Величина коэффициента подавления латерального торможения";
      const string range = "[0.0:1.0]";
      return ValidateValueCustom(value, paramName, 0.0f, 1.0f, range);
    }

    /// <summary>
    /// Валидация базового порога активации для фильтрации значимых стилей поведения
    /// </summary>
    /// <param name="value">Значение для валидации (float)</param>
    public static (bool isValid, string errorMessage) ValidateDefaultBaseThreshold(float? value)
    {
      const string paramName = "Величина порога активации для фильтрации значимых стилей поведения";
      const string range = "[0.0:1.0]";
      return ValidateValueCustom(value, paramName, 0.0f, 1.0f, range);
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

    #endregion
  }
}
