﻿using ISIDA.Common;
using ISIDA.Actions;
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
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private readonly GeneticReflexesSystem _geneticReflexesSystem;
    private bool _disposed = false;

    /// <summary>
    /// Событие удаления цепочки
    /// </summary>
    public event Action<int> ReflexChainDeleted;

    private void OnReflexChainDeleted(int chainId)
    {
      ReflexChainDeleted?.Invoke(chainId);
    }

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
    public static void InitializeInstance(GeneticReflexesSystem geneticReflexesSystem, AdaptiveActionsSystem adaptiveActionsSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("ReflexChainsSystem уже инициализирован.");

      _instance = new ReflexChainsSystem(geneticReflexesSystem, adaptiveActionsSystem);
    }

    private ReflexChainsSystem(GeneticReflexesSystem geneticReflexesSystem, AdaptiveActionsSystem adaptiveActionsSystem)
    {
      _geneticReflexesSystem = geneticReflexesSystem ??
    throw new ArgumentNullException(nameof(geneticReflexesSystem));

      _adaptiveActionsSystem = adaptiveActionsSystem ??
          throw new ArgumentNullException(nameof(adaptiveActionsSystem));

      try
      {
        EnsureDataDirectory();
        LoadReflexChains();
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка инициализации ReflexChainsSystem: {ex.Message}");
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

      /// <summary>ID адаптивного действия для выполнения</summary>
      public int ActionId { get; set; }

      /// <summary>ID следующего звена при успешном выполнении</summary>
      public int SuccessNextLink { get; set; }

      /// <summary>ID следующего звена при неудачном выполнении</summary>
      public int FailureNextLink { get; set; }

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

      /// <summary>Звенья цепочки</summary>
      public List<ChainLink> Links { get; set; } = new List<ChainLink>();
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

    /// <summary>
    /// Получает все цепочки рефлексов
    /// </summary>
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
    /// <param name="links">Звенья цепочки</param>
    /// <returns>ID созданной цепочки и предупреждения</returns>
    public (int ChainId, string[] Warnings) AddReflexChain(
        string name, string description, List<ChainLink> links)
    {
      var warnings = new List<string>();

      if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Наименование цепочки не может быть пустым", nameof(name));

      if (links == null || !links.Any())
        throw new ArgumentException("Цепочка должна содержать хотя бы одно звено", nameof(links));

      var allActions = _adaptiveActionsSystem.GetAllAdaptiveActionsList();
      foreach (var link in links)
      {
        if (!allActions.Any(a => a.Id == link.ActionId))
          warnings.Add($"Адаптивное действие с ID {link.ActionId} не существует");

        int duplicateCount = links.Count(l => l.ActionId == link.ActionId);
        if (duplicateCount > 1)
          warnings.Add($"Действие {link.ActionId} повторяется {duplicateCount} раз в цепочке");
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
          Links = links
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
        if (!_reflexChains.TryGetValue(chainId, out var chain))
          throw new KeyNotFoundException($"Цепочка с ID {chainId} не найдена");

        var linkIds = chain.Links.Select(l => l.ID).ToList();
        bool removed = _reflexChains.Remove(chainId);

        if (removed)
        {
          SaveReflexChainsCore();
          OnReflexChainDeleted(chainId);
          Logger.Info($"Цепочка {chainId} удалена. Удалено звеньев: {linkIds.Count}");
        }

        return removed;
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка при удалении цепочки {chainId}: {ex.Message}");
        return false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Проверяет, используется ли действие в цепочках</summary>
    /// <param name="actionId">ID адаптивного действия</param>
    /// <returns>True если действие используется в цепочках</returns>
    public bool IsActionUsedInChains(int actionId)
    {
      _lock.EnterReadLock();
      try
      {
        return _reflexChains.Values
            .Any(chain => chain.Links.Any(link => link.ActionId == actionId));
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

    /// <summary>Добавляет звено к существующей цепочке</summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="actionId">ID адаптивного действия</param>
    /// <param name="successNextLink">ID следующего звена при успехе</param>
    /// <param name="failureNextLink">ID следующего звена при неудаче</param>
    /// <param name="description">Описание звена</param>
    /// <returns>ID созданного звена и предупреждения</returns>
    public (int LinkId, string[] Warnings) AddChainLink(
        int chainId, int actionId, int successNextLink,
        int failureNextLink, string description)
    {
      var warnings = new List<string>();

      _lock.EnterWriteLock();
      try
      {
        if (!_reflexChains.TryGetValue(chainId, out var chain))
          throw new KeyNotFoundException($"Цепочка с ID {chainId} не найдена");

        var allActions = _adaptiveActionsSystem.GetAllAdaptiveActionsList();
        if (!allActions.Any(a => a.Id == actionId))
          warnings.Add($"Адаптивное действие с ID {actionId} не существует");

        if (successNextLink != 0)
        {
          var existingLink = chain.Links.FirstOrDefault(l => l.ID == successNextLink);
          if (existingLink == null)
            warnings.Add($"Следующее звено при успехе (ID:{successNextLink}) не найдено в цепочке");
          else if (successNextLink <= chain.Links.Max(l => l.ID))
            warnings.Add($"Ссылка на предыдущее звено (ID:{successNextLink}) запрещена");
        }

        if (failureNextLink != 0)
        {
          var existingLink = chain.Links.FirstOrDefault(l => l.ID == failureNextLink);
          if (existingLink == null)
            warnings.Add($"Следующее звено при неудаче (ID:{failureNextLink}) не найдено в цепочке");
          else if (failureNextLink <= chain.Links.Max(l => l.ID))
            warnings.Add($"Ссылка на предыдущее звено (ID:{failureNextLink}) запрещена");
        }

        int newLinkId = ++_lastLinkId;

        var link = new ChainLink
        {
          ID = newLinkId,
          ChainID = chainId,
          ActionId = actionId,
          SuccessNextLink = successNextLink,
          FailureNextLink = failureNextLink,
          Description = description ?? $"Звено {newLinkId}"
        };

        chain.Links.Add(link);
        return (newLinkId, warnings.ToArray());
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обновляет существующее звено цепочки
    /// </summary>
    public (bool Success, string[] Warnings) UpdateChainLink(
        int chainId, int linkId, int actionId, int successNextLink,
        int failureNextLink, string description)
    {
      var warnings = new List<string>();

      _lock.EnterWriteLock();
      try
      {
        if (!_reflexChains.TryGetValue(chainId, out var chain))
          throw new KeyNotFoundException($"Цепочка с ID {chainId} не найдена");

        var link = chain.Links.FirstOrDefault(l => l.ID == linkId);
        if (link == null)
          throw new KeyNotFoundException($"Звено с ID {linkId} не найдено в цепочке {chainId}");

        var allActions = _adaptiveActionsSystem.GetAllAdaptiveActionsList();
        if (!allActions.Any(a => a.Id == actionId))
          warnings.Add($"Адаптивное действие с ID {actionId} не существует");

        if (successNextLink != 0 && successNextLink != linkId)
        {
          var existingLink = chain.Links.FirstOrDefault(l => l.ID == successNextLink);
          if (existingLink == null)
            warnings.Add($"Следующее звено при успехе (ID:{successNextLink}) не найдено");
          else if (successNextLink <= linkId)
            warnings.Add($"Ссылка на предыдущее звено (ID:{successNextLink}) запрещена");
        }

        if (failureNextLink != 0 && failureNextLink != linkId)
        {
          var existingLink = chain.Links.FirstOrDefault(l => l.ID == failureNextLink);
          if (existingLink == null)
            warnings.Add($"Следующее звено при неудаче (ID:{failureNextLink}) не найдено");
          else if (failureNextLink <= linkId)
            warnings.Add($"Ссылка на предыдущее звено (ID:{failureNextLink}) запрещена");
        }

        link.ActionId = actionId;
        link.SuccessNextLink = successNextLink;
        link.FailureNextLink = failureNextLink;
        link.Description = description ?? link.Description;

        SaveReflexChainsCore();
        return (true, warnings.ToArray());
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Удаляет звено из цепочки</summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="linkId">ID звена</param>
    /// <param name="reconnectLinks">Обновлять ли ссылки других звеньев на удаляемое</param>
    /// <returns>True если звено удалено</returns>
    /// <exception cref="InvalidOperationException">Если удаление последнего звена</exception>
    public bool RemoveChainLink(int chainId, int linkId, bool reconnectLinks = false)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_reflexChains.TryGetValue(chainId, out var chain))
          throw new KeyNotFoundException($"Цепочка с ID {chainId} не найдена");

        var linkToRemove = chain.Links.FirstOrDefault(l => l.ID == linkId);
        if (linkToRemove == null)
          return false;

        if (chain.Links.Count <= 1)
        {
          throw new InvalidOperationException(
              $"Невозможно удалить последнее звено {linkId} из цепочки {chainId}. " +
              "Цепочка должна содержать хотя бы одно звено. " +
              "Для удаления цепочки используйте метод RemoveReflexChain().");
        }

        var referencingLinks = chain.Links.Where(l =>
            l.SuccessNextLink == linkId || l.FailureNextLink == linkId).ToList();

        if (referencingLinks.Any())
        {
          if (!reconnectLinks)
            throw new InvalidOperationException(
                $"Звено {linkId} используется другими звеньями: " +
                string.Join(", ", referencingLinks.Select(l => l.ID)));

          foreach (var refLink in referencingLinks)
          {
            if (refLink.SuccessNextLink == linkId)
              refLink.SuccessNextLink = 0;
            if (refLink.FailureNextLink == linkId)
              refLink.FailureNextLink = 0;
          }
        }

        bool removed = chain.Links.Remove(linkToRemove);
        if (removed)
          SaveReflexChainsCore();

        return removed;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Проверяет целостность цепочки
    /// </summary>
    public (bool IsValid, string[] Issues) ValidateChain(int chainId)
    {
      var issues = new List<string>();

      _lock.EnterReadLock();
      try
      {
        if (!_reflexChains.TryGetValue(chainId, out var chain))
        {
          issues.Add($"Цепочка с ID {chainId} не найдена");
          return (false, issues.ToArray());
        }

        if (chain.Links.Count < 1)
        {
          issues.Add("Цепочка должна содержать хотя бы одно звено");
          return (false, issues.ToArray());
        }

        var allActions = _adaptiveActionsSystem.GetAllAdaptiveActionsList();
        foreach (var link in chain.Links)
        {
          if (!allActions.Any(a => a.Id == link.ActionId))
            issues.Add($"Адаптивное действие {link.ActionId} в звене {link.ID} не существует");

          if (link.SuccessNextLink != 0 && link.SuccessNextLink <= link.ID)
            issues.Add($"Звено {link.ID} ссылается на предыдущее звено {link.SuccessNextLink}");

          if (link.FailureNextLink != 0 && link.FailureNextLink <= link.ID)
            issues.Add($"Звено {link.ID} ссылается на предыдущее звено {link.FailureNextLink}");

          if (link.SuccessNextLink != 0 && link.SuccessNextLink != link.ID &&
              !chain.Links.Any(l => l.ID == link.SuccessNextLink))
            issues.Add($"Звено {link.ID}: следующее при успехе {link.SuccessNextLink} не найдено");

          if (link.FailureNextLink != 0 && link.FailureNextLink != link.ID &&
              !chain.Links.Any(l => l.ID == link.FailureNextLink))
            issues.Add($"Звено {link.ID}: следующее при неудаче {link.FailureNextLink} не найдено");
        }

        var terminalLinks = chain.Links.Where(l =>
            l.SuccessNextLink == 0 && l.FailureNextLink == 0).ToList();

        if (terminalLinks.Count == 0)
        {
          issues.Add("Цепочка не содержит конечных звеньев (бесконечный цикл)");
        }

        return (!issues.Any(), issues.ToArray());
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

    /// <summary>
    /// Загружает цепочки рефлексов из файла
    /// </summary>
    private void LoadReflexChains()
    {
      string filePath = GetReflexChainsFilePath();

      if (!File.Exists(filePath))
      {
        CreateDefaultReflexChainsFile();
        return;
      }

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

            if (parts.Length >= 2 && parts[0] == "CHAIN")
            {
              if (int.TryParse(parts[1], out int chainId) && chainId > 0)
              {
                currentChain = new ReflexChain
                {
                  ID = chainId,
                  Name = parts.Length > 2 ? parts[2] : "",
                  Description = parts.Length > 3 ? parts[3] : "",
                  Links = new List<ChainLink>()
                };

                _reflexChains[chainId] = currentChain;
                if (chainId > _lastChainId)
                  _lastChainId = chainId;
              }
              continue;
            }

            if (parts.Length >= 5 && parts[0] == "LINK" && currentChain != null)
            {
              if (int.TryParse(parts[1], out int linkId) &&
                  int.TryParse(parts[2], out int actionId) &&
                  int.TryParse(parts[3], out int successNext) &&
                  int.TryParse(parts[4], out int failureNext))
              {
                var link = new ChainLink
                {
                  ID = linkId,
                  ChainID = currentChain.ID,
                  ActionId = actionId,
                  SuccessNextLink = successNext,
                  FailureNextLink = failureNext,
                  Description = parts.Length > 5 ? parts[5] : ""
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
        Logger.Error($"Error loading reflex chains: {ex.Message}");
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
        FileHeaders.ReflexChainsLinkDesc,
        FileHeaders.ReflexChainsReflexDesc,
        FileHeaders.ReflexChainsSuccessDesc,
        FileHeaders.ReflexChainsFailureDesc
    };

      File.WriteAllLines(GetReflexChainsFilePath(), lines);
    }

    /// <summary>Сохраняет цепочки рефлексов в файл</summary>
    public (bool Success, string ErrorMessage) SaveReflexChains()
    {
      _lock.EnterWriteLock();
      try
      {
        return SaveReflexChainsCore();
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка при сохранении цепочек рефлексов: {ex.Message}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    private (bool Success, string ErrorMessage) SaveReflexChainsCore()
    {
      var lines = new List<string>
      {
        FileHeaders.ReflexChainsFormat,
        FileHeaders.ReflexChainsChain,
        FileHeaders.ReflexChainsLink,
        FileHeaders.ReflexChainsChainDesc,
        FileHeaders.ReflexChainsNameDesc,
        FileHeaders.ReflexChainsLinkDesc,
        FileHeaders.ReflexChainsReflexDesc,
        FileHeaders.ReflexChainsSuccessDesc,
        FileHeaders.ReflexChainsFailureDesc
      };
      lines.Add("");

      if (_reflexChains.Count > 0)
      {
        foreach (var chain in _reflexChains.Values.OrderBy(c => c.ID))
        {
          string name = chain.Name ?? "";
          string description = chain.Description ?? "";

          lines.Add($"CHAIN|{chain.ID}|{name}|{description}");

          foreach (var link in chain.Links.OrderBy(l => l.ID))
          {
            lines.Add($"LINK|{link.ID}|{link.ActionId}|{link.SuccessNextLink}|{link.FailureNextLink}|{link.Description}");
          }
          lines.Add("");
        }
      }

      var result = FileValidator.SafeSaveFile(
          GetReflexChainsFilePath(),
          lines,
          FileValidator.IsValidReflexChainsFile,
          minLinesCount: 10,
          fileDescription: "цепочек рефлексов");

      if (!result.Success)
        Logger.Error($"Ошибка сохранения цепочек: {result.ErrorMessage}");
      else
      {
        Logger.Info($"Цепочки сохранены. Файл сохранен, строк: {lines.Count}");
        if (_reflexChains.Count > 0)
          Logger.Info($"Цепочек сохранено: {_reflexChains.Count}");
      }

      return result;
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
        Logger.Error($"Error during disposal: {ex.Message}");
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