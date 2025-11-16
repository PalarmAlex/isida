using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.IO;
using System.Threading;
using static ISIDA.Common.FileValidator;

namespace ISIDA.Sensors
{
  /// <summary>
  /// Представляет систему сенсорного восприятия агента
  /// </summary>
  public sealed class SensorySystem : IDisposable
  {
    #region Поля и свойства

    private GeneticReflexesSystem _geneticReflexesSystem;
    private PerceptionImagesSystem _perceptionImagesSystem;
    private readonly GomeostasSystem _gomeostas;
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    private static SensorySystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы сенсорного восприятия
    /// </summary>
    /// <exception cref="InvalidOperationException">Выбрасывается если система не инициализирована</exception>
    public static SensorySystem Instance => _instance ??
        throw new InvalidOperationException("SensorySystem не инициализирован");

    /// <summary>
    /// Признак инициализации системы
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Имя файла с первичными символами по умолчанию
    /// </summary>
    public const string DefaultVerbalPrimariesFileName = "DefaultVerbalPrimaries";

    /// <summary>
    /// Имя директории вербального канала
    /// </summary>
    public const string VerbalChannelFolder = "";

    private readonly string _sensorsFolderPath;

    /// <summary>
    /// Устанавливает или получает авторитарный режим вербального канала
    /// (делегирует вызов VerbalChannel с его собственной блокировкой)
    /// </summary>
    public bool VerbalAuthoritativeMode
    {
      get => VerbalChannel?.AuthoritativeMode ?? false;
      set
      {
        if (VerbalChannel != null)
        {
          VerbalChannel.AuthoritativeMode = value;
        }
      }
    }

    /// <summary>
    /// Устанавливает или получает порог подтверждения для вербального канала
    /// (делегирует вызов VerbalChannel с его собственной блокировкой)
    /// </summary>
    public int VerbalRecognitionThreshold
    {
      get => VerbalChannel?.RecognitionThreshold ?? 0;
      set
      {
        if (VerbalChannel != null)
        {
          VerbalChannel.RecognitionThreshold = value;
        }
      }
    }

    /// <summary>
    /// Вербальный сенсорный канал
    /// </summary>
    public VerbalSensorChannel VerbalChannel { get; private set; }

    #endregion

    #region Инициализация

    /// <summary>
    /// Устанавливает системы для каскадной очистки (вторичная инициализация)
    /// </summary>
    public void SetDependentSystems(GeneticReflexesSystem geneticReflexesSystem, PerceptionImagesSystem perceptionImagesSystem)
    {
      _lock.EnterWriteLock();
      try
      {
        UnsubscribeFromEvents();

        _geneticReflexesSystem = geneticReflexesSystem;
        _perceptionImagesSystem = perceptionImagesSystem;

        SubscribeToEvents();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Инициализирует глобальный экземпляр системы
    /// </summary>
    /// <param name="gomeostasSystem">Ссылка на класс гомеостаза</param>
    /// <param name="sensorsFolderPath">Путь к директории сенсоров (если null - используется путь по умолчанию)</param>
    /// <exception cref="InvalidOperationException">Выбрасывается если система уже инициализирована</exception>
    public static void InitializeInstance(
        GomeostasSystem gomeostasSystem,
        string sensorsFolderPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("SensorySystem уже инициализирован");

      _instance = new SensorySystem(gomeostasSystem, sensorsFolderPath);
    }

    private SensorySystem(
      GomeostasSystem gomeostasSystem,
      string sensorsFolderPath)
    {
      _sensorsFolderPath = string.IsNullOrWhiteSpace(sensorsFolderPath)
          ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ISIDA", "Data", "Sensors")
          : sensorsFolderPath;

      _gomeostas = gomeostasSystem ?? throw new ArgumentNullException(nameof(gomeostasSystem));

      SubscribeToEvents();
      try
      {
        EnsureDataDirectory();

        // Загружаем первичные сенсоры и инициализируем вербальный канал
        var primarySensorsPath = Path.Combine(_sensorsFolderPath,
            $"{DefaultVerbalPrimariesFileName}.tmp");

        VerbalChannel = new VerbalSensorChannel(
            _gomeostas,
            _sensorsFolderPath,
            primarySensorsPath);

        SubscribeToEvents();
      }
      catch (Exception ex)
      {
        LogError($"Ошибка инициализации: {ex.Message}");
        throw;
      }
    }

    private void EnsureDataDirectory()
    {
      if (!Directory.Exists(_sensorsFolderPath))
        Directory.CreateDirectory(_sensorsFolderPath);
    }

    private void SubscribeToEvents()
    {
      if (VerbalChannel != null)
        VerbalChannel.AllPhrasesCleared += OnAllPhrasesCleared;
    }

    private void UnsubscribeFromEvents()
    {
      if (VerbalChannel != null)
        VerbalChannel.AllPhrasesCleared -= OnAllPhrasesCleared;
    }

    private void OnAllPhrasesCleared()
    {
      try
      {
        _perceptionImagesSystem?.ClearAllPhraseIds();
      }
      catch (Exception ex)
      {
        LogError($"Ошибка при каскадной очистке PhraseId: {ex.Message}");
      }
    }

    #endregion

    #region Освобождение ресурсов

    /// <summary>
    /// Освобождает ресурсы системы
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        UnsubscribeFromEvents();
        VerbalChannel?.Dispose();
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
      }
    }

    #endregion

  }
}