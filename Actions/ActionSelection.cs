using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ISIDA.Actions
{
  /// <summary>
  /// Класс для выбора антагонистических действий
  /// </summary>
  public class ActionSelection
  {
    /// <summary>
    /// ID действия
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Наименование действия
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Флаг выбора действия как антагониста
    /// </summary>
    public bool IsSelected { get; set; }
  }
}
