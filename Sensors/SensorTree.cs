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

    #region Загрузка и сохранение

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

        // Временное хранилище для данных узлов (element, parentId)
        var nodeData = new Dictionary<TNodeId, (TElement element, TNodeId parentId)>();

        // Чтение и парсинг файла (третий фрагмент строки |#|... при наличии игнорируется)
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

            nodeData[id] = (element, parentId);

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

        // Сохраняем все узлы, кроме корневого
        foreach (var node in _nodes.Values)
        {
          if (node.Parent == null && !EqualityComparer<TNodeId>.Default.Equals(node.Id, default(TNodeId)))
            continue;

          var line = $"{node.Id}|{node.ParentID}|#|{node.Element}";
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
    }
  }
}