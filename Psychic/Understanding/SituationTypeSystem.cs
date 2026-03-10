using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Справочник типов ситуаций (редактируется на пульте).
  /// Id неизменяем после использования в SituationImage.
  /// </summary>
  public sealed class SituationTypeSystem : IDisposable
  {
    private readonly string _dataPath;
    private readonly Dictionary<int, SituationTypeRecord> _byId = new Dictionary<int, SituationTypeRecord>();
    private bool _disposed;

    #region Инициализация

    private static SituationTypeSystem _instance;

    /// <summary>Глобальный экземпляр</summary>
    public static SituationTypeSystem Instance => _instance ??
        throw new InvalidOperationException("SituationTypeSystem не инициализирован.");

    /// <summary>Признак инициализации</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>Инициализировать экземпляр</summary>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("SituationTypeSystem уже инициализирован.");
      _instance = new SituationTypeSystem(psychicDataPath);
    }

    private SituationTypeSystem(string psychicDataPath)
    {
      _dataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Understanding")
          : Path.Combine(psychicDataPath, "Understanding");
      EnsureDirectory();
      Load();
      if (_byId.Count == 0)
        CreateDefaultTypes();
    }

    #endregion

    #region Доступ

    /// <summary>Получить тип по ID</summary>
    public SituationTypeRecord GetById(int id)
    {
      return _byId.TryGetValue(id, out var r) ? r : null;
    }

    /// <summary>Получить тип по коду</summary>
    public SituationTypeRecord GetByCode(string code)
    {
      if (string.IsNullOrEmpty(code)) return null;
      return _byId.Values.FirstOrDefault(r =>
          string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Все типы</summary>
    public IReadOnlyList<SituationTypeRecord> GetAll()
    {
      return _byId.Values.OrderBy(r => r.Id).ToList();
    }

    /// <summary>Существует ли тип с таким ID</summary>
    public bool Exists(int id)
    {
      return _byId.ContainsKey(id);
    }

    #endregion

    #region Load / Save

    private const string FileName = "situation_types.dat";

    private void EnsureDirectory()
    {
      if (!string.IsNullOrEmpty(_dataPath) && !Directory.Exists(_dataPath))
        Directory.CreateDirectory(_dataPath);
    }

    private void Load()
    {
      var path = Path.Combine(_dataPath, FileName);
      _byId.Clear();
      if (!File.Exists(path) || !FileValidator.IsValidSituationTypeFile(path))
        return;

      foreach (var line in File.ReadLines(path))
      {
        var t = line?.Trim();
        if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#")) continue;
        var p = t.Split('|');
        if (p.Length < 2) continue;
        if (!int.TryParse(p[0], out int id) || id <= 0) continue;
        var name = p.Length > 1 ? p[1] : "";
        var code = p.Length > 2 ? p[2] : "";
        _byId[id] = new SituationTypeRecord { Id = id, Name = name, Code = code };
      }
    }

    /// <summary>Сохранить справочник</summary>
    public (bool Success, string Error) Save()
    {
      try
      {
        EnsureDirectory();
        var path = Path.Combine(_dataPath, FileName);
        var lines = new List<string>
        {
          FileValidator.FileHeaders.SituationTypesFormat,
          FileValidator.FileHeaders.SituationTypesDesc
        };
        foreach (var r in _byId.Values.OrderBy(x => x.Id))
          lines.Add($"{r.Id}|{r.Name ?? ""}|{r.Code ?? ""}");

        var result = FileValidator.SafeSaveFile(
            path,
            lines,
            p => FileValidator.IsValidSituationTypeFile(p),
            minLinesCount: 2,
            fileDescription: "справочника типов ситуаций");

        return result.Success ? (true, null) : (false, result.ErrorMessage);
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    private void CreateDefaultTypes()
    {
      var defaults = new[]
      {
        (1, "Ответное действие", "ResponseAction"),
        (2, "Запуск автоматизма", "AutomatizmRun"),
        (3, "Нужно осмысление", "NeedThinking"),
        (4, "Экспериментировать", "Experiment"),
        (5, "Игнор оператора", "OperatorIgnore")
      };
      foreach (var d in defaults)
        _byId[d.Item1] = new SituationTypeRecord { Id = d.Item1, Name = d.Item2, Code = d.Item3 };
      Save();
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы, сохраняет справочник типов ситуаций на диск</summary>
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
    }

    #endregion
  }
}
