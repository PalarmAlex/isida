using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ISIDA.Common
{
  /// <summary>
  /// Интерфейс для объектов, которые отслеживают свое состояние освобождения
  /// </summary>
  public interface IDisposableState : IDisposable
  {
    /// <summary>
    /// Флаг, указывающий, что объект был освобожден
    /// </summary>
    bool IsDisposed { get; }
  }
}
