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
                 int? baseStyleId, int? triggerStimulusId, int? hasCriticalChanges,
                 int? geneticReflexId, int? conditionedReflexId);

    /// <summary>
    /// Записывает лог параметров гомеостаза
    /// </summary>
    void WriteParameterLog(int pulse, int paramId, string paramName, int weight,
                          int normaWell, int speed, float value, float urgencyFunction,
                          string parameterState, string activationZone);

    /// <summary>
    /// Записывает лог стилей поведения
    /// </summary>
    void WriteStyleLog(int pulse, string stage, int styleId, string styleName,
                      int weight, float activity);
  }
}
