using System.Collections.Generic;

namespace ISIDA.Gomeostas
{
  /// <summary>
  /// Класс для хранения условий активации стилей
  /// </summary>
  public class StyleActivationCondition
  {
    /// <summary>
    /// ID параметра
    /// </summary>
    public int ParamId { get; set; }

    /// <summary>
    /// Для каждого ID состояния параметра — список ID стилей для активации
    /// </summary>
    public Dictionary<int, List<int>> StateStyles { get; } = new Dictionary<int, List<int>>();
  }
}
