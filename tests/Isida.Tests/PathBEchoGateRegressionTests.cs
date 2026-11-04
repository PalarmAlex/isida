using System;
using System.Collections.Generic;
using System.Reflection;
using ISIDA.Common;
using ISIDA.Psychic;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регрессионные тесты для Случая 35: гейт пути B (вербальное эхо) на 2-й стадии.
  /// <para>
  /// Гейт <see cref="PurposeGeneticImageSystem.IsOperatorActionRepeatStimulus"/> обязан опираться
  /// на моторную часть ТЕКУЩЕГО стимула (<see cref="AppGlobalState.CurrentStimulusActionIdList"/>),
  /// а не на статичный <c>IsActive</c> probe-EA метрик среды: в AIStudio/Velum метрики активны
  /// постоянно как каналы восприятия, из-за чего гейт был истинен всегда и легитимное вербальное
  /// эхо не создавалось (в логе — «skipping path B echo — active probe actions detected»).
  /// </para>
  /// Функция чистая (зависит только от статического состояния), поэтому тесты не поднимают
  /// <see cref="PurposeGeneticImageSystem"/> — её <c>InitializeInstance</c> требует четыре системы,
  /// а для пути B дополнительно MirrorAutomatizmService / VerbalBrocaImagesSystem / SensorySystem.
  /// Коллекция <c>EngineIntegration</c> отключает параллелизм, поэтому общий статический
  /// <see cref="AppGlobalState"/> не «течёт» между тестами; список стимула явно восстанавливается.
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class PathBEchoGateRegressionTests : EngineIntegrationTestBase, IDisposable
  {
    private readonly List<int> _savedStimulusActionIds;

    public PathBEchoGateRegressionTests(EngineFixture engine) : base(engine)
    {
      _savedStimulusActionIds = AppGlobalState.CurrentStimulusActionIdList;
    }

    public void Dispose()
    {
      AppGlobalState.CurrentStimulusActionIdList = _savedStimulusActionIds;
    }

    #region Моторная часть текущего стимула

    [Fact]
    public void IsOperatorActionRepeatStimulus_EmptyMotorPart_IsFalse()
    {
      // Чистая фраза с пульта: моторной части нет → эхокаем, гейт не должен блокировать путь B.
      AppGlobalState.CurrentStimulusActionIdList = new List<int>();

      Assert.False(PurposeGeneticImageSystem.IsOperatorActionRepeatStimulus());
    }

    [Fact]
    public void IsOperatorActionRepeatStimulus_SingleAction_IsTrue()
    {
      // Стимул содержит действие оператора → это повтор действия, эхо не создаём.
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { Engine.NextSeed() };

      Assert.True(PurposeGeneticImageSystem.IsOperatorActionRepeatStimulus());
    }

    [Fact]
    public void IsOperatorActionRepeatStimulus_SeveralActions_IsTrue()
    {
      AppGlobalState.CurrentStimulusActionIdList = new List<int>
      {
        Engine.NextSeed(), Engine.NextSeed(), Engine.NextSeed()
      };

      Assert.True(PurposeGeneticImageSystem.IsOperatorActionRepeatStimulus());
    }

    [Fact]
    public void IsOperatorActionRepeatStimulus_NullMotorPart_IsFalseAndDoesNotThrow()
    {
      // Сеттер AppGlobalState нормализует null в пустой список, поэтому гейт обязан вернуть false,
      // а не бросить: исключение здесь оборвало бы весь цикл мышления.
      AppGlobalState.CurrentStimulusActionIdList = null;

      bool isOperatorActionRepeat = PurposeGeneticImageSystem.IsOperatorActionRepeatStimulus();

      Assert.False(isOperatorActionRepeat);
      Assert.NotNull(AppGlobalState.CurrentStimulusActionIdList);
    }

    [Fact]
    public void IsOperatorActionRepeatStimulus_TransitionsBackToCleanPhrase_IsFalseAgain()
    {
      // Стимулы приходят с пульта последовательно: после действия оператора снова может идти
      // чистая фраза — гейт должен корректно переключаться, не «запоминать» моторную часть.
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { Engine.NextSeed() };
      Assert.True(PurposeGeneticImageSystem.IsOperatorActionRepeatStimulus());

      AppGlobalState.CurrentStimulusActionIdList = new List<int>();
      Assert.False(PurposeGeneticImageSystem.IsOperatorActionRepeatStimulus());
    }

    #endregion

    #region Гейт не зависит от активности метрик среды

    [Fact]
    public void PurposeGeneticImageSystem_HasNoProbeActivityGateLeft()
    {
      // Прежний гейт читал IsActive у probe-EA из InfluenceActionSystem (метрикы активны постоянно
      // как каналы восприятия → путь B был закрыт навсегда). Метод удалён вместе с фиксом и
      // не должен вернуться.
      MethodInfo gate = typeof(PurposeGeneticImageSystem).GetMethod(
          "HasActiveProbeActions",
          BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

      Assert.Null(gate);
    }

    [Fact]
    public void IsOperatorActionRepeatStimulus_CleansUpAfterMotorStimulusAndKeepsGlobalStateUsable()
    {
      // Страховка от «течения» состояния: после стимула с моторной частью пустой список
      // возвращается тем же способом, каким PsychicSystem заполняет его на каждой подаче.
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { Engine.NextSeed() };
      AppGlobalState.CurrentStimulusActionIdList = new List<int>();

      Assert.Empty(AppGlobalState.CurrentStimulusActionIdList);
      Assert.False(PurposeGeneticImageSystem.IsOperatorActionRepeatStimulus());
    }

    #endregion
  }
}