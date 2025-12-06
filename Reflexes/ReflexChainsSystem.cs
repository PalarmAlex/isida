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

      /// <summary>ID адаптивного действия для выполнения</summary>
      public int ActionId { get; set; }

      /// <summary>ID следующего звена при успешном выполнении</summary>
      public int SuccessNextLink { get; set; }

      /// <summary>ID следующего звена при неудачном выполнении</summary>
      public int FailureNextLink { get; set; }

      /// <summary>Описание звена (для отладки)</summary>
      public string Description { get; set; }

      /// <summary>Максимальное количество повторений для циклических ссылок</summary>
      public int MaxCyclicRepetitions { get; set; }

      /// <summary>Текущее количество выполненных повторений</summary>
      public int CurrentRepetitions { get; set; }
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

      /// <summary>Максимальное количество повторений циклических ссылок по умолчанию</summary>
      public int DefaultMaxCyclicRepetitions { get; set; } = 3;
    }

    #endregion

    #region Поля и свойства

    private const string ReflexChainsFileName = "ReflexChains";
    private readonly Dictionary<int, ReflexChain> _reflexChains = new Dictionary<int, ReflexChain>();
    private int _lastChainId = 0;
    private int _lastLinkId = 0;

    /// <summary>
    /// Максимальное количество повторений циклических ссылок по умолчанию для всех цепочек
    /// </summary>
    public int GlobalMaxCyclicRepetitions { get; set; } = 3;

    /// <summary>
    /// Флаг разрешения циклических ссылок (ссылок на предыдущие звенья)
    /// </summary>
    public bool AllowCyclicReferences { get; set; } = true;

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
    /// <param name="priority">Приоритет цепочки</param>
    /// <param name="links">Звенья цепочки</param>
    /// <param name="defaultMaxCyclicRepetitions">Максимальное количество повторений циклических ссылок по умолчанию</param>
    /// <returns>ID созданной цепочки и предупреждения</returns>
    public (int ChainId, string[] Warnings) AddReflexChain(
        string name, string description, int priority, List<ChainLink> links,
        int defaultMaxCyclicRepetitions = 3)
    {
      var warnings = new List<string>();

      if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Наименование цепочки не может быть пустым", nameof(name));

      if (links == null || !links.Any())
        throw new ArgumentException("Цепочка должна содержать хотя бы одно звено", nameof(links));

      // Проверяем существование адаптивных действий
      var allActions = _adaptiveActionsSystem.GetAllAdaptiveActionsList();
      foreach (var link in links)
      {
        if (!allActions.Any(a => a.Id == link.ActionId))
        {
          warnings.Add($"Адаптивное действие с ID {link.ActionId} не существует");
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
          DefaultMaxCyclicRepetitions = defaultMaxCyclicRepetitions
        };

        // Устанавливаем максимальное количество повторений для каждого звена
        foreach (var link in chain.Links)
        {
          if (link.MaxCyclicRepetitions <= 0)
            link.MaxCyclicRepetitions = chain.DefaultMaxCyclicRepetitions;
        }

        _reflexChains.Add(newId, chain);
        SaveReflexChains();
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

        bool removed = _reflexChains.Remove(chainId);
        if (removed)
        {
          SaveReflexChains();
        }
        return removed;
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
    /// <param name="maxCyclicRepetitions">Максимальное количество повторений для циклических ссылок</param>
    /// <returns>ID созданного звена и предупреждения</returns>
    public (int LinkId, string[] Warnings) AddChainLink(
        int chainId, int actionId, int successNextLink,
        int failureNextLink, string description, int maxCyclicRepetitions = 0)
    {
      var warnings = new List<string>();

      _lock.EnterWriteLock();
      try
      {
        // Проверяем существование цепочки
        if (!_reflexChains.TryGetValue(chainId, out var chain))
          throw new KeyNotFoundException($"Цепочка с ID {chainId} не найдена");

        // Проверяем существование адаптивного действия
        var allActions = _adaptiveActionsSystem.GetAllAdaptiveActionsList();
        if (!allActions.Any(a => a.Id == actionId))
          warnings.Add($"Адаптивное действие с ID {actionId} не существует");

        // Проверяем циклические ссылки
        if (AllowCyclicReferences)
        {
          // Разрешены циклические ссылки - проверяем только существование звеньев
          if (successNextLink != 0 && !chain.Links.Any(l => l.ID == successNextLink))
            warnings.Add($"Следующее звено при успехе (ID:{successNextLink}) не найдено в цепочке");

          if (failureNextLink != 0 && !chain.Links.Any(l => l.ID == failureNextLink))
            warnings.Add($"Следующее звено при неудаче (ID:{failureNextLink}) не найдено в цепочке");
        }
        else
        {
          // Циклические ссылки запрещены
          if (successNextLink != 0)
          {
            var existingLink = chain.Links.FirstOrDefault(l => l.ID == successNextLink);
            if (existingLink == null)
              warnings.Add($"Следующее звено при успехе (ID:{successNextLink}) не найдено в цепочке");
            else if (successNextLink <= chain.Links.Max(l => l.ID))
              warnings.Add($"Ссылка на предыдущее звено (ID:{successNextLink}) запрещена (AllowCyclicReferences = false)");
          }

          if (failureNextLink != 0)
          {
            var existingLink = chain.Links.FirstOrDefault(l => l.ID == failureNextLink);
            if (existingLink == null)
              warnings.Add($"Следующее звено при неудаче (ID:{failureNextLink}) не найдено в цепочке");
            else if (failureNextLink <= chain.Links.Max(l => l.ID))
              warnings.Add($"Ссылка на предыдущее звено (ID:{failureNextLink}) запрещена (AllowCyclicReferences = false)");
          }
        }

        // Генерируем уникальный ID
        int newLinkId = ++_lastLinkId;

        // Устанавливаем максимальное количество повторений
        if (maxCyclicRepetitions <= 0)
          maxCyclicRepetitions = chain.DefaultMaxCyclicRepetitions;

        var link = new ChainLink
        {
          ID = newLinkId,
          ChainID = chainId,
          ActionId = actionId,
          SuccessNextLink = successNextLink,
          FailureNextLink = failureNextLink,
          Description = description ?? $"Звено {newLinkId}",
          MaxCyclicRepetitions = maxCyclicRepetitions,
          CurrentRepetitions = 0
        };

        chain.Links.Add(link);

        SaveReflexChains();
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
        int failureNextLink, string description, int maxCyclicRepetitions = 0)
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

        // Проверяем существование адаптивного действия
        var allActions = _adaptiveActionsSystem.GetAllAdaptiveActionsList();
        if (!allActions.Any(a => a.Id == actionId))
          warnings.Add($"Адаптивное действие с ID {actionId} не существует");

        if (AllowCyclicReferences)
        {
          // Циклические ссылки разрешены - проверяем только существование звеньев
          if (successNextLink != 0 && successNextLink != linkId && !chain.Links.Any(l => l.ID == successNextLink))
            warnings.Add($"Следующее звено при успехе (ID:{successNextLink}) не найдено");

          if (failureNextLink != 0 && failureNextLink != linkId && !chain.Links.Any(l => l.ID == failureNextLink))
            warnings.Add($"Следующее звено при неудаче (ID:{failureNextLink}) не найдено");
        }
        else
        {
          // Циклические ссылки запрещены
          if (successNextLink != 0 && successNextLink != linkId)
          {
            var existingLink = chain.Links.FirstOrDefault(l => l.ID == successNextLink);
            if (existingLink == null)
              warnings.Add($"Следующее звено при успехе (ID:{successNextLink}) не найдено");
            else if (successNextLink <= linkId)
              warnings.Add($"Ссылка на предыдущее звено (ID:{successNextLink}) запрещена (AllowCyclicReferences = false)");
          }

          if (failureNextLink != 0 && failureNextLink != linkId)
          {
            var existingLink = chain.Links.FirstOrDefault(l => l.ID == failureNextLink);
            if (existingLink == null)
              warnings.Add($"Следующее звено при неудаче (ID:{failureNextLink}) не найдено");
            else if (failureNextLink <= linkId)
              warnings.Add($"Ссылка на предыдущее звено (ID:{failureNextLink}) запрещена (AllowCyclicReferences = false)");
          }
        }

        // Устанавливаем максимальное количество повторений
        if (maxCyclicRepetitions <= 0)
          maxCyclicRepetitions = link.MaxCyclicRepetitions;

        // Обновляем звено
        link.ActionId = actionId;
        link.SuccessNextLink = successNextLink;
        link.FailureNextLink = failureNextLink;
        link.Description = description ?? link.Description;
        link.MaxCyclicRepetitions = maxCyclicRepetitions;

        SaveReflexChains();
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

        // Находим все звенья, которые ссылаются на удаляемое
        var referencingLinks = chain.Links.Where(l =>
            l.SuccessNextLink == linkId || l.FailureNextLink == linkId).ToList();

        if (referencingLinks.Any())
        {
          if (!reconnectLinks)
            throw new InvalidOperationException(
                $"Звено {linkId} используется другими звеньями: " +
                string.Join(", ", referencingLinks.Select(l => l.ID)));

          // Перенаправляем ссылки на 0 (ничего не делать)
          foreach (var refLink in referencingLinks)
          {
            if (refLink.SuccessNextLink == linkId)
              refLink.SuccessNextLink = 0;
            if (refLink.FailureNextLink == linkId)
              refLink.FailureNextLink = 0;
          }
        }

        // Удаляем звено
        bool removed = chain.Links.Remove(linkToRemove);
        if (removed)
        {
          SaveReflexChains();
        }
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

        if (!chain.Links.Any())
        {
          issues.Add("Цепочка не содержит звеньев");
          return (false, issues.ToArray());
        }

        // Проверяем существование адаптивных действий
        var allActions = _adaptiveActionsSystem.GetAllAdaptiveActionsList();
        foreach (var link in chain.Links)
        {
          if (!allActions.Any(a => a.Id == link.ActionId))
            issues.Add($"Адаптивное действие {link.ActionId} в звене {link.ID} не существует");

          // Если циклические ссылки запрещены, проверяем ссылки на предыдущие звенья
          if (!AllowCyclicReferences)
          {
            if (link.SuccessNextLink != 0 && link.SuccessNextLink <= link.ID)
              issues.Add($"Звено {link.ID} ссылается на предыдущее звено {link.SuccessNextLink} (AllowCyclicReferences = false)");

            if (link.FailureNextLink != 0 && link.FailureNextLink <= link.ID)
              issues.Add($"Звено {link.ID} ссылается на предыдущее звено {link.FailureNextLink} (AllowCyclicReferences = false)");
          }

          // Проверяем существование следующих звеньев (кроме 0 и кроме себя)
          if (link.SuccessNextLink != 0 && link.SuccessNextLink != link.ID &&
              !chain.Links.Any(l => l.ID == link.SuccessNextLink))
            issues.Add($"Звено {link.ID}: следующее при успехе {link.SuccessNextLink} не найдено");

          if (link.FailureNextLink != 0 && link.FailureNextLink != link.ID &&
              !chain.Links.Any(l => l.ID == link.FailureNextLink))
            issues.Add($"Звено {link.ID}: следующее при неудаче {link.FailureNextLink} не найдено");

          // Проверяем максимальное количество повторений
          if (link.MaxCyclicRepetitions <= 0)
            issues.Add($"Звено {link.ID}: максимальное количество повторений должно быть больше 0");
        }

        // Проверяем наличие конечных звеньев (звеньев с SuccessNextLink = 0 и FailureNextLink = 0)
        var terminalLinks = chain.Links.Where(l =>
            (l.SuccessNextLink == 0 || l.SuccessNextLink == l.ID) &&
            (l.FailureNextLink == 0 || l.FailureNextLink == l.ID)).ToList();

        // Если есть только циклические ссылки без конечных, проверяем наличие счетчиков повторений
        if (terminalLinks.Count == 0 && !chain.Links.Any(l => l.MaxCyclicRepetitions > 0))
        {
          issues.Add("Цепочка не содержит конечных звеньев и не настроены счетчики повторений (бесконечный цикл)");
        }

        return (!issues.Any(), issues.ToArray());
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Сбрасывает счетчики повторений для всех звеньев цепочки
    /// </summary>
    public void ResetChainRepetitions(int chainId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_reflexChains.TryGetValue(chainId, out var chain))
          return;

        foreach (var link in chain.Links)
        {
          link.CurrentRepetitions = 0;
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Проверяет, достигнуто ли максимальное количество повторений для циклической ссылки
    /// </summary>
    public bool HasReachedMaxRepetitions(int chainId, int linkId, int targetLinkId)
    {
      _lock.EnterReadLock();
      try
      {
        if (!_reflexChains.TryGetValue(chainId, out var chain))
          return true;

        var sourceLink = chain.Links.FirstOrDefault(l => l.ID == linkId);
        if (sourceLink == null)
          return true;

        // Если это ссылка на самого себя
        if (targetLinkId == linkId)
        {
          sourceLink.CurrentRepetitions++;
          return sourceLink.CurrentRepetitions >= sourceLink.MaxCyclicRepetitions;
        }

        // Если это ссылка на предыдущее звено
        var targetLink = chain.Links.FirstOrDefault(l => l.ID == targetLinkId);
        if (targetLink != null && targetLink.ID < linkId)
        {
          targetLink.CurrentRepetitions++;
          return targetLink.CurrentRepetitions >= targetLink.MaxCyclicRepetitions;
        }

        return false;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает текущее количество повторений для звена
    /// </summary>
    public int GetCurrentRepetitions(int chainId, int linkId)
    {
      _lock.EnterReadLock();
      try
      {
        if (!_reflexChains.TryGetValue(chainId, out var chain))
          return 0;

        var link = chain.Links.FirstOrDefault(l => l.ID == linkId);
        return link?.CurrentRepetitions ?? 0;
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

    /// <summary>
    /// Загружает цепочки рефлексов из файла
    /// </summary>
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
            if (parts.Length >= 6 && parts[0] == "CHAIN")
            {
              if (int.TryParse(parts[1], out int chainId) &&
                  int.TryParse(parts[4], out int priority) &&
                  int.TryParse(parts[5], out int maxCyclicRepetitions))
              {
                currentChain = new ReflexChain
                {
                  ID = chainId,
                  Name = parts[2],
                  Description = parts[3],
                  Priority = priority,
                  DefaultMaxCyclicRepetitions = maxCyclicRepetitions,
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
                  int.TryParse(parts[2], out int actionId) &&
                  int.TryParse(parts[3], out int successNext) &&
                  int.TryParse(parts[4], out int failureNext) &&
                  int.TryParse(parts[6], out int maxCyclicRepetitions))
              {
                var link = new ChainLink
                {
                  ID = linkId,
                  ChainID = currentChain.ID,
                  ActionId = actionId,
                  SuccessNextLink = successNext,
                  FailureNextLink = failureNext,
                  Description = parts[5],
                  MaxCyclicRepetitions = maxCyclicRepetitions > 0 ? maxCyclicRepetitions : currentChain.DefaultMaxCyclicRepetitions,
                  CurrentRepetitions = 0
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
        FileHeaders.ReflexChainsMaxRepetitionsDesc,
        FileHeaders.ReflexChainsLinkDesc,
        FileHeaders.ReflexChainsReflexDesc,
        FileHeaders.ReflexChainsSuccessDesc,
        FileHeaders.ReflexChainsFailureDesc,
        "# Пример цепочки 'Охота':",
        "CHAIN|1|Охота|Цепочка охотничьего поведения|10|3",
        "LINK|1|5|2|1|Обнаружение добычи|3",
        "LINK|2|6|3|1|Преследование|3",
        "LINK|3|7|0|0|Захват добычи|3"
    };

      File.WriteAllLines(GetReflexChainsFilePath(), lines);
    }

    /// <summary>Сохраняет цепочки рефлексов в файл</summary>
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
            FileHeaders.ReflexChainsMaxRepetitionsDesc,
            FileHeaders.ReflexChainsLinkDesc,
            FileHeaders.ReflexChainsReflexDesc,
            FileHeaders.ReflexChainsSuccessDesc,
            FileHeaders.ReflexChainsFailureDesc
        };

        foreach (var chain in _reflexChains.Values.OrderBy(c => c.ID))
        {
          // Заголовок цепочки
          lines.Add($"CHAIN|{chain.ID}|{chain.Name}|{chain.Description}|{chain.Priority}|{chain.DefaultMaxCyclicRepetitions}");

          // Звенья
          foreach (var link in chain.Links.OrderBy(l => l.ID))
          {
            lines.Add($"LINK|{link.ID}|{link.ActionId}|{link.SuccessNextLink}|{link.FailureNextLink}|{link.Description}|{link.MaxCyclicRepetitions}");
          }

          lines.Add(""); // Разделитель
        }

        var result = FileValidator.SafeSaveFile(
            GetReflexChainsFilePath(),
            lines,
            FileValidator.IsValidReflexChainsFile,
            minLinesCount: 9,
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