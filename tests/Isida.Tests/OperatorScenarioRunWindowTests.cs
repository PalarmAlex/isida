using System;
using System.Reflection;
using System.Threading;
using ISIDA.Common;
using ISIDA.Scenarios;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регресс (Случай «фантомные автоматизмы в отчёте»): HTML-отчёт прогона агрегирует in-memory
  /// буфер логов по ГЛОБАЛЬНОМУ пульсу (<c>ScenarioLogComparer.AggregateByPulse</c>), а предзапуск
  /// сценария обнуляет счётчик пульсов (<see cref="GlobalTimer.Reset"/>). Из-за этого записи старого
  /// и нового прогонов получают ОДИНАКОВЫЕ номера пульсов и склеиваются в один снапшот — в отчёте
  /// появляются фантомные автоматизмы/ОР и теряются условные рефлексы.
  /// <para>
  /// Лечение — фильтр буфера по временному окну конкретного прогона. Его границы несёт
  /// <see cref="OperatorScenarioCompletedEventArgs"/> (<see cref="OperatorScenarioCompletedEventArgs.RunStartLocal"/> /
  /// <see cref="OperatorScenarioCompletedEventArgs.RunEndLocal"/>), которые выставляет раннер.
  /// Тест проверяет ДВИЖКОВЫЙ КОНТРАКТ этого лечения: даже когда после <see cref="GlobalTimer.Reset"/>
  /// номера глобальных пульсов двух прогонов ПЕРЕКРЫВАЮТСЯ, их временные окна не пересекаются —
  /// именно на этом держится корректность фильтра в отчёте.
  /// </para>
  /// </summary>
  public sealed class OperatorScenarioRunWindowTests
  {
    /// <summary>Ставит GlobalPulsCount через рефлексию (у свойства private set).</summary>
    private static void SetGlobalPulsCount(int value)
    {
      typeof(GlobalTimer)
          .GetProperty("GlobalPulsCount", BindingFlags.Public | BindingFlags.Static)
          ?.SetValue(null, value);
    }

    /// <summary>Минимальный документ с одним шагом, чтобы <c>Start</c> не падал.</summary>
    private static ScenarioDocument MakeDoc(int id)
    {
      return new ScenarioDocument
      {
        Header = new ScenarioHeader { Id = id, Title = "Тест окна прогона " + id },
        Lines =
        {
          new ScenarioLineRow { StepIndex = 1, PulseWithinScenario = 1 }
        }
      };
    }

    /// <summary>
    /// Прогон фиксирует непустое временное окно: конец не раньше старта, оба отличны от default.
    /// </summary>
    [Fact]
    public void Run_Complete_ExposesNonEmptyTimeWindow()
    {
      var runner = new OperatorScenarioRunner();
      OperatorScenarioCompletedEventArgs done = null;
      runner.Finished += (s, e) => done = e;

      runner.Start(MakeDoc(9), () => null, null);
      runner.StopUser(); // тот же путь Complete, что и успех/ошибка

      Assert.NotNull(done);
      Assert.NotEqual(default(DateTime), done.RunStartLocal);
      Assert.NotEqual(default(DateTime), done.RunEndLocal);
      Assert.True(done.RunEndLocal >= done.RunStartLocal,
          "Верхняя граница окна не может быть раньше нижней.");
    }

    /// <summary>
    /// Ключевой регресс: два прогона подряд с обнулением пульса между ними (как предзапуск) дают
    /// ПЕРЕКРЫВАЮЩИЕСЯ номера глобальных пульсов, но РАЗДЕЛЬНЫЕ временные окна. Фильтр отчёта по
    /// окну прогона корректно разделяет их, тогда как агрегация только по пульсу — склеивает.
    /// </summary>
    [Fact]
    public void PreRun_PulseReset_KeepsRunTimeWindowsDisjoint()
    {
      // Прогон 1 (например, стадия 3): пульс «наработан» предысторией.
      SetGlobalPulsCount(50);
      var runner1 = new OperatorScenarioRunner();
      OperatorScenarioCompletedEventArgs run1 = null;
      runner1.Finished += (s, e) => run1 = e;
      runner1.Start(MakeDoc(11), () => null, null);
      runner1.StopUser();

      // Предзапуск сценария обнуляет глобальный счётчик (корень бага).
      GlobalTimer.Reset();
      Assert.Equal(0, GlobalTimer.GlobalPulsCount);

      // Гарантируем строго положительный зазор между прогонами (тест временных границ).
      Thread.Sleep(20);

      // Прогон 2 (стадия 1): стартует с обнулённого пульса.
      var runner2 = new OperatorScenarioRunner();
      OperatorScenarioCompletedEventArgs run2 = null;
      runner2.Finished += (s, e) => run2 = e;
      runner2.Start(MakeDoc(9), () => null, null);
      runner2.StopUser();

      Assert.NotNull(run1);
      Assert.NotNull(run2);

      // Якоря по пульсу ПЕРЕКРЫВАЮТСЯ: у прогона 2 якорь (=0 после Reset) лежит внутри
      // пульсного диапазона прогона 1 — это и вызывает склейку при агрегации по пульсу.
      Assert.True(run2.AnchorGlobalPulse < run1.AnchorGlobalPulse,
          "После Reset якорь нового прогона должен быть меньше старого (иллюстрация перекрытия по пульсу).");

      // Но временные окна РАЗДЕЛЕНЫ: окно прогона 2 начинается строго после конца окна прогона 1.
      Assert.True(run1.RunEndLocal < run2.RunStartLocal,
          "Временные окна прогонов не должны пересекаться — на этом держится фильтр отчёта.");
    }
  }
}
