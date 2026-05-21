using ISIDA.Common;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace ISIDA.Sensors
{
  /// <summary>
  /// Реализует вербальный канал восприятия текстовой информации
  /// </summary>
  public sealed class VerbalSensorChannel : SensorChannel
  {
    private int _recognitionThreshold = 3;
    private int _maxPhraseLength = 5;
    private bool _authoritativeMode = false;
    private string _primarySensorsPath = "";
    private readonly GomeostasSystem _gomeostas;
    private readonly SensorChannelOptions _options;

    #region Поля и свойства

    /// <summary>Параметры канала (вербальный или командный).</summary>
    public SensorChannelOptions Options => _options;

    /// <summary>true — атомарные контуры (команда), false — побуквенные слова.</summary>
    public bool UsesAtomicTokens => Options.AtomicTokens;

    /// <summary>Дерево слов (символы → слова). Только для побуквенного режима.</summary>
    public SensorTree<int, char> WordTree { get; }

    /// <summary>Дерево контуров (primary id → слово). Только для атомарного режима.</summary>
    public SensorTree<int, int> AtomicWordTree { get; }

    /// <summary>Дерево распознавания фраз (слова → фразы)</summary>
    public SensorTree<int, int> PhraseTree { get; }

    /// <summary>
    /// Песочница для новых слов
    /// </summary>
    public SensorSandbox<string> WordSandbox { get; private set; }

    /// <summary>
    /// Песочница для новых фраз
    /// </summary>
    public SensorSandbox<List<int>> PhraseSandbox { get; private set; }

    /// <summary>
    /// Текстовая песочница фраз — считает повторения по тексту, независимо от наличия ID слов в дереве
    /// </summary>
    public SensorSandbox<string> PhraseTextSandbox { get; private set; }

    private Dictionary<char, int> _primarySensors = new Dictionary<char, int>();
    private Dictionary<string, int> _primaryTokens = new Dictionary<string, int>();

    /// <summary>
    /// Словарь узлов дерева фраз (ID -> узел)
    /// </summary>
    private IReadOnlyDictionary<int, SensorTree<int, int>.TreeNode<int>> PhraseTreeFromID =>
        PhraseTree.Nodes;

    /// <summary>
    /// Получает или устанавливает флаг авторитарного режима работы
    /// </summary>
    public bool AuthoritativeMode 
    {
      get => _authoritativeMode;
      set
      {
        _authoritativeMode = value;
      }
    }

    /// <summary>
    /// Получает или устанавливает порог подтверждения для новых элементов
    /// </summary>
    public int RecognitionThreshold
    {
      get => _recognitionThreshold;
      set
      {
        if (value < 1)
          throw new ArgumentOutOfRangeException(nameof(value), "Порог должен быть не менее 1");

        _recognitionThreshold = value;
      }
    }

    /// <summary>
    /// Получает или устанавливает максимальную длину воспринимаемых фраз
    /// </summary>
    public int MaxPhraseLength
    {
      get => _maxPhraseLength;
      set
      {
        if (value < 1)
          throw new ArgumentOutOfRangeException(nameof(value), "Максимальная длина фраз должна быть не менее 1");

        _maxPhraseLength = value;
      }
    }

    #endregion

    #region Инициализация и загрузка

    /// <summary>
    /// Инициализирует новый экземпляр вербального канала
    /// </summary>
    /// <param name="gomeostasSystem">Ссылка на класс гомеостаза</param> 
    /// <param name="baseFolderPath">Базовый путь к директории данных</param>
    /// <param name="primarySensorsPath">Путь к файлу первичных сенсоров</param>
    /// <param name="options">Параметры канала (по умолчанию вербальный)</param>
    /// <exception cref="ArgumentNullException">Выбрасывается если logger равен null</exception>
    public VerbalSensorChannel(
        GomeostasSystem gomeostasSystem,
        string baseFolderPath,
        string primarySensorsPath,
        SensorChannelOptions options = null)
        : base(baseFolderPath, "")
    {
      try
      {
        _gomeostas = gomeostasSystem ?? throw new ArgumentNullException(nameof(gomeostasSystem));
        _options = options ?? SensorChannelOptions.Verbal;

        _primarySensorsPath = primarySensorsPath;
        LoadPrimarySensors(_primarySensorsPath);

        string wordsTreeName = ResolveTreeFileBaseName(baseFolderPath, _options.WordsTreeName, "CadWords");
        string phrasesTreeName = ResolveTreeFileBaseName(baseFolderPath, _options.PhrasesTreeName, "CadPhrases");
        string wordSandboxName = ResolveTreeFileBaseName(baseFolderPath, _options.WordSandboxName, "CadWords");
        string phraseSandboxName = ResolveTreeFileBaseName(baseFolderPath, _options.PhraseSandboxName, "CadPhrases");
        string phraseTextSandboxName = ResolveTreeFileBaseName(baseFolderPath, _options.PhraseTextSandboxName, "CadPhrasesText");

        if (_options.AtomicTokens)
        {
          AtomicWordTree = new SensorTree<int, int>(wordsTreeName, baseFolderPath);
          WordTree = null;
        }
        else
        {
          WordTree = new SensorTree<int, char>(wordsTreeName, baseFolderPath);
          AtomicWordTree = null;
        }

        PhraseTree = new SensorTree<int, int>(phrasesTreeName, baseFolderPath);
        WordSandbox = new SensorSandbox<string>(wordSandboxName, baseFolderPath);
        PhraseSandbox = new SensorSandbox<List<int>>(phraseSandboxName, baseFolderPath);
        PhraseTextSandbox = new SensorSandbox<string>(phraseTextSandboxName, baseFolderPath);

        LoadTrees();
        LoadSandboxes();
      }
      catch
      {
        throw;
      }
    }

    private static string ResolveTreeFileBaseName(string baseFolderPath, string preferredName, string legacyName)
    {
      if (File.Exists(Path.Combine(baseFolderPath, $"{preferredName}.dat")))
        return preferredName;
      if (!string.IsNullOrEmpty(legacyName) &&
          File.Exists(Path.Combine(baseFolderPath, $"{legacyName}.dat")))
        return legacyName;
      return preferredName;
    }

    private void LoadPrimarySensors(string filePath)
    {
      if (_options.AtomicTokens)
      {
        _primaryTokens.Clear();
        if (!File.Exists(filePath))
          return;

        foreach (var line in File.ReadAllLines(filePath))
        {
          if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

          var parts = line.Split(new[] { "|#|" }, StringSplitOptions.None);
          if (parts.Length != 2) continue;

          var token = parts[0].Trim();
          if (string.IsNullOrEmpty(token)) continue;

          if (int.TryParse(parts[1].Trim(), out int id))
            _primaryTokens[token] = id;
        }

        return;
      }

      if (!File.Exists(filePath))
        throw new FileNotFoundException($"Файл первичных сенсоров не найден: {filePath}");

      _primarySensors.Clear();

      foreach (var line in File.ReadAllLines(filePath))
      {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

        var parts = line.Split(new[] { "|#|" }, StringSplitOptions.None);
        if (parts.Length != 2) continue;

        var symbol = parts[0].Trim();
        if (symbol.Length != 1) continue;

        if (int.TryParse(parts[1].Trim(), out int id))
          _primarySensors[symbol[0]] = id;
      }

      if (_primarySensors.Count == 0)
        throw new InvalidDataException("Файл первичных сенсоров не содержит валидных данных");
    }

    private void LoadTrees()
    {
      try
      {
        if (_options.AtomicTokens)
          AtomicWordTree.Load();
        else
          WordTree.Load();

        PhraseTree.Load();
      }
      catch
      {
        // Не фатальная ошибка - деревья могут быть пустыми
      }
    }

    private void LoadSandboxes()
    {
      try
      {
        WordSandbox.Load();
        PhraseSandbox.Load();
        PhraseTextSandbox.Load();
      }
      catch
      {
        // Не фатальная ошибка - песочницы могут быть пустыми
      }
    }

    /// <summary>
    /// Полностью очищает все вербальные деревья и песочницы
    /// </summary>
    public void ClearAllTrees()
    {
      if (AppGlobalState.EvolutionStage > 0)
        throw new InvalidOperationException("Очистка сенсорных деревьев разрешена только в стадии 0");

      _lock.EnterWriteLock();
      try
      {
        if (_options.AtomicTokens)
          AtomicWordTree.Clear();
        else
          WordTree.Clear();

        PhraseTree.Clear();

        WordSandbox.Clear();
        PhraseSandbox.Clear();
        PhraseTextSandbox.Clear();

        OnClearAllTrees();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }
    
    #endregion

    #region Работа с первичными сенсорами

    /// <summary>
    /// Получить ID первичного символа.
    /// При отсутствии прямого совпадения пробует строчный вариант (для "Х" → "х"), т.к. в первичных сенсорах часто заданы только строчные.
    /// </summary>
    public int GetPrimarySensorId(char symbol)
    {
      if (_primarySensors.TryGetValue(symbol, out int id))
        return id;

      char lower = char.ToLowerInvariant(symbol);
      if (lower != symbol && _primarySensors.TryGetValue(lower, out id))
        return id;

      return 0;
    }

    /// <summary>
    /// Получить первичный символ 
    /// </summary>
    public char GetPrimarySensorSymbol(int id)
    {
      return _primarySensors.FirstOrDefault(x => x.Value == id).Key;
    }

    #endregion

    #region Внутренние операции со словами

    private int FindWordBranchId(string word)
    {
      if (_options.AtomicTokens)
      {
        if (!_primaryTokens.TryGetValue(word, out int primaryId))
          return 0;

        return AtomicWordTree.FindBranchInternal(new[] { primaryId });
      }

      return WordTree.FindBranchInternal(word);
    }

    private int AddWordBranch(string word)
    {
      if (_options.AtomicTokens)
      {
        if (!_primaryTokens.TryGetValue(word, out int primaryId))
          return 0;

        return AtomicWordTree.AddBranch(new[] { primaryId });
      }

      return WordTree.AddBranch(word);
    }

    #endregion

    #region Работа со словами

    /// <summary>
    /// Проверяет существование слова в дереве слов
    /// </summary>
    /// <param name="word">Слово для проверки</param>
    /// <returns>true если слово существует в дереве, false в противном случае</returns>
    public bool WordExists(string word)
    {
      if (string.IsNullOrWhiteSpace(word))
        return false;

      _lock.EnterReadLock();
      try
      {
        if (_options.AtomicTokens)
        {
          if (!_primaryTokens.ContainsKey(word))
            return false;

          return FindWordBranchId(word) != 0;
        }

        return FindWordBranchId(word) != 0;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получить первый символ слова
    /// </summary>
    public int GetFirstSymbolFromWordId(int wordId)
    {
      _lock.EnterReadLock();
      try
      {
        if (_options.AtomicTokens)
        {
          if (!AtomicWordTree.Nodes.TryGetValue(wordId, out var node))
            return 0;

          return node.Element;
        }

        var word = GetWordFromWordIdInternal(wordId);
        if (string.IsNullOrEmpty(word))
          return 0;

        return GetPrimarySensorId(word[0]);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Обрабатывает слово, добавляя его в дерево или песочницу
    /// </summary>
    /// <param name="word">Слово для обработки. Не может быть null или пустой строкой.</param>
    /// <returns>
    /// ID слова в дереве, если слово было найдено или добавлено;
    /// null, если слово не было добавлено (не в авторитарном режиме и не достигнут порог подтверждения)
    /// </returns>
    /// <exception cref="ArgumentNullException">Если word равен null</exception>
    public int? ProcessWord(string word)
    {
      if (string.IsNullOrWhiteSpace(word)) return null;

      _lock.EnterWriteLock();
      try
      {
        if (_options.AtomicTokens && !_primaryTokens.ContainsKey(word))
          return null;

        var existingId = FindWordBranchId(word);
        if (existingId != 0)
          return existingId;

        if (_options.FilterGarbageWords && IsGarbageWord(word)) return null;

        if (_authoritativeMode)
        {
          var newId = AddWordBranch(word);
          return newId != 0 ? newId : (int?)null;
        }

        bool isNew = WordSandbox.FindOrAdd(word, out int count);
        if (!isNew && count >= _recognitionThreshold)
        {
          var newId = AddWordBranch(word);
          WordSandbox.Remove(word);
          return newId != 0 ? newId : (int?)null;
        }

        return null;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    private bool IsGarbageWord(string word)
    {
      if (word.Length == 1 && !char.IsLetter(word[0]))
        return true;

      if (word.Length > 50)
        return true;

      var garbagePatterns = new[]
      {
        @"\d{12,}",
        @"[^\w\s]{4,}",
        @"(.)\1{5,}",
        @"^[\W_]+$",
        @"^https?://",
        @"^www\.",
    };
      foreach (var pattern in garbagePatterns)
      {
        if (Regex.IsMatch(word, pattern))
          return true;
      }

      return false;
    }

    /// <summary>
    /// Получает слово по его ID из дерева слов
    /// </summary>
    /// <param name="wordId">ID слова</param>
    /// <returns>Строка, представляющая слово, или пустая строка если слово не найдено</returns>
    public string GetWordFromWordId(int wordId)
    {
      _lock.EnterReadLock();
      try
      {
        return GetWordFromWordIdInternal(wordId);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает слово по его ID из дерева слов
    /// </summary>
    internal string GetWordFromWordIdInternal(int wordId)
    {
      if (_options.AtomicTokens)
      {
        if (!AtomicWordTree.Nodes.TryGetValue(wordId, out var node))
          return string.Empty;

        int primaryId = node.Element;
        return _primaryTokens.FirstOrDefault(x => x.Value == primaryId).Key ?? string.Empty;
      }

      if (!WordTree.Nodes.TryGetValue(wordId, out var charNode))
        return string.Empty;

      var symbols = new Stack<char>();
      while (charNode != null && charNode.Id != 0)
      {
        symbols.Push(charNode.Element);
        charNode = charNode.Parent;
      }

      return new string(symbols.ToArray());
    }

    /// <summary>
    /// Получает все слова из дерева слов
    /// </summary>
    /// <returns>Словарь, где ключ - ID слова, значение - само слово</returns>
    internal Dictionary<int, string> GetAllWordsInternal()
    {
      var words = new Dictionary<int, string>();
      var branchEndpoints = _options.AtomicTokens
          ? AtomicWordTree.GetBranchEndpointIds()
          : WordTree.GetBranchEndpointIds();

      foreach (var id in branchEndpoints)
      {
        if (id == 0) continue;
        var word = GetWordFromWordIdInternal(id);
        if (!string.IsNullOrEmpty(word))
          words.Add(id, word);
      }

      return words;
    }

    /// <summary>
    /// Получает все слова из дерева слов
    /// </summary>
    /// <returns>Словарь, где ключ - ID слова, значение - само слово</returns>
    public Dictionary<int, string> GetAllWords()
    {
      _lock.EnterReadLock();
      try
      {
        return GetAllWordsInternal();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Работа с фразами

    /// <summary>
    /// Находит ID фразы по точной последовательности ID слов.
    /// Вызывающий код должен удерживать read lock при вызове (используется из FindPhraseId).
    /// </summary>
    private int FindExactPhraseIdInternal(List<int> wordIds)
    {
      if (wordIds == null || wordIds.Count == 0) return 0;

      if (!PhraseTreeFromID.TryGetValue(0, out var currentNode))
        return 0;

      foreach (var wordId in wordIds)
      {
        var childNode = currentNode.Children
            .FirstOrDefault(c => c.Element.Equals(wordId));

        if (childNode == null)
          return 0;

        currentNode = childNode;
      }

      return currentNode.Id;
    }

    /// <summary>
    /// Находит ID фразы по точной последовательности ID слов (с захватом блокировки).
    /// </summary>
    private int FindExactPhraseId(List<int> wordIds)
    {
      if (wordIds == null || wordIds.Count == 0) return 0;

      _lock.EnterReadLock();
      try
      {
        return FindExactPhraseIdInternal(wordIds);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получить первый символ фразы
    /// </summary>
    public int GetFirstSymbolFromPhraseId(int phraseId)
    {
      _lock.EnterReadLock();
      try
      {
        if (!PhraseTreeFromID.TryGetValue(phraseId, out var phraseNode))
          return 0;

        var wordIds = new List<int>();
        var currentNode = phraseNode;

        while (currentNode != null && currentNode.Id != 0)
        {
          wordIds.Add(currentNode.Element);
          currentNode = currentNode.Parent;
        }
        wordIds.Reverse();

        if (wordIds.Count == 0)
          return 0;

        int firstWordId = wordIds[0];
        var firstWord = GetWordFromWordIdInternal(firstWordId);
        if (string.IsNullOrEmpty(firstWord))
          return 0;

        if (_options.AtomicTokens)
        {
          if (_primaryTokens.TryGetValue(firstWord, out int primaryId))
            return primaryId;

          return 0;
        }

        return GetPrimarySensorId(firstWord[0]);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Проверяет существование фразы в дереве фраз
    /// </summary>
    /// <param name="phraseWords">Список слов фразы</param>
    /// <returns>true если фраза существует в дереве, false в противном случае</returns>
    public bool PhraseExists(List<string> phraseWords)
    {
      if (phraseWords == null || !phraseWords.Any())
        return false;

      foreach (var word in phraseWords)
      {
        if (!WordExists(word))
          return false;
      }

      var wordIds = new List<int>();
      foreach (var word in phraseWords)
      {
        var id = FindWordBranchId(word);
        if (id == 0)
          return false;
        wordIds.Add(id);
      }

      _lock.EnterReadLock();
      try
      {
        return PhraseTree.FindBranchInternal(wordIds) != 0;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Проверяет существование фразы по тексту
    /// </summary>
    /// <param name="phraseText">Текст фразы</param>
    /// <returns>true если фраза существует в дереве, false в противном случае</returns>
    public bool PhraseExists(string phraseText)
    {
      if (string.IsNullOrWhiteSpace(phraseText))
        return false;

      var words = Regex.Matches(phraseText, @"(\S+)")
                     .Cast<Match>()
                     .Select(m => m.Value)
                     .ToList();

      return PhraseExists(words);
    }

    /// <summary>
    /// Находит ID существующей фразы по тексту
    /// </summary>
    /// <param name="phraseText">Текст фразы</param>
    /// <returns>ID фразы или 0 если не найдена</returns>
    public int FindPhraseId(string phraseText)
    {
      if (string.IsNullOrWhiteSpace(phraseText))
        return 0;

      var words = Regex.Matches(phraseText, @"(\S+)")
                     .Cast<Match>()
                     .Select(m => m.Value)
                     .ToList();

      if (words.Count == 0)
        return 0;

      var wordIds = new List<int>();
      foreach (var word in words)
      {
        var wordId = FindWordBranchId(word);
        if (wordId == 0)
          return 0;
        wordIds.Add(wordId);
      }

      _lock.EnterReadLock();
      try
      {
        var exactId = FindExactPhraseIdInternal(wordIds);
        if (exactId != 0)
          return exactId;

        return PhraseTree.FindBranchInternal(wordIds);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Обрабатывает фразу, добавляя ее в дерево или песочницу
    /// </summary>
    /// <param name="wordIds">Список ID слов, составляющих фразу. Не может быть null или пустым.</param>
    /// <returns>
    /// ID фразы в дереве, если фраза была найдена или добавлена;
    /// null, если фраза не была добавлена (не в авторитарном режиме и не достигнут порог подтверждения)
    /// </returns>
    /// <exception cref="ArgumentNullException">Если wordIds равен null</exception>
    public int? ProcessPhrase(List<int> wordIds)
    {
      if (wordIds == null || wordIds.Count == 0) return null;

      _lock.EnterWriteLock();
      try
      {
        var existingId = PhraseTree.FindBranchInternal(wordIds);
        if (existingId != 0)
          return existingId;

        if (_authoritativeMode)
        {
          var newId = PhraseTree.AddBranch(wordIds);
          return newId;
        }

        bool isNew = PhraseSandbox.FindOrAdd(wordIds, out int count);
        if (!isNew && count >= _recognitionThreshold)
        {
          var newId = PhraseTree.AddBranch(wordIds);
          PhraseSandbox.Remove(wordIds);
          return newId;
        }

        return null;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Получает фразу по ее ID из дерева фраз
    /// </summary>
    /// <param name="phraseId">ID фразы</param>
    /// <returns>Строка, представляющая фразу, или пустая строка если фраза не найдена</returns>
    public string GetPhraseFromPhraseId(int phraseId)
    {
      _lock.EnterReadLock();
      try
      {
        return GetPhraseFromPhraseIdInternal(phraseId);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает фразу по ее ID из дерева фраз
    /// </summary>
    internal string GetPhraseFromPhraseIdInternal(int phraseId)
    {
      if (!PhraseTreeFromID.TryGetValue(phraseId, out var phraseNode))
        return string.Empty;

      var wordIds = new List<int>();
      var currentNode = phraseNode;

      while (currentNode != null && currentNode.Id != 0)
      {
        wordIds.Add(currentNode.Element);
        currentNode = currentNode.Parent;
      }

      wordIds.Reverse();

      var words = wordIds.Select(id => GetWordFromWordIdInternal(id));
      return string.Join(" ", words);
    }

    /// <summary>
    /// Разбивает фразу на части по пробелам и дефисам, возвращает список ID фраз (по одному на часть).
    /// Для цепочки: «тик-так» → [тик, так], «со ба ка» → [со, ба, ка]. Триггер при этом не меняется (остаётся «тик-так» или «собака»).
    /// </summary>
    /// <param name="phraseId">ID фразы (вербального стимула)</param>
    /// <returns>Список ID фраз по частям; пустой список при ошибке или пустой фразе</returns>
    public List<int> GetPartPhraseIdsFromPhraseId(int phraseId)
    {
      if (phraseId <= 0)
        return new List<int>();

      string phraseText = GetPhraseFromPhraseId(phraseId);
      if (string.IsNullOrWhiteSpace(phraseText))
        return new List<int>();

      var words = phraseText.Trim()
          .Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
      if (words.Length == 0)
        return new List<int>();

      var partPhraseIds = new List<int>();
      foreach (var word in words)
      {
        if (string.IsNullOrEmpty(word)) continue;
        var wordIdOpt = ProcessWord(word);
        if (wordIdOpt.HasValue)
          ProcessPhrase(new List<int> { wordIdOpt.Value });
        int partId = FindPhraseId(word);
        if (partId != 0)
          partPhraseIds.Add(partId);
      }
      return partPhraseIds;
    }

    #endregion

    #region Обработка текста

    /// <summary>
    /// Обрабатывает текст, разбивая его на слова и фразы.
    /// Вся обработка выполняется в одном захвате канальной блокировки,
    /// чтобы гарантировать атомарность перехода слово→дерево→фраза.
    /// </summary>
    /// <param name="text">Текст для обработки</param>
    /// <param name="maxPhraseLength">Максимальная длина фразы (по умолчанию 5)</param>
    public void ProcessText(string text, int maxPhraseLength = 0)
    {
      if (string.IsNullOrWhiteSpace(text)) return;

      var words = Regex.Matches(text, @"(\S+)")
                     .Cast<Match>()
                     .Select(m => m.Value)
                     .ToList();

      if (maxPhraseLength == 0)
        maxPhraseLength = _maxPhraseLength;

      _lock.EnterWriteLock();
      try
      {
        var wordIdMap = new Dictionary<string, int>();
        foreach (var word in words)
        {
          if (string.IsNullOrWhiteSpace(word)) continue;

          if (_options.AtomicTokens && !_primaryTokens.ContainsKey(word))
            continue;

          var existingId = FindWordBranchId(word);
          if (existingId != 0)
          {
            wordIdMap[word] = existingId;
            WordSandbox.Remove(word);
            continue;
          }

          if (_options.FilterGarbageWords && IsGarbageWord(word)) continue;

          if (_authoritativeMode)
          {
            var newId = AddWordBranch(word);
            if (newId != 0) wordIdMap[word] = newId;
            continue;
          }

          bool isNewWord = WordSandbox.FindOrAdd(word, out int wordCount);
          if (!isNewWord && wordCount >= _recognitionThreshold)
          {
            var newId = AddWordBranch(word);
            WordSandbox.Remove(word);
            if (newId != 0) wordIdMap[word] = newId;
          }
        }

        for (int i = 0; i < words.Count; i++)
        {
          for (int j = 1; j <= maxPhraseLength && i + j <= words.Count; j++)
          {
            var phraseSlice = words.Skip(i).Take(j).ToList();
            var phraseText = string.Join(" ", phraseSlice);

            var wordIds = new List<int>();
            bool allResolved = true;
            foreach (var w in phraseSlice)
            {
              if (wordIdMap.TryGetValue(w, out int wId))
              {
                wordIds.Add(wId);
              }
              else
              {
                allResolved = false;
                break;
              }
            }

            bool isNew = PhraseTextSandbox.FindOrAdd(phraseText, out int textCount);

            if (!allResolved) continue;

            var existingId = PhraseTree.FindBranchInternal(wordIds);
            if (existingId != 0)
            {
              PhraseTextSandbox.Remove(phraseText);
              PhraseSandbox.Remove(wordIds);
              continue;
            }

            if (_authoritativeMode)
            {
              PhraseTree.AddBranch(wordIds);
              PhraseTextSandbox.Remove(phraseText);
              continue;
            }

            if (!isNew && textCount >= _recognitionThreshold)
            {
              PhraseTree.AddBranch(wordIds);
              PhraseTextSandbox.Remove(phraseText);
              PhraseSandbox.Remove(wordIds);
            }
          }
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Распознает текст и возвращает ID фраз
    /// </summary>
    /// <param name="text">Текст для распознавания</param>
    /// <param name="authoritativeWrite">Флаг авторитарной записи (true - сразу в дерево, false - через песочницу)</param>
    /// <param name="maxPhraseLength">Максимальная длина фразы (0 - использовать системное значение)</param>
    /// <returns>Список ID распознанных фраз</returns>
    public List<int> RecognizeText(string text, bool authoritativeWrite = false, int maxPhraseLength = 0)
    {
      if (string.IsNullOrWhiteSpace(text))
        return new List<int>();

      bool originalMode = _authoritativeMode;

      if (authoritativeWrite)
        _authoritativeMode = true;

      try
      {
        return RecognizeTextInternal(text, maxPhraseLength);
      }
      finally
      {
        _authoritativeMode = originalMode;
      }
    }

    /// <summary>
    /// Внутренняя логика распознавания текста
    /// </summary>
    private List<int> RecognizeTextInternal(string text, int maxPhraseLength = 0)
    {
      try
      {
        var recognizedPhraseIds = new List<int>();

        ProcessText(text, maxPhraseLength);

        var words = Regex.Matches(text, @"(\S+)")
                       .Cast<Match>()
                       .Select(m => m.Value)
                       .ToList();

        var wordIds = new List<int>();
        foreach (var word in words)
        {
          var wordId = FindWordBranchId(word);
          if (wordId != 0)
            wordIds.Add(wordId);
        }

        if (wordIds.Count > 0)
        {
          var exactPhraseId = FindExactPhraseId(wordIds);
          if (exactPhraseId != 0)
          {
            recognizedPhraseIds.Add(exactPhraseId);
            return recognizedPhraseIds;
          }
        }

        return recognizedPhraseIds;
      }
      catch (Exception ex)
      {
        throw new InvalidOperationException(ex.Message);
      }
    }

    #endregion

    #region События очистки

    /// <summary>
    /// Событие очистки всех слов
    /// </summary>
    public event Action AllWordsCleared;

    /// <summary>
    /// Событие очистки всех фраз
    /// </summary>
    public event Action AllPhrasesCleared;

    /// <summary>
    /// Вызывает события очистки
    /// </summary>
    private void OnClearAllTrees()
    {
      AllWordsCleared?.Invoke();
      AllPhrasesCleared?.Invoke();
    }

    #endregion

    #region Освобождение ресурсов

    /// <summary>
    /// Освобождает ресурсы вербального канала
    /// </summary>
    public override void Dispose()
    {
      if (_disposed) return;
      try
      {
        WordTree?.Dispose();
        AtomicWordTree?.Dispose();
        PhraseTree?.Dispose();
        WordSandbox?.Dispose();
        PhraseSandbox?.Dispose();
        PhraseTextSandbox?.Dispose();
      }
      finally
      {
        base.Dispose();
      }
    }

    #endregion
  }
}
