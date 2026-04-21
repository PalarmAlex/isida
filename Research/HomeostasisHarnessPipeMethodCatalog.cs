using System;
using System.Collections.Generic;

namespace ISIDA.Research
{
  /// <summary>Описание метода для прогона в формате «строки с разделителем |» (карточка + пример данных).</summary>
  public sealed class ResearchHarnessPipeMethodInfo
  {
    private readonly ResearchHarnessResultColumn[] _resultColumns;

    /// <summary>Создаёт описание pipe-метода для UI и прогона.</summary>
    /// <param name="harnessId">Идентификатор прогона (<see cref="HomeostasisHarnessIds"/>).</param>
    /// <param name="title">Краткое имя в списке.</param>
    /// <param name="cardDescription">Текст карточки.</param>
    /// <param name="pipeFormatLine">Одна строка с именами колонок через |.</param>
    /// <param name="columnLabels">Подписи всех колонок (входы, затем ожидаемые выходы).</param>
    /// <param name="defaultSampleText">Встроенный пример многострочного сценария.</param>
    /// <param name="resultColumns">Типы выходных слотов; по умолчанию один булев Out1.</param>
    public ResearchHarnessPipeMethodInfo(
        string harnessId,
        string title,
        string cardDescription,
        string pipeFormatLine,
        string[] columnLabels,
        string defaultSampleText,
        IReadOnlyList<ResearchHarnessResultColumn> resultColumns = null)
    {
      HarnessId = harnessId ?? throw new ArgumentNullException(nameof(harnessId));
      Title = title ?? throw new ArgumentNullException(nameof(title));
      CardDescription = cardDescription ?? "";
      PipeFormatLine = pipeFormatLine ?? "";
      ColumnLabels = columnLabels ?? throw new ArgumentNullException(nameof(columnLabels));
      DefaultSampleText = defaultSampleText ?? "";

      _resultColumns = resultColumns == null || resultColumns.Count == 0
          ? new[] { new ResearchHarnessResultColumn("Out1", HarnessValueKind.Boolean) }
          : new List<ResearchHarnessResultColumn>(resultColumns).ToArray();

      if (ColumnLabels.Length <= _resultColumns.Length)
        throw new ArgumentException("Должны быть колонки входа и отдельно — ожидаемые выходы в конце.", nameof(columnLabels));
    }

    /// <summary>Идентификатор прогона (как в <see cref="HomeostasisHarnessIds"/>).</summary>
    public string HarnessId { get; }

    /// <summary>Краткое имя для списка выбора.</summary>
    public string Title { get; }

    /// <summary>Текст карточки: что делает метод, какие поля.</summary>
    public string CardDescription { get; }

    /// <summary>Одна строка-подсказка: имена колонок через |.</summary>
    public string PipeFormatLine { get; }

    /// <summary>Подписи колонок в порядке следования в строке через | (входы, затем ожидаемые выходы).</summary>
    public string[] ColumnLabels { get; }

    /// <summary>Встроенный пример сценария (не из файла).</summary>
    public string DefaultSampleText { get; }

    /// <summary>Последние <see cref="ResultSlotCount"/> колонок в строке — ожидаемые значения выходов.</summary>
    public IReadOnlyList<ResearchHarnessResultColumn> ResultColumns => _resultColumns;

    /// <summary>Число колонок ожидаемого выхода в конце строки.</summary>
    public int ResultSlotCount => _resultColumns.Length;

    /// <summary>Число колонок входа (все кроме выходных слотов).</summary>
    public int InputColumnCount => ColumnLabels.Length - ResultSlotCount;

    /// <summary>Общее число колонок в одной строке pipe.</summary>
    public int ColumnCount => ColumnLabels.Length;

