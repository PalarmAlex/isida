using ISIDA.Common;
using ISIDA.Reflexes;
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
    /// Ухудшение жизненно важных параметров относительно снимка на конце предыдущего пульса
    /// (см. <see cref="GomeostasSystem.UpdateStateOnly"/>): для дефицит-ориентированных
    /// (Speed &lt; 0) — значение уменьшилось; для избыток-ориентированных — увеличилось.
    /// Не требует «плохой» зоны относительно NormaWell (её даёт <see cref="AnyVitalParameterInHarmfulZone"/> / Danger).
    /// Игнорирует изменения не больше ожидаемого одно-пульсового шага |Speed|/100 (шум и фоновая пульсация).
    /// </summary>
    public bool HasCriticalParameterChanges(IEnumerable<ParameterData> currentParameters,
                                          IEnumerable<ParameterData> previousParameters)
    {
      try
      {
        if (previousParameters == null || !previousParameters.Any())
          return false;
        if (currentParameters == null || !currentParameters.Any())
          return false;

        foreach (var param in currentParameters)
        {
          if (!param.IsVital)
            continue;

          var prevParam = previousParameters.FirstOrDefault(p => p.Id == param.Id);
          if (prevParam == null)
            continue;

          float speed = param.Speed;
          if (speed == 0f)
            continue;

          float change = Math.Abs(param.Value - prevParam.Value);
          float naturalStep = Math.Abs(speed) / 100f;
          float minSignificant = Math.Max(naturalStep, 1e-4f);
          if (change <= minSignificant + 1e-5f)
            continue;

          bool worsening = speed < 0 ? param.Value < prevParam.Value : param.Value > prevParam.Value;
          if (worsening)
            return true;
        }

        return false;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Оценка ответа оператора (−1 / 0 / +1) по изменению жизненных параметров между снимком «до» и текущими значениями,
    /// с запасным вариантом по интегральному состоянию, если дельты незначимы.
    /// Если знак изменения фокусного параметра противоречит интегральному состоянию, а совокупность жизненных
    /// параметров не даёт сигнала (<c>vitalScore==0</c>), используется интегральная оценка.
    /// </summary>
    public int ComputeOperatorAutomatizmAssessment(
        IReadOnlyDictionary<int, float> valuesBefore,
        IList<ParameterData> currentParameters,
        int focusParameterId,
        AppGlobalState.HomeostasisState overallBefore,
        AppGlobalState.HomeostasisState overallAfter)
    {
      if (valuesBefore == null || valuesBefore.Count == 0 || currentParameters == null || currentParameters.Count == 0)
      {
        int r = AssessmentFromOverallStates(overallBefore, overallAfter);
        return r;
      }

      int? focusSigned = null;
      if (focusParameterId > 0 && valuesBefore.TryGetValue(focusParameterId, out float beforeFocus))
      {
        var curFocus = currentParameters.FirstOrDefault(p => p.Id == focusParameterId);
        if (curFocus != null)
          focusSigned = TrySignedParameterDelta(beforeFocus, curFocus);
      }

      int vitalScore = 0;
      foreach (var p in currentParameters)
      {
        if (!p.IsVital)
          continue;
        if (!valuesBefore.TryGetValue(p.Id, out float bpv))
          continue;
        var s = TrySignedParameterDelta(bpv, p);
        if (s == 1)
          vitalScore++;
        else if (s == -1)
          vitalScore--;
      }

      int overallAssess = AssessmentFromOverallStates(overallBefore, overallAfter);

      int result;
      // Фокусный параметр может дать заметную дельту по одному каналу (шум/доминанта),
      // тогда как интегральное состояние уже отражает суммарный эффект ответа оператора.
      // При отсутствии сигнала по совокупности жизненных (vitalScore==0) не наказываем автоматизм
      // знаком фокуса, если интеграл явно в противоположную сторону (Normal→Well и т.п.).
      if (focusSigned.HasValue && focusSigned.Value != 0)
      {
        if (vitalScore == 0 && overallAssess != 0 &&
            Math.Sign(overallAssess) != Math.Sign(focusSigned.Value))

          result = overallAssess;
        else
          result = focusSigned.Value;
      }
      else if (vitalScore != 0)
        result = vitalScore > 0 ? 1 : -1;
      else
        result = overallAssess;
 
      return result;
    }

    private static int AssessmentFromOverallStates(AppGlobalState.HomeostasisState before, AppGlobalState.HomeostasisState after)
    {
      if (after > before)
        return 1;
      if (after < before)
        return -1;
      return 0;
    }

    /// <summary>
    /// Знак изменения параметра: +1 к «лучше» (к норме), −1 к «хуже», 0 — в пределах шума (как в <see cref="HasCriticalParameterChanges"/>).
    /// </summary>
    private static int? TrySignedParameterDelta(float valueBefore, ParameterData param)
    {
      float speed = param.Speed;
      if (Math.Abs(speed) < 1e-6f)
        return null;

      float after = param.Value;
      float change = after - valueBefore;
      float naturalStep = Math.Abs(speed) / 100f;
      float minSignificant = Math.Max(naturalStep, 1e-4f);
      if (Math.Abs(change) <= minSignificant + 1e-5f)
        return 0;

      bool worsening = speed < 0 ? after < valueBefore : after > valueBefore;
      bool improving = speed < 0 ? after > valueBefore : after < valueBefore;
      if (worsening)
        return -1;
      if (improving)
        return 1;
      return 0;
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
    /// true, если хотя бы один параметр с <see cref="ParameterData.IsVital"/> находится в зоне хуже нормы:
    /// для дефицит-ориентированных (Speed &lt; 0) — значение ниже NormaWell; для избыток-ориентированных — выше NormaWell.
    /// Используется как маркер опасности для информационной среды (InformationEnvironment.Danger).
    /// </summary>
    public bool AnyVitalParameterInHarmfulZone(IEnumerable<ParameterData> parameters)
    {
      if (parameters == null)
        return false;

      foreach (var param in parameters)
      {
        if (!param.IsVital)
          continue;
        if (IsBadZone(param.Value, param.NormaWell, param.Speed))
          return true;
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

      float deviationForZone = 0;
      float rangeForZone = 100;

      float value = param.Value;
      float norma = param.NormaWell;

      if (param.Speed < 0)
      {
        // Дефицит-ориентированный: плохо когда value < norma
        if (value < norma)
        {
          deviationForZone = norma - value; // насколько ниже нормы
          rangeForZone = norma; // максимум отклонения = до 0
        }
      }
      else
      {
        // Избыток-ориентированный: плохо когда value > norma  
        if (value > norma)
        {
          deviationForZone = value - norma; // насколько выше нормы
          rangeForZone = 100 - norma; // максимум отклонения = до 100
        }
      }

      // Защита от деления на ноль
      if (rangeForZone <= 0.1f) rangeForZone = 0.1f;

      float percentForZone = (deviationForZone / rangeForZone) * 100;

      // Если есть отклонение от нормы (хуже нормы), определяем степень
      if (deviationForZone > 0)
      {
        if (percentForZone < 5) zone = 3;    // Слабое отклонение
        else if (percentForZone < 15) zone = 4; // Умеренное отклонение
        else if (percentForZone < 30) zone = 5; // Значительное отклонение
        else zone = 6;                 // Сильное отклонение
      }

      float signedDeviation = param.Speed < 0 ? value - norma : norma - value;
      float absDeviation = Math.Abs(signedDeviation);
      float maxPossibleDeviation = param.Speed < 0 ? norma : 100 - norma;
      if (maxPossibleDeviation <= 0.1f) maxPossibleDeviation = 0.1f;
      float percentForLogs = (absDeviation / maxPossibleDeviation) * 100;

      return (zone, $"{param.Id}|{signedDeviation:F2}|{maxPossibleDeviation:F1}|{percentForLogs:F1}");
    }

    /// <summary>
    /// Вычисляет функцию потребности Ui для параметра
    /// </summary>
    public float CalculateUrgencyFunction(ParameterData param)
    {
      if (param == null)
        throw new ArgumentNullException(nameof(param));

      var state = CalculateParameterState(param, 50, 0.5f); 
      var (zone, zoneDetails) = GetStateForStyleActivation(param, state.State);

      float value = param.Value;
      float threshold = param.NormaWell;
      float weight = AddUtils.Clamp(param.Weight / 100f, 0f, 1f);

      bool isDeficitOriented = param.Speed < 0;
      bool isExcessOriented = !isDeficitOriented;
      float urgency = 0f;

      if (isDeficitOriented)
      {
        if (value >= threshold)
          urgency = 0f;
        else
        {
          float denominator = threshold > 0 ? threshold : 1f;
          urgency = weight * (threshold - value) / denominator;
        }
      }

      if (isExcessOriented)
      {
        if (value <= threshold)
          urgency = 0f;
        else
        {
          float denominator = Math.Max(100f - threshold, 1f);
          urgency = weight * (value - threshold) / denominator;
        }
      }

      return AddUtils.Clamp(urgency, 0f, 1f);
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
          param.LastStateChangePulse.HasValue &&
          absDelta < difSensorPar)
      {
        int pulsesSinceChange = GlobalTimer.GlobalPulsCount - param.LastStateChangePulse.Value;
        bool keepHolding = (pulsesSinceChange < dynamicTime) || AppGlobalState.IsReflexChainActive || AppGlobalState.IsAutomatizmChainActive;

        if (ParameterData.TracePulseHold)
        {
          Trace.WriteLine(
              $"[ISIDA.PulseHold] HOLD branch id={param.Id} name={param.Name} pulse={GlobalTimer.GlobalPulsCount} " +
              $"lastState={param.LastState} anchorPulse={param.LastStateChangePulse} since={pulsesSinceChange}/{dynamicTime} " +
              $"keep={keepHolding} reflex={AppGlobalState.IsReflexChainActive} auto={AppGlobalState.IsAutomatizmChainActive} " +
              $"absΔ={absDelta:F6}<dif={difSensorPar:F6}");
        }

        if (keepHolding)
        {
          // Продолжаем удерживать состояние
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
          // Время удержания истекло
          if (ParameterData.TracePulseHold)
          {
            Trace.WriteLine(
                $"[ISIDA.PulseHold] HOLD expire id={param.Id} name={param.Name} pulse={GlobalTimer.GlobalPulsCount} " +
                $"since={pulsesSinceChange}>={dynamicTime} → fall through to recalc");
          }

          param.LastStateChangePulse = null;
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
        // Временное состояние — удерживаем. Якорь LastStateChangePulse задаёт длительность удержания
        // (dynamicTime тактов). Если хост на каждом пульсе снова даёт «удар» того же транзиторного
        // смысла (например Well→Well при непрерывном потоке метрик из среды), не сбрасывать якорь —
        // иначе отсчёт никогда не дойдёт до возврата в Норму;
        ParameterState priorLast = param.LastState;
        param.LastState = newState;
        if (!(param.LastStateChangePulse.HasValue && newState == priorLast))
          param.LastStateChangePulse = GlobalTimer.GlobalPulsCount;
      }
      else
      {
        // Постоянное состояние — не удерживаем
        param.LastState = newState;
        param.LastStateChangePulse = null;
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
    /// <param name="dynamicTime">Длительность удержания состояний Well/Bad у параметра в тактах <see cref="GlobalTimer.GlobalPulsCount"/> (не секундах).</param>
    /// <param name="difSensorPar">Порог значимого изменения параметра</param>
    /// <param name="lastWellStatePulse">Время в пульсах последнего перехода в состояние Well (для гистерезиса)</param>
    /// <param name="relativeThreshold">Относительный порог активации состояния (0-1). 
    /// Например, 0.3 означает, что состояние активируется при 30% от максимально возможного отклонения</param>
    /// <returns>Состояние гомеостаза агента</returns>
    public AgentHomeostasisState CalculateAgentState(
        IEnumerable<ParameterData> parameters,
        int dynamicTime,
        float difSensorPar,
        ref int? lastWellStatePulse,
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
      var badParameterIds = new List<int>(); // список Bad параметров

      relativeThreshold = relativeThreshold / 100f; // нормализуем интегральный порог к [0,1] 

      // Параметры в состоянии Normal не учитываются в суммарных отклонениях
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
          badParameterIds.Add(param.Id);
        }
        else if (paramState.State == ParameterState.Well)
        {
          wellSum += weightedValue;
          totalPossibleWell += normalizedWeight;
          hasWell = true;
        }
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
      // Интегральное Well сбрасывается после dynamicTime тактов пульса (и без активных цепочек рефлексов/автоматизма).
      if (overallState == HomeostasisOverallState.Well)
      {
        if (lastWellStatePulse.HasValue)
        {
          int pulsesSinceWell = GlobalTimer.GlobalPulsCount - lastWellStatePulse.Value;
          if (pulsesSinceWell >= dynamicTime && !AppGlobalState.IsReflexChainActive && !AppGlobalState.IsAutomatizmChainActive)
          {
            overallState = HomeostasisOverallState.Normal;
            lastWellStatePulse = null;
          }
        }
        else
          lastWellStatePulse = GlobalTimer.GlobalPulsCount;
      }
      else
        lastWellStatePulse = null;

      AppGlobalState.CurrentOverallState = (AppGlobalState.HomeostasisState)overallState;

      var weightById = parameters.ToDictionary(p => p.Id, p => p.Weight);
      var badTargetsSorted = badParameterIds
          .OrderByDescending(id => weightById.TryGetValue(id, out var w) ? w : 0f)
          .ToList();

      var result = new AgentHomeostasisState
      {
        OverallState = overallState,
        BadSum = badSum,
        WellSum = wellSum,
        ParametersState = parametersState,
        BadParameterTargetIds = badTargetsSorted
      };

      return result;
    }

    /// <summary>
    /// Данные стиля с динамическим весом для логирования
    /// </summary>
    public class StyleWithDynamicWeight
    {
      /// <summary>
      /// Стиль
      /// </summary>
      public BehaviorStyle Style { get; set; }
    }

    /// <summary>
    /// Получает финальные активные стили на основе доминирующего параметра
    /// </summary>
    public (List<StyleWithDynamicWeight> finalStyles,
            List<ResearchLogger.StyleActivationLog> activations,
            List<ResearchLogger.StyleParameterActivation> parameterActivations,
            ParameterData dominantParameter)
        GetFinalActiveStyles(
        List<BehaviorStyle> baseStyles,
        List<ParameterData> parameters,
        int dynamicTime,
        float difSensorPar)
    {
      var activations = new List<ResearchLogger.StyleActivationLog>();
      var parameterActivations = new List<ResearchLogger.StyleParameterActivation>();
      var (dominantParam, dominantZone, dominanceScore) = FindDominantParameter(parameters, dynamicTime, difSensorPar);
      var finalStyles = baseStyles
          .Take(3)
          .Select(style => new StyleWithDynamicWeight
          {
            Style = style
          })
          .ToList();

      if (dominantParam != null)
      {
        CollectParameterStyleActivations(baseStyles, new List<ParameterData> { dominantParam },
            parameterActivations, dynamicTime, difSensorPar);
        AppGlobalState.DominantParam = dominantParam.Id;        
      }

      return (finalStyles, activations, parameterActivations, dominantParam);
    }

    /// <summary>
    /// Собирает связи между параметрами и изначально активированными стилями
    /// </summary>
    private void CollectParameterStyleActivations(
        List<BehaviorStyle> baseStyles,
        List<ParameterData> parameters,
        List<ResearchLogger.StyleParameterActivation> parameterActivations,
        int dynamicTime,
        float difSensorPar)
    {
      // Для каждого параметра находим, какие стили он активировал
      foreach (var param in parameters)
      {
        var state = CalculateParameterState(param, dynamicTime, difSensorPar);
        var (currentZone, zoneDetails) = GetStateForStyleActivation(param, state.State);

        if (param.StyleActivations.TryGetValue(currentZone, out var styleIds))
        {
          foreach (var styleId in styleIds.Where(id => id > 0))
          {
            var baseStyle = baseStyles.FirstOrDefault(s => s.Id == styleId);
            if (baseStyle != null)
            {
              parameterActivations.Add(new ResearchLogger.StyleParameterActivation
              {
                Pulse = -1,
                Time = DateTime.Now,
                Stage = "ParameterActivation",
                ParameterId = param.Id,
                ParameterName = param.Name,
                ZoneId = currentZone,
                ZoneDescription = GetStateDescription(currentZone),
                StyleId = styleId,
                StyleName = baseStyle.Name,
                ActivationDetails = $"{param.Id}|{zoneDetails}|Zone{currentZone}"
              });
            }
          }
        }
      }
    }

    /// <summary>
    /// Получает текстовое описание состояния по ID
    /// </summary>
    private string GetStateDescription(int stateId)
    {
      string stateDescript = "";

      switch (stateId)
      {
        case 0:
          stateDescript = "Выход из нормы"; break;
        case 1:
          stateDescript = "Возврат в норму"; break;
        case 2:
          stateDescript = "Норма"; break;
        case 3:
          stateDescript = "Слабое отклонение"; break;
        case 4:
          stateDescript = "Умеренное отклонение"; break;
        case 5:
          stateDescript = "Значительное отклонение"; break;
        case 6:
          stateDescript = "Критическое отклонение"; break;
        default:
          stateDescript = "Неизвестное состояние"; break;
      }
      return stateDescript;
    }

    /// <summary>
    /// Определяет доминирующий параметр для активации стилей
    /// </summary>
    public (ParameterData dominantParam, int zone, float dominanceScore) FindDominantParameter(
        List<ParameterData> parameters,
        int dynamicTime,
        float difSensorPar)
    {
      ParameterData dominant = null;
      int dominantZone = 0;
      float maxScore = -1f; // Начинаем с -1 чтобы отсечь незначительные

      foreach (var param in parameters)
      {
        var state = CalculateParameterState(param, dynamicTime, difSensorPar);
        var (zone, zoneDetails) = GetStateForStyleActivation(param, state.State);

        // Пропускаем нормальные состояния если есть более критичные
        if (zone == 2 && maxScore > difSensorPar) continue;

        float significance = CalculateBidirectionalUrgency(param, zone);
        float priority = param.Weight / 100f;

        // Основной фактор - зона, затем значимость, затем приоритет
        float zoneBaseScore = GetZoneBaseScore(zone);
        float dominanceScore = zoneBaseScore * (1f + significance) * (1f + priority);

        if (dominanceScore > maxScore)
        {
          maxScore = dominanceScore;
          dominant = param;
          dominantZone = zone;
        }
      }

      // Если ничего не найдено, берем первый параметр в норме
      if (dominant == null)
      {
        foreach (var param in parameters)
        {
          var state = CalculateParameterState(param, dynamicTime, difSensorPar);
          var (zone, zoneDetails) = GetStateForStyleActivation(param, state.State);
          if (zone == 2)
          {
            dominant = param;
            dominantZone = zone;
            maxScore = 0.1f;
            break;
          }
        }
      }

      return (dominant, dominantZone, maxScore);
    }

    /// <summary>
    /// Двусторонняя функция потребности - учитывает как негативные, так и позитивные отклонения
    /// </summary>
    private float CalculateBidirectionalUrgency(ParameterData param, int zone)
    {
      float value = param.Value;
      float norma = param.NormaWell;
      float weight = param.Weight / 100f;

      if (param.Speed < 0) // Дефицит-ориентированный
      {
        if (value < norma) // Хуже нормы - критично
          return weight * (norma - value) / norma;
        else // Лучше нормы - хорошо, но менее значимо
          return weight * (value - norma) / (100 - norma) * 0.3f;
      }
      else // Избыток-ориентированный
      {
        if (value > norma) // Хуже нормы - критично
          return weight * (value - norma) / (100 - norma);
        else // Лучше нормы - хорошо, но менее значимо
          return weight * (norma - value) / norma * 0.3f;
      }
    }

    private float GetZoneBaseScore(int zone)
    {
      // Базовые очки по зонам - экспоненциальный рост
      var zoneScores = new Dictionary<int, float>
    {
        { 0, 10f },   // Выход из нормы
        { 1, 5f },    // Возврат в норму  
        { 2, 1f },    // Норма
        { 3, 20f },   // Слабое отклонение
        { 4, 40f },   // Умеренное отклонение
        { 5, 80f },   // Значительное отклонение
        { 6, 160f }   // Критическое отклонение
    };

      if (zoneScores.TryGetValue(zone, out float score))
        return score;
      return 1f;
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