using System.Collections.Generic;

namespace ISIDA.Scenarios
{
  /// <summary>Пульт оператора для подачи стимулов сценария (реализация в UI).</summary>
  public interface IOperatorScenarioPult
  {
    /// <summary>Ошибка — непустая строка, успех — null.</summary>
    string TryApplyScenarioStimulus(IReadOnlyList<int> actionIds, string phraseText, int toneId, int moodId);
  }
}
