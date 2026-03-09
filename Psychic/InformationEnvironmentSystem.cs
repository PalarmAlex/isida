using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Система информационной среды - основа текущего самоощущения
  /// </summary>
  public sealed class InformationEnvironmentSystem : IDisposable
  {
    #region Инициализация

    private static InformationEnvironmentSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы информационной среды. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static InformationEnvironmentSystem Instance => _instance ??
        throw new InvalidOperationException("InformationEnvironmentSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы информационной среды
    /// </summary>
    /// <exception cref="InvalidOperationException">Выбрасывается, если система уже была инициализирована ранее</exception>
    public static void InitializeInstance()
    {
      if (_instance != null)
        throw new InvalidOperationException("InformationEnvironmentSystem уже инициализирован.");

      _instance = new InformationEnvironmentSystem();
    }

    private InformationEnvironmentSystem()
    {
      try
      {
        InitCurrentInformationEnvironment();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Структуры данных

    /// <summary>
    /// Информационная среда - интегративная информационная среда
    /// </summary>
    public class InformationEnvironment
    {
      /// <summary>
      /// Момент создания кадра инф.окружения
      /// </summary>
      public int LifeTime { get; set; }

      /// <summary>
      /// True - данные записаны на стороне психики
      /// </summary>
      public bool IsPsyLevel { get; set; }

      /// <summary>
      /// True - организм спит (во сне контекст задает тоже InformationEnvironment)
      /// </summary>
      public bool IsSleep { get; set; }

      /// <summary>
      /// Не было действий Beast более 100 пульсов
      /// </summary>
      public bool IsIdleness100pulse { get; set; }

      /// <summary>
      /// Общая оценка гомео-настроения: сила Плохо -10 ... 0 ...+10 Хорошо
      /// </summary>
      public int Mood { get; set; }

      /// <summary>
      /// ID параметров гомеостаза как цели для улучшения в данных условиях
      /// </summary>
      public List<int> CurTargetArrID { get; set; } = new List<int>();

      /// <summary>
      /// Текущая эмоция Emotion, может быть произвольно изменена
      /// </summary>
      public int PsyEmotionId { get; set; }

      /// <summary>
      /// Опасность состояния
      /// </summary>
      public bool Danger { get; set; }

      /// <summary>
      /// Оценка важности ситуации, необходимость срочных действий
      /// </summary>
      public bool VeryActualSituation { get; set; }

      /// <summary>
      /// Субъективно ощущаемая оценка, текущее осознаваемое настроение, которое можно произвольно изменять
      /// </summary>
      public int PsyMood { get; set; }

      /// <summary>
      /// Текущий образ сочетания действий с Пульта
      /// </summary>
      public int ActionsImageID { get; set; }

      /// <summary>
      /// Образ, не имеющий модели понимания, вызывает беспокойство и активирует пассивный режим
      /// </summary>
      public int IsUnknownActionsImageID { get; set; }

      /// <summary>
      /// Текущий образ сочетания ОТВЕТНОГО действия мот.автоматизма
      /// </summary>
      public int AnswerImageID { get; set; }

      /// <summary>
      /// Период ожидания ответа с пульта на действие
      /// </summary>
      public bool IsWaitingPeriod { get; set; }

      /// <summary>
      /// Наиболее важный образ типа extremImportance
      /// </summary>
      public int ExtremImportanceObjectID { get; set; }

      /// <summary>
      /// Актуальное по эффекту Правило, выделенное из ExtremImportanceObjectID в ходе перебора
      /// </summary>
      public int ActualEpisodicMemoryID { get; set; }

      /// <summary>
      /// Текущая Доминанта нерешенной проблемы
      /// </summary>
      public int DominantaID { get; set; }

      /// <summary>
      /// Нужно подумать о проблеме автоматизма или проявить инициативу
      /// </summary>
      public bool NeedThinkingAboutAutomatizm { get; set; }

      /// <summary>
      /// True - текущий стимул навязывает то, что не соответствует текущей Теме и Цели
      /// </summary>
      public bool IsStimulToForce { get; set; }
    }

    #endregion

    #region Поля и свойства

    private readonly List<InformationEnvironment> _informationEnvironmentObjects = new List<InformationEnvironment>();
    private InformationEnvironment _currentInformationEnvironment = new InformationEnvironment();
    private InformationEnvironment _oldInformationEnvironment = new InformationEnvironment();

    /// <summary>
    /// Кратковременная память кадров ИЕ. В файл не записывается, освобождается во сне.
    /// </summary>
    public IReadOnlyList<InformationEnvironment> InformationEnvironmentObjects => _informationEnvironmentObjects.AsReadOnly();

    /// <summary>
    /// Текущая информационная среда
    /// </summary>
    public InformationEnvironment CurrentInformationEnvironment
    {
      get => _currentInformationEnvironment;
      private set => _currentInformationEnvironment = value ?? new InformationEnvironment();
    }

    /// <summary>
    /// Предыдущая информационная среда
    /// </summary>
    public InformationEnvironment OldInformationEnvironment
    {
      get => _oldInformationEnvironment;
      private set => _oldInformationEnvironment = value ?? new InformationEnvironment();
    }

    /// <summary>
    /// Счетчик пульсов времени жизни
    /// </summary>
    public int LifeTime { get; private set; } = 0;

    /// <summary>
    /// Установить LifeTime
    /// </summary>
    public void SetLifeTime(int lifeTime)
    {
      LifeTime = lifeTime;
      CurrentInformationEnvironment.LifeTime = lifeTime;
    }

    /// <summary>
    /// Флаг сна
    /// </summary>
    public bool IsSleeping { get; set; } = false;

    /// <summary>
    /// Текущее настроение психики
    /// </summary>
    public int PsyMood { get; set; } = 0;

    /// <summary>
    /// Флаг очень актуальной ситуации
    /// </summary>
    public bool VeryActualSituation { get; set; } = false;

    /// <summary>
    /// Установить признак опасной ситуации
    /// </summary>
    public void SetVeryActualSituation(bool hasCriticalChanges)
    {
      VeryActualSituation = hasCriticalChanges;
      CurrentInformationEnvironment.VeryActualSituation = hasCriticalChanges;
    }

    /// <summary>
    /// Текущие целевые ID гомеостаза
    /// </summary>
    public List<int> CurTargetArrID { get; set; } = new List<int>();

    #endregion

    #region Основные методы

    /// <summary>
    /// Инициализирует текущую информационную среду
    /// </summary>
    public void InitCurrentInformationEnvironment()
    {
      CurrentInformationEnvironment.ActionsImageID = 0;
      CurrentInformationEnvironment.IsUnknownActionsImageID = 0;
      CurrentInformationEnvironment.LifeTime = LifeTime;
      CurrentInformationEnvironment.ActualEpisodicMemoryID = 0;
      CurrentInformationEnvironment.ExtremImportanceObjectID = 0;
    }

    /// <summary>
    /// Сохраняет старую информационную среду и создает новый кадр
    /// </summary>
    public void SaveOldIE()
    {
      // Создаем новый объект инфосреды
      var ie = new InformationEnvironment
      {
        LifeTime = OldInformationEnvironment.LifeTime,
        IsPsyLevel = OldInformationEnvironment.IsPsyLevel,
        IsSleep = OldInformationEnvironment.IsSleep,
        IsIdleness100pulse = OldInformationEnvironment.IsIdleness100pulse,
        Mood = OldInformationEnvironment.Mood,
        CurTargetArrID = OldInformationEnvironment.CurTargetArrID?.ToList() ?? new List<int>(),
        PsyEmotionId = OldInformationEnvironment.PsyEmotionId,
        Danger = OldInformationEnvironment.Danger,
        VeryActualSituation = OldInformationEnvironment.VeryActualSituation,
        PsyMood = OldInformationEnvironment.PsyMood,
        ActionsImageID = OldInformationEnvironment.ActionsImageID,
        IsUnknownActionsImageID = OldInformationEnvironment.IsUnknownActionsImageID,
        AnswerImageID = OldInformationEnvironment.AnswerImageID,
        IsWaitingPeriod = OldInformationEnvironment.IsWaitingPeriod,
        ExtremImportanceObjectID = OldInformationEnvironment.ExtremImportanceObjectID,
        ActualEpisodicMemoryID = OldInformationEnvironment.ActualEpisodicMemoryID,
        DominantaID = OldInformationEnvironment.DominantaID,
        NeedThinkingAboutAutomatizm = OldInformationEnvironment.NeedThinkingAboutAutomatizm,
        IsStimulToForce = OldInformationEnvironment.IsStimulToForce
      };

      _informationEnvironmentObjects.Add(ie);
      OldInformationEnvironment = CurrentInformationEnvironment;
      CurrentInformationEnvironment = ie;
    }

    /// <summary>
    /// Получает текущее состояние информационной среды
    /// </summary>
    /// <remarks>
    /// Отражение Базового состояния и Активных Базовых контекстов
    /// только при ориентировочном рефлексе и осмыслении результатов - обновление самоощущения!
    /// </remarks>
    public void GetCurrentInformationEnvironment(int currentEmotionId, int actionsImageId)
    {
      SaveOldIE();

      CurrentInformationEnvironment.LifeTime = LifeTime;
      CurrentInformationEnvironment.IsSleep = IsSleeping;
      CurrentInformationEnvironment.PsyEmotionId = currentEmotionId;
      CurrentInformationEnvironment.PsyMood = currentEmotionId;
      CurrentInformationEnvironment.ActionsImageID = actionsImageId;

      PsyMood = currentEmotionId;

      // CurrentInformationEnvironment.VeryActualSituation, CurrentInformationEnvironment.CurTargetArrID = gomeostas.FindTargetGomeostazID();
      // CurrentInformationEnvironment.Danger = GetAttentionDanger();
      // CurrentInformationEnvironment.Mood = GetCurMood();

      WriteInformationEnvironmentMarker();
    }

    /// <summary>
    /// Обновляет состояние информационной среды
    /// </summary>
    public void RefreshCurrentInformationEnvironment(int currentEmotionId, int actionsImageId)
    {
      // Информация просто перекрывается новой
      GetCurrentInformationEnvironment(currentEmotionId, actionsImageId);

      // Обновляем глобальные переменные
      VeryActualSituation = CurrentInformationEnvironment.VeryActualSituation;
      CurTargetArrID = CurrentInformationEnvironment.CurTargetArrID?.ToList() ?? new List<int>();
    }

    /// <summary>
    /// Очищает массив информационных сред (выполняется во сне)
    /// </summary>
    public void ClearInformationEnvironmentObjects()
    {
      _informationEnvironmentObjects.Clear();
    }

    /// <summary>
    /// Получает количество объектов информационной среды (для сна)
    /// </summary>
    public int GetInformationEnvironmentObjectsLength()
    {
      return _informationEnvironmentObjects.Count;
    }

    #endregion

    #region Вспомогательные методы

    /// <summary>
    /// Записывает метку изменения information_environment при каждом обновлении
    /// </summary>
    private void WriteInformationEnvironmentMarker()
    {
      try
      {

      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    /// <summary>
    /// Устанавливает флаг простоя (не было действий более 100 пульсов)
    /// </summary>
    public void SetIdlenessFlag(bool isIdle)
    {
      CurrentInformationEnvironment.IsIdleness100pulse = isIdle;
    }

    #endregion

    #region IDisposable

    private bool _disposed = false;

    /// <summary>
    /// Освобождает ресурсы, используемые объектом InformationEnvironmentSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        // Не сохраняем в файл, только очищаем память
        ClearInformationEnvironmentObjects();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
      finally
      {
        _disposed = true;
      }
    }

    #endregion
  }
}