using ISIDA.Niche.Contour;
using System;
using System.Collections.Generic;

namespace ISIDA.Niche
{
  /// <summary>
  /// Движок Niche: host-гомеостаз, рефлексы, такт §6.5 (§1.4, этап 4).
  /// </summary>
  public sealed class NicheEngine : IDisposable
  {
    private readonly NicheHostState _state = new NicheHostState();
    private readonly NicheReflexLayer _reflexLayer;
    private readonly object _lock = new object();
    private RoleProfile _roleProfile = RoleProfile.NicheMinimal;
    private IContourAdapter _contour;
    private TriadExperimentConfig _config;
    private string _nicheDataFolder;
    private string _environmentFolder;
    private int _lastReflexesApplied;
    private ContourInputApplication _lastContourApplication;
    private bool _disposed;

    /// <summary>
    /// Создаёт NicheEngine с профилем роли.
    /// </summary>
    /// <param name="roleProfile">Профиль Niche или null (niche_minimal).</param>
    public NicheEngine(RoleProfile roleProfile = null)
    {
      _roleProfile = roleProfile ?? RoleProfile.NicheMinimal;
      _reflexLayer = new NicheReflexLayer(_roleProfile);
      _contour = new StaticContourAdapter("static_mvp");
    }

    /// <summary>Host-state параметров Niche.</summary>
    public NicheHostState State => _state;

    /// <summary>Активный профиль роли.</summary>
    public RoleProfile RoleProfile => _roleProfile;

    /// <summary>Число рефлексов, применённых на последнем такте.</summary>
    public int LastReflexesApplied => _lastReflexesApplied;

    /// <summary>True, если Niche инициализирована.</summary>
    public bool IsInitialized => _state.IsInitialized;

    /// <summary>
    /// Инициализирует Niche из конфигурации триады и каталога Data/Niche.
    /// </summary>
    /// <param name="config">Конфигурация эксперимента.</param>
    /// <param name="nicheDataFolder">Каталог Data/Niche.</param>
    /// <param name="environmentFolder">Каталог Environment (fallback параметров).</param>
    public void Initialize(TriadExperimentConfig config, string nicheDataFolder, string environmentFolder)
    {
      lock (_lock)
      {
        _config = config ?? new TriadExperimentConfig();
        _nicheDataFolder = nicheDataFolder;
        _environmentFolder = environmentFolder;
        if (!_config.UseFullNicheEngine)
        {
          _roleProfile = new RoleProfile
          {
            ProfileId = "host_mvp",
            ActiveMask = SymbiontSubsystem.Gomeostasis
          };
        }
        else
        {
          _roleProfile = RoleProfile.FromConfigName(_config.NicheRoleProfileId);
        }

        _contour = CreateContourAdapter(_config, environmentFolder);

        if (_contour is ProbeContourAdapter probeAdapter)
          probeAdapter.ReloadProbes(environmentFolder);

        NicheReflexLoader.EnsureTemplateFile(_nicheDataFolder);

        var parameters = _config.NicheParameters;
        if (parameters == null || parameters.Count == 0)
          parameters = CouplingMappingLoader.LoadFromFolder(environmentFolder ?? string.Empty).NicheParameters;

        _state.Initialize(parameters);
        _reflexLayer.LoadRules(_config.UseFullNicheEngine ? _nicheDataFolder : null);
      }
    }

    /// <summary>
    /// Начало такта Niche.
    /// </summary>
    public void BeginPulse()
    {
      lock (_lock)
      {
        _lastReflexesApplied = 0;
        _state.BeginPulse();
      }
    }

    /// <summary>Последнее применение contour InputSnapshot.</summary>
    public ContourInputApplication LastContourApplication => _lastContourApplication;

