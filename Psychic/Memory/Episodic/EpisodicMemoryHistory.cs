using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic.Memory.Episodic
{
  /// <summary>
  /// Историческая лента кадров эпизодической памяти (порядок появления)
  /// </summary>
  public class EpisodicMemoryHistory
  {
    private readonly List<EpisodicHistoryEntry> _entries = new List<EpisodicHistoryEntry>();

    /// <summary>Список записей истории (только чтение)</summary>
    public IReadOnlyList<EpisodicHistoryEntry> Entries => _entries;

    /// <summary>Добавить запись в историю</summary>
    public void Append(int nodeId, int lifeTime)
    {
      _entries.Add(new EpisodicHistoryEntry { NodeId = nodeId, LifeTime = lifeTime });
    }

    /// <summary>Вставить пустой кадр (ID=-1) — конец темы</summary>
    public void SetInterruption(int lifeTime)
    {
      if (_entries.Count == 0) return;
      if (_entries[_entries.Count - 1].NodeId == -1) return;
      _entries.Add(new EpisodicHistoryEntry { NodeId = -1, LifeTime = lifeTime });
    }

    /// <summary>Последние limit записей (только NodeId)</summary>
    public List<int> GetLastSequence(int limit)
    {
      var len = _entries.Count;
      if (len == 0) return new List<int>();
      if (len < limit) limit = len;
      return _entries.Skip(len - limit).Select(e => e.NodeId).ToList();
    }

    /// <summary>Последние limit записей истории (от старых к новым)</summary>
    public List<EpisodicHistoryEntry> GetLastEntries(int limit)
    {
      var len = _entries.Count;
      if (len == 0) return new List<EpisodicHistoryEntry>();
      if (len < limit) limit = len;
      return _entries.Skip(len - limit).ToList();
    }

    /// <summary>Очистить историю</summary>
    public void Clear()
    {
      _entries.Clear();
    }

    /// <summary>Загрузить историю из перечисления записей</summary>
    public void LoadFromEntries(IEnumerable<EpisodicHistoryEntry> entries)
    {
      _entries.Clear();
      if (entries != null)
        _entries.AddRange(entries);
    }
  }
}
