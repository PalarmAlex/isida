using ISIDA.Common;
using System;

namespace ISIDA.Psychic.Memory.Episodic
{
  /// <summary>
  /// Сервис записи правил в эпизодическую память (fix direct / teacher)
  /// </summary>
  public sealed class EpisodicMemoryRulesService
  {
    private readonly EpisodicMemorySystem _episodicMemory;

    /// <summary>Специальный эффект для учительских правил (100)</summary>
    public const int TeacherRuleEffect = 100;

    /// <summary>Создать сервис с привязкой к системе эпизодической памяти</summary>
    /// <param name="episodicMemory">Экземпляр EpisodicMemorySystem</param>
    public EpisodicMemoryRulesService(EpisodicMemorySystem episodicMemory)
    {
      _episodicMemory = episodicMemory ?? throw new ArgumentNullException(nameof(episodicMemory));
    }

    /// <summary>
    /// Записать прямое правило (после FinishTracking)
    /// TriggerId = CurStimulusImageId, ActionId = результат, Effect = UsefulnessDelta
    /// </summary>
    public void FixDirectRule(int triggerId, int actionId, int usefulnessDelta, int stimulsEffect)
    {
      if (!EpisodicMemorySystem.IsInitialized) return;
      if (AppGlobalState.EvolutionStage < 4)
      {
        Logger.Warning($"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для эпизодической памяти");
        return;
      }
      if (actionId <= 0) return;
      if (triggerId <= 0) return; // нет стимула — не записывать (кроме провокации, TODO)

      int effect = AddUtils.Clamp(usefulnessDelta, -10, 10);
      _episodicMemory.SaveNewEpisode(triggerId, actionId, effect, stimulsEffect, useOldCondition: false);
    }

    /// <summary>
    /// Записать учительское правило (при MarkOperatorRecognition)
    /// Effect = 100 (учительское), TriggerId = ответ оператора, ActionId = ответ Beast
    /// </summary>
    public void FixTeacherRule(int triggerId, int actionId, int stimulsEffect)
    {
      if (!EpisodicMemorySystem.IsInitialized) return;
      if (AppGlobalState.EvolutionStage < 4)
      {
        Logger.Warning($"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для эпизодической памяти");
        return;
      }
      if (actionId <= 0 || triggerId <= 0) return;

      _episodicMemory.SaveNewEpisode(triggerId, actionId, TeacherRuleEffect, stimulsEffect, useOldCondition: true);
    }

    /// <summary>Вставить пустой кадр — конец темы</summary>
    /// <remarks>Доступно с 4 стадии развития</remarks>
    public void SetInterruption()
    {
      if (AppGlobalState.EvolutionStage < 4)
      {
        Logger.Warning($"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для эпизодической памяти");
        return;
      }
      _episodicMemory?.SetInterruption();
    }
  }
}
