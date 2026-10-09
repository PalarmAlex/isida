using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static ISIDA.Common.FileValidator;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Направленные сенсорные ассоциации CS₁→CS₂ между образами восприятия (модель Рескорла–Вагнера).
  /// </summary>
  public sealed class SensoryAssociationSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly GeneticReflexesSystem _geneticReflexes;
    private readonly ConditionedReflexesSystem _conditionedReflexes;
    private bool _disposed;

    private const string SensoryAssociationsFileName = "SensoryAssociations";

    /// <summary>
    /// Запись направленной сенсорной связи между образами восприятия.
    /// </summary>
    public class SensoryAssociation
    {
      /// <summary>ID более раннего образа (CS₁)</summary>
      public int EarlierImageId { get; set; }

      /// <summary>ID более позднего образа (CS₂)</summary>
      public int LaterImageId { get; set; }

      /// <summary>Крепость связи C ∈ [0, β]</summary>
      public float Strength { get; set; }

      /// <summary>Пульс последнего усиления</summary>
      public int LastStrengthenPulse { get; set; }

      /// <summary>Пульс создания связи</summary>
      public int BirthTimePulse { get; set; }

      /// <summary>Максимальная достигнутая крепость</summary>
      public float MaxAchievedStrength { get; set; }
    }

    /// <summary>
    /// Результат обхода транзитивной цепочки CS→CS… от A до C (не пустая, только длина ≥ 2).
    /// </summary>
    public class TransitiveChainResult
    {
      /// <summary>Образы-вершины цепи от A до C включительно (например A,B,C)</summary>
      public List<int> ImageIds { get; set; } = new List<int>();

      /// <summary>Сила цепи = Π Cᵢ по рёбрам · δ^(hops−1)</summary>
      public float Strength { get; set; }
    }

    #region Инициализация

    private static SensoryAssociationSystem _instance;

    /// <summary>Глобальный экземпляр системы сенсорных ассоциаций</summary>
    public static SensoryAssociationSystem Instance => _instance ??
        throw new InvalidOperationException("SensoryAssociationSystem не инициализирован.");

    /// <summary>Флаг инициализации класса</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы сенсорных ассоциаций
    /// </summary>
    public static void InitializeInstance(
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes)
    {
      if (_instance != null)
        throw new InvalidOperationException("SensoryAssociationSystem уже инициализирован.");

      _instance = new SensoryAssociationSystem(geneticReflexes, conditionedReflexes);
    }

    private SensoryAssociationSystem(
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes)
    {
      _geneticReflexes = geneticReflexes ?? throw new ArgumentNullException(nameof(geneticReflexes));
      _conditionedReflexes = conditionedReflexes ?? throw new ArgumentNullException(nameof(conditionedReflexes));

      try
      {
        EnsureDataDirectory();
        Load();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Поля

    private readonly Dictionary<(int Earlier, int Later), SensoryAssociation> _links =
        new Dictionary<(int Earlier, int Later), SensoryAssociation>();

    /// <summary>
    /// Индекс исходящих рёбер: образ-источник → список (образ-приёмник, крепость).
    /// Обновляется при любом изменении графа (StrengthenLink / ApplyDecay / Load / RemoveLink).
    /// </summary>
    private readonly Dictionary<int, List<(int Later, float Strength)>> _outLinks =
        new Dictionary<int, List<(int Later, float Strength)>>();

    private ConditionedReflexesSystem.ConditionedReflexSettings Settings =>
        _conditionedReflexes.Settings;

    /// <summary>
    /// Коэффициент адресного штрафа связи по запрету оператора: крепость связи умножается
    /// на это значение (обратная операция к усилению по Рескорлу–Вагнеру).
    /// </summary>
    private const float OperatorPenaltyFactor = 0.3f;

    #endregion

    #region Внутренние методы — индекс

    /// <summary>
    /// Перестраивает индекс исходящих рёбер из _links.
    /// Вызывается из Load, ApplyDecay и при удалении рёбер.
    /// </summary>
    private void RebuildOutLinksIndex()
    {
      _outLinks.Clear();
      foreach (var kv in _links)
      {
        var key = kv.Key;
        var link = kv.Value;
        if (!_outLinks.TryGetValue(key.Earlier, out var list))
        {
          list = new List<(int Later, float Strength)>();
          _outLinks[key.Earlier] = list;
        }
        list.Add((key.Later, link.Strength));
      }
    }

    /// <summary>
    /// Инкрементально обновляет/добавляет одно исходящее ребро в индекс
    /// (используется в StrengthenLink, чтобы не перестраивать весь индекс).
    /// </summary>
    private void UpsertOutLink(int earlier, int later, float strength)
    {
      if (!_outLinks.TryGetValue(earlier, out var list))
      {
        list = new List<(int Later, float Strength)>();
        _outLinks[earlier] = list;
      }

      for (int i = 0; i < list.Count; i++)
      {
        if (list[i].Later == later)
        {
          list[i] = (later, strength);
          return;
        }
      }
      list.Add((later, strength));
    }

    #endregion

    #region Публичный API

    /// <summary>
    /// Усиливает направленную связь earlierImageId → laterImageId по модели Рескорла–Вагнера.
    /// </summary>
    public void StrengthenLink(int earlierImageId, int laterImageId)
    {
      if (earlierImageId <= 0 || laterImageId <= 0 || earlierImageId == laterImageId)
        return;

      int currentPulse = GetAgentLifetime();
      var key = (earlierImageId, laterImageId);

      _lock.EnterWriteLock();
      try
      {
        if (!_links.TryGetValue(key, out var link))
        {
          link = new SensoryAssociation
          {
            EarlierImageId = earlierImageId,
            LaterImageId = laterImageId,
            Strength = 0f,
            BirthTimePulse = currentPulse
          };
          _links[key] = link;
        }

        float alpha = Settings.LearningRate;
        float beta = Settings.MaxAssociationStrength;
        link.Strength = link.Strength + alpha * (beta - link.Strength);
        link.Strength = Math.Min(link.Strength, beta);

        if (link.Strength > link.MaxAchievedStrength)
          link.MaxAchievedStrength = link.Strength;

        link.LastStrengthenPulse = currentPulse;

        // Индекс исходящих рёбер синхронизируется инкрементально (без полной перестройки).
        UpsertOutLink(earlierImageId, laterImageId, link.Strength);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Получает крепость связи earlier → later</summary>
    public bool TryGetStrength(int earlierImageId, int laterImageId, out float strength)
    {
      strength = 0f;
      if (earlierImageId <= 0 || laterImageId <= 0)
        return false;

      _lock.EnterReadLock();
      try
      {
        if (_links.TryGetValue((earlierImageId, laterImageId), out var link))
        {
          strength = link.Strength;
          return true;
        }
        return false;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Проверяет, достаточна ли крепость связи для иерархической активации (≥ γ)</summary>
    public bool IsLinkActivatable(int earlierImageId, int laterImageId)
    {
      return TryGetStrength(earlierImageId, laterImageId, out float strength) &&
             strength >= Settings.ActivationThreshold;
    }

    /// <summary>
    /// Читает текущую готовность (крепость) направленной связи cs1 → cs2. Если прямого звена нет,
    /// но гейт открыт транзитивной цепью cs1 → … → cs2, возвращает силу этой цепи.
    /// </summary>
    /// <param name="cs1">ID более раннего образа (CS₁).</param>
    /// <param name="cs2">ID более позднего образа (CS₂).</param>
    /// <param name="associability">Текущая крепость связи (или сила цепи) C ∈ [0, β]; 0, если ничего нет.</param>
    /// <returns>True, если найдено прямое звено или транзитивная цепь.</returns>
    public bool TryGetAssociability(int cs1, int cs2, out float associability)
    {
      associability = 0f;
      if (cs1 <= 0 || cs2 <= 0)
        return false;

      _lock.EnterReadLock();
      try
      {
        if (_links.TryGetValue((cs1, cs2), out var link))
        {
          associability = link.Strength;
          return true;
        }
      }
      finally
      {
        _lock.ExitReadLock();
      }

      // Прямого звена нет: готовность может задаваться транзитивной цепью CS₁→…→CS₂.
      // TryGetChainStrength берёт свой read-lock, поэтому вызываем вне текущего дока.
      if (Settings.EnableTransitiveLearning &&
          TryGetChainStrength(cs1, cs2, out float chainStrength, out _))
      {
        associability = chainStrength;
        return true;
      }
      return false;
    }

    /// <summary>
    /// Адресно снижает готовность (крепость) пути cs1 → cs2 по запрету оператора — умножение
    /// крепости каждого звена на <see cref="OperatorPenaltyFactor"/>. Если прямого звена нет,
    /// наказываются рёбра лучшей транзитивной цепи cs1 → … → cs2. Ослабленные ниже
    /// <see cref="ConditionedReflexesSystem.ConditionedReflexSettings.MinAssociationStrength"/>
    /// звенья удаляются. Обратная операция к <see cref="StrengthenLink"/>: у-рефлекс не трогается,
    /// наказывается именно путь активации через сенсорный гейт.
    /// </summary>
    /// <param name="cs1">ID более раннего образа (CS₁).</param>
    /// <param name="cs2">ID более позднего образа (CS₂).</param>
    /// <returns>Кортеж (успех, сообщение) для показа оператору.</returns>
    public (bool Success, string Message) PenalizeLinkByOperator(int cs1, int cs2)
    {
      if (cs1 <= 0 || cs2 <= 0 || cs1 == cs2)
        return (false, $"Некорректная сенсорная связь: {cs1}→{cs2}.");

      var key = (cs1, cs2);

      _lock.EnterWriteLock();
      try
      {
        if (_links.TryGetValue(key, out var link))
        {
          float before = link.Strength;
          link.Strength *= OperatorPenaltyFactor;

          if (link.Strength < Settings.MinAssociationStrength)
          {
            _links.Remove(key);
            RebuildOutLinksIndex();
            return (true, $"Готовность сенсорной связи {cs1}→{cs2} понижена оператором " +
                          $"({before:0.###} → 0, связь удалена).");
          }

          // Индекс исходящих рёбер синхронизируется, иначе транзитивные цепи видят старую крепость.
          UpsertOutLink(cs1, cs2, link.Strength);

          return (true, $"Готовность сенсорной связи {cs1}→{cs2} понижена оператором " +
                        $"({before:0.###} → {link.Strength:0.###}).");
        }

        // Прямого звена нет: гейт мог быть открыт транзитивной цепью CS₁→…→CS₂.
        if (Settings.EnableTransitiveLearning && TryGetBestChainPath(cs1, cs2, out var chainPath))
        {
          int penalizedEdges = PenalizeChainEdges(chainPath);
          string chainText = string.Join("→", chainPath);
          if (penalizedEdges > 0)
            return (true, $"Готовность сенсорной цепи {chainText} понижена оператором " +
                          $"({penalizedEdges} зв.).");
          return (false, $"Не удалось понизить готовность сенсорной цепи {chainText}.");
        }

        return (false, $"Сенсорная связь {cs1}→{cs2} не найдена.");
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Наказывает все рёбра пути (умножение крепости на <see cref="OperatorPenaltyFactor"/>),
    /// удаляя звенья ниже <see cref="ConditionedReflexesSystem.ConditionedReflexSettings.MinAssociationStrength"/>.
    /// Вызывается из-под write-lock; при удалении перестраивает индекс исходящих рёбер.
    /// </summary>
    /// <param name="path">Вершины пути (A,…,C), по рёбрам которого применяется штраф.</param>
    /// <returns>Число наказанных рёбер.</returns>
    private int PenalizeChainEdges(List<int> path)
    {
      int penalized = 0;
      bool removedAny = false;

      for (int i = 0; i + 1 < path.Count; i++)
      {
        var edgeKey = (path[i], path[i + 1]);
        if (!_links.TryGetValue(edgeKey, out var edge))
          continue;

        edge.Strength *= OperatorPenaltyFactor;
        penalized++;

        if (edge.Strength < Settings.MinAssociationStrength)
        {
          _links.Remove(edgeKey);
          removedAny = true;
        }
        else
        {
          UpsertOutLink(edgeKey.Item1, edgeKey.Item2, edge.Strength);
        }
      }

      if (removedAny)
        RebuildOutLinksIndex();

      return penalized;
    }

    /// <summary>
    /// Ищет силу транзитивной цепочки earlier → … → later (длина ≥ 2 рёбер) методом обхода
    /// по индексу исходящих рёбер. Сила пути = Π Cᵢ по рёбрам · δ^(hops−1), где δ —
    /// <see cref="ConditionedReflexesSystem.ConditionedReflexSettings.TransitiveDecayPerHop"/>.
    /// Выбирается простейший (максимальный по силе) простой путь глубиной не более
    /// <see cref="ConditionedReflexesSystem.ConditionedReflexSettings.TransitiveMaxDepth"/>.
    /// visited по текущему пути защищает от циклов (звено не усиливает само себя).
    /// </summary>
    /// <param name="earlierImageId">ID стартового образа (A)</param>
    /// <param name="laterImageId">ID целевого образа (C)</param>
    /// <param name="strength">Сила лучшей найденной цепи (Π Cᵢ · δ^(hops−1))</param>
    /// <param name="hops">Число рёбер в лучшей цепи</param>
    /// <returns>True, если найден хотя бы один путь длиной ≥ 2 рёбер</returns>
    public bool TryGetChainStrength(int earlierImageId, int laterImageId,
        out float strength, out int hops)
    {
      strength = 0f;
      hops = 0;
      if (earlierImageId <= 0 || laterImageId <= 0 || earlierImageId == laterImageId)
        return false;

      _lock.EnterReadLock();
      try
      {
        int maxDepth = Settings.TransitiveMaxDepth > 0 ? Settings.TransitiveMaxDepth : 3;
        float delta = Settings.TransitiveDecayPerHop;
        if (delta <= 0f || delta > 1f)
          delta = 1f;

        float bestStrength = 0f;
        int bestHops = 0;
        var bestPath = new List<int>(); // путь здесь не нужен — только сила/длина

        // visited по текущему пути: защита от циклов (A→B→A не даёт усиления).
        var visited = new HashSet<int> { earlierImageId };
        var currentPath = new List<int> { earlierImageId };

        FindBestChain(earlierImageId, laterImageId, 1f, 0, maxDepth, delta, visited,
            currentPath, ref bestStrength, ref bestHops, ref bestPath);

        if (bestHops >= 2)
        {
          strength = bestStrength;
          hops = bestHops;
          return true;
        }
        return false;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Рекурсивный DFS по исходящим рёбрам: перебирает простые пути глубиной ≤ maxDepth,
    /// накапливает произведение крепостей и фиксирует лучший путь до целевого узла.
    /// Вызывается из-под read-lock (данные читаются, не мутируются). Попутно сохраняет
    /// вершины лучшего пути в <paramref name="bestPath"/> (для адресного штрафа цепи).
    /// </summary>
    private void FindBestChain(int current, int target, float accProduct, int hops,
        int maxDepth, float delta, HashSet<int> visited, List<int> currentPath,
        ref float bestStrength, ref int bestHops, ref List<int> bestPath)
    {
      if (hops >= maxDepth)
        return;

      if (!_outLinks.TryGetValue(current, out var edges))
        return;

      foreach (var e in edges)
      {
        if (visited.Contains(e.Later))
          continue; // цикл по текущему пути — пропускаем

        float newProduct = accProduct * e.Strength;
        int newHops = hops + 1;

        if (e.Later == target)
        {
          // Сила цепи с штрафом за длину: Π Cᵢ · δ^(hops−1).
          float chainStrength = newProduct * (float)Math.Pow(delta, newHops - 1);
          if (chainStrength > bestStrength)
          {
            bestStrength = chainStrength;
            bestHops = newHops;
            bestPath = new List<int>(currentPath) { target };
          }
          // Целевой узел не расширяем дальше — путь до него завершён.
          continue;
        }

        visited.Add(e.Later);
        currentPath.Add(e.Later);
        FindBestChain(e.Later, target, newProduct, newHops, maxDepth, delta, visited,
            currentPath, ref bestStrength, ref bestHops, ref bestPath);
        currentPath.RemoveAt(currentPath.Count - 1);
        visited.Remove(e.Later);
      }
    }

    /// <summary>
    /// Ищет простейший (максимальный по силе) простой путь earlier → … → later длиной ≥ 2 рёбер
    /// и возвращает его вершины. Используется для адресного штрафа транзитивной цепи оператором.
    /// Вызывается из-под уже взятого lock (сам лок не берёт).
    /// </summary>
    /// <param name="earlierImageId">ID стартового образа (A).</param>
    /// <param name="laterImageId">ID целевого образа (C).</param>
    /// <param name="path">Вершины лучшего пути от A до C включительно (A,…,C).</param>
    /// <returns>True, если найден путь длиной ≥ 2 рёбер.</returns>
    private bool TryGetBestChainPath(int earlierImageId, int laterImageId, out List<int> path)
    {
      path = null;
      if (earlierImageId <= 0 || laterImageId <= 0 || earlierImageId == laterImageId)
        return false;

      int maxDepth = Settings.TransitiveMaxDepth > 0 ? Settings.TransitiveMaxDepth : 3;
      float delta = Settings.TransitiveDecayPerHop;
      if (delta <= 0f || delta > 1f)
        delta = 1f;

      float bestStrength = 0f;
      int bestHops = 0;
      var bestPath = new List<int>();
      var visited = new HashSet<int> { earlierImageId };
      var currentPath = new List<int> { earlierImageId };

      FindBestChain(earlierImageId, laterImageId, 1f, 0, maxDepth, delta, visited,
          currentPath, ref bestStrength, ref bestHops, ref bestPath);

      if (bestHops >= 2 && bestPath.Count >= 3)
      {
        path = bestPath;
        return true;
      }
      return false;
    }

    /// <summary>
    /// Гейт транзитивной активации: допускает ли цепочка earlier → … → later активацию.
    /// Порог повышен относительно прямого звена: γ_tr = ActivationThreshold · k, где k —
    /// <see cref="ConditionedReflexesSystem.ConditionedReflexSettings.TransitiveGammaCoefficient"/>
    /// (k ≥ 1). Прямое звено при этом остаётся на γ (см. <see cref="IsLinkActivatable"/>).
    /// Если <see cref="ConditionedReflexesSystem.ConditionedReflexSettings.EnableTransitiveLearning"/>
    /// выключен — всегда false (цепи не участвуют в активации).
    /// </summary>
    /// <param name="earlierImageId">ID стартового образа (A)</param>
    /// <param name="laterImageId">ID целевого образа (C)</param>
    /// <returns>True, если цепь найдена и её сила ≥ γ_tr</returns>
    public bool IsChainActivatable(int earlierImageId, int laterImageId)
    {
      if (!Settings.EnableTransitiveLearning)
        return false;

      if (!TryGetChainStrength(earlierImageId, laterImageId, out float strength, out _))
        return false;

      float k = Settings.TransitiveGammaCoefficient;
      if (k < 1f)
        k = 1f;

      float gammaTr = Settings.ActivationThreshold * k;
      return strength >= gammaTr;
    }

    /// <summary>Применяет затухание ко всем связям и удаляет ослабленные</summary>
    public void ApplyDecay()
    {
      int currentPulse = GetAgentLifetime();
      // Период затухания — из настроек (ConditionedReflexSettings.dat), а не захардкожен.
      int period = Settings.SensoryDecayPeriodPulses > 0 ? Settings.SensoryDecayPeriodPulses : 100;
      if (period <= 0 || currentPulse % period != 0)
        return;

      _lock.EnterWriteLock();
      try
      {
        var keysToRemove = new List<(int Earlier, int Later)>();

        foreach (var kv in _links)
        {
          var link = kv.Value;
          ApplyDecayToLink(link);

          if (link.Strength < Settings.MinAssociationStrength)
            keysToRemove.Add(kv.Key);
        }

        foreach (var key in keysToRemove)
          _links.Remove(key);

        // Индекс перестраивается полностью после удаления слабых звеньев.
        RebuildOutLinksIndex();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Сохраняет связи в файл</summary>
    public (bool Success, string ErrorMessage) Save()
    {
      _lock.EnterReadLock();
      try
      {
        var lines = new List<string>
        {
          FileHeaders.SensoryAssociationsFormat,
          FileHeaders.SensoryAssociationsFields
        };

        foreach (var link in _links.Values
            .Where(l => l.Strength >= Settings.MinAssociationStrength)
            .OrderBy(l => l.EarlierImageId)
            .ThenBy(l => l.LaterImageId))
        {
          lines.Add($"{link.EarlierImageId}|{link.LaterImageId}|{link.Strength}|" +
                    $"{link.LastStrengthenPulse}|{link.BirthTimePulse}|{link.MaxAchievedStrength}");
        }

        return FileValidator.SafeSaveFile(
            GetFilePath(),
            lines,
            content => true,
            minLinesCount: 2,
            fileDescription: "сенсорных ассоциаций");
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

    /// <summary>Загружает связи из файла</summary>
    public void Load()
    {
      string filePath = GetFilePath();
      if (!File.Exists(filePath))
        return;

      _lock.EnterWriteLock();
      try
      {
        _links.Clear();

        foreach (var line in File.ReadLines(filePath))
        {
          if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

          var parts = line.Split('|');
          if (parts.Length < 6)
            continue;

          if (!int.TryParse(parts[0], out int earlierId) ||
              !int.TryParse(parts[1], out int laterId))
            continue;

          if (earlierId <= 0 || laterId <= 0 || earlierId == laterId)
            continue;

          if (!float.TryParse(parts[2], out float strength))
            continue;

          var link = new SensoryAssociation
          {
            EarlierImageId = earlierId,
            LaterImageId = laterId,
            Strength = strength,
            LastStrengthenPulse = int.TryParse(parts[3], out int last) ? last : 0,
            BirthTimePulse = int.TryParse(parts[4], out int birth) ? birth : 0,
            MaxAchievedStrength = float.TryParse(parts[5], out float max) ? max : strength
          };

          if (link.Strength >= Settings.MinAssociationStrength)
            _links[(earlierId, laterId)] = link;
        }

        // Индекс исходящих рёбер строится один раз после загрузки всего графа.
        RebuildOutLinksIndex();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Внутренние методы

    private void ApplyDecayToLink(SensoryAssociation link)
    {
      // Кривая затухания CS→CS целиком параметризуется настройками (ConditionedReflexSettings.dat):
      // зоны крепости, эффективный коэффициент устойчивых связей и нижний предел для степени/корня.
      float decayRate = Settings.DecayRate;
      float strengthFactor = Math.Max(Settings.SensoryStrengthFloor, link.Strength);
      float effectiveDecayRate;

      if (link.Strength > Settings.SensoryHighStrengthThreshold)
        effectiveDecayRate = Settings.SensoryHighStrengthDecayRate;
      else if (link.Strength > Settings.SensoryMidStrengthThreshold)
        effectiveDecayRate = (float)Math.Pow(decayRate, strengthFactor);
      else
        effectiveDecayRate = (float)Math.Pow(decayRate, Math.Sqrt(strengthFactor));

      link.Strength *= effectiveDecayRate;

      if (link.Strength > link.MaxAchievedStrength)
        link.MaxAchievedStrength = link.Strength;
    }

    private int GetAgentLifetime()
    {
      try
      {
        return _conditionedReflexes.GetCurrentAgentLifetime();
      }
      catch
      {
        return AppGlobalState.Lifetime;
      }
    }

    private void EnsureDataDirectory()
    {
      string directory = Path.GetDirectoryName(GetFilePath());
      if (!Directory.Exists(directory))
        Directory.CreateDirectory(directory);
    }

    private string GetFilePath()
    {
      string reflexesPath = _geneticReflexes.GetGeneticReflexesFilePath();
      string directory = Path.GetDirectoryName(reflexesPath);
      return Path.Combine(directory, $"{SensoryAssociationsFileName}.dat");
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы системы</summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        Save();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
        _instance = null;
      }
    }

    #endregion
  }
}
