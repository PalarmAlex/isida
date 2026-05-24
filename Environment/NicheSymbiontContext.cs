using ISIDA.Actions;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;

namespace ISIDA.Niche
{
  /// <summary>
  /// Стек универсального симбионта Niche: гомеостаз + БР (+ опц. УР), отдельно от Creature.
  /// </summary>
  public sealed class NicheSymbiontContext : IDisposable
  {
    private InformationEnvironmentSystem _informationEnvironment;
    private bool _disposed;

    /// <summary>Гомеостаз Niche.</summary>
    public GomeostasSystem Gomeostas { get; private set; }

    /// <summary>Безусловные рефлексы Niche.</summary>
    public GeneticReflexesSystem GeneticReflexes { get; private set; }

    /// <summary>Адаптивные действия Niche (справочник для БР).</summary>
    public AdaptiveActionsSystem AdaptiveActions { get; private set; }

    /// <summary>Воздействия на параметры Niche.</summary>
    public InfluenceActionSystem InfluenceActions { get; private set; }

    /// <summary>Условные рефлексы (стадия 1).</summary>
    public NicheConditionedReflexStore ConditionedReflexes { get; private set; }

    /// <summary>Активный профиль.</summary>
    public RoleProfile RoleProfile { get; private set; }

    /// <summary>
    /// Создаёт или пересоздаёт контекст симбионта Niche.
    /// </summary>
    public void Initialize(string nicheDataFolder, RoleProfile roleProfile, IEnumerable<NicheParameterDef> fallbackParams)
    {
      DisposeInner();

      RoleProfile = roleProfile ?? RoleProfile.NicheStage0;
      NicheSymbiontBootstrap.EnsureSymbiontLayout(nicheDataFolder, fallbackParams);
      NicheSymbiontMigration.MigrateLegacyFiles(nicheDataFolder);

      string gomeostasFolder = NicheSymbiontBootstrap.GetGomeostasFolder(nicheDataFolder);
      string actionsFolder = NicheSymbiontBootstrap.GetActionsFolder(nicheDataFolder);
      string reflexesFolder = NicheSymbiontBootstrap.GetReflexesFolder(nicheDataFolder);

      _informationEnvironment = InformationEnvironmentSystem.CreateDetachedForNicheHost();
      Gomeostas = new GomeostasSystem(_informationEnvironment, gomeostasFolder, detachedNicheHost: true);
      AdaptiveActions = AdaptiveActionsSystem.CreateDetachedForNicheHost(Gomeostas, actionsFolder);
      InfluenceActions = InfluenceActionSystem.CreateDetachedForNicheHost(Gomeostas, actionsFolder);
      GeneticReflexes = GeneticReflexesSystem.CreateDetachedForNicheHost(Gomeostas, reflexesFolder, AdaptiveActions, InfluenceActions);

      if (RoleProfile.IsActive(SymbiontSubsystem.ConditionedReflexes))
        ConditionedReflexes = new NicheConditionedReflexStore(reflexesFolder, GeneticReflexes);
      else
        ConditionedReflexes = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;
      DisposeInner();
    }

    private void DisposeInner()
    {
      GeneticReflexes?.Dispose();
      GeneticReflexes = null;
      InfluenceActions?.Dispose();
      InfluenceActions = null;
      AdaptiveActions?.Dispose();
      AdaptiveActions = null;
      Gomeostas?.Dispose();
      Gomeostas = null;
      _informationEnvironment?.Dispose();
      _informationEnvironment = null;
      ConditionedReflexes = null;
    }
  }
}
