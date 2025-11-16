using System;
using System.IO;
using System.Threading;
using ISIDA.Common;

namespace ISIDA.Sensors
{
  /// <summary>
  /// Базовый класс для всех сенсорных каналов системы
  /// </summary>
  public abstract class SensorChannel : IDisposable
  {
    #region Поля и свойства

    /// <summary>
    /// Синхронизатор для потокобезопасного доступа к ресурсам канала
    /// </summary>
    protected readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

    /// <summary>
    /// Флаг, указывающий были ли освобождены ресурсы
    /// </summary>
    protected bool _disposed = false;

    /// <summary>
    /// Путь к директории хранения данных канала
    /// </summary>
    protected readonly string _channelFolderPath;

    #endregion

    #region Инициализация

    /// <summary>
    /// Инициализирует новый экземпляр сенсорного канала
    /// </summary>
    /// <param name="baseFolderPath">Базовый путь к директории сенсоров</param>
    /// <param name="channelSubfolder">Поддиректория канала</param>
    /// <exception cref="ArgumentNullException">Выбрасывается если logger равен null</exception>
    protected SensorChannel(string baseFolderPath, string channelSubfolder)
    {
      _channelFolderPath = baseFolderPath.EndsWith(channelSubfolder)
        ? baseFolderPath
        : Path.Combine(baseFolderPath, channelSubfolder);

      if (!Directory.Exists(_channelFolderPath))
      {
        Directory.CreateDirectory(_channelFolderPath);
      }
    }

    #endregion

    #region Освобождение ресурсов

    /// <summary>
    /// Освобождает ресурсы, используемые сенсорным каналом
    /// </summary>
    public virtual void Dispose()
    {
      if (_disposed) return;
      _lock?.Dispose();
      _disposed = true;
    }

    #endregion

  }
}