    /// <summary>
    /// Спонтанный дрейф и contour-input.
    /// </summary>
    /// <param name="pulse">Глобальный пульс.</param>
    public void ApplySpontaneousAndContour(int pulse)
    {
      lock (_lock)
      {
        if (!_roleProfile.IsActive(SymbiontSubsystem.Gomeostasis))
          return;

        _lastContourApplication = null;
        bool drift = _config != null && _config.SpontaneousDriftEnabled;
        var contourDeltas = _contour?.GetNicheDeltasForPulse(pulse);
        if (_contour is ProbeContourAdapter probeAdapter)
          _lastContourApplication = probeAdapter.LastApplication;

        _state.ApplySpontaneousUpdate(drift, contourDeltas);
      }
    }

    /// <summary>
    /// Coupling Creature→Niche и реактивные рефлексы.
    /// </summary>
    /// <param name="actionId">ID действия Creature.</param>
    /// <param name="pulse">Пульс.</param>
    /// <param name="vigorScale">Множитель vigor.</param>
    /// <param name="targets">Цели coupling.</param>
    public void ApplyCreatureCoupling(int actionId, int pulse, float vigorScale, IReadOnlyList<CouplingTarget> targets)
    {
      lock (_lock)
      {
        if (actionId <= 0 || targets == null || targets.Count == 0)
          return;

        foreach (var t in targets)
        {
          float delta = t.Delta * t.Scale * vigorScale;
          _state.ApplyCouplingDelta(t.NicheParamId, delta);
        }

        _state.MarkCreatureAction(actionId, pulse);
        _lastReflexesApplied = _reflexLayer.ApplyReactiveReflexes(_state, actionId);
      }
    }

    /// <summary>
    /// Завершение такта: снимок «после».
    /// </summary>
    /// <returns>Состояние после такта.</returns>
    public Dictionary<int, float> EndPulse()
    {
      lock (_lock)
        return _state.EndPulse();
    }

    /// <summary>
    /// Перенос на Niche₂: новые начальные параметры при сохранении mapping (§6.6).
    /// </summary>
    /// <param name="newInitialParams">Новые значения paramId→value.</param>
    public void ApplyNicheTransfer(IReadOnlyDictionary<int, float> newInitialParams)
    {
      lock (_lock)
      {
        if (newInitialParams == null || newInitialParams.Count == 0)
          return;

        _state.RestoreFromSnapshot(newInitialParams);
      }
    }

    /// <summary>
    /// Устанавливает probe-key контура для следующего такта (§6.8, EnvironmentMetricProbeKey).
    /// </summary>
    /// <param name="probeKey">Ключ пробы или пусто.</param>
    public void SetContourProbeKey(string probeKey)
    {
      lock (_lock)
      {
        if (_contour is ProbeContourAdapter probeAdapter)
          probeAdapter.SetActiveProbeKey(probeKey);
      }
    }

    /// <summary>
    /// Передаёт InputSnapshot контура напрямую (host API).
    /// </summary>
    /// <param name="snapshot">Снимок входа.</param>
    /// <param name="probeKey">Опциональный probeKey для лога.</param>
    public void ApplyContourInputSnapshot(InputSnapshot snapshot, string probeKey = null)
    {
      lock (_lock)
      {
        if (_contour is ProbeContourAdapter probeAdapter)
          probeAdapter.ApplyInputSnapshot(snapshot, probeKey);
      }
    }

    /// <summary>
    /// Сброс Niche к начальным значениям из конфигурации.
    /// </summary>
    public void ResetToInitial()
    {
      lock (_lock)
      {
        if (_config?.NicheParameters != null)
          _state.ResetToInitial(_config.NicheParameters);
      }
    }

    /// <summary>
    /// Восстановление из снимка InitSnapshot.
    /// </summary>
    /// <param name="snapshot">Снимок параметров.</param>
    public void RestoreFromSnapshot(IReadOnlyDictionary<int, float> snapshot)
    {
      lock (_lock)
        _state.RestoreFromSnapshot(snapshot);
    }

    /// <inheritdoc />
    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;
    }

    private static IContourAdapter CreateContourAdapter(TriadExperimentConfig config, string environmentFolder)
    {
      string contourId = config?.ContourId ?? "static_mvp";
      if (config != null && config.UseProbeContour)
        return new ProbeContourAdapter(contourId, environmentFolder);

      return new StaticContourAdapter(contourId);
    }
  }
}
