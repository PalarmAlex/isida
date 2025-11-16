using System.Collections.Generic;

namespace ISIDA.Gomeostas
{
  /// <summary>
  /// Класс для хранения информации о несовместимости стилей
  /// </summary>
  public class StyleAntagonism
  {
    /// <summary>
    /// ID основного стиля
    /// </summary>
    public int StyleId { get; set; }

    /// <summary>
    /// Список ID стилей-антагонистов
    /// </summary>
    public List<int> AntagonistIds { get; set; } = new List<int>();
  }
}
