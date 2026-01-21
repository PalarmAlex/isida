using ISIDA.Common;
using ISIDA.Psychic.Automatism;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Система управления ориентировоным рефлексом
  /// </summary>
  public sealed class OrientationReflexSystem: IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    private readonly InformationEnvironmentSystem _informationEnvironmentSystem;
    private AutomatizmSystem _automatizmSystem;
    private AutomatizmTreeSystem _automatizmTreeSystem;
    
    #region Инициализация

    private static OrientationReflexSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static OrientationReflexSystem Instance => _instance ??
        throw new InvalidOperationException("OrientationReflexSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы
    /// </summary>
    public static void InitializeInstance(InformationEnvironmentSystem informationEnvironmentSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("OrientationReflexSystem уже инициализирован.");

      _instance = new OrientationReflexSystem(informationEnvironmentSystem);
    }

    /// <summary>
    /// Вторичная инициализация зависимостей
    /// </summary>
    public void SetDependencies(
        AutomatizmSystem automatizmSystem,
        AutomatizmTreeSystem automatizmTreeSystem)
    {
      if (_automatizmSystem != null || _automatizmTreeSystem != null)
        throw new InvalidOperationException("Зависимости уже установлены.");

      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
      _automatizmTreeSystem = automatizmTreeSystem ?? throw new ArgumentNullException(nameof(automatizmTreeSystem));
    }

    private OrientationReflexSystem(InformationEnvironmentSystem informationEnvironmentSystem)
    {
      _informationEnvironmentSystem = informationEnvironmentSystem ?? throw new ArgumentNullException(nameof(informationEnvironmentSystem));
    }

    /// <summary>
    /// Проверка, инициализированы ли зависимости
    /// </summary>
    public bool AreDependenciesSet => _automatizmSystem != null && _automatizmTreeSystem != null;

    #endregion

    #region Управление ориентировочным рефлексом

    /// <summary>
    /// Ориентировочный рефлекс
    /// </summary>
    internal Automatizm OrientationReflex(int automatizmID, int currentEmotionId)
    {
      try
      {
        Automatizm atmtzm = null;

        if (automatizmID == 0)
          atmtzm = OrientationReflex_1(automatizmID, currentEmotionId);
        else
        {
          atmtzm = _automatizmSystem.GetAutomatizmById(automatizmID);
          if (atmtzm != null)
            atmtzm = OrientationReflex_2(automatizmID, currentEmotionId);
        }

        if (atmtzm != null)
        {
          if(atmtzm.BranchID == 0 && AutomatizmTreeSystem.IsInitialized)
            atmtzm.BranchID = _automatizmTreeSystem.DetectedActiveLastNodeId;

          return atmtzm;
        }

        return null;
      }
      catch(Exception ex)
      {
        Logger.Error($"{ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Ориентировочный рефлекс 1 уровня: нет автоматизма, нужно быстро создать его по гомеостатическим целям
    /// </summary>
    internal Automatizm OrientationReflex_1(int automatizmID, int currentEmotionId)
    {
      try
      {
        _informationEnvironmentSystem.GetCurrentInformationEnvironment(currentEmotionId);



        return null;
      }
      catch(Exception ex)
      {
        Logger.Error($"{ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Ориентировочный рефлекс 2 уровня: автоматизм есть, надо его проверить в текущих условиях
    /// </summary>
    internal Automatizm OrientationReflex_2(int automatizmID, int currentEmotionId)
    {
      try
      {
        _informationEnvironmentSystem.GetCurrentInformationEnvironment(currentEmotionId);

        return null;
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        return null;
      }
    }

    #endregion

    /// <summary>
    /// Освобождает ресурсы, используемые объектом
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        _lock?.Dispose();
        _disposed = true;
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
      }
    }
  }
}
