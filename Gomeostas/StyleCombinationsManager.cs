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
    private readonly Func<HomeostasisCalculator> _getCalculatorFunc;
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    /// <summary>
    /// Инициализирует новый экземпляр менеджера комбинаций стилей
    /// </summary>
    public StyleCombinationsManager(
        string gomeostasFolderPath,
        Func<ReadOnlyDictionary<int, GomeostasSystem.BehaviorStyle>> getStylesFunc,
        Func<List<GomeostasSystem.ParameterData>> getParametersFunc,
        Func<HomeostasisCalculator> getCalculatorFunc)
    {
      _gomeostasFolderPath = gomeostasFolderPath ?? throw new ArgumentNullException(nameof(gomeostasFolderPath));
      _getStylesFunc = getStylesFunc ?? throw new ArgumentNullException(nameof(getStylesFunc));
      _getParametersFunc = getParametersFunc ?? throw new ArgumentNullException(nameof(getParametersFunc));
      _getCalculatorFunc = getCalculatorFunc ?? throw new ArgumentNullException(nameof(getCalculatorFunc));
    }

    /// <summary>
    /// Получает путь к файлу комбинаций стилей
    /// </summary>
    private string GetStyleCombinationsFilePath()
    {
      return Path.Combine(_gomeostasFolderPath, $"{StyleCombinationsFileName}.comb");
    }

    /// <summary>
    /// Генерирует все возможные комбинации стилей реагирования с учетом антагонистов
    /// </summary>
    /// <param name="dynamicTime">Время в пульсах удержания состояний параметров</param>
    /// <param name="difSensorPar">Минимальное изменение параметра для детектирования</param>
    /// <param name="maxCombinationSize">Максимальный размер комбинации (минимум 1)</param>
    /// <param name="forceRegenerate">Принудительная генерация новых комбинаций</param>
    /// <returns>Список валидных комбинаций стилей</returns>
    public List<List<GomeostasSystem.BehaviorStyle>> GenerateStyleCombinations(
        int dynamicTime,
        float difSensorPar,
        int maxCombinationSize = 3,
        bool forceRegenerate = false)
    {
      if (maxCombinationSize < 1)
        throw new ArgumentOutOfRangeException(nameof(maxCombinationSize), "Размер комбинации должен быть не менее 1");

      // Пытаемся загрузить из файла, если не принудительная генерация
      if (!forceRegenerate)
      {
        var loadedCombinations = LoadStyleCombinations();
        if (loadedCombinations.Any())
        {
          return loadedCombinations;
        }
      }

      // Получаем все стили
      List<GomeostasSystem.BehaviorStyle> allStyles;
      _lock.EnterReadLock();
      try
      {
        allStyles = GetAllBehaviorStyles().Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }

      // Генерируем новые комбинации
      var validCombinations = GenerateCombinationsInternal(allStyles, maxCombinationSize);

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

        // Фильтруем пустые комбинации
        var validCombinations = combinations.Where(c => c != null && c.Any()).ToList();

        // Добавляем сначала все одиночные стили (комбинации размером 1)
        var singleStyles = validCombinations.Where(c => c.Count == 1).ToList();
        foreach (var combination in singleStyles)
        {
          var style = combination[0];
          lines.Add($"{style.Id}|{style.Name}");
        }

        // Добавляем разделитель между одиночными и комбинированными стилями
        if (singleStyles.Any() && validCombinations.Any(c => c.Count > 1))
        {
          lines.Add("");
          lines.Add("# Комбинированные стили:");
        }

        // Добавляем комбинации из 2+ стилей
        foreach (var combination in validCombinations.Where(c => c.Count > 1))
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

    private HomeostasisCalculator GetCalculator()
    {
      return _getCalculatorFunc();
    }

    private List<List<GomeostasSystem.BehaviorStyle>> GenerateCombinationsInternal(
        List<GomeostasSystem.BehaviorStyle> allStyles,
        int maxCombinationSize)
    {
      var validCombinations = new List<List<GomeostasSystem.BehaviorStyle>>();

      for (int size = 1; size <= maxCombinationSize; size++)
      {
        GenerateCombinationsRecursive(allStyles, new List<GomeostasSystem.BehaviorStyle>(), 0, size, validCombinations);
      }

      return validCombinations;
    }

    private void GenerateCombinationsRecursive(List<GomeostasSystem.BehaviorStyle> allStyles,
        List<GomeostasSystem.BehaviorStyle> currentCombination, int startIndex, int targetSize,
        List<List<GomeostasSystem.BehaviorStyle>> validCombinations)
    {
      // Если достигли нужного размера комбинации
      if (currentCombination.Count == targetSize)
      {
        if (IsValidStyleCombination(currentCombination))
        {
          // Убедимся, что комбинация уникальна (отсортирована по ID)
          var sortedCombination = currentCombination.OrderBy(s => s.Id).ToList();
          if (!validCombinations.Any(existing =>
              existing.Count == sortedCombination.Count &&
              existing.Select(s => s.Id).SequenceEqual(sortedCombination.Select(s => s.Id))))
          {
            validCombinations.Add(sortedCombination);
          }
        }
        return;
      }

      // Рекурсивно добавляем стили в комбинацию
      for (int i = startIndex; i < allStyles.Count; i++)
      {
        var style = allStyles[i];

        // Проверяем, можно ли добавить этот стиль в текущую комбинацию
        if (CanAddStyleToCombination(style, currentCombination))
        {
          currentCombination.Add(style);

          // Проверяем, не превысили ли мы максимальный размер 3
          if (currentCombination.Count <= 3)
          {
            GenerateCombinationsRecursive(allStyles, currentCombination, i + 1, targetSize, validCombinations);
          }

          currentCombination.RemoveAt(currentCombination.Count - 1);
        }
      }
    }

    private bool CanAddStyleToCombination(GomeostasSystem.BehaviorStyle style, List<GomeostasSystem.BehaviorStyle> currentCombination)
    {
      foreach (var existingStyle in currentCombination)
      {
        if (style.AntagonistStyles.Contains(existingStyle.Id) ||
            existingStyle.AntagonistStyles.Contains(style.Id))
        {
          return false;
        }
      }
      return true;
    }

    private bool IsValidStyleCombination(List<GomeostasSystem.BehaviorStyle> combination)
    {
      if (!combination.Any()) return false;

      for (int i = 0; i < combination.Count; i++)
      {
        for (int j = i + 1; j < combination.Count; j++)
        {
          var style1 = combination[i];
          var style2 = combination[j];

          if (style1.AntagonistStyles.Contains(style2.Id) ||
              style2.AntagonistStyles.Contains(style1.Id))
          {
            return false;
          }
        }
      }
      return true;
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