using ISIDA.Psychic.Automatism;
using ISIDA.Psychic.Memory.Episodic;
using ISIDA.Psychic.Understanding;

namespace ISIDA.Psychic.Thinking.Strategies
{
  /// <summary>Контекст для выполнения одного шага стратегии: пульс, цикл и ссылки на подсистемы.</summary>
  public sealed class ThinkingStrategyContext
  {
    /// <summary>Номер текущего пульса.</summary>
    public int PulseCount { get; set; }

    /// <summary>Текущий цикл мышления.</summary>
    public ThinkingCycleInfo Cycle { get; set; }

    /// <summary>Система информационной среды.</summary>
    public InformationEnvironmentSystem InformationEnvironmentSystem { get; set; }

    /// <summary>Эпизодическая память.</summary>
    public EpisodicMemorySystem EpisodicMemorySystem { get; set; }

    /// <summary>Дерево понимания.</summary>
    public UnderstandingTreeSystem UnderstandingTreeSystem { get; set; }

    /// <summary>Дерево проблем.</summary>
    public ProblemTreeSystem ProblemTreeSystem { get; set; }

    /// <summary>Система автоматизмов.</summary>
    public AutomatizmSystem AutomatizmSystem { get; set; }

    /// <summary>Текущий штатный автоматизм в ветке (если есть).</summary>
    public Automatizm CurrentStaffAutomatizm { get; set; }

    /// <summary>Id инфо-функции для вызова (если диспетчер передаёт конкретный id).</summary>
    public int? OptionalInfoFuncId { get; set; }
  }
}

