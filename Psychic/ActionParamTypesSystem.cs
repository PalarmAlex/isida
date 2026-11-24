using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ISIDA.Common;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Справочник типов параметров моторных действий: <c>paramId → type</c>.
  /// <para>
  /// Тип параметра определяет, как его значение интерпретируется и валидируется в среде-хосте
  /// (Velum, AIStudio). Поддерживаемые типы: <c>text</c>, <c>number</c>, <c>int</c>,
  /// <c>bool</c>, <c>enum</c>, <c>date</c>.
  /// </para>
  /// <para>
  /// Система управляемая: добавление/удаление записей из кода, сохранение на диск.
  /// Созданный по умолчанию справочник пуст (все параметры без типа трактуются как текст).
  /// </para>
  /// </summary>
  public sealed class ActionParamTypesSystem : IDisposable
  {
    private readonly string _dataPath;
    private readonly Dictionary<int, ActionParamTypeRecord> _byId =
        new Dictionary<int, ActionParamTypeRecord>();
    private readonly Dictionary<string, int> _byName =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    #region Константы

    private const string FileName = "action_param_types.dat";

    #endregion

    #region Инициализация

    private static ActionParamTypesSystem _instance;

    /// <summary>
    /// Глобальный экземпляр справочника типов параметров моторных действий.
    /// </summary>
    public static ActionParamTypesSystem Instance => _instance ??
        throw new InvalidOperationException("ActionParamTypesSystem не инициализирован.");

    /// <summary>
    /// Признак инициализации.
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализировать экземпляр.
    /// </summary>
    /// <param name="dataFolderPath">Путь к корню каталога <c>Data</c>.</param>
    /// <exception cref="InvalidOperationException">Выбрасывается, если система уже была инициализирована ранее.</exception>
    public static void InitializeInstance(string dataFolderPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("ActionParamTypesSystem уже инициализирован.");

      _instance = new ActionParamTypesSystem(dataFolderPath);
    }

    private ActionParamTypesSystem(string dataFolderPath)
    {
      _dataPath = IsidaDataPaths.ResolvePsychicSubmoduleFolder(dataFolderPath, "Automatism");
      EnsureDirectory();

      // Создаём файл по умолчанию, если не существует
      var path = Path.Combine(_dataPath, FileName);
      if (!File.Exists(path))
        CreateDefaultFile(path);

      Load();
      RebuildIndexes();
    }

    /// <summary>
    /// Создать файл справочника по умолчанию (только шапка).
    /// </summary>
    private void CreateDefaultFile(string path)
    {
      var lines = new List<string>
      {
        FileValidator.FileHeaders.ActionParamTypesFormat,
        FileValidator.FileHeaders.ActionParamTypesNameDesc,
        FileValidator.FileHeaders.ActionParamTypesTypeDesc
      };
      File.WriteAllLines(path, lines);
    }

    #endregion

    #region Загрузка / Сохранение

    /// <summary>
    /// Загрузить справочник из файла.
    /// </summary>
    private void Load()
    {
      _byId.Clear();
      _byName.Clear();

      var path = Path.Combine(_dataPath, FileName);
      if (!File.Exists(path) || !FileValidator.IsValidActionParamTypesFile(path))
        return;

      foreach (var line in File.ReadLines(path))
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#"))
          continue;

        var p = t.Split('|');
        if (p.Length < 2)
          continue;

        if (!int.TryParse(p[0], out int id) || id < 0)
          continue;

        string name = p[1].Trim();
        if (string.IsNullOrWhiteSpace(name))
          continue;

        string type = p.Length > 2 ? p[2].Trim() : string.Empty;
        // Дефолтный тип для обратной совместимости
        if (string.IsNullOrEmpty(type))
          type = "text";

        _byId[id] = new ActionParamTypeRecord
        {
          Id = id,
          Name = name,
          Type = type
        };
      }
    }

    /// <summary>
    /// Сохранить справочник на диск.
    /// </summary>
    /// <returns>Успешность сохранения.</returns>
    public (bool Success, string Error) Save()
    {
      try
      {
        EnsureDirectory();
        var path = Path.Combine(_dataPath, FileName);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.ActionParamTypesFormat,
          FileValidator.FileHeaders.ActionParamTypesNameDesc,
          FileValidator.FileHeaders.ActionParamTypesTypeDesc
        };

        foreach (var kv in _byId.OrderBy(x => x.Key))
        {
          var rec = kv.Value;
          lines.Add($"{rec.Id}|{rec.Name}|{rec.Type}");
        }

        var result = FileValidator.SafeSaveFile(
            path,
            lines,
            p => FileValidator.IsValidActionParamTypesFile(p),
            minLinesCount: 3,
            fileDescription: "справочника типов параметров моторных действий");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    #endregion

    #region Управление записями

    /// <summary>
    /// Получить запись по ID.
    /// </summary>
    /// <param name="id">Идентификатор параметра.</param>
    /// <returns>Запись или <c>null</c>.</returns>
    public ActionParamTypeRecord GetById(int id)
    {
      return _byId.TryGetValue(id, out var r) ? r : null;
    }

    /// <summary>
    /// Получить тип параметра по имени.
    /// </summary>
    /// <param name="name">Имя параметра (регистронезависимо).</param>
    /// <returns>Запись или <c>null</c>.</returns>
    public ActionParamTypeRecord GetByName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
        return null;

      _byName.TryGetValue(name.Trim(), out int id);
      return id > 0 ? GetById(id) : null;
    }

    /// <summary>
    /// Получить список всех записей.
    /// </summary>
    public IReadOnlyList<ActionParamTypeRecord> GetAll()
    {
      return _byId.Values.OrderBy(r => r.Id).ToList();
    }

    /// <summary>
    /// Добавить или обновить запись.
    /// </summary>
    /// <param name="id">Идентификатор параметра.</param>
    /// <param name="name">Имя параметра.</param>
    /// <param name="type">Тип (<c>text</c>/<c>number</c>/<c>int</c>/<c>bool</c>/<c>enum</c>/<c>date</c>).</param>
    /// <returns>Обновлённая запись.</returns>
    public ActionParamTypeRecord SetType(int id, string name, string type)
    {
      if (string.IsNullOrWhiteSpace(name))
        name = string.Empty;

      type = (type ?? string.Empty).Trim().ToLowerInvariant();
      if (string.IsNullOrEmpty(type))
        type = "text";

      ActionParamTypeRecord rec;
      if (_byId.TryGetValue(id, out rec))
      {
        rec.Name = name;
        rec.Type = type;
      }
      else
      {
        rec = new ActionParamTypeRecord { Id = id, Name = name, Type = type };
        _byId[id] = rec;
      }

      RebuildIndexes();
      return rec;
    }

    /// <summary>
    /// Удалить запись по ID.
    /// </summary>
    /// <param name="id">Идентификатор параметра.</param>
    public void Remove(int id)
    {
      _byId.Remove(id);
      RebuildIndexes();
    }

    /// <summary>
    /// Удалить запись по имени параметра.
    /// </summary>
    /// <param name="name">Имя параметра.</param>
    /// <returns>Была ли запись удалена.</returns>
    public bool RemoveByName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
        return false;

      if (_byName.TryGetValue(name.Trim(), out int id) && _byId.Remove(id))
      {
        RebuildIndexes();
        return true;
      }

      return false;
    }

    /// <summary>
    /// Проверить, существует ли запись.
    /// </summary>
    /// <param name="id">Идентификатор параметра.</param>
    /// <returns><c>true</c>, если запись существует.</returns>
    public bool Exists(int id)
    {
      return _byId.ContainsKey(id);
    }

    /// <summary>
    /// Перестроить индексы (имя → ID) после изменений.
    /// </summary>
    private void RebuildIndexes()
    {
      _byName.Clear();
      foreach (var kv in _byId)
      {
        if (!string.IsNullOrEmpty(kv.Value.Name))
          _byName[kv.Value.Name] = kv.Key;
      }
    }

    #endregion

    #region Утилиты

    /// <summary>
    /// Создать каталог данных, если не существует.
    /// </summary>
    private void EnsureDirectory()
    {
      if (!string.IsNullOrEmpty(_dataPath) && !Directory.Exists(_dataPath))
        Directory.CreateDirectory(_dataPath);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы. Сохраняет справочник на диск.
    /// </summary>
    public void Dispose()
    {
      if (_disposed)
        return;

      try
      {
        Save();
      }
      catch (Exception ex)
      {
        Logger.Error("ActionParamTypesSystem.Dispose: " + ex.Message);
      }
      finally
      {
        _disposed = true;
        _instance = null;
      }
    }

    #endregion
  }

  /// <summary>
  /// Запись справочника типов параметров моторных действий.
  /// </summary>
  public class ActionParamTypeRecord
  {
    /// <summary>
    /// Идентификатор параметра.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Имя параметра (ключ, по которому хост ищет тип).
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Тип значения: <c>text</c>, <c>number</c>, <c>int</c>, <c>bool</c>, <c>enum</c>, <c>date</c>.
    /// </summary>
    public string Type { get; set; }
  }
}