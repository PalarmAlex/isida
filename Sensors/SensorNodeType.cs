namespace ISIDA.Sensors
{
  /// <summary>
  /// Тип узла сенсорного дерева (категория токена восприятия).
  /// </summary>
  public enum SensorNodeType
  {
    /// <summary>
    /// Вербальный токен (слова, буквы, текстовые примитивы).
    /// </summary>
    Verbal = 0,

    /// <summary>
    /// Командный токен (например, CAD-маркеры sw:, pt:).
    /// </summary>
    Command = 1
  }
}
