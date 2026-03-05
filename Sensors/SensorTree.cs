using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ISIDA.Common;

namespace ISIDA.Sensors
{
  /// <summary>
  /// Реализует древовидную структуру для распознавания последовательностей элементов
  /// </summary>
  /// <typeparam name="TNodeId">Тип идентификатора узла (должен поддерживать инкремент)</typeparam>
  /// <typeparam name="TElement">Тип элементов последовательности</typeparam>
  public class SensorTree<TNodeId, TElement> : IDisposable
  {
    #region Поля и свойства

    /// <summary>
    /// Словарь веток дерева (ID конечного узла -> список узлов ветки)
    /// </summary>
    protected readonly Dictionary<TNodeId, List<TreeNode<TElement>>> _branches =
        new Dictionary<TNodeId, List<TreeNode<TElement>>>();

    /// <summary>
    /// Имя дерева (используется для именования файлов)
    /// </summary>
    protected readonly string _treeName;

    /// <summary>
    /// Путь к директории хранения данных дерева
    /// </summary>
    protected readonly string _treeFolderPath;

    /// <summary>
    /// Синхронизатор для потокобезопасного доступа
    /// </summary>
    protected readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

    /// <summary>
    /// Флаг, указывающий были ли освобождены ресурсы
    /// </summary>
    protected bool _disposed = false;

    /// <summary>
    /// Словарь всех узлов дерева (ID узла -> узел)
    /// </summary>
    protected readonly Dictionary<TNodeId, TreeNode<TElement>> _nodes =
        new Dictionary<TNodeId, TreeNode<TElement>>();

    /// <summary>
    /// Доступ к узлам дерева
    /// </summary>
    public IReadOnlyDictionary<TNodeId, TreeNode<TElement>> Nodes => _nodes;

    /// <summary>
    /// ID последнего созданного узла
    /// </summary>
    protected TNodeId _lastNodeId;

    /// <summary>
    /// Параметр сглаживания Лапласа для редких контекстов (по умолчанию 0.01).
    /// </summary>
    private double _smoothingAlpha = 0.01;

    /// <summary>
    /// Параметр сглаживания для вероятностной модели переходов.
    /// </summary>
    public double SmoothingAlpha
    {
      get => _smoothingAlpha;
      set => _smoothingAlpha = value < 0 ? 0 : value;
    }

    #endregion

    #region Инициализация

    /// <summary>
    /// Инициализирует новое дерево распознавания
    /// </summary>
    /// <param name="treeName">Имя дерева (используется для именования файлов)</param>
    /// <param name="baseFolderPath">Базовый путь к директории данных</param>
    /// <exception cref="ArgumentNullException">Выбрасывается если treeName или logger равны null</exception>
    public SensorTree(string treeName, string baseFolderPath)
    {
      _treeName = treeName ?? throw new ArgumentNullException(nameof(treeName));
      _treeFolderPath = baseFolderPath;
      _lastNodeId = default;

      if (!Directory.Exists(_treeFolderPath))
        Directory.CreateDirectory(_treeFolderPath);

      // Создаем корневой узел сразу
      var rootNode = new TreeNode<TElement>(default, default, null);
      _nodes.Add(default, rootNode);
    }

    #endregion

    #region Работа с узлами

