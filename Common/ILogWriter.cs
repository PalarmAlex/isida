using System;

namespace ISIDA.Common
{
  /// <summary>
  /// Интерфейс для записи логов из библиотеки в клиент
  /// </summary>
  public interface ILogWriter
  {
    /// <summary>
    /// Записывает лог в память
    /// </summary>
    void WriteLog(string className, string method, int? pulse, int? baseId,
                 int? baseStyleId, int? triggerStimulusId, int? orientationReflexType,
                 int? geneticReflexId, int? conditionedReflexId, int? automatizmId = null,
                 string reflexChainInfo = null, string automatizmChainInfo = null,
                 int? thinkingLevel = null, bool? thinkingLevelSuccess = null,
                 int? thinkingThemeTypeId = null, string thinkingThemeTooltip = null,
                 int? mainThinkingCycleId = null, string mainThinkingCycleTooltip = null);

    /// <summary>
    /// Записывает лог параметров гомеостаза
    /// </summary>
    void WriteParameterLog(int pulse, int paramId, string paramName, int weight,
                          int normaWell, int speed, float value, float urgencyFunction,
                          string parameterState, string activationZone);

    /// <summary>
    /// Записывает лог стилей поведения
    /// </summary>
    void WriteStyleLog(int pulse, string stage, int styleId, string styleName);

    /// <summary>
    /// Запись активаций параметров
    /// </summary>
    void WriteStyleParameterActivation(int pulse, string stage, int parameterId, string parameterName,
                                      int zoneId, string zoneDescription, int styleId, string styleName,
                                      string activationDetails);
  }
}
