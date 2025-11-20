using ISIDA.Actions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace ISIDA.Gomeostas
{
  internal static class ValidationService
  {
    internal static Dictionary<int, Dictionary<int, float>> BuildInfluenceGraph(IEnumerable<ParameterData> parameters)
    {
      var graph = new Dictionary<int, Dictionary<int, float>>();

      foreach (var param in parameters)
      {
        var influences = new Dictionary<int, float>();

        // Добавляем влияния из BadStateInfluence
        foreach (var influence in param.BadStateInfluence.Where(x => x.Value != 0))
        {
          influences[influence.Key] = influence.Value;
        }

        // Добавляем влияния из WellStateInfluence
        foreach (var influence in param.WellStateInfluence.Where(x => x.Value != 0))
        {
          influences[influence.Key] = influence.Value;
        }

        if (influences.Any())
          graph[param.Id] = influences;
      }

      return graph;
    }

    internal static string GetParameterName(int paramId, IEnumerable<ParameterData> parameters)
    {
      var param = parameters.FirstOrDefault(p => p.Id == paramId);
      return param != null ? $"{param.Name} (№{param.Id})" : $"№{paramId}";
    }

    /// <summary>
    /// Проверяет параметры на циклические зависимости влияний
    /// </summary>
    internal static bool CheckForInfluenceCycles(IEnumerable<ParameterData> parameters, out string cycleMessage)
    {
      var graph = BuildInfluenceGraph(parameters);

      // Преобразуем граф влияний в граф зависимостей для проверки циклов
      var dependencyGraph = graph.ToDictionary(
          g => g.Key,
          g => g.Value.Keys.ToList());

      return InfluenceCycleChecker.CheckForCycles(
          dependencyGraph,
          id => GetParameterName(id, parameters),
          out cycleMessage);
    }

    /// <summary>
    /// Проверяет валидность активации набора элементов с учётом их антагонистов
    /// </summary>
    internal static bool ValidateActivation(
      IReadOnlyList<int> itemsToActivate, 
      IReadOnlyList<int[]> antagonistsMap,
      out string errorsMessge)
    {
      errorsMessge = String.Empty;

      // Проверка входных параметров
      if (itemsToActivate == null)
      {
        errorsMessge = "Список активируемых элементов не может быть null";
        return false;
      }

      if (antagonistsMap == null)
      {
        errorsMessge = "Карта антагонистов не может быть null";
        return false;
      }

      // Проверка что ни один элемент не является антагонистом самому себе
      foreach (var item in itemsToActivate)
      {
        if (item < 0 || item >= antagonistsMap.Count)
        {
          errorsMessge = $"Элемент с ID {item} выходит за пределы карты антагонистов";
          return false;
        }

        var antagonists = antagonistsMap[item];
        if (antagonists == null)
          continue;

        if (antagonists.Contains(item))
        {
          errorsMessge = $"Элемент с ID {item} не может быть антагонистом самому себе";
          return false;
        }
      }

      // Проверка на взаимное погашение всех элементов
      var remainingItems = new HashSet<int>(itemsToActivate);
      var itemsToDeactivate = new HashSet<int>();

      // Собираем все элементы, которые нужно деактивировать
      foreach (var item in itemsToActivate)
      {
        if (antagonistsMap[item] != null)
        {
          foreach (var antagonist in antagonistsMap[item])
          {
            if (remainingItems.Contains(antagonist))
            {
              itemsToDeactivate.Add(antagonist);
            }
          }
        }
      }

      // Удаляем элементы, которые должны быть деактивированы
      remainingItems.ExceptWith(itemsToDeactivate);

      // Если не осталось ни одного активного элемента
      if (remainingItems.Count == 0 && itemsToActivate.Count > 0)
      {
        errorsMessge = "Все элементы будут взаимно деактивированы из-за антагонистических отношений";
        return false;
      }
      return true;
    }

    /// <summary>
    /// Класс для проверки циклических зависимостей влияний параметров
    /// </summary>
    private static class InfluenceCycleChecker
    {
      /// <summary>
      /// Универсальный метод для проверки циклов
      /// </summary>
      internal static bool CheckForCycles<T>(
          Dictionary<T, List<T>> graph,
          Func<T, string> getName,
          out string cycleMessage)
      {
        cycleMessage = null;
        var cycles = FindCycles(graph, getId: x => x.ToString(), getName);

        if (!cycles.Any()) return true;

        cycleMessage = BuildCycleMessage(cycles);
        return false;
      }

      private static List<CycleInfo<T>> FindCycles<T>(
          Dictionary<T, List<T>> graph,
          Func<T, string> getId,
          Func<T, string> getName)
      {
        var cycles = new List<CycleInfo<T>>();
        var visited = new HashSet<T>();
        var recursionStack = new HashSet<T>();
        var path = new List<T>();

        foreach (var node in graph.Keys)
        {
          if (!visited.Contains(node))
          {
            FindCyclesDfs(node, graph, visited, recursionStack, path, cycles, getId, getName);
          }
        }

        return cycles
            .GroupBy(c => string.Join("→", c.Path.OrderBy(x => x)))
            .Select(g => g.First())
            .ToList();
      }

      private static void FindCyclesDfs<T>(
          T currentNode,
          Dictionary<T, List<T>> graph,
          HashSet<T> visited,
          HashSet<T> recursionStack,
          List<T> path,
          List<CycleInfo<T>> cycles,
          Func<T, string> getId,
          Func<T, string> getName)
      {
        visited.Add(currentNode);
        recursionStack.Add(currentNode);
        path.Add(currentNode);

        if (graph.TryGetValue(currentNode, out var neighbors))
        {
          foreach (var neighbor in neighbors)
          {
            if (!visited.Contains(neighbor))
            {
              FindCyclesDfs(neighbor, graph, visited, recursionStack, path, cycles, getId, getName);
            }
            else if (recursionStack.Contains(neighbor))
            {
              // Найден цикл
              var cycleStart = path.IndexOf(neighbor);
              if (cycleStart >= 0)
              {
                var cyclePath = path.GetRange(cycleStart, path.Count - cycleStart);
                cyclePath.Add(neighbor); // Замыкаем цикл

                cycles.Add(new CycleInfo<T>
                {
                  Path = cyclePath,
                  NodeNames = cyclePath.Select(getName).ToList(),
                  NodeIds = cyclePath.Select(getId).ToList()
                });
              }
            }
          }
        }

        recursionStack.Remove(currentNode);
        path.RemoveAt(path.Count - 1);
      }

      private static string BuildCycleMessage<T>(List<CycleInfo<T>> cycles)
      {
        var cycleDescriptions = cycles.Select(cycle =>
        {
          var path = string.Join(" → ", cycle.NodeNames);
          var nodeIds = string.Join(", ", cycle.NodeIds.Distinct().Select(id => $"№{id}"));
          return $"{path} (элементы: {nodeIds})";
        });

        return "Обнаружены циклические зависимости:\n\n" +
               string.Join("\n\n", cycleDescriptions) +
               "\n\nТакие зависимости приведут к нестабильности системы. " +
               "Пожалуйста, измените настройки зависимостей для указанных элементов.";
      }

      private class CycleInfo<T>
      {
        public List<T> Path { get; set; }
        public List<string> NodeNames { get; set; }
        public List<string> NodeIds { get; set; }
      }
    }
  }
}