﻿using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ISIDA.Gomeostas
{
  /// <summary>
  /// Менеджер для работы с комбинациями стилей реагирования
  /// </summary>
  internal sealed class StyleCombinationsManager : IDisposable
  {
    private const string StyleCombinationsFileName = "StyleCombinations";
    private readonly string _gomeostasFolderPath;
    private readonly Func<ReadOnlyDictionary<int, GomeostasSystem.BehaviorStyle>> _getStylesFunc;
    private readonly Func<List<GomeostasSystem.ParameterData>> _getParametersFunc;
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    /// <summary>
    /// Инициализирует новый экземпляр менеджера комбинаций стилей
    /// </summary>
    public StyleCombinationsManager(
        string gomeostasFolderPath,
        Func<ReadOnlyDictionary<int, GomeostasSystem.BehaviorStyle>> getStylesFunc,
        Func<List<GomeostasSystem.ParameterData>> getParametersFunc)
    {
      _gomeostasFolderPath = gomeostasFolderPath ?? throw new ArgumentNullException(nameof(gomeostasFolderPath));
      _getStylesFunc = getStylesFunc ?? throw new ArgumentNullException(nameof(getStylesFunc));
      _getParametersFunc = getParametersFunc ?? throw new ArgumentNullException(nameof(getParametersFunc));
    }

    /// <summary>
    /// Получает путь к файлу комбинаций стилей
    /// </summary>
    private string GetStyleCombinationsFilePath()
    {
      return Path.Combine(_gomeostasFolderPath, $"{StyleCombinationsFileName}.comb");
    }

    /// <summary>
    /// Получает все возможные комбинации стилей реагирования из привязок к зонам параметров
    /// </summary>
    /// <param name="forceRegenerate">Принудительная генерация новых комбинаций</param>
    /// <returns>Список валидных комбинаций стилей</returns>
    public List<List<GomeostasSystem.BehaviorStyle>> GenerateStyleCombinations(bool forceRegenerate = false)
    {
      // Пытаемся загрузить из файла, если не принудительная генерация
      if (!forceRegenerate)
      {
        var loadedCombinations = LoadStyleCombinations();
        if (loadedCombinations.Any())
        {
          return loadedCombinations;
        }
      }

      // Генерируем новые комбинации из привязок параметров
      var validCombinations = GenerateCombinationsFromParameterBindings();

      // Сохраняем сгенерированные комбинации
      var saveResult = SaveStyleCombinations(validCombinations);

      return validCombinations;
    }

    /// <summary>
    /// Загружает комбинации стилей из файла
    /// </summary>
    /// <returns>Список загруженных комбинаций стилей</returns>
    public List<List<GomeostasSystem.BehaviorStyle>> LoadStyleCombinations()
    {
      try
      {
        var path = GetStyleCombinationsFilePath();
        var combinations = new List<List<GomeostasSystem.BehaviorStyle>>();

        if (!File.Exists(path))
          return combinations;

        var lines = File.ReadAllLines(path);

        _lock.EnterReadLock();
        try
        {
          var allStyles = GetAllBehaviorStyles();

          foreach (var line in lines)
          {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
              continue;

            var parts = line.Split('|');
            if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
            {
              var styleIds = parts[0].Split(',')
                  .Where(s => !string.IsNullOrWhiteSpace(s))
                  .Select(int.Parse)
                  .ToList();

              var combination = new List<GomeostasSystem.BehaviorStyle>();
              foreach (var styleId in styleIds)
              {
                if (allStyles.TryGetValue(styleId, out var style))
                {
                  combination.Add(style);
                }
              }

              if (combination.Any())
              {
                combinations.Add(combination);
              }
            }
          }
        }
        finally
        {
          _lock.ExitReadLock();
        }

        return combinations;
      }
      catch
      {
        return new List<List<GomeostasSystem.BehaviorStyle>>();
      }
    }

    /// <summary>
    /// Сохраняет комбинации стилей в файл
    /// </summary>
    /// <param name="combinations">Список комбинаций для сохранения</param>
    /// <returns>Результат операции сохранения</returns>
    public (bool Success, string ErrorMessage) SaveStyleCombinations(List<List<GomeostasSystem.BehaviorStyle>> combinations)
    {
      try
      {
        var path = GetStyleCombinationsFilePath();

        var lines = new List<string>
        {
            "# Файл комбинаций стилей поведения",
            "# Формат: ID_стиля1,ID_стиля2,ID_стиля3|Название_стиля1+Название_стиля2+Название_стиля3",
            "# Сгенерировано: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ""
        };

        // Фильтруем пустые комбинации и удаляем дубликаты
        var validCombinations = combinations
            .Where(c => c != null && c.Any())
            .Select(c => c.OrderBy(s => s.Id).ToList())
            .Distinct(new StyleCombinationComparer())
            .ToList();

        // Сортируем по количеству стилей в комбинации (возрастание)
        validCombinations = validCombinations
            .OrderBy(c => c.Count)
            .ThenBy(c => c.First().Id)
            .ToList();

        foreach (var combination in validCombinations)
        {
          var styleIds = combination.Select(s => s.Id).OrderBy(id => id).ToList();
          var styleNames = combination.Select(s => s.Name).ToList();

          var idsStr = string.Join(",", styleIds);
          var namesStr = string.Join("+", styleNames);

          lines.Add($"{idsStr}|{namesStr}");
        }

        try
        {
          File.WriteAllLines(path, lines, Encoding.UTF8);
          return (true, string.Empty);
        }
        catch (Exception ex)
        {
          return (false, ex.Message);
        }
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }
    #region Вспомогательные методы

    private ReadOnlyDictionary<int, GomeostasSystem.BehaviorStyle> GetAllBehaviorStyles()
    {
      return _getStylesFunc();
    }

    private List<GomeostasSystem.ParameterData> GetAllParameters()
    {
      return _getParametersFunc();
    }

    /// <summary>
    /// Генерирует комбинации стилей из привязок к зонам параметров
    /// </summary>
    private List<List<GomeostasSystem.BehaviorStyle>> GenerateCombinationsFromParameterBindings()
    {
      var allCombinations = new List<List<GomeostasSystem.BehaviorStyle>>();
      var allStyles = GetAllBehaviorStyles();
      var parameters = GetAllParameters();

      // Собираем все уникальные комбинации стилей из привязок параметров
      var uniqueCombinations = new HashSet<string>();

      foreach (var param in parameters)
      {
        foreach (var activation in param.StyleActivations.Values)
        {
          if (activation != null && activation.Any())
          {
            // Фильтруем только положительные ID (активации)
            var styleIds = activation.Where(id => id > 0).ToList();

            if (styleIds.Any())
            {
              // Создаем комбинацию из доступных стилей
              var combination = new List<GomeostasSystem.BehaviorStyle>();
              foreach (var styleId in styleIds)
              {
                if (allStyles.TryGetValue(styleId, out var style))
                {
                  combination.Add(style);
                }
              }

              if (combination.Any())
              {
                // Добавляем только полную комбинацию (без отдельных стилей)
                var sortedCombination = combination.OrderBy(s => s.Id).ToList();
                var combinationKey = string.Join(",", sortedCombination.Select(s => s.Id).OrderBy(id => id));

                if (!uniqueCombinations.Contains(combinationKey))
                {
                  uniqueCombinations.Add(combinationKey);
                  allCombinations.Add(sortedCombination);
                }
              }
            }
          }
        }
      }

      // Сортируем по количеству стилей в комбинации (возрастание)
      return allCombinations
          .OrderBy(c => c.Count)
          .ThenBy(c => c.First().Name)
          .ToList();
    }

    /// <summary>
    /// Компаратор для сравнения комбинаций стилей
    /// </summary>
    private class StyleCombinationComparer : IEqualityComparer<List<GomeostasSystem.BehaviorStyle>>
    {
      public bool Equals(List<GomeostasSystem.BehaviorStyle> x, List<GomeostasSystem.BehaviorStyle> y)
      {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;
        if (x.Count != y.Count) return false;

        return x.Select(s => s.Id).OrderBy(id => id)
            .SequenceEqual(y.Select(s => s.Id).OrderBy(id => id));
      }

      public int GetHashCode(List<GomeostasSystem.BehaviorStyle> obj)
      {
        if (obj == null) return 0;

        int hash = 17;
        foreach (var id in obj.Select(s => s.Id).OrderBy(id => id))
        {
          hash = hash * 31 + id.GetHashCode();
        }
        return hash;
      }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
      if (_disposed) return;
      _lock?.Dispose();
      _disposed = true;
    }

    #endregion
  }
}