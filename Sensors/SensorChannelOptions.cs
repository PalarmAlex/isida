namespace ISIDA.Sensors
{
  /// <summary>
  /// Параметры экземпляра канала токенов/паттернов (вербальный или командный).
  /// </summary>
  public sealed class SensorChannelOptions
  {
    /// <summary>Вербальный канал: побуквенные токены, Words.dat / Phrases.dat.</summary>
    public static readonly SensorChannelOptions Verbal = new SensorChannelOptions(
        wordsTreeName: "Words",
        phrasesTreeName: "Phrases",
        wordSandboxName: "Words",
        phraseSandboxName: "Phrases",
        phraseTextSandboxName: "PhrasesText",
        atomicTokens: false,
        filterGarbageWords: true);

    /// <summary>Командный канал: атомарные контуры из DefaultCommandPrimaries, CommandWords.dat / CommandPhrases.dat.</summary>
    public static readonly SensorChannelOptions Command = new SensorChannelOptions(
        wordsTreeName: "CommandWords",
        phrasesTreeName: "CommandPhrases",
        wordSandboxName: "CommandWords",
        phraseSandboxName: "CommandPhrases",
        phraseTextSandboxName: "CommandPhrasesText",
        atomicTokens: true,
        filterGarbageWords: false);

    private SensorChannelOptions(
        string wordsTreeName,
        string phrasesTreeName,
        string wordSandboxName,
        string phraseSandboxName,
        string phraseTextSandboxName,
        bool atomicTokens,
        bool filterGarbageWords)
    {
      WordsTreeName = wordsTreeName;
      PhrasesTreeName = phrasesTreeName;
      WordSandboxName = wordSandboxName;
      PhraseSandboxName = phraseSandboxName;
      PhraseTextSandboxName = phraseTextSandboxName;
      AtomicTokens = atomicTokens;
      FilterGarbageWords = filterGarbageWords;
    }

    /// <summary>Имя файла дерева токенов/контуров (без расширения).</summary>
    public string WordsTreeName { get; }

    /// <summary>Имя файла дерева паттернов/групп (без расширения).</summary>
    public string PhrasesTreeName { get; }

    /// <summary>Имя файла песочницы токенов/контуров.</summary>
    public string WordSandboxName { get; }

    /// <summary>Имя файла песочницы паттернов (списки ID токенов).</summary>
    public string PhraseSandboxName { get; }

    /// <summary>Имя файла текстовой песочницы паттернов.</summary>
    public string PhraseTextSandboxName { get; }

    /// <summary>true — токен атомарный (команда); false — побуквенное дерево (речь).</summary>
    public bool AtomicTokens { get; }

    /// <summary>true — отфильтровывать «мусорные» вербальные токены перед записью в дерево.</summary>
    public bool FilterGarbageWords { get; }
  }
}
