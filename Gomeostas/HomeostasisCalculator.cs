using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using static ISIDA.Common.FileValidator;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace ISIDA.Gomeostas
{
  /// <summary>
  /// Калькулятор параметров гомеостаза с расширенным логированием
  /// </summary>
  public sealed class HomeostasisCalculator : IDisposable
  {
    /// <summary>
    /// Определение критичности изменений
    /// </summary>
    public bool HasCriticalParameterChanges(IEnumerable<ParameterData> currentParameters,
                                          IEnumerable<ParameterData> previousParameters)
    {
      try
      {
        if (previousParameters == null || !previousParameters.Any())
          return true;

        bool hasRealCriticalChanges = false;

        foreach (var param in currentParameters)
        {
          if (!param.IsVital) continue;

          var prevParam = previousParameters.FirstOrDefault(p => p.Id == param.Id);
          if (prevParam == null) continue;

          float change = Math.Abs(param.Value - prevParam.Value);
          float _speed = Math.Abs(param.Speed);

          bool isDeficitOriented = param.Speed < 0;
          bool isExcessOriented = param.Speed > 0;

          bool isCriticalValue = false;
          bool isNaturalDecay = change <= _speed;

          if (isDeficitOriented)
          {
            float deviation = param.NormaWell - param.Value;
            isCriticalValue = deviation > 0;
          }
          else if (isExcessOriented)
          {
            float deviation = param.Value - param.NormaWell;
            isCriticalValue = deviation > 0;
          }

          if (isCriticalValue && !isNaturalDecay)
          {
            hasRealCriticalChanges = true;
            break;
          }
        }
        return hasRealCriticalChanges;
      }
      catch
      {
        return true;
      }
    }

    /// <summary>
    /// Опреляет критичность параметра
    /// </summary>
    public bool IsExternalImpactCritical(Dictionary<int, int> externalInfluences,
                                   IEnumerable<ParameterData> parameters)
    {
      if (externalInfluences == null) return false;

      foreach (var influence in externalInfluences)
      {
        var param = parameters.FirstOrDefault(p => p.Id == influence.Key);
        if (param != null && param.IsVital)
        {
          // определяем критичность воздействия по типу параметра
          bool isDeficitOriented = param.Speed < 0;
          bool isExcessOriented = param.Speed > 0;

          bool isHarmfulImpact = false;

          if (isDeficitOriented)
            // Для дефицит-ориентированного: вредно ОТРИЦАТЕЛЬНОЕ воздействие (уменьшает значение)
            isHarmfulImpact = influence.Value < 0;
          else if (isExcessOriented)
            // Для избыток-ориентированного: вредно ПОЛОЖИТЕЛЬНОЕ воздействие (увеличивает значение)
            isHarmfulImpact = influence.Value > 0;

          float _speed = Math.Abs(param.Speed);
          bool isSignificantImpact = Math.Abs(influence.Value) > _speed;
          bool paramNearCritical = IsParameterNearCritical(param);

          if (isHarmfulImpact && isSignificantImpact && paramNearCritical)
            return true;
        }
      }

      return false;
    }

    private bool IsParameterNearCritical(ParameterData param)
    {
      bool isDeficitOriented = param.Speed < 0;
      bool isExcessOriented = param.Speed > 0;
      float _speed = Math.Abs(param.Speed);

      if (isDeficitOriented)
      {
        // Дефицит-ориентированный: опасно близко к НИЗКОМУ значению
        return param.Value < param.NormaWell + _speed;
      }
      else if (isExcessOriented)
      {
        // Избыток-ориентированный: опасно близко к ВЫСОКОМУ значению  
        return param.Value > param.NormaWell - _speed;
      }

      return false;
    }

    /// <summary>
    /// Определение внешних критических воздействий
    /// </summary>
    public bool HasExternalCriticalImpact(Dictionary<int, int> externalInfluences,
                                        IEnumerable<ParameterData> parameters)
    {
      if (externalInfluences == null) return false;

      foreach (var influence in externalInfluences)
      {
        var param = parameters.FirstOrDefault(p => p.Id == influence.Key);
        if (param != null && param.IsVital)
        {
          // Внешнее воздействие на жизненно важный параметр считается критическим
          // если его величина превышает порог 5
          if (Math.Abs(influence.Value) > 5)
            return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Определяет состояние параметра для активации стилей (0–6)
    /// </summary>
    internal (int, string) GetStateForStyleActivation(ParameterData param, ParameterState state)
    {
      int zone = 0;

      // Базовые зоны на основе состояния
      if (state == ParameterState.Well) zone = 1;
      else if (state == ParameterState.Bad) zone = 0;
      else zone = 2; // Normal

      float value = param.Value;
      float norma = param.NormaWell;

      float deviation;
      float range;

      if (param.Speed < 0)
      {
        // Дефицит-ориентированный: значение ВЫШЕ нормы
        deviation = Math.Max(0, norma - value);
        range = norma;
      }
      else
      {
        // Избыток-ориентированный: значение НИЖЕ нормы  
        deviation = Math.Max(0, value - norma);
        range = 100 - norma;
      }

      // Защита от деления на ноль
      if (range <= 0.1f) range = 0.1f;

      float percent = (deviation / range) * 100;

      // Если есть отклонение от нормы, определяем степень
      if (deviation > 0)
      {
        if (percent < 5) zone = 3;    // Слабое отклонение
        else if (percent < 15) zone = 4; // Умеренное отклонение
        else if (percent < 30) zone = 5; // Значительное отклонение
        else zone = 6;                 // Сильное отклонение
      }

      //Debug.WriteLine($"param: {param.Name}, value: {value:F2}, norma: {norma}, " +
      //               $"speed: {param.Speed}, deviation: {deviation:F2}, " +
      //               $"range: {range}, percent: {percent:F1}%, zone: {zone}");

      return (zone, $"{param.Id}|{deviation:F2}|{range}|{percent:F1}");
    }
    
    /// <summary>
    /// Вычисляет функцию потребности Ui для параметра на основе его типа (дефицит/избыток)
    /// текущего значения, критического порога и веса
    /// </summary>
    /// <param name="param">Данные параметра</param>
    /// <returns>Значение функции потребности Ui ∈ [0, 1]</returns>
    public float CalculateUrgencyFunction(ParameterData param)
    {
      if (param == null)
        throw new ArgumentNullException(nameof(param)); 

      float value = param.Value;
      float threshold = param.NormaWell;
      float weight = Math.Max(0f, Math.Min(1f, param.Weight/100f)); // Ограничение веса [0,1]

      // Определение типа параметра по знаку Speed
      bool isDeficitOriented = param.Speed < 0; // Рост полезен → дефицит при P < T
      bool isExcessOriented = !isDeficitOriented; // Падение полезно → избыток при P > T

      float urgency = 0f;

      if (isDeficitOriented)
      {
        // Дефицит-ориентированный: критично, если значение ниже порога
        if (value >= threshold)
          urgency = 0f;
        else
        {
          float denominator = threshold > 0 ? threshold : 1f; // защита от threshold ≤ 0
          urgency = weight * (threshold - value) / denominator;
        }
      }

      if (isExcessOriented)
      {
        // Избыток-ориентированный: критично, если значение выше порога
        if (value <= threshold)
          urgency = 0f;
        else
        {
          float denominator = Math.Max(100f - threshold, 1f); // защита от threshold ≥ 100
          urgency = weight * (value - threshold) / denominator;
        }
      }

      // Ограничиваем результат [0, 1]
      return Math.Max(0f, Math.Min(1f, urgency));
    }

    /// <summary>
    /// Рассчитывает текущее состояние параметра
    /// </summary>
    public ParameterStateInfo CalculateParameterState(ParameterData param, int dynamicTime, float difSensorPar)
    {
      float delta = param.Value - param.PreviousValue;
      float absDelta = Math.Abs(delta);

      bool isDecaying = param.Speed < 0;
      bool wasInBadZone = IsBadZone(param.PreviousValue, param.NormaWell, param.Speed);
      bool isInBadZone = IsBadZone(param.Value, param.NormaWell, param.Speed);
      bool isImproving = (isDecaying && delta > 0) || (!isDecaying && delta < 0);
      bool isWorsening = !isImproving;

      // удержание — если оно активно и изменение параметра малое
      if (param.LastState != ParameterState.Normal &&
          param.LastStateChangeTime.HasValue &&
          absDelta < difSensorPar)
      {
        var duration = (DateTime.UtcNow - param.LastStateChangeTime.Value).TotalSeconds;
        if (duration < dynamicTime)
        {
          // Продолжаем удерживать
          float rawDev = CalculateDeviation(param.Value, param.NormaWell, param.Speed);
          return new ParameterStateInfo
          {
            State = param.LastState,
            Value = (float)(100 * (1 - Math.Exp(-rawDev / 20f))),
            ParameterId = param.Id,
            ParameterName = param.Name
          };
        }
        else
        {
          // Время истекло — сбрасываем
          param.LastStateChangeTime = null;
          param.LastState = ParameterState.Normal;
        }
      }

      // пересчет — если изменение значимое или удержание истекло
      ParameterState newState;

      if (absDelta < difSensorPar)
        // Малое изменение и не в удержании → по зоне
        newState = isInBadZone ? ParameterState.Bad : ParameterState.Normal;
      else
      {
        // Значительное изменение — "удар"
        if (wasInBadZone && !isInBadZone)
          newState = ParameterState.Well; // вышли из плохой
        else if (!wasInBadZone && isInBadZone)
          newState = ParameterState.Bad; // вошли в плохую
        else
          // Остались в одной зоне
          newState = isImproving ? ParameterState.Well : ParameterState.Bad;
      }

      // обновление состояния удержания
      if (newState == ParameterState.Well ||
          (newState == ParameterState.Bad && !isInBadZone))
      {
        // Временное состояние — удерживаем
        param.LastState = newState;
        param.LastStateChangeTime = DateTime.UtcNow;
      }
      else
      {
        // Постоянное состояние — не удерживаем
        param.LastState = newState;
        param.LastStateChangeTime = null;
      }

      // расчет отклонения
      float rawDeviation = newState != ParameterState.Normal
          ? CalculateDeviation(param.Value, param.NormaWell, param.Speed)
          : 0;

      return new ParameterStateInfo
      {
        State = newState,
        Value = newState != ParameterState.Normal
              ? (float)(100 * (1 - Math.Exp(-rawDeviation / 20f)))
              : 0,
        ParameterId = param.Id,
        ParameterName = param.Name
      };
    }

    /// <summary>
    /// Рассчитывает общее состояние гомеостаза агента с учётом динамики и гистерезиса
    /// Использует относительные пороги вместо абсолютных для корректной работы с любым количеством параметров
    /// </summary>
    /// <param name="parameters">Коллекция параметров гомеостаза</param>
    /// <param name="dynamicTime">Время динамического состояния в секундах</param>
    /// <param name="difSensorPar">Порог значимого изменения параметра</param>
    /// <param name="lastWellStateTime">Время последнего перехода в состояние Well (для гистерезиса)</param>
    /// <param name="relativeThreshold">Относительный порог активации состояния (0-1). 
    /// Например, 0.3 означает, что состояние активируется при 30% от максимально возможного отклонения</param>
    /// <returns>Состояние гомеостаза агента</returns>
    public AgentHomeostasisState CalculateAgentState(
        IEnumerable<ParameterData> parameters,
        int dynamicTime,
        float difSensorPar,
        ref DateTime? lastWellStateTime,
        float relativeThreshold = 30f)
    {
      var startTime = DateTime.UtcNow;

      float badSum = 0f;           // Суммарное взвешенное отклонение для плохих состояний
      float wellSum = 0f;          // Суммарное взвешенное отклонение для хороших состояний
      float totalPossibleBad = 0f; // Сумма весов параметров в состоянии Bad (максимально возможное отклонение)
      float totalPossibleWell = 0f; // Сумма весов параметров в состоянии Well (максимально возможное отклонение)
      bool hasBad = false;         // Флаг наличия хотя бы одного параметра в состоянии Bad
      bool hasWell = false;        // Флаг наличия хотя бы одного параметра в состоянии Well
      var parametersState = new List<ParameterStateInfo>(); // Состояния отдельных параметров

      relativeThreshold = relativeThreshold / 100f; // нормализуем интегральный порог к [0,1] 

      foreach (var param in parameters)
      {
        var paramState = CalculateParameterState(param, dynamicTime, difSensorPar);
        parametersState.Add(paramState);

        float normalizedWeight = param.Weight / 100f;
        float weightedValue = Math.Abs(paramState.Value) * normalizedWeight;

        if (paramState.State == ParameterState.Bad)
        {
          badSum += weightedValue;
          totalPossibleBad += normalizedWeight;
          hasBad = true;
        }
        else if (paramState.State == ParameterState.Well)
        {
          wellSum += weightedValue;
          totalPossibleWell += normalizedWeight;
          hasWell = true;
        }
        // Параметры в состоянии Normal не учитываются в суммарных отклонениях
      }

      HomeostasisOverallState overallState = HomeostasisOverallState.Normal;

      // Расчет относительных величин отклонений
      float relativeBad = totalPossibleBad > 0 ? badSum / totalPossibleBad : 0;
      float relativeWell = totalPossibleWell > 0 ? wellSum / totalPossibleWell : 0;

      // Логика определения общего состояния с приоритетом более критичного состояния
      if (hasBad && relativeBad >= relativeThreshold)
      {
        // Есть значительные плохие отклонения
        if (hasWell && relativeWell >= relativeThreshold)
        {
          // Есть также значительные хорошие отклонения - выбираем более выраженное состояние
          overallState = relativeBad > relativeWell
              ? HomeostasisOverallState.Bad
              : HomeostasisOverallState.Well;
        }
        else
          // Только плохие отклонения значительны
          overallState = HomeostasisOverallState.Bad;
      }
      else if (hasWell && relativeWell >= relativeThreshold)
        // Только хорошие отклонения значительны
        overallState = HomeostasisOverallState.Well;
      // В противном случае состояние остается Normal

      // Обработка гистерезиса для состояния Well
      // Состояние Well временное и сбрасывается после dynamicTime секунд
      if (overallState == HomeostasisOverallState.Well)
      {
        if (lastWellStateTime.HasValue)
        {
          // Проверяем, не истекло ли время действия состояния Well
          var wellDuration = (DateTime.UtcNow - lastWellStateTime.Value).TotalSeconds;
          if (wellDuration >= dynamicTime)
          {
            // Время истекло - возвращаемся в нормальное состояние
            overallState = HomeostasisOverallState.Normal;
            lastWellStateTime = null;
          }
        }
        else
          // Первый вход в состояние Well - запоминаем время
          lastWellStateTime = DateTime.UtcNow;
      }
      else
        // Не в состоянии Well - сбрасываем таймер
        lastWellStateTime = null;

      var result = new AgentHomeostasisState
      {
        OverallState = overallState,
        BadSum = badSum,
        WellSum = wellSum,
        ParametersState = parametersState
      };

      return result;
    }

    /// <summary>
    /// Контрастирование стилей - отбор по весу с учетом веса параметра
    /// </summary>
    public List<BehaviorStyle> ApplySimpleStyleContrasting(
        List<BehaviorStyle> activeStyles,
        List<ParameterData> parameters)
    {
      if (activeStyles.Count <= 3)
        return activeStyles;

      var styleScores = new Dictionary<int, float>();

      foreach (var style in activeStyles)
      {
        float totalScore = style.Weight;

        foreach (var param in parameters)
        {
          foreach (var activation in param.StyleActivations.Values)
          {
            if (activation.Contains(style.Id) || activation.Contains(-style.Id))
            {
              // Учитываем вес параметра в оценке стиля
              totalScore += param.Weight * 0.1f; // Коэффициент влияния веса параметра
              break;
            }
          }
        }

        styleScores[style.Id] = totalScore;
      }

      return activeStyles
          .OrderByDescending(s => styleScores[s.Id])
          .Take(3)
          .ToList();
    }

    /// <summary>
    /// Освобождает ресурсы калькулятора
    /// </summary>
    public void Dispose()
    {

    }

    #region Вспомогательные методы

    private bool IsBadZone(float value, float normaWell, float speed) =>
        (speed < 0 && value < normaWell) || (speed >= 0 && value > normaWell);

    private float CalculateDeviation(float value, float normaWell, float speed) =>
        speed < 0 ? (value < normaWell ? normaWell - value : value - normaWell)
                 : (value > normaWell ? value - normaWell : normaWell - value);
    
    #endregion
  }
}