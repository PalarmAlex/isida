using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ISIDA.Common;

namespace ISIDA.Sensors
{
  internal sealed class ListIntEqualityComparer : IEqualityComparer<List<int>>
  {
    public static readonly ListIntEqualityComparer Instance = new ListIntEqualityComparer();

    public bool Equals(List<int> x, List<int> y)
    {
      if (ReferenceEquals(x, y)) return true;
      if (x == null || y == null) return false;
      if (x.Count != y.Count) return false;
      for (int i = 0; i < x.Count; i++)
        if (x[i] != y[i]) return false;
      return true;
    }

    public int GetHashCode(List<int> list)
    {
      if (list == null) return 0;
      unchecked
      {
        int hash = 17;
        for (int i = 0; i < list.Count; i++)
          hash = hash * 31 + list[i];
        return hash;
      }
    }
  }

  /// <summary>
  /// Песочница для новых элементов, ожидающих подтверждения
  /// </summary>
  public class SensorSandbox<TElement> : IDisposable
  {
    #region Поля и свойства

    /// <summary>
    /// Хранилище элементов и счетчиков повторений
    /// </summary>
    protected readonly Dictionary<TElement, int> _items;

    /// <summary>
    /// Путь к файлу хранения данных песочницы
    /// </summary>
    protected readonly string _filePath;

    /// <summary>
    /// Синхронизатор для потокобезопасного доступа
    /// </summary>
    protected readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

    /// <summary>
    /// Флаг, указывающий были ли освобождены ресурсы
    /// </summary>
    protected bool _disposed = false;

    #endregion

    #region Инициализация

    /// <summary>
    /// Инициализирует новый экземпляр песочницы
    /// </summary>
    /// <param name="sandboxName">Имя песочницы (используется для именования файлов)</param>
    /// <param name="baseFolderPath">Базовый путь к директории данных</param>
    /// <exception cref="ArgumentNullException">Выбрасывается если logger равен null</exception>
    public SensorSandbox(string sandboxName, string baseFolderPath)
    {
      if (typeof(TElement) == typeof(List<int>))
        _items = new Dictionary<TElement, int>((IEqualityComparer<TElement>)(object)ListIntEqualityComparer.Instance);
      else
        _items = new Dictionary<TElement, int>();

      var folderPath = baseFolderPath;

      if (!Directory.Exists(folderPath))
      {
        Directory.CreateDirectory(folderPath);
      }

      _filePath = Path.Combine(folderPath, $"{sandboxName}Sandbox.dat");
    }

    #endregion

    #region Загрузка и сохранение

    /// <summary>
    /// Загружает данные песочницы из файла
    /// </summary>
    public void Load()
    {
      _lock.EnterWriteLock();
      try
      {
        if (!File.Exists(_filePath)) return;

        _items.Clear();

        var lines = File.ReadAllLines(_filePath);
        foreach (var line in lines)
        {
          if (string.IsNullOrWhiteSpace(line)) continue;

          var parts = line.Split(new[] { "|#|" }, StringSplitOptions.None);
          if (parts.Length != 2) continue;

          try
          {
            TElement element;
            if (typeof(TElement) == typeof(List<int>))
            {
              var ints = parts[0].Split(',')
                  .Where(s => !string.IsNullOrWhiteSpace(s))
                  .Select(s => int.Parse(s.Trim()))
                  .ToList();
              element = (TElement)(object)ints;
            }
            else
            {
              element = (TElement)Convert.ChangeType(parts[0], typeof(TElement));
            }
            var count = int.Parse(parts[1]);

            _items[element] = count;
          }
          catch
          {

          }
        }
      }
      catch
      {
        throw;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сохраняет данные песочницы в файл
    /// </summary>
    public void Save()
    {
      _lock.EnterReadLock();
      try
      {
        var tempFilePath = _filePath + ".tmp";

        // Сначала пишем во временный файл
        using (var writer = new StreamWriter(tempFilePath))
        {
          foreach (var item in _items)
          {
            try
            {
              string keyStr;
              if (item.Key is List<int> intList)
                keyStr = string.Join(",", intList);
              else
                keyStr = item.Key?.ToString() ?? "";
              var line = $"{keyStr}|#|{item.Value}";
              writer.WriteLine(line);
            }
            catch
            {

            }
          }
        }

        // Затем заменяем оригинальный файл
        if (File.Exists(_filePath))
          File.Delete(_filePath);

        File.Move(tempFilePath, _filePath);
      }
      catch
      {
        throw;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Работа с элементами

    /// <summary>
    /// Находит или добавляет элемент в песочницу
    /// </summary>
    public bool FindOrAdd(TElement element, out int currentCount)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_items.TryGetValue(element, out var count))
        {
          _items[element] = count + 1;
          currentCount = count + 1;
          return false;
        }

        _items.Add(element, 1);
        currentCount = 1;
        return true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Удаляет элемент из песочницы
    /// </summary>
    public bool Remove(TElement element)
    {
      _lock.EnterWriteLock();
      try
      {
        return _items.Remove(element);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Получает количество повторений элемента
    /// </summary>
    public int GetCount(TElement element)
    {
      _lock.EnterReadLock();
      try
      {
        return _items.TryGetValue(element, out var count) ? count : 0;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Полностью очищает песочницу
    /// </summary>
    public void Clear()
    {
      _lock.EnterWriteLock();
      try
      {
        _items.Clear();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Освобождение ресурсов

    /// <summary>
    /// Освобождает ресурсы песочницы
    /// </summary>
    /// <remarks>
    /// Выполняет сохранение данных перед освобождением ресурсов
    /// </remarks>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        Save();
      }
      catch
      {
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
      }
    }

    #endregion
  }
}