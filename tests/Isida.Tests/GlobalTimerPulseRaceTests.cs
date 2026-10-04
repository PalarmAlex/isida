using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регресс-тесты на гонку выгрузки и пульса в <see cref="GlobalTimer"/> (DEBUG_CASEBOOK, Случай 7):
  /// ранее <c>ClearSystems()</c> обнулял статические ссылки на системы под <c>_timerLock</c>,
  /// тогда как <c>ProcessAgentPulse</c> читал их вне дока → <see cref="NullReferenceException"/>
  /// парами в <c>SaveErrors.log</c> при закрытии/перезапуске движка.
  /// </summary>
  /// <remarks>
  /// GlobalTimer — статический класс с приватным обработчиком пульса; полная инициализация
  /// (PsychicSystem/ReflexesActivator) вне объёма <see cref="EngineFixture"/>. Тесты работают на
  /// контракте синхронизации через рефлексивный доступ к приватным членам (как в <see cref="EngineFixture"/>),
  /// не поднимая живой пульс. Коллекция без параллелизма: тесты мутируют общее статическое состояние таймера.
  /// </remarks>
  [Collection("GlobalTimerRace")]
  public sealed class GlobalTimerPulseRaceTests
  {
    private static readonly Type TimerType = typeof(GlobalTimer);

    private static FieldInfo Field(string name) =>
        TimerType.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);

    private static void SetRunning(bool value) =>
        Field("_isRunning").SetValue(null, value);

    private static ManualResetEventSlim PulseSignal() =>
        (ManualResetEventSlim)Field("_pulseCompletionSignal").GetValue(null);

    /// <summary>
    /// Снимок систем под <c>_timerLock</c>: если пульс стартовал, но ссылки уже сброшены
    /// (состояние гонки — флаг running при null-системах), обработчик обязан штатно выйти,
    /// НЕ считая это критической ошибкой. До правки здесь падал NullReferenceException
    /// (ловился внутренним catch и уходил в <see cref="GlobalTimer.OnPulseError"/>).
    /// </summary>
    [Fact]
    public void ProcessAgentPulse_RunningButSystemsCleared_ReportsNoCriticalError()
    {
      GlobalTimer.ClearSystems(); // гарантированно null-системы и остановленный таймер

      bool criticalErrorReported = false;
      Action<string> handleError = _ => criticalErrorReported = true;
      GlobalTimer.OnPulseError += handleError;
      try
      {
        // Воспроизводим момент гонки: пульс «идёт» (_isRunning == true), но системы уже null.
        SetRunning(true);

        var processAgentPulse = TimerType.GetMethod(
            "ProcessAgentPulse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(processAgentPulse);

        // Вызов не должен бросать наружу, а ранний выход по null-снимку не должен
        // порождать событие критической ошибки пульса (SafeStopWithError не вызывается).
        processAgentPulse.Invoke(null, null);
      }
      finally
      {
        GlobalTimer.OnPulseError -= handleError;
        SetRunning(false);
        PulseSignal().Set();
      }

      Assert.False(criticalErrorReported,
          "Обработчик пульса при сброшенных системах сообщил о критической ошибке — "
          + "признак отсутствия снимка систем под локом (регресс Случая 7).");
    }

    /// <summary>
    /// Выгрузка обязана ждать фактического конца активного пульса, а не фиксированной паузы.
    /// Эмулируем долгий пульс: сбрасываем сигнал завершения и освобождаем его с задержкой —
    /// <see cref="GlobalTimer.ClearSystems"/> должен вернуться только после освобождения.
    /// </summary>
    [Fact]
    public void ClearSystems_WaitsForActivePulseToFinish()
    {
      GlobalTimer.ClearSystems();
      var signal = PulseSignal();
      Assert.True(GlobalTimer.WaitForPulseCompletion(0),
          "В простое сигнал завершения пульса должен быть взведён.");

      const int pulseHoldMs = 400;
      bool released = false;
      signal.Reset(); // «пульс пошёл»: активный обработчик обращается к системам
      var holder = new Thread(() =>
      {
        Thread.Sleep(pulseHoldMs);
        released = true;
        signal.Set(); // «пульс завершился»: только теперь безопасно обнулять ссылки
      });
      holder.IsBackground = true;

      var sw = Stopwatch.StartNew();
      holder.Start();
      try
      {
        GlobalTimer.ClearSystems();
      }
      finally
      {
        holder.Join(TimeSpan.FromSeconds(5));
        sw.Stop();
        signal.Set();
      }

      Assert.True(released, "ClearSystems вернулся раньше освобождения сигнала активного пульса.");
      Assert.True(sw.ElapsedMilliseconds >= pulseHoldMs / 2,
          $"ClearSystems не дождался конца пульса (выполнен за {sw.ElapsedMilliseconds} мс при удержании {pulseHoldMs} мс) — "
          + "признак возврата к фиксированной паузе вместо ожидания (регресс Случая 7).");
    }
  }

  /// <summary>Коллекция тестов гонки GlobalTimer (общее статическое состояние — без параллелизма).</summary>
  [CollectionDefinition("GlobalTimerRace", DisableParallelization = true)]
  public sealed class GlobalTimerRaceCollection { }
}
