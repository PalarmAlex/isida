using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using static ISIDA.Common.FileValidator;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Система управления цепочками рефлексов
  /// </summary>
  public sealed class ReflexChainsSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly GeneticReflexesSystem _geneticReflexesSystem;
    private bool _disposed = false;

    #region Инициализация

    private static ReflexChainsSystem _instance;

    /// <summary>Глобальный экземпляр системы цепочек рефлексов</summary>
    public static ReflexChainsSystem Instance => _instance ??
        throw new InvalidOperationException("ReflexChainsSystem не инициализирован.");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы цепочек рефлексов
    /// </summary>
    public static void InitializeInstance(GeneticReflexesSystem geneticReflexesSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("ReflexChainsSystem уже инициализирован.");

      _instance = new ReflexChainsSystem(geneticReflexesSystem);
    }

    private ReflexChainsSystem(GeneticReflexesSystem geneticReflexesSystem)
    {
      _geneticReflexesSystem = geneticReflexesSystem ??
          throw new ArgumentNullException(nameof(geneticReflexesSystem));

      try
      {
        EnsureDataDirectory();
        LoadReflexChains();
      }
      catch (Exception ex)
      {
        LogError($"Ошибка инициализации ReflexChainsSystem: {ex.Message}");
        throw;
      }
    }

    #endregion

    #region Структуры данных

    /// <summary>Звено цепочки рефлексов</summary>
    public class ChainLink
    {
      /// <summary>Уникальный идентификатор звена</summary>
      public int ID { get; set; }

      /// <summary>ID цепочки, к которой принадлежит звено</summary>
      public int ChainID { get; set; }

      /// <summary>ID рефлекса для выполнения</summary>
      public int ReflexID { get; set; }

      /// <summary>ID следующего звена при успешном выполнении</summary>
      public int SuccessNextLink { get; set; }

      /// <summary>ID следующего звена при неудачном выполнении</summary>
      public int FailureNextLink { get; set; }

      /// <summary>Флаг конечного звена (завершает цепочку)</summary>
      public bool IsTerminal { get; set; }

      /// <summary>Описание звена (для отладки)</summary>
      public string Description { get; set; }
    }

    /// <summary>Цепочка рефлексов</summary>
    public class ReflexChain
    {
      /// <summary>Уникальный идентификатор цепочки</summary>
      public int ID { get; set; }

      /// <summary>Наименование цепочки</summary>
      public string Name { get; set; }

      /// <summary>Описание цепочки</summary>
      public string Description { get; set; }

      /// <summary>Приоритет цепочки (выше = приоритетнее)</summary>
      public int Priority { get; set; }

      /// <summary>Звенья цепочки</summary>
      public List<ChainLink> Links { get; set; } = new List<ChainLink>();

      /// <summary>Флаг активности цепочки</summary>
      public bool IsActive { get; set; } = true;
    }

    #endregion

    #region Поля и свойства

    private const string ReflexChainsFileName = "ReflexChains";
    private readonly Dictionary<int, ReflexChain> _reflexChains = new Dictionary<int, ReflexChain>();
    private int _lastChainId = 0;
    private int _lastLinkId = 0;

    private string GetReflexChainsFilePath()
    {
      string reflexesPath = _geneticReflexesSystem.GetGeneticReflexesFilePath();
      string directory = Path.GetDirectoryName(reflexesPath);
      return Path.Combine(directory, $"{ReflexChainsFileName}.dat");
    }

    #endregion

    #region Управление цепочками

    /// <summary>Получает все цепочки рефлексов</summary>
    public ReadOnlyDictionary<int, ReflexChain> GetAllReflexChains()
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyDictionary<int, ReflexChain>(_reflexChains);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Получает цепочку по ID</summary>
    /// <param name="chainId">ID цепочки</param>
    /// <returns>Цепочка или null если не найдена</returns>
    public ReflexChain GetChain(int chainId)
    {
      _lock.EnterReadLock();
      try
      {
        return _reflexChains.TryGetValue(chainId, out var chain) ? chain : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Получает звенья цепочки</summary>
    /// <param name="chainId">ID цепочки</param>
    /// <returns>Список звеньев цепочки</returns>
    public List<ChainLink> GetChainLinks(int chainId)
    {
      _lock.EnterReadLock();
      try
      {
        return _reflexChains.TryGetValue(chainId, out var chain) ?
            new List<ChainLink>(chain.Links) : new List<ChainLink>();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Добавляет новую цепочку рефлексов</summary>
    /// <param name="name">Наименование цепочки</param>
    /// <param name="description">Описание цепочки</param>
    /// <param name="priority">Приоритет цепочки</param>
    /// <param name="links">Звенья цепочки</param>
    /// <returns>ID созданной цепочки и предупреждения</returns>
    public (int ChainId, string[] Warnings) AddReflexChain(
        string name, string description, int priority, List<ChainLink> links)
    {
      var warnings = new List<string>();

      if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Наименование цепочки не может быть пустым", nameof(name));

      if (links == null || !links.Any())
        throw new ArgumentException("Цепочка должна содержать хотя бы одно звено", nameof(links));

      // Проверяем существование рефлексов
      var allReflexes = _geneticReflexesSystem.GetAllGeneticReflexesList();
      foreach (var link in links)
      {
        if (!allReflexes.Any(r => r.Id == link.ReflexID))
        {
          warnings.Add($"Рефлекс с ID {link.ReflexID} не существует");
        }
      }

      _lock.EnterWriteLock();
      try
      {
        int newId = ++_lastChainId;

        var chain = new ReflexChain
        {
          ID = newId,
          Name = name,
          Description = description,
          Priority = priority,
          Links = links,
          IsActive = true
        };

        _reflexChains.Add(newId, chain);
        return (newId, warnings.ToArray());
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Удаляет цепочку рефлексов</summary>
    /// <param name="chainId">ID цепочки для удаления</param>
    /// <returns>True если цепочка удалена, иначе False</returns>
    public bool RemoveReflexChain(int chainId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_reflexChains.ContainsKey(chainId))
          throw new KeyNotFoundException($"Цепочка с ID {chainId} не найдена");

        return _reflexChains.Remove(chainId);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Проверяет, используется ли рефлекс в цепочках</summary>
    /// <param name="reflexId">ID рефлекса</param>
    /// <returns>True если рефлекс используется в цепочках</returns>
    public bool IsReflexUsedInChains(int reflexId)
    {
      _lock.EnterReadLock();
      try
      {
        return _reflexChains.Values
            .Any(chain => chain.Links.Any(link => link.ReflexID == reflexId));
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Проверяет существование цепочки и звена</summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="linkId">ID звена</param>
    /// <returns>True если цепочка и звено существуют</returns>
    public bool ChainAndLinkExist(int chainId, int linkId)
    {
      _lock.EnterReadLock();
      try
      {
        if (!_reflexChains.TryGetValue(chainId, out var chain))
          return false;

        return chain.Links.Any(l => l.ID == linkId);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Работа с файлами

    private void EnsureDataDirectory()
    {
      string directory = Path.GetDirectoryName(GetReflexChainsFilePath());
      if (!Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }
    }

    private bool IsValidReflexChainsFile(string filePath)
    {
      if (!File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidReflexChainsFile(lines);
      }
      catch
      {
        return false;
      }
    }

    private bool IsValidReflexChainsFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 3)
          return false;

        return true;
      }

      return true;
    }

    private void LoadReflexChains()
    {
      string filePath = GetReflexChainsFilePath();

      if (!IsValidReflexChainsFile(filePath))
      {
        CreateDefaultReflexChainsFile();
        return;
      }

      try
      {
        _lock.EnterWriteLock();
        try
        {
          _reflexChains.Clear();
          _lastChainId = 0;
          _lastLinkId = 0;

          ReflexChain currentChain = null;

          foreach (var line in File.ReadLines(filePath))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');

            // Заголовок цепочки
            if (parts.Length >= 5 && parts[0] == "CHAIN")
            {
              if (int.TryParse(parts[1], out int chainId) &&
                  int.TryParse(parts[4], out int priority))
              {
                currentChain = new ReflexChain
                {
                  ID = chainId,
                  Name = parts[2],
                  Description = parts[3],
                  Priority = priority,
                  IsActive = true,
                  Links = new List<ChainLink>()
                };

                _reflexChains[chainId] = currentChain;
                if (chainId > _lastChainId)
                  _lastChainId = chainId;
              }
              continue;
            }

            // Звено цепочки
            if (parts.Length >= 7 && parts[0] == "LINK" && currentChain != null)
            {
              if (int.TryParse(parts[1], out int linkId) &&
                  int.TryParse(parts[2], out int reflexId) &&
                  int.TryParse(parts[3], out int successNext) &&
                  int.TryParse(parts[4], out int failureNext) &&
                  bool.TryParse(parts[5], out bool isTerminal))
              {
                var link = new ChainLink
                {
                  ID = linkId,
                  ChainID = currentChain.ID,
                  ReflexID = reflexId,
                  SuccessNextLink = successNext,
                  FailureNextLink = failureNext,
                  IsTerminal = isTerminal,
                  Description = parts[6]
                };

                currentChain.Links.Add(link);
                if (linkId > _lastLinkId)
                  _lastLinkId = linkId;
              }
            }
          }
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }
      catch (Exception ex)
      {
        LogError($"Error loading reflex chains: {ex.Message}");
        CreateDefaultReflexChainsFile();
      }
    }

    private void CreateDefaultReflexChainsFile()
    {
      EnsureDataDirectory();

      var lines = new List<string>
    {
        FileHeaders.ReflexChainsFormat,
        FileHeaders.ReflexChainsChain,
        FileHeaders.ReflexChainsLink,
        FileHeaders.ReflexChainsChainDesc,
        FileHeaders.ReflexChainsNameDesc,
        FileHeaders.ReflexChainsPriorityDesc,
        FileHeaders.ReflexChainsLinkDesc,
        FileHeaders.ReflexChainsReflexDesc,
        FileHeaders.ReflexChainsSuccessDesc,
        FileHeaders.ReflexChainsFailureDesc,
        FileHeaders.ReflexChainsTerminalDesc,
        "# Пример цепочки 'Охота':",
        "CHAIN|1|Охота|Цепочка охотничьего поведения|10",
        "LINK|1|5|2|1|false|Обнаружение добычи",
        "LINK|2|6|3|1|false|Преследование",
        "LINK|3|7|0|0|true|Захват добычи"
    };

      File.WriteAllLines(GetReflexChainsFilePath(), lines);
    }

    /// <summary>Сохраняет цепочки рефлексов в файл</summary>
    /// <returns>Результат операции сохранения</returns>
    public (bool Success, string ErrorMessage) SaveReflexChains()
    {
      _lock.EnterReadLock();
      try
      {
        var lines = new List<string>
        {
            FileHeaders.ReflexChainsFormat,
            FileHeaders.ReflexChainsChain,
            FileHeaders.ReflexChainsLink,
            FileHeaders.ReflexChainsChainDesc,
            FileHeaders.ReflexChainsNameDesc,
            FileHeaders.ReflexChainsPriorityDesc,
            FileHeaders.ReflexChainsLinkDesc,
            FileHeaders.ReflexChainsReflexDesc,
            FileHeaders.ReflexChainsSuccessDesc,
            FileHeaders.ReflexChainsFailureDesc,
            FileHeaders.ReflexChainsTerminalDesc
        };

        foreach (var chain in _reflexChains.Values.OrderBy(c => c.ID))
        {
          // Заголовок цепочки
          lines.Add($"CHAIN|{chain.ID}|{chain.Name}|{chain.Description}|{chain.Priority}");

          // Звенья
          foreach (var link in chain.Links.OrderBy(l => l.ID))
          {
            lines.Add($"LINK|{link.ID}|{link.ReflexID}|{link.SuccessNextLink}|{link.FailureNextLink}|{link.IsTerminal}|{link.Description}");
          }

          lines.Add(""); // Разделитель
        }

        var result = FileValidator.SafeSaveFile(
            GetReflexChainsFilePath(),
            lines,
            FileValidator.IsValidReflexChainsFile,
            minLinesCount: 10, // Учитываем все строки шапки
            fileDescription: "цепочек рефлексов");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Вспомогательные методы

    private static void LogInfo(string message)
    {
      Debug.WriteLine($"[ReflexChainsSystem] INFO: {message}");
    }

    private static void LogError(string message)
    {
      FileValidator.LogError($"[ReflexChainsSystem] ERROR: {message}");
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы системы цепочек рефлексов</summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        SaveReflexChains();
      }
      catch (Exception ex)
      {
        LogError($"Error during disposal: {ex.Message}");
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