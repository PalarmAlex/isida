namespace ISIDA.Psychic
{
  /// <summary>
  /// Снимок текущей информационной среды (инфо-картины) для диагностического UI.
  /// </summary>
  public sealed class InformationEnvironmentViewSnapshot
  {
    /// <summary>Момент кадра (LifeTime).</summary>
    public int LifeTime { get; set; }

    /// <summary>Опасность.</summary>
    public bool Danger { get; set; }

    /// <summary>Срочность / очень актуальная ситуация.</summary>
    public bool VeryActualSituation { get; set; }

    /// <summary>Общее настроение (Mood).</summary>
    public int Mood { get; set; }

    /// <summary>Субъективное настроение (PsyMood).</summary>
    public int PsyMood { get; set; }

    /// <summary>Текущая эмоция (Id).</summary>
    public int PsyEmotionId { get; set; }

    /// <summary>Образ действий (ActionsImageID).</summary>
    public int ActionsImageId { get; set; }

    /// <summary>Актуальный эпизод (ActualEpisodicMemoryID).</summary>
    public int ActualEpisodicMemoryId { get; set; }

    /// <summary>Доминанта нерешённой проблемы.</summary>
    public int DominantaId { get; set; }

    /// <summary>Нужно думать об автоматизме.</summary>
    public bool NeedThinkingAboutAutomatizm { get; set; }

    /// <summary>Период ожидания ответа с пульта.</summary>
    public bool IsWaitingPeriod { get; set; }

    /// <summary>Проблема не решена на 2 уровне.</summary>
    public bool UnresolvedAtThinkingLevel2 { get; set; }

    /// <summary>Узел нерешённой проблемы (2 уровень).</summary>
    public int UnresolvedNodeId { get; set; }

    /// <summary>Образ действий при нерешённой проблеме.</summary>
    public int UnresolvedActionsImageId { get; set; }

    /// <summary>Пульс фиксации нерешённой проблемы.</summary>
    public int UnresolvedPulseCount { get; set; }

    /// <summary>Сон.</summary>
    public bool IsSleep { get; set; }

    /// <summary>Стимул навязывает несоответствие теме/цели.</summary>
    public bool IsStimulToForce { get; set; }

    /// <summary>Строка целей гомеостаза (CurTargetArrID), через запятую.</summary>
    public string CurTargetArrIdText { get; set; }
  }
}
