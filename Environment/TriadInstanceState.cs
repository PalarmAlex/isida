namespace ISIDA.Niche
{
  /// <summary>
  /// Состояние экземпляра триады (AOE Niche), постепенная замена AppGlobalState (§6.5, этап 4.6).
  /// </summary>
  public sealed class TriadInstanceState
  {
    private bool _waitingForNicheResponse;
    private int _automatizmIdWaitingForNicheResponse;
    private int _nicheAoeWindowCountdown;

    /// <summary>Ожидание первичного отклика Niche.</summary>
    public bool WaitingForNicheResponse => _waitingForNicheResponse;

    /// <summary>Automatizm в окне AOE Niche.</summary>
    public int AutomatizmIdWaitingForNicheResponse => _automatizmIdWaitingForNicheResponse;

    /// <summary>Оставшиеся пульсы окна W_eval.</summary>
    public int NicheAoeWindowCountdown => _nicheAoeWindowCountdown;

    /// <summary>
    /// Открывает окно AOE Niche и синхронизирует с <see cref="AppGlobalState"/> для UI.
    /// </summary>
    /// <param name="automatizmId">ID automatizm.</param>
    /// <param name="windowPulses">W_eval.</param>
    public void StartWaitingForNicheResponse(int automatizmId, int windowPulses)
    {
      _waitingForNicheResponse = true;
      _automatizmIdWaitingForNicheResponse = automatizmId > 0 ? automatizmId : 0;
      _nicheAoeWindowCountdown = windowPulses < 1 ? 1 : windowPulses;
      AppGlobalState.StartWaitingForNicheResponse(automatizmId, windowPulses);
    }

    /// <summary>
    /// Сбрасывает окно AOE Niche.
    /// </summary>
    public void ResetWaitingForNicheResponse()
    {
      _waitingForNicheResponse = false;
      _automatizmIdWaitingForNicheResponse = 0;
      _nicheAoeWindowCountdown = 0;
      AppGlobalState.ResetWaitingForNicheResponse();
    }
  }
}
