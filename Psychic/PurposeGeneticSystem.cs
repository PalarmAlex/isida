using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Psychic.Automatism;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static ISIDA.Psychic.Automatism.ActionsImagesSystem;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TreeView;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Система управления гомеостатическими целями агента
  /// </summary>
  public sealed class PurposeGeneticImageSystem: IDisposable
  {
    private readonly InformationEnvironmentSystem _informationEnvironmentSystem;
    private readonly AutomatizmSystem _automatizmSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;

    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private int oldAutomatizmId = 0;

    #region Инициализация

    private static PurposeGeneticImageSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static PurposeGeneticImageSystem Instance => _instance ??
        throw new InvalidOperationException("PurposeGeneticSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы
    /// </summary>
    /// <exception cref="InvalidOperationException">Выбрасывается, если система уже была инициализирована ранее</exception>
    public static void InitializeInstance(
      InformationEnvironmentSystem informationEnvironmentSystem,
      ActionsImagesSystem actionsImagesSystem,
      AutomatizmSystem automatizmSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("PurposeGeneticSystem уже инициализирован.");

      _instance = new PurposeGeneticImageSystem(informationEnvironmentSystem, actionsImagesSystem, automatizmSystem);
    }

    private PurposeGeneticImageSystem(
      InformationEnvironmentSystem informationEnvironmentSystem,
      ActionsImagesSystem actionsImagesSystem,
      AutomatizmSystem automatizmSystem)
    {
      try
      {
        _informationEnvironmentSystem = informationEnvironmentSystem ?? throw new ArgumentNullException(nameof(informationEnvironmentSystem));
        _actionsImagesSystem = actionsImagesSystem ?? throw new ArgumentNullException(nameof(actionsImagesSystem));
        _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    /// <summary>
    /// Образ гомеостатической целм
    /// </summary>
    public class PurposeGeneticImage
    {
      /// <summary>
      /// Номер пульса
      /// </summary>
      public int Puls { get; set; }

      /// <summary>
      /// Флаг актуальности цели (True - очень актуальна)
      /// </summary>
      public bool VeryActual { get; set; }

      /// <summary>
      /// ID параметра гомеостаза как цели для улучшения в данных условиях - текущий приоритет по функции потребности
      /// </summary>
      public int TargetId { get; set; }

      /// <summary>
      /// Выбранный образ действия для данной цели
      /// </summary>
      public ActionsImage ActionImage { get; set; }
    }

    #endregion

    #region Статические поля

    /// <summary>
    /// Объекты PurposeGeneticObject накапливаются в оперативке и удаляются во сне
    /// </summary>
    private static List<PurposeGeneticImage> PurposeGeneticObject = new List<PurposeGeneticImage>();

    /// <summary>
    /// Текущая цель сохраняется до перекрытия следующим orientation_N()
    /// </summary>
    private static PurposeGeneticImage CurrentPurposeGenetic { get; set; }

    /// <summary>
    /// Предыдущая цель
    /// </summary>
    private static PurposeGeneticImage OldPurposeGenetic { get; set; }

    #endregion

    #region Управление образами целей

    /// <summary>
    /// Получить текущий гомеостатический образ
    /// </summary>
    public PurposeGeneticImage GetPurposeGeneticImage()
    {
      _lock.EnterReadLock();
      try
      {
        var purposeGenetic = new PurposeGeneticImage
        {
          Puls = GlobalTimer.GlobalPulsCount,
          VeryActual = _informationEnvironmentSystem.VeryActualSituation,
          TargetId = AppGlobalState.DominantParam
        };

        var activeActions = GetActiveAdaptiveActions();
        var actionIdList = activeActions.Select(a => a.Id).ToList();
        ActionsImage actionImage = null;
        if (activeActions.Count > 0)
        {
          (_, actionImage) = _actionsImagesSystem.CreateNewActionsImageWithIdNoLock(0, 0, actionIdList, null, 0, 0, true);
          purposeGenetic.ActionImage = actionImage;
        }
        else
        {
          actionIdList = new List<int> { AppGlobalState.DefaultAdaptiveActionId };
          (_, actionImage) = _actionsImagesSystem.CreateNewActionsImageWithIdNoLock(0, 0, actionIdList, null, 0, 0, true);
          purposeGenetic.ActionImage = actionImage;
        }

        PurposeGeneticObject.Add(purposeGenetic);
        OldPurposeGenetic = CurrentPurposeGenetic;
        CurrentPurposeGenetic = purposeGenetic;

        return purposeGenetic;
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        return null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает список активных адаптивных действий
    /// </summary>
    public List<AdaptiveActionsSystem.AdaptiveAction> GetActiveAdaptiveActions()
    {
      if (AppGlobalState.ActiveAdaptiveActions == null)
        return new List<AdaptiveActionsSystem.AdaptiveAction>();

      return AppGlobalState.ActiveAdaptiveActions.ToList();
    }

    /// <summary>
    /// Получить автоматизм по гомеостатической цели
    /// </summary>
    public Automatizm GetAutomatizmByGeneticPurpose()
    {
      try
      {
        var purposeGenetic = GetPurposeGeneticImage();
        Automatizm atmz = null;
        
        if (purposeGenetic.VeryActual || AppGlobalState.FlgConditionReflexes || AppGlobalState.CurActiveVerbalId == 0)
        {
          if(purposeGenetic.ActionImage != null)
            atmz = CreateAutomatizmByGeneticPurpose(purposeGenetic);
        }
        return atmz;
      }
      catch(Exception ex)
      {
        Logger.Error($"{ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Обработка автоматизма, рвущегося на выполнение - простейший вариант
    /// </summary>
    public Automatizm GetBasicAutomatizmByPurpose(int atmtzmID)
    {
      try
      {
        var atmz = _automatizmSystem.GetAutomatizmById(atmtzmID);
        if(atmz == null)
        {
          Logger.Error($"Не найден автоматизм ID={atmtzmID}");
          return null;
        }

        var purposeGenetic = GetPurposeGeneticImage();
        ActionsImage actImg = null;
        actImg = _actionsImagesSystem.GetActionsImage(atmz.ActionsImageID);
        bool IsHasThreat = HasThreat(actImg.ToneId, actImg.MoodId);

        if (IsHasThreat || purposeGenetic.VeryActual)
        {
          if(oldAutomatizmId != atmz.ID)
          {
            oldAutomatizmId = atmz.ID;
            return atmz;
          }
        }

        return null;
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        return null;
      }     
    }

    /// <summary>
    /// Создать и запустить автоматизм по гомеостатической цели
    /// </summary>
    public Automatizm CreateAutomatizmByGeneticPurpose(PurposeGeneticImage purposeGenetic)
    {
      if(purposeGenetic == null || purposeGenetic.ActionImage == null)
        return null;

      try
      {
        int branchID = AppGlobalState.AutomatizmNodeId;
        var aArr = purposeGenetic.ActionImage.ActIdList;
        var sArr = purposeGenetic.ActionImage.PhraseIdList;
        int toneId = purposeGenetic.ActionImage.ToneId;
        int moodId = purposeGenetic.ActionImage.MoodId;

        int actionImageId = 0;
        (actionImageId, _) = _actionsImagesSystem.CreateNewActionsImageWithIdNoLock(0, 0, aArr, sArr, toneId, moodId, true);
        Automatizm atmz = null;
        (_, atmz) = _automatizmSystem.CreateNewAutomatizm(branchID, actionImageId);

        return atmz;
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        return null;
      }
    }

    #endregion

    #region IDisposable

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

    #endregion

  }
}