    /// <summary>Встроенные прогоны гомеостаза (pipe).</summary>
    public static ResearchHarnessPipeMethodInfo[] All { get; } =
    {
      new ResearchHarnessPipeMethodInfo(
          HomeostasisHarnessIds.HasCriticalParameterChanges,
          "Ухудшение жизненно важных параметров (HasCriticalParameterChanges)",
          "Проверяет, было ли за один шаг значительное ухудшение хотя бы одного жизненно важного параметра по сравнению с предыдущим снимком.\n" +
          "Вход: один параметр в «текущем» и «предыдущем» состоянии (значение, вес, норма, скорость, жизненная важность, критические границы).\n" +
          "Первая колонка — числовой id параметра (как в модели), отдельное текстовое имя кейса не используется.\n" +
          "Выход Out1: ожидаемый результат метода (0/1): было ли критическое ухудшение.",
          "id параметра|текущее значение|предыдущее значение|вес|норма|скорость|жизненно важен|крит.мин|крит.макс|Out1 ожидание",
          new[]
          {
            "P1 id параметра (целое)",
            "P2 текущее значение (число)",
            "P3 предыдущее значение (число)",
            "P4 вес (целое)",
            "P5 норма (целое)",
            "P6 скорость (целое, отрицательная — дефицит)",
            "P7 жизненно важен (0/1, да/нет)",
            "P8 критический минимум (число)",
            "P9 критический максимум (число)",
            "P10 Out1 ожидаемый результат (0/1)"
          },
          "1|40|50|50|50|-10|1|0|100|1\n" +
          "2|10|50|50|50|-10|0|0|100|0"),

      new ResearchHarnessPipeMethodInfo(
          HomeostasisHarnessIds.AnyVitalHarmfulZone,
          "Опасная зона для жизненно важных (AnyVitalParameterInHarmfulZone)",
          "Проверяет, находится ли хотя бы один жизненно важный параметр в зоне «хуже нормы» (для дефицита — значение ниже нормы, для избытка — выше).\n" +
          "Вход: один параметр (id, значение, вес, норма, скорость, жизненная важность, критические границы).\n" +
          "Выход Out1: ожидаемый результат (0/1).",
          "id параметра|значение|вес|норма|скорость|жизненно важен|крит.мин|крит.макс|Out1 ожидание",
          new[]
          {
            "P1 id параметра (целое)",
            "P2 значение (число)",
            "P3 вес (целое)",
            "P4 норма (целое)",
            "P5 скорость (целое)",
            "P6 жизненно важен (0/1, да/нет)",
            "P7 критический минимум (число)",
            "P8 критический максимум (число)",
            "P9 Out1 ожидаемый результат (0/1)"
          },
          "1|40|50|50|-10|1|0|100|1\n" +
          "1|55|50|50|-10|1|0|100|0"),

      new ResearchHarnessPipeMethodInfo(
          HomeostasisHarnessIds.ExternalImpactCriticalFlags,
          "Внешнее воздействие: порог и ориентация (HasExternalCriticalImpact + IsExternalImpactCritical)",
          "Один жизненно важный (или нет) параметр и одно внешнее воздействие на его id (целое, знак важен для ориентации).\n" +
          "Out «порог»: HasExternalCriticalImpact — |воздействие| > 5 на жизненно важном.\n" +
          "Out «ориентация»: IsExternalImpactCritical — вредное по знаку воздействие, |воздействие| > |Speed| и параметр «у критики» (см. код калькулятора).\n" +
          "Критические границы min/max участвуют во втором методе.",
          "id|значение|вес|норма|скорость|жизненно важен|крит.мин|крит.макс|воздействие|Out порог ожид.|Out ориентация ожид.",
          new[]
          {
            "P1 id параметра (целое)",
            "P2 значение (число)",
            "P3 вес (целое)",
            "P4 норма (целое)",
            "P5 скорость (целое)",
            "P6 жизненно важен (0/1)",
            "P7 критический минимум",
            "P8 критический максимум",
            "P9 воздействие (целое, знак)",
            "P10 Out порог (0/1)",
            "P11 Out ориентация (0/1)"
          },
          "1|48|50|50|-10|1|0|100|-6|1|0\n" +
          "1|48|50|50|-10|1|0|100|-12|1|1",
          new[]
          {
            new ResearchHarnessResultColumn("порог>|impact|>5", HarnessValueKind.Boolean),
            new ResearchHarnessResultColumn("ориентация (крит.)", HarnessValueKind.Boolean)
          }),

      new ResearchHarnessPipeMethodInfo(
          HomeostasisHarnessIds.CalculateUrgencyFunction,
          "Функция потребности Ui (CalculateUrgencyFunction)",
          "Скаляр срочности 0…1 по одному параметру (вес, норма, скорость, значение).\n" +
          "Сравнение с эталоном — по абсолютной погрешности 1e-5.",
          "id|значение|вес|норма|скорость|жизненно важен|крит.мин|крит.макс|Out срочность ожидание",
          new[]
          {
            "P1 id параметра (целое)",
            "P2 значение (число)",
            "P3 вес (целое)",
            "P4 норма (целое)",
            "P5 скорость (целое)",
            "P6 жизненно важен (0/1, не влияет на Ui)",
            "P7 критический минимум",
            "P8 критический максимум",
            "P9 Out срочность ожидание (число 0…1)"
          },
          "1|30|80|50|-10|1|0|100|0.32\n" +
          "1|60|80|50|10|1|0|100|0.16",
          new[] { new ResearchHarnessResultColumn("срочность", HarnessValueKind.Float) }),

      new ResearchHarnessPipeMethodInfo(
          HomeostasisHarnessIds.ComputeOperatorAutomatizmAssessment,
          "Оценка оператора −1/0/+1 (ComputeOperatorAutomatizmAssessment), до 2 параметров",
          "Снимок «до» в словаре valuesBefore и текущие ParameterData (1 или 2 штуки; второй с id=0 не используется).\n" +
          "focus_parameter_id: 0 — не задавать фокус; иначе id параметра из строки.\n" +
          "overall_before/after: −1 = Bad, 0 = Normal, 1 = Well (как AppGlobalState.HomeostasisState).\n" +
          "Для каждого параметра: id, значение до, текущее значение, вес, норма, скорость, жизненность, крит.мин, крит.макс.",
          "focus_id|overall_before|overall_after|p1_id|before1|cur1|w1|n1|sp1|vit1|cmin1|cmax1|p2_id|before2|cur2|w2|n2|sp2|vit2|cmin2|cmax2|Out оценка ожидание",
          new[]
          {
            "P1 focus_parameter_id (0 = нет)",
            "P2 overall_before (−1/0/1)",
            "P3 overall_after (−1/0/1)",
            "P4 p1 id",
            "P5 p1 значение до",
            "P6 p1 текущее",
            "P7 p1 вес",
            "P8 p1 норма",
            "P9 p1 скорость",
            "P10 p1 жизненно важен",
            "P11 p1 крит.мин",
            "P12 p1 крит.макс",
            "P13 p2 id (0 = только один параметр)",
            "P14 p2 значение до",
            "P15 p2 текущее",
            "P16 p2 вес",
            "P17 p2 норма",
            "P18 p2 скорость",
            "P19 p2 жизненно важен",
            "P20 p2 крит.мин",
            "P21 p2 крит.макс",
            "P22 Out ожидание (−1, 0 или 1)"
          },
          "0|0|0|1|50|40|50|50|-10|1|0|100|0|0|0|50|50|-1|0|0|100|-1\n" +
          "1|0|0|1|50|55|50|50|-10|1|0|100|0|0|0|50|50|-1|0|0|100|1",
          new[] { new ResearchHarnessResultColumn("оценка", HarnessValueKind.TrinaryInt) }),

      new ResearchHarnessPipeMethodInfo(
          HomeostasisHarnessIds.DominantAndFinalStyles,
          "Доминирующий параметр и финальные стили (FindDominantParameter + вызов GetFinalActiveStyles)",
          "Один параметр с привязками стилей по зонам (формат активаций как в данных агента: «зона:id1,id2;…»).\n" +
          "base_style_ids — список id стилей через запятую (создаются минимальные BehaviorStyle для вызова GetFinalActiveStyles).\n" +
          "Перед прогоном фиксируется GlobalPulsCount для детерминизма.\n" +
          "Выходы: id доминирующего параметра, зона (int), dominanceScore (float).\n" +
          "Рекомендуется сразу нажать «Автогенерация» в UI — встроенная строка примерная.",
          "dynamic_time|dif_sensor|base_style_ids|id|значение|вес|норма|скорость|жизненно важен|крит.мин|крит.макс|активации|dominant_id|зона|score",
          new[]
          {
            "P1 dynamic_time (целое)",
            "P2 dif_sensor (число)",
            "P3 base_style_ids (id через запятую)",
            "P4 id параметра",
            "P5 значение",
            "P6 вес",
            "P7 норма",
            "P8 скорость",
            "P9 жизненно важен",
            "P10 крит.мин",
            "P11 крит.макс",
            "P12 активации (зона:стили;…)",
            "P13 ожидаемый dominant_id",
            "P14 ожидаемая зона",
            "P15 ожидаемый score"
          },
          "5|0.5|9101,9102,9103|1|48|55|50|-10|1|0|100|4:9101;5:9102;6:9103|1|4|120\n",
          new[]
          {
            new ResearchHarnessResultColumn("dominant_id", HarnessValueKind.Int32),
            new ResearchHarnessResultColumn("зона", HarnessValueKind.Int32),
            new ResearchHarnessResultColumn("score", HarnessValueKind.Float)
          })
    };
  }
}
