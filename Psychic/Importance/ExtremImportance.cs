namespace ISIDA.Psychic.Importance
{
  /// <summary>
  /// Объект с экстремальной значимостью — для выделения самого значимого объекта восприятия.
  /// По аналогии с BOT: extremImportance { objID, extremVal }.
  /// Значимости в коде обычно имеют значения от -10 до 10.
  /// </summary>
  public sealed class ExtremImportance
  {
    /// <summary>ID объекта значимости (ActionsImage ID — образ стимула или действия)</summary>
    public int ObjId { get; set; }

    /// <summary>Экстремальная значимость (значение StimulsEffect из эпизодической памяти, -10..10)</summary>
    public int ExtremVal { get; set; }

    /// <summary>Конструктор по умолчанию</summary>
    public ExtremImportance() { }

    /// <summary>Создать объект значимости с заданными ID объекта и значением</summary>
    /// <param name="objId">ID образа (ActionsImage)</param>
    /// <param name="extremVal">Значение значимости (-10..10)</param>
    public ExtremImportance(int objId, int extremVal)
    {
      ObjId = objId;
      ExtremVal = extremVal;
    }
  }
}
