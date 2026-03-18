using System.Collections.Generic;

namespace ISIDA.Psychic.Thinking
{
  internal sealed class ThinkingInterruptImage
  {
    public int UnresolvedNodeId { get; set; }
    public int UnresolvedActionsImageId { get; set; }
    public int ProblemNodeId { get; set; }
    public int ThemeId { get; set; }
    public int PurposeId { get; set; }
    public int SavedPulse { get; set; }
  }

  /// <summary>
  /// Стек прерванных задач (упрощённый аналог BOT InterruptMemory).
  /// Хранит несколько последних прерванных контекстов мышления.
  /// </summary>
  internal sealed class ThinkingInterruptMemory
  {
    private readonly List<ThinkingInterruptImage> _stack = new List<ThinkingInterruptImage>();
    private readonly int _maxSize;

    public ThinkingInterruptMemory(int maxSize = 7)
    {
      _maxSize = maxSize < 1 ? 1 : maxSize;
    }

    public int Count => _stack.Count;

    public void Push(ThinkingInterruptImage img)
    {
      if (img == null) return;
      if (_stack.Count >= _maxSize)
        _stack.RemoveAt(0);
      _stack.Add(img);
    }

    public ThinkingInterruptImage PopLast()
    {
      if (_stack.Count == 0) return null;
      var idx = _stack.Count - 1;
      var img = _stack[idx];
      _stack.RemoveAt(idx);
      return img;
    }
  }
}