    /// <summary>
    /// Получает узел по его ID
    /// </summary>
    /// <param name="id">ID узла</param>
    /// <returns>Узел дерева или null если не найден</returns>
    public TreeNode<TElement> GetNodeById(TNodeId id)
    {
      _lock.EnterReadLock();
      try
      {
        return _nodes.TryGetValue(id, out var node) ? node : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Генерирует новый уникальный ID для узла дерева
    /// </summary>
    /// <returns>Новый ID узла</returns>
    protected virtual TNodeId GetNewNodeId()
    {
      dynamic lastId = _lastNodeId;
      lastId++;
      _lastNodeId = (TNodeId)lastId;
      return _lastNodeId;
    }

    /// <summary>
    /// Полностью очищает дерево, оставляя только корневой узел
    /// </summary>
    public void Clear()
    {
      _lock.EnterWriteLock();
      try
      {
        _nodes.Clear();
        _branches.Clear();

        // Создаем корневой узел
        var rootNode = new TreeNode<TElement>(default, default, null);
        _nodes.Add(default, rootNode);

        _lastNodeId = default;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Работа с ветками

    /// <summary>
    /// Находит ветку в дереве по последовательности элементов
    /// </summary>
    /// <param name="branch">Последовательность элементов для поиска</param>
    /// <returns>Идентификатор конечного узла ветки или значение по умолчанию если ветка не найдена</returns>
    internal TNodeId FindBranchInternal(IEnumerable<TElement> branch)
    {
      if (branch == null) return default(TNodeId);

      var elements = branch.ToList();
      if (!elements.Any()) return default(TNodeId);

      // Начинаем с корневого узла
      if (!_nodes.TryGetValue(default(TNodeId), out var currentNode))
        return default(TNodeId);

      // Проходим по всем элементам ветки
      foreach (var element in elements)
      {
        // Ищем дочерний узел с таким элементом
        var childNode = currentNode.Children
            .FirstOrDefault(c => c.Element.Equals(element));

        if (childNode == null) return default(TNodeId);
        currentNode = childNode;
      }

      // Возвращаем ID конечного узла
      return currentNode.Id;
    }

    /// <summary>
    /// Находит ветку в дереве по последовательности элементов
    /// </summary>
    /// <param name="branch">Последовательность элементов для поиска</param>
    /// <returns>Идентификатор конечного узла ветки или значение по умолчанию если ветка не найдена</returns>

    public TNodeId FindBranch(IEnumerable<TElement> branch)
    {
      _lock.EnterReadLock();
      try
      {
        return FindBranchInternal(branch);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Добавляет новую ветку в дерево
    /// </summary>
    /// <param name="branch">Последовательность элементов для добавления</param>
    /// <returns>Идентификатор конечного узла добавленной ветки</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается если branch равен null</exception>
    public TNodeId AddBranch(IEnumerable<TElement> branch)
    {
      if (branch == null) throw new ArgumentNullException(nameof(branch));

      _lock.EnterWriteLock();
      try
      {
        var elements = branch.ToList();

        // Проверяем полное совпадение
        var existingId = FindBranchInternal(elements);
        if (!EqualityComparer<TNodeId>.Default.Equals(existingId, default(TNodeId)))
          return existingId;

        // Постепенно строим ветку, переиспользуя существующие узлы где возможно
        TreeNode<TElement> parentNode = _nodes[default];

        foreach (var element in elements)
        {
          var existingChild = parentNode.Children.FirstOrDefault(c => c.Element.Equals(element));

          if (existingChild != null)
          {
            parentNode = existingChild;
          }
          else
          {
            // Создаем новый узел
            var newNode = new TreeNode<TElement>(GetNewNodeId(), element, parentNode);
            _nodes.Add(newNode.Id, newNode);
            parentNode.Children.Add(newNode);
            parentNode = newNode;
          }
        }

        // Всегда создаем новый конечный узел для уникального ID
        if (!EqualityComparer<TNodeId>.Default.Equals(parentNode.Id, default(TNodeId)))
        {
          _branches[parentNode.Id] = new List<TreeNode<TElement>> { parentNode };
          return parentNode.Id;
        }

        return default(TNodeId);
      }
      finally
      {
        _lock.ExitWriteLock();
        Save();
      }
    }

    #endregion

    #region Вероятностная статистика (PST)

    /// <summary>
    /// Обновляет счётчики переходов по всей последовательности (каждый префикс контекста учитывается).
    /// </summary>
    public void UpdateTransitionStatistics(IEnumerable<TElement> sequence)
    {
      if (sequence == null) return;

      var elements = sequence.ToList();
      if (elements.Count == 0) return;

      _lock.EnterWriteLock();
      try
      {
        if (!_nodes.TryGetValue(default(TNodeId), out var node))
          return;

        foreach (var next in elements)
        {
          node.AddTransition(next);
          var child = node.Children.FirstOrDefault(c => c.Element.Equals(next));
          if (child == null) break;
          node = child;
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Находит самый длинный существующий контекст и возвращает наиболее вероятное следующее значение.
    /// </summary>
    /// <param name="context">Контекст (префикс последовательности).</param>
    /// <param name="nextElement">Наиболее вероятный следующий элемент.</param>
    /// <param name="probability">Вероятность перехода.</param>
    /// <returns>true, если найден узел с ненулевой статистикой.</returns>
    public bool GetMostProbableNext(IEnumerable<TElement> context, out TElement nextElement, out double probability)
    {
      nextElement = default;
      probability = 0;

      _lock.EnterReadLock();
      try
      {
        if (!_nodes.TryGetValue(default(TNodeId), out var node))
          return false;

        var elements = context?.ToList() ?? new List<TElement>();
        foreach (var e in elements)
        {
          var child = node.Children.FirstOrDefault(c => c.Element.Equals(e));
          if (child == null) break;
          node = child;
        }

        return node.GetMostProbableNext(_smoothingAlpha, out nextElement, out probability);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Вычисляет вероятность последовательности по модели (произведение вероятностей переходов).
    /// </summary>
    public double GetSequenceProbability(IEnumerable<TElement> sequence)
    {
      if (sequence == null) return 0;

      var elements = sequence.ToList();
      if (elements.Count == 0) return 1.0;

      _lock.EnterReadLock();
      try
      {
        if (!_nodes.TryGetValue(default(TNodeId), out var node))
          return 0;

        double product = 1.0;
        foreach (var next in elements)
        {
          product *= node.GetTransitionProbability(next, _smoothingAlpha);
          var child = node.Children.FirstOrDefault(c => c.Element.Equals(next));
          if (child == null) break;
          node = child;
        }
        return product;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Лог-потери последовательности: −∑ log2(p_i). Чем меньше, тем правдоподобнее последовательность.
    /// </summary>
    public double GetSequenceLogLoss(IEnumerable<TElement> sequence)
    {
      if (sequence == null) return double.PositiveInfinity;

      var elements = sequence.ToList();
      if (elements.Count == 0) return 0;

      _lock.EnterReadLock();
      try
      {
        if (!_nodes.TryGetValue(default(TNodeId), out var node))
          return double.PositiveInfinity;

        double sumLog = 0;
        foreach (var next in elements)
        {
          double p = node.GetTransitionProbability(next, _smoothingAlpha);
          if (p <= 0) return double.PositiveInfinity;
          sumLog -= Math.Log(p, 2);
          var child = node.Children.FirstOrDefault(c => c.Element.Equals(next));
          if (child == null) break;
          node = child;
        }
        return sumLog;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Проверяет правдоподобность последовательности: log-loss не превышает порог (чем меньше порог, тем строже).
    /// </summary>
    public bool IsPlausible(IEnumerable<TElement> sequence, double logLossThreshold)
    {
      if (double.IsInfinity(logLossThreshold) || logLossThreshold < 0) return true;
      return GetSequenceLogLoss(sequence) <= logLossThreshold;
    }

    /// <summary>
    /// Перестраивает статистику переходов по всем существующим веткам (каждая ветка учитывается как одно наблюдение).
    /// </summary>
    public void RebuildStatistics()
    {
      Logger.Info($"Пересчёт статистики PST дерева '{_treeName}': начало");
      _lock.EnterWriteLock();
      try
      {
        foreach (var node in _nodes.Values)
          node.ClearTransitionStatistics();

        foreach (var branch in _branches.Values)
        {
          foreach (var leaf in branch)
          {
            var path = new List<TElement>();
            var n = leaf;
            while (n != null && !EqualityComparer<TNodeId>.Default.Equals(n.Id, default(TNodeId)))
            {
              path.Add(n.Element);
              n = n.Parent;
            }
            path.Reverse();
            if (path.Count == 0) continue;

            if (!_nodes.TryGetValue(default(TNodeId), out var node))
              continue;
            foreach (var next in path)
            {
              node.AddTransition(next);
              var child = node.Children.FirstOrDefault(c => c.Element.Equals(next));
              if (child == null) break;
              node = child;
            }
          }
        }
        Logger.Info($"Пересчёт статистики PST дерева '{_treeName}': конец");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Загрузка и сохранение

    /// <summary>
    /// Сериализует элемент для блока статистики (char — как код, int — как число).
    /// </summary>
    private static string SerializeElementForStats(TElement element)
    {
      if (typeof(TElement) == typeof(char))
        return ((int)(char)(object)element).ToString();
      return ((object)element).ToString();
    }

    /// <summary>
    /// Десериализует элемент из блока статистики.
    /// </summary>
    private static TElement ParseElementForStats(string s)
    {
      int n = int.Parse(s);
      if (typeof(TElement) == typeof(char))
        return (TElement)(object)(char)n;
      return (TElement)Convert.ChangeType(n, typeof(TElement));
    }

    /// <summary>
    /// Загружает состояние дерева из файла
    /// </summary>
    public void Load()
    {
      _lock.EnterWriteLock();
      try
      {
        var path = Path.Combine(_treeFolderPath, $"{_treeName}.dat");

        // Очищаем текущие данные
        _nodes.Clear();
        _branches.Clear();

        // Создаем корневой узел
        var rootNode = new TreeNode<TElement>(default, default, null);
        _nodes.Add(default, rootNode);

        if (!File.Exists(path))
        {
          return; // Файла нет - возвращаем только корневой узел
        }

        // Временное хранилище для данных узлов (element, parentId, опциональный блок статистики)
        var nodeData = new Dictionary<TNodeId, (TElement element, TNodeId parentId, string statsPart)>();

        // Чтение и парсинг файла
        foreach (var line in File.ReadLines(path))
        {
          if (string.IsNullOrWhiteSpace(line)) continue;

          var parts = line.Split(new[] { "|#|" }, StringSplitOptions.None);
          if (parts.Length < 2) continue;

          var idParts = parts[0].Split('|');
          if (idParts.Length != 2) continue;

          try
          {
            var id = (TNodeId)Convert.ChangeType(idParts[0], typeof(TNodeId));
            var parentId = (TNodeId)Convert.ChangeType(idParts[1], typeof(TNodeId));
            var element = (TElement)Convert.ChangeType(parts[1], typeof(TElement));
            var statsPart = parts.Length >= 3 ? parts[2] : null;

            nodeData[id] = (element, parentId, statsPart);

            // Обновляем последний ID
            if (Comparer<TNodeId>.Default.Compare(id, _lastNodeId) > 0)
            {
              _lastNodeId = id;
            }
          }
          catch
          {

          }
        }

        // Создаем все узлы (без связей)
        foreach (var item in nodeData)
        {
          // Пропускаем корневой узел — он уже создан
          if (EqualityComparer<TNodeId>.Default.Equals(item.Key, default(TNodeId)))
            continue;

          var newNode = new TreeNode<TElement>(item.Key, item.Value.element, null);
          _nodes.Add(item.Key, newNode);
        }

        // Устанавливаем связи родитель-потомок
        foreach (var node in _nodes.Values.ToList()) // ToList для копирования
        {
          if (EqualityComparer<TNodeId>.Default.Equals(node.Id, default(TNodeId)))
            continue;

          if (nodeData.TryGetValue(node.Id, out var data) &&
              _nodes.TryGetValue(data.parentId, out var parentNode))
          {
            node.SetParent(parentNode);
            parentNode.Children.Add(node);
          }
        }

        // Загружаем статистику переходов (если есть в файле)
        foreach (var kv in nodeData)
        {
          if (string.IsNullOrWhiteSpace(kv.Value.statsPart)) continue;
          if (!_nodes.TryGetValue(kv.Key, out var node)) continue;
          try
          {
            var statsTokens = kv.Value.statsPart.Split('|');
            if (statsTokens.Length == 0) continue;
            int visitCount = int.Parse(statsTokens[0]);
            var counts = new Dictionary<TElement, int>();
            for (int i = 1; i < statsTokens.Length; i++)
            {
              var pair = statsTokens[i].Split(':');
              if (pair.Length != 2) continue;
              counts[ParseElementForStats(pair[0])] = int.Parse(pair[1]);
            }
            node.SetLoadedTransitionStatistics(visitCount, counts);
          }
          catch
          {
            // игнорируем повреждённый блок статистики
          }
        }

        // Заполняем ветки (конечные узлы)
        foreach (var node in _nodes.Values)
        {
          if (node.Children.Count == 0 && !EqualityComparer<TNodeId>.Default.Equals(node.Id, default(TNodeId)))
          {
            _branches[node.Id] = new List<TreeNode<TElement>> { node };
          }
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сохраняет состояние дерева в файл
    /// </summary>
    public void Save()
    {
      _lock.EnterReadLock();
      try
      {
        var path = Path.Combine(_treeFolderPath, $"{_treeName}.dat");
        var lines = new List<string>();

        // Сохраняем все узлы, кроме корневого (с опциональным блоком статистики)
        foreach (var node in _nodes.Values)
        {
          if (node.Parent == null && !EqualityComparer<TNodeId>.Default.Equals(node.Id, default(TNodeId)))
            continue;

          var line = $"{node.Id}|{node.ParentID}|#|{node.Element}";
          if (node.VisitCount > 0 || node.TransitionCounts.Count > 0)
          {
            var statsTokens = new List<string> { node.VisitCount.ToString() };
            foreach (var kv in node.TransitionCounts)
              statsTokens.Add($"{SerializeElementForStats(kv.Key)}:{kv.Value}");
            line += "|#|" + string.Join("|", statsTokens);
          }
          lines.Add(line);
        }

        // Сначала записываем во временный файл
        var tempPath = path + ".tmp";
        File.WriteAllLines(tempPath, lines);

        // Затем атомарно заменяем старый файл
        if (File.Exists(path))
          File.Delete(path);

        File.Move(tempPath, path);
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

    #region Освобождение ресурсов

    /// <summary>
    /// Освобождает ресурсы дерева
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;

      _lock?.Dispose();
      _disposed = true;
    }

    #endregion

    /// <summary>
    /// Представляет узел древовидной структуры с элементами типа <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Тип элемента, хранящегося в узле.</typeparam>
    public class TreeNode<T>
    {
      /// <summary>
      /// Получает уникальный идентификатор узла.
      /// </summary>
      /// <value>
      /// Идентификатор узла типа <typeparamref name="TNodeId"/>.
      /// </value>
      public TNodeId Id { get; }

      /// <summary>
      /// Получает идентификатор родительского узла.
      /// </summary>
      /// <value>
      /// Идентификатор родительского узла или значение по умолчанию для типа <typeparamref name="TNodeId"/>, 
      /// если узел является корневым.
      /// </value>
      public TNodeId ParentID { get; private set; }

      /// <summary>
      /// Получает элемент данных, хранящийся в узле.
      /// </summary>
      /// <value>
      /// Элемент данных типа <typeparamref name="T"/>.
      /// </value>
      public T Element { get; }

      /// <summary>
      /// Получает ссылку на родительский узел.
      /// </summary>
      /// <value>
      /// Родительский узел или <c>null</c>, если узел является корневым.
      /// </value>
      public TreeNode<T> Parent { get; private set; }

      /// <summary>
      /// Получает список дочерних узлов.
      /// </summary>
      /// <value>
      /// Список дочерних узлов. Если узел не имеет дочерних элементов, возвращается пустой список.
      /// </value>
      public List<TreeNode<T>> Children { get; } = new List<TreeNode<T>>();

      /// <summary>
      /// Счётчики переходов к следующим элементам (элемент → количество наблюдений).
      /// </summary>
      public Dictionary<T, int> TransitionCounts { get; } = new Dictionary<T, int>();

      /// <summary>
      /// Количество «посещений» узла — сколько раз из него был совершён переход (сумма по TransitionCounts).
      /// </summary>
      public int VisitCount { get; private set; }

      /// <summary>
      /// Кэш вероятностей переходов; инвалидируется при вызове AddTransition.
      /// </summary>
      private Dictionary<T, double> _cachedProbabilities;
      private double _cachedSmoothingAlpha = double.NaN;

      /// <summary>
      /// Инициализирует новый экземпляр узла дерева.
      /// </summary>
      /// <param name="id">Уникальный идентификатор узла.</param>
      /// <param name="element">Элемент данных узла.</param>
      /// <param name="parent">Родительский узел (может быть null для корневого узла).</param>
      /// <exception cref="ArgumentNullException">Генерируется, если element равен null.</exception>
      public TreeNode(TNodeId id, T element, TreeNode<T> parent)
      {
        if (element == null)
        {
          throw new ArgumentNullException(nameof(element));
        }

        Id = id;
        Element = element;
        SetParent(parent);
      }

      /// <summary>
      /// Устанавливает родительский узел и обновляет связанные свойства.
      /// </summary>
      /// <param name="parent">Родительский узел или <c>null</c> для сброса родителя.</param>
      /// <remarks>
      /// Этот метод автоматически обновляет свойство <see cref="ParentID"/> в соответствии 
      /// с идентификатором родительского узла.
      /// </remarks>
      public void SetParent(TreeNode<T> parent)
      {
        Parent = parent;
        ParentID = parent != null ? parent.Id : default(TNodeId);
      }

      /// <summary>
      /// Загружает сохранённую статистику переходов (вызывается при десериализации из файла).
      /// </summary>
      public void SetLoadedTransitionStatistics(int visitCount, IReadOnlyDictionary<T, int> transitionCounts)
      {
        TransitionCounts.Clear();
        if (transitionCounts != null)
        {
          foreach (var kv in transitionCounts)
            TransitionCounts[kv.Key] = kv.Value;
        }
        VisitCount = visitCount;
        _cachedProbabilities = null;
        _cachedSmoothingAlpha = double.NaN;
      }

      /// <summary>
      /// Очищает счётчики переходов и кэш (используется при RebuildStatistics).
      /// </summary>
      public void ClearTransitionStatistics()
      {
        TransitionCounts.Clear();
        VisitCount = 0;
        _cachedProbabilities = null;
        _cachedSmoothingAlpha = double.NaN;
      }

      /// <summary>
      /// Учитывает один переход к следующему элементу (обновляет счётчики и инвалидирует кэш вероятностей).
      /// </summary>
      public void AddTransition(T nextElement)
      {
        if (!TransitionCounts.TryGetValue(nextElement, out _))
          TransitionCounts[nextElement] = 0;
        TransitionCounts[nextElement]++;
        VisitCount++;
        _cachedProbabilities = null;
        _cachedSmoothingAlpha = double.NaN;
      }

      /// <summary>
      /// Возвращает вероятность перехода к заданному следующему элементу при сглаживании Лапласа.
      /// Использует кэш по smoothingAlpha.
      /// </summary>
      /// <param name="nextElement">Следующий элемент.</param>
      /// <param name="smoothingAlpha">Параметр сглаживания (alpha).</param>
      public double GetTransitionProbability(T nextElement, double smoothingAlpha)
      {
        if (smoothingAlpha < 0) smoothingAlpha = 0;
        if (smoothingAlpha == _cachedSmoothingAlpha && _cachedProbabilities != null &&
            _cachedProbabilities.TryGetValue(nextElement, out var cached))
          return cached;

        int count = TransitionCounts.TryGetValue(nextElement, out var c) ? c : 0;
        int k = TransitionCounts.Count + 1;
        double denom = VisitCount + smoothingAlpha * Math.Max(1, k);
        double p = (count + smoothingAlpha) / denom;

        if (_cachedProbabilities == null || _cachedSmoothingAlpha != smoothingAlpha)
        {
          _cachedProbabilities = new Dictionary<T, double>();
          _cachedSmoothingAlpha = smoothingAlpha;
        }
        _cachedProbabilities[nextElement] = p;
        return p;
      }

      /// <summary>
      /// Возвращает наиболее вероятный следующий элемент и его вероятность.
      /// </summary>
      /// <param name="smoothingAlpha">Параметр сглаживания.</param>
      /// <param name="nextElement">Наиболее вероятный следующий элемент (default при отсутствии данных).</param>
      /// <param name="probability">Вероятность этого перехода.</param>
      /// <returns>true, если есть хотя бы один учтённый переход.</returns>
      public bool GetMostProbableNext(double smoothingAlpha, out T nextElement, out double probability)
      {
        nextElement = default;
        probability = 0;

        var candidates = new HashSet<T>(TransitionCounts.Keys);
        foreach (var child in Children)
          candidates.Add(child.Element);

        if (candidates.Count == 0 && VisitCount == 0)
          return false;

        T best = default;
        double bestProb = -1;
        foreach (var e in candidates)
        {
          double p = GetTransitionProbability(e, smoothingAlpha);
          if (p > bestProb)
          {
            bestProb = p;
            best = e;
          }
        }

        if (bestProb < 0) return false;
        nextElement = best;
        probability = bestProb;
        return true;
      }
    }
  }
}