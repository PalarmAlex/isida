using ISIDA.Common;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    #region Поля и свойства

    /// <summary>
    /// Дерево распознавания слов (символы -> слова)
    /// </summary>
    public SensorTree<int, char> WordTree { get; private set; }

    /// <summary>
    /// Дерево распознавания фраз (слова -> фразы)
    /// </summary>
    public SensorTree<int, int> PhraseTree { get; private set; }

    /// <summary>
    /// Песочница для новых слов
    /// </summary>
    public SensorSandbox<string> WordSandbox { get; private set; }

    /// <summary>
    /// Песочница для новых фраз
    /// </summary>
    public SensorSandbox<List<int>> PhraseSandbox { get; private set; }

    private Dictionary<char, int> _primarySensors = new Dictionary<char, int>();

    /// <summary>
    /// Словарь узлов дерева слов (ID -> узел)
    /// </summary>
    private IReadOnlyDictionary<int, SensorTree<int, char>.TreeNode<char>> WordTreeFromID =>
        WordTree.Nodes;

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
    /// <exception cref="ArgumentNullException">Выбрасывается если logger равен null</exception>
    public VerbalSensorChannel(GomeostasSystem gomeostasSystem, string baseFolderPath, string primarySensorsPath)
        : base(baseFolderPath, "")
    {
      try
      {
        _gomeostas = gomeostasSystem ?? throw new ArgumentNullException(nameof(gomeostasSystem));

        _primarySensorsPath = primarySensorsPath;
        LoadPrimarySensors(_primarySensorsPath);

        // Инициализация деревьев и песочниц
        WordTree = new SensorTree<int, char>("Words", baseFolderPath);
        PhraseTree = new SensorTree<int, int>("Phrases", baseFolderPath);
        WordSandbox = new SensorSandbox<string>("Words", baseFolderPath);
        PhraseSandbox = new SensorSandbox<List<int>>("Phrases", baseFolderPath);

        LoadTrees();
        LoadSandboxes();
      }
      catch
      {
        throw;
      }
    }

    private void LoadPrimarySensors(string filePath)
    {
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
        {
          _primarySensors[symbol[0]] = id;
        }
      }

      if (_primarySensors.Count == 0)
        throw new InvalidDataException("Файл первичных сенсоров не содержит валидных данных");
    }

    private void LoadTrees()
    {
      try
      {
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
        // Очищаем деревья
        WordTree.Clear();
        PhraseTree.Clear();

        // Очищаем песочницы
        WordSandbox.Clear();
        PhraseSandbox.Clear();

        // Сохраняем изменения
        WordTree.Save();
        PhraseTree.Save();
        WordSandbox.Save();
        PhraseSandbox.Save();

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
    /// Получить ID первичного символоа 
    /// </summary>
    public int GetPrimarySensorId(char symbol)
    {
      return _primarySensors.TryGetValue(symbol, out int id) ? id : 0;
    }

    /// <summary>
    /// Получить первичный символ 
    /// </summary>
    public char GetPrimarySensorSymbol(int id)
    {
      return _primarySensors.FirstOrDefault(x => x.Value == id).Key;
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
        // Ищем слово в дереве (включая промежуточные узлы)
        var existingId = WordTree.FindBranchInternal(word);
        return existingId != 0;
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
      // Получаем слово по ID
      var word = GetWordFromWordIdInternal(wordId);

      if (string.IsNullOrEmpty(word))
        return 0;

      // Берем первый символ
      char firstChar = word[0];

      // Ищем его ID в первичных сенсорах
      foreach (var line in File.ReadLines(_primarySensorsPath))
      {
        if (line.StartsWith("#")) continue;

        var parts = line.Split(new[] { "|#|" }, StringSplitOptions.None);
        if (parts.Length == 2 && parts[0].Trim() == firstChar.ToString())
        {
          return int.Parse(parts[1]);
        }
      }
      return 0;
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
        // Проверяем в дереве
        var existingId = WordTree.FindBranchInternal(word);
        if (existingId != 0) return existingId;

        // Фильтр мусора
        if (IsGarbageWord(word)) return null;

        // Авторитарный режим - сразу в дерево
        if (_authoritativeMode)
        {
          var newId = WordTree.AddBranch(word);
          WordTree.Save(); // Явное сохранение
          return newId;
        }

        // Работа с песочницей
        bool isNew = WordSandbox.FindOrAdd(word, out int count);
        if (!isNew && count >= _recognitionThreshold)
        {
          var newId = WordTree.AddBranch(word);
          WordSandbox.Remove(word);
          WordTree.Save(); // Явное сохранение
          return newId;
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
      // Одиночные не-буквы
      if (word.Length == 1 && !char.IsLetter(word[0]))
        return true;

      // Слишком длинные "слова" (возможные ошибки склейки)
      if (word.Length > 50)
        return true;

      // Регулярные выражения для типичного мусора
      var garbagePatterns = new[]
      {
        @"\d{12,}",           // Очень длинные цифры (ID, хеш, timestamp)
        @"[^\w\s]{4,}",      // 4+ спецсимволов подряд (но ..., !!! — разрешены)
        @"(.)\1{5,}",        // 6+ повторений символа (аааааа, !!!!!!)
        @"^[\W_]+$",         // Только символы, без букв/цифр (###, ---, ~~~)
        @"^https?://",       // Ссылки
        @"^www\.",           // Ссылки
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
      if (!WordTreeFromID.TryGetValue(wordId, out var node))
        return string.Empty;

      var symbols = new Stack<char>();
      while (node != null && node.Id != 0)
      {
        symbols.Push(node.Element);
        node = node.Parent;
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
      foreach (var node in WordTreeFromID.Values)
      {
        // Конечный узел - это узел без детей И с ненулевым ID
        if (node.Children.Count == 0 && node.Id != 0)
        {
          var word = GetWordFromWordIdInternal(node.Id);
          if (!string.IsNullOrEmpty(word))
          {
            words.Add(node.Id, word);
          }
        }
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

        char firstChar = firstWord[0];

        return GetPrimarySensorId(firstChar);
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

      // Сначала проверяем существование всех слов
      foreach (var word in phraseWords)
      {
        if (!WordExists(word))
          return false;
      }

      // Конвертируем слова в их ID
      var wordIds = new List<int>();
      foreach (var word in phraseWords)
      {
        var id = WordTree.FindBranchInternal(word);
        if (id == 0)
          return false;
        wordIds.Add(id);
      }

      _lock.EnterReadLock();
      try
      {
        // Ищем фразу в дереве
        var existingId = PhraseTree.FindBranchInternal(wordIds);
        return existingId != 0;
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

      // Разбиваем текст на слова
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

      // Разбиваем текст на слова
      var words = Regex.Matches(phraseText, @"(\S+)")
                     .Cast<Match>()
                     .Select(m => m.Value)
                     .ToList();

      if (words.Count == 0)
        return 0;

      // Сначала проверяем существование всех слов
      var wordIds = new List<int>();
      foreach (var word in words)
      {
        var wordId = WordTree.FindBranchInternal(word);
        if (wordId == 0)
          return 0;
        wordIds.Add(wordId);
      }

      _lock.EnterReadLock();
      try
      {
        // Ищем фразу в дереве
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
        // Проверяем в дереве
        var existingId = PhraseTree.FindBranchInternal(wordIds);
        if (existingId != 0) return existingId;

        // Фильтр мусора
        if (IsGarbagePhrase(wordIds)) return null;

        // Авторитарный режим - сразу в дерево
        if (_authoritativeMode)
        {
          var newId = PhraseTree.AddBranch(wordIds);
          PhraseTree.Save(); // Явное сохранение
          return newId;
        }

        // Работа с песочницей
        bool isNew = PhraseSandbox.FindOrAdd(wordIds, out int count);
        if (!isNew && count >= _recognitionThreshold)
        {
          var newId = PhraseTree.AddBranch(wordIds);
          PhraseSandbox.Remove(wordIds);
          PhraseTree.Save(); // Явное сохранение
          return newId;
        }

        return null;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    private bool IsGarbagePhrase(List<int> wordIds)
    {
      //// 1. Фразы из 1 слова (кроме исключений типа "Стоп!")
      //if (wordIds.Count == 1 && GetFirstSymbolFromWordId(wordIds[0]) != 60 /* ! */)
      //  return true;

      // 2. Повторяющиеся слова ("да да да")
      if (wordIds.Distinct().Count() < wordIds.Count)
        return true;

      return false;
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
      // Находим конечный узел фразы
      if (!PhraseTreeFromID.TryGetValue(phraseId, out var phraseNode))
        return string.Empty;

      // Собираем цепочку слов В ПРАВИЛЬНОМ ПОРЯДКЕ
      var wordIds = new List<int>();
      var currentNode = phraseNode;

      // Собираем все элементы от конечного узла до корня
      while (currentNode != null && currentNode.Id != 0)
      {
        wordIds.Add(currentNode.Element);
        currentNode = currentNode.Parent;
      }

      // РАЗВОРАЧИВАЕМ список, потому что дерево хранит слова в обратном порядке
      wordIds.Reverse();

      var words = wordIds.Select(id => GetWordFromWordIdInternal(id));
      return string.Join(" ", words);
    }

    /// <summary>
    /// Получает все фразы из дерева фраз
    /// </summary>
    internal Dictionary<int, string> GetAllPhrasesInternal()
    {
      var phrases = new Dictionary<int, string>();
      foreach (var node in PhraseTreeFromID.Values)
      {
        // Конечный узел - это узел без детей И с ненулевым ID
        if (node.Children.Count == 0 && node.Id != 0)
        {
          var phrase = GetPhraseFromPhraseIdInternal(node.Id);
          if (!string.IsNullOrEmpty(phrase))
            phrases.Add(node.Id, phrase);
        }
      }
      return phrases;
    }

    /// <summary>
    /// Получает все фразы из дерева фраз
    /// </summary>
    /// <returns>Словарь, где ключ - ID фразы, значение - сама фраза</returns>
    public Dictionary<int, string> GetAllPhrases()
    {
      _lock.EnterReadLock();
      try
      {
        return GetAllPhrasesInternal();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Обработка текста

    /// <summary>
    /// Обрабатывает текст, разбивая его на слова и фразы
    /// </summary>
    /// <param name="text">Текст для обработки</param>
    /// <param name="maxPhraseLength">Максимальная длина фразы (по умолчанию 5)</param>
    public void ProcessText(string text, int maxPhraseLength = 0)
    {
      if (string.IsNullOrWhiteSpace(text)) return;

      // Разбиваем текст на слова
      var words = Regex.Matches(text, @"(\S+)")  // \S+ - все непробельные символы
                     .Cast<Match>()
                     .Select(m => m.Value)
                     .ToList();

      // Обрабатываем слова
      var wordIds = new List<int>();
      foreach (var word in words)
      {
        var wordId = ProcessWord(word);
        if (wordId.HasValue)
        {
          wordIds.Add(wordId.Value);
        }
      }

      // если не передали в метод специальную длину фразы
      if (maxPhraseLength == 0)
        maxPhraseLength = _maxPhraseLength;

      // Обрабатываем фразы
      for (int i = 0; i < wordIds.Count; i++)
      {
        for (int j = 1; j <= maxPhraseLength && i + j <= wordIds.Count; j++)
        {
          var phraseWords = wordIds.Skip(i).Take(j).ToList();
          ProcessPhrase(phraseWords);
        }
      }

      // Явное сохранение всех данных
      WordTree.Save();
      PhraseTree.Save();
      WordSandbox.Save();
      PhraseSandbox.Save();
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

      // Сохраняем оригинальный режим
      bool originalMode = _authoritativeMode;

      // Временно устанавливаем нужный режим
      if (authoritativeWrite)
        _authoritativeMode = true;

      try
      {
        return RecognizeTextInternal(text, maxPhraseLength);
      }
      finally
      {
        // Восстанавливаем оригинальный режим
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

        // Нормализуем входной текст
        var inputTextNormalized = text.Trim().ToLower();

        //Logger.Info($"=== Распознавание текста: '{text}' ===");

        // обрабатываем текст (добавляем слова и фразы в дерево/песочницу)
        ProcessText(text, maxPhraseLength);

        // получаем все фразы из дерева после обработки
        var allPhrases = GetAllPhrasesInternal();

        //Logger.Info($"Доступные фразы в дереве: {string.Join("; ", allPhrases.Values)}");

        // Ищем точное совпадение
        foreach (var phrase in allPhrases)
        {
          var phraseNormalized = phrase.Value.ToLower();

          if (phraseNormalized == inputTextNormalized)
          {
            //Logger.Info($"✓ Найдено точное совпадение: '{phrase.Value}' -> ID {phrase.Key}");
            recognizedPhraseIds.Add(phrase.Key);
            return recognizedPhraseIds;
          }
        }

        //Logger.Info($"✗ Точное совпадение не найдено");

        // Если точного совпадения нет, ищем наиболее длинную подходящую фразу
        var candidatePhrases = new List<(int id, string phrase, int length)>();

        foreach (var phrase in allPhrases)
        {
          var phraseNormalized = phrase.Value.ToLower();

          // Проверяем, содержится ли фраза в тексте как подстрока
          if (inputTextNormalized.Contains(phraseNormalized))
          {
            candidatePhrases.Add((phrase.Key, phrase.Value, phrase.Value.Length));
            //Logger.Info($"~ Найдено частичное совпадение: '{phrase.Value}' в '{text}'");
          }
        }

        // Выбираем самую длинную подходящую фразу
        if (candidatePhrases.Any())
        {
          var bestMatch = candidatePhrases.OrderByDescending(x => x.length).First();
          //Logger.Info($"★ Выбрана фраза: '{bestMatch.phrase}' (ID: {bestMatch.id}, длина: {bestMatch.length})");
          recognizedPhraseIds.Add(bestMatch.id);
        }
        else
        {
          //Logger.Warning($"✗ Не найдено подходящих фраз для текста: '{text}'");
        }

        //Logger.Info($"=== Завершено распознавание ===");
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
        PhraseTree?.Dispose();
        WordSandbox?.Dispose();
        PhraseSandbox?.Dispose();
        _gomeostas?.Dispose();
      }
      finally
      {
        base.Dispose();
      }
    }
  }

  #endregion
}