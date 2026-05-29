using System.Collections.Generic;

namespace ISIDA.Niche
{
  /// <summary>
  /// Состояние параметров Niche для coupling и рефлексов (host-MVP или симбионт на GomeostasSystem).
  /// </summary>
  public interface INicheParameterState
  {
    /// <summary>True, если задан хотя бы один параметр Niche.</summary>
    bool IsInitialized { get; }

    /// <summary>ID последнего действия Creature, повлиявшего на Niche.</summary>
    int LastCreatureActionId { get; }

    /// <summary>Пульс последнего coupling-действия.</summary>
    int LastCreatureActionPulse { get; }

    /// <summary>Инициализирует параметры Niche.</summary>
    void Initialize(IEnumerable<NicheParameterDef> parameters);

    /// <summary>Сбрасывает значения к начальным из конфигурации.</summary>
    void ResetToInitial(IEnumerable<NicheParameterDef> parameters);

    /// <summary>Восстанавливает значения из снимка.</summary>
    void RestoreFromSnapshot(IReadOnlyDictionary<int, float> snapshot);

    /// <summary>Начало такта.</summary>
    void BeginPulse();

    /// <summary>Спонтанный дрейф и contour-input.</summary>
    void ApplySpontaneousUpdate(bool driftEnabled, IReadOnlyDictionary<int, float> contourDeltas);

    /// <summary>Coupling: дельта к параметру Niche.</summary>
    void ApplyCouplingDelta(int nicheParamId, float delta);

    /// <summary>Отмечает действие Creature на такте.</summary>
    void MarkCreatureAction(int actionId, int pulse);

    /// <summary>Завершение такта.</summary>
    Dictionary<int, float> EndPulse();

    /// <summary>Текущие значения параметров.</summary>
    Dictionary<int, float> GetCurrentValues();

    /// <summary>Снимок до начала такта.</summary>
    Dictionary<int, float> GetSnapshotBeforePulse();

    /// <summary>Расчёт spontaneous и response delta для лога.</summary>
    void ComputeDeltasForLog(
        Dictionary<int, float> stateAfter,
        out Dictionary<int, float> spontaneous,
        out Dictionary<int, float> response);
  }
}
