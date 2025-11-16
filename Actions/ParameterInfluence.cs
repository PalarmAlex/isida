using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ISIDA.Actions
{
  /// <summary>
  /// Класс для редактирования влияний действий на параметры
  /// </summary>
  public class ParameterInfluence
  {
    private int _effect;

    /// <summary>
    /// ID параметра
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Наименование параметра
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Величина влияния (-10..+10)
    /// </summary>
    public int Effect
    {
      get => _effect;
      set
      {
        if (value < -10 || value > 10)
          throw new ArgumentOutOfRangeException(nameof(value),
              "Значение влияния должно быть в диапазоне от -10 до 10");

        _effect = value;
      }
    }
  }
}
