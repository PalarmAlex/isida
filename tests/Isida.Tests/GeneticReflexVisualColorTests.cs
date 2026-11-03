using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Зрительный канал (тип документа) как свойство безусловного рефлекса
  /// (<see cref="GeneticReflexesSystem.GeneticReflex.VisualColorId"/>).
  ///
  /// Дерево б/у-рефлексов матчится строгим равенством ID образа-триггера
  /// (<c>ReflexTreeSystem.GetReflexTreeNode</c> → <c>child.ActionID == conditions[0]</c>), поэтому
  /// цвет зашит внутрь ID образа: рефлекс «заточен» под конкретный тип документа, и его узел
  /// ссылается на цветной образ. Проверки:
  /// - активация срабатывает ровно при совпадении цвета узла и цвета стимула;
  /// - старый .dat без 8-го поля грузится с цветом 0 (миграция);
  /// - рефлексы, отличающиеся только цветом, не считаются дублями (у них разные образы-триггеры);
  /// - Save пишет цвет 8-м полем, Load читает его обратно.
  /// </summary>
  public sealed class GeneticReflexVisualColorTests : EngineIntegrationTestBase, IDisposable
  {
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    // Снимок состояния генетических рефлексов: LoadGeneticReflexes() полностью перезаписывает
    // словарь рефлексов фикстуры, а ResetToCleanState его НЕ восстанавливает (п. 6 фикстуры).
    // Без отката тест «отравил» бы всю последовательную коллекцию EngineIntegration.
    private Dictionary<int, GeneticReflexesSystem.GeneticReflex> _savedReflexes;
    private Dictionary<int, string> _savedTriggerKeys;
    private int _savedLastId;

    // Снимок счётчиков ID образов: ResetToCleanState обнуляет их, но дерево б/у (ReflexTree.dat)
    // переживает тесты. Если создавать образы с малыми ID, они пересекутся с ActionID чужих узлов
    // и фолбэк-поиск (FindReflexTreeNodeFromCondition) даст ложное DetectedLevel==2. Задерая
    // счётчики на заведомо большое значение, получаем уникальные ID образов, которых нет в дереве.
    private int _savedLastPerceptionImageId;
    private int _savedLastStyleImageId;

    public GeneticReflexVisualColorTests(EngineFixture engine) : base(engine)
    {
      // Снимок берём ПОСЛЕ ResetToCleanState (в базовом конструкторе) — он фиксирует чистое
      // состояние с эталонным рефлексом фикстуры.
      _savedReflexes = new Dictionary<int, GeneticReflexesSystem.GeneticReflex>(ReflexesField());
      _savedTriggerKeys = new Dictionary<int, string>(TriggerKeysField());
      _savedLastId = (int)LastIdField();

      _savedLastPerceptionImageId = (int)GetField(Images, "_lastPerceptionImageId");
      _savedLastStyleImageId = (int)GetField(Images, "_lastBehaviorStyleImageId");
      SetField(Images, "_lastPerceptionImageId", 1_000_000);
      SetField(Images, "_lastBehaviorStyleImageId", 1_000_000);
    }

    /// <summary>
    /// Восстанавливает словарь генетических рефлексов и счётчики ID образов к снимку,
    /// возвращает движок в стадию 1.
    /// </summary>
    public void Dispose()
    {
      var reflexes = ReflexesField();
      reflexes.Clear();
      foreach (var kv in _savedReflexes)
        reflexes[kv.Key] = kv.Value;

      var keys = TriggerKeysField();
      keys.Clear();
      foreach (var kv in _savedTriggerKeys)
        keys[kv.Key] = kv.Value;

      SetLastId(_savedLastId);
      SetField(Images, "_lastPerceptionImageId", _savedLastPerceptionImageId);
      SetField(Images, "_lastBehaviorStyleImageId", _savedLastStyleImageId);
      AppGlobalState.EvolutionStage = 1;
    }

    private static GeneticReflexesSystem System => GeneticReflexesSystem.Instance;

    private static object GetField(object target, string name) =>
        target.GetType().GetField(name, PrivateInstance).GetValue(target);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance).SetValue(target, value);

    private static Dictionary<int, GeneticReflexesSystem.GeneticReflex> ReflexesField() =>
        (Dictionary<int, GeneticReflexesSystem.GeneticReflex>)typeof(GeneticReflexesSystem)
            .GetField("_geneticReflexes", PrivateInstance).GetValue(System);

    private static Dictionary<int, string> TriggerKeysField() =>
        (Dictionary<int, string>)typeof(GeneticReflexesSystem)
            .GetField("_geneticReflexTriggerKeys", PrivateInstance).GetValue(System);

    private static object LastIdField() =>
        typeof(GeneticReflexesSystem)
            .GetField("_lastGeneticReflexId", PrivateInstance).GetValue(System);

    private static void SetLastId(int value) =>
        typeof(GeneticReflexesSystem)
            .GetField("_lastGeneticReflexId", PrivateInstance).SetValue(System, value);

    private int NewStyle()
    {
      var (id, _) = GomeostasSystem.Instance.AddBehaviorStyle(
          "Стиль-" + Guid.NewGuid().ToString("N"), "");
      Assert.True(id > 0);
      return id;
    }

    private int NewAction()
    {
      var (id, _) = AdaptiveActionsSystem.Instance.AddAction(
          "Действие-" + Guid.NewGuid().ToString("N"), "", Vigor: 5);
      Assert.True(id > 0);
      return id;
    }

    [Fact]
    public void TreeMatch_HitsOnlyWhenStimulusColorMatchesReflexColor()
    {
      int level1 = 0;
      int color = AgentVisualColor.Black;

      // Свой (уникальный) стиль и его образ: фолбэк-поиск дерева (FindReflexTreeNodeFromCondition)
      // ищет узел по тройке (base, style, action). Если бы стиль-образ был общим для всех
      // тестов (SeedStyleId), чужой узел с тем же числовым ActionID дал бы ложное DetectedLevel==2.
      AppGlobalState.EvolutionStage = 0;
      int style = NewStyle();
      int styleImageId = Images.AddBehaviorStyleImage(new List<int> { style });

      // Цветной образ-триггер заводится так же, как ReflexTreeSystem.OnGeneticReflexCreated
      // при привязке узла б/у-рефлекса.
      int triggerImageId = Images.AddPerceptionImage(
          new List<int> { Engine.NextSeed() }, new List<int>(), color, null);
      Assert.True(triggerImageId > 0, "цветной образ-триггер не создан");

      // Привязываем узел дерева к цветному триггеру (как OnGeneticReflexCreated → FindOrCreateNodeForReflex).
      int nodeId = ReflexTreeSystem.Instance.FindOrCreateNodeForReflex(
          new[] { level1, styleImageId, triggerImageId }, Engine.SeedGeneticReflexId);
      Assert.True(nodeId > 0);

      // Совпадение цвета: узел найден на полном уровне (состояние+стиль+триггер).
      ReflexTreeSystem.Instance.ConditionsDetection(new[] { level1, styleImageId, triggerImageId });
      Assert.Equal(2, ReflexTreeSystem.Instance.DetectedLevel);

      // Другой цвет → другой ID образа-триггера → узел не находится полностью (код 2 = красный).
      int otherTriggerImageId = Images.AddPerceptionImage(
          new List<int> { Engine.NextSeed() }, new List<int>(), 2, null);
      Assert.NotEqual(triggerImageId, otherTriggerImageId);

      ReflexTreeSystem.Instance.ConditionsDetection(new[] { level1, styleImageId, otherTriggerImageId });
      Assert.NotEqual(2, ReflexTreeSystem.Instance.DetectedLevel);
    }

    [Fact]
    public void Load_MigratesLegacyLineWithoutColorToZero()
    {
      string path = System.GetGeneticReflexesFilePath();
      string styleToken = Engine.SeedStyleId.ToString();
      string actionToken = AdaptiveActionsSystem.Instance.GetAllAdaptiveActions().First().Id.ToString();

      // Старый формат без 8-го поля цвета (7 полей, ReflexChainID = 0).
      File.WriteAllLines(path, new List<string>
      {
        "# Формат: ID|Level1|Level2|influence_action_ids|command_pattern_ids|Моторные действия|ReflexChainID",
        "501|0|" + styleToken + "|||" + actionToken + "|0"
      });

      InvokeLoad();

      var migrated = System.GetGeneticReflex(501);
      Assert.NotNull(migrated);
      Assert.Equal(AgentVisualColor.White, migrated.VisualColorId);

      // Строка с 8-м полем цвета читается как указанный цвет (код 5 = зелёный).
      File.WriteAllLines(path, new List<string>
      {
        "502|0|" + styleToken + "|||" + actionToken + "|0|5"
      });

      InvokeLoad();

      var colored = System.GetGeneticReflex(502);
      Assert.NotNull(colored);
      Assert.Equal(5, colored.VisualColorId);
    }

    [Fact]
    public void Save_WritesColorAsEighthField_RoundTrips()
    {
      AppGlobalState.EvolutionStage = 0;
      int style = NewStyle();
      int reflexId = System.AddGeneticReflex(
          0, new List<int> { style }, null, null,
          new List<int> { NewAction() }, 7).ActionId; // код 7 = синий

      string path = System.GetGeneticReflexesFilePath();
      var (ok, err) = System.SaveGeneticReflexes(false);
      Assert.True(ok, "SaveGeneticReflexes: " + err);

      var line = File.ReadAllLines(path).First(l => l.StartsWith(reflexId + "|", StringComparison.Ordinal));
      var parts = line.Split('|');
      Assert.True(parts.Length >= 8, "ожидалось 8-е поле цвета, строка: " + line);
      Assert.Equal(7, int.Parse(parts[7]));

      // Обратное чтение сохраняет цвет.
      InvokeLoad();
      Assert.Equal(7, System.GetGeneticReflex(reflexId).VisualColorId);
    }

    [Fact]
    public void SameReflexes_DifferentColor_AreNotDuplicates()
    {
      AppGlobalState.EvolutionStage = 0;
      int style = NewStyle();
      int action = NewAction();

      var (idBlack, _) = System.AddGeneticReflex(
          0, new List<int> { style }, null, null, new List<int> { action }, AgentVisualColor.Black);

      // Те же уровни, но другой цвет — это разные рефлексы, дублем не считаются.
      var (idWhite, _) = System.AddGeneticReflex(
          0, new List<int> { style }, null, null, new List<int> { action }, AgentVisualColor.White);

      Assert.True(idBlack > 0);
      Assert.True(idWhite > 0);
      Assert.NotEqual(idBlack, idWhite);

      // Тот же цвет повторно — дубль (защита от регресса: проверка не «сломалась совсем»).
      Assert.Throws<ArgumentException>(() => System.AddGeneticReflex(
          0, new List<int> { style }, null, null, new List<int> { action }, AgentVisualColor.Black));
    }

    /// <summary>Вызывает приватный <c>LoadGeneticReflexes()</c> на живом синглтоне.</summary>
    private static void InvokeLoad()
    {
      typeof(GeneticReflexesSystem)
          .GetMethod("LoadGeneticReflexes", PrivateInstance)
          ?.Invoke(System, null);
    }
  }
}
