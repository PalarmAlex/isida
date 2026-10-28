using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Сервис формирования условных рефлексов на основе временных корреляций.
  /// Два разведённых контура обучения:
  /// <list type="bullet">
  /// <item><description>Сенсорная прекондиция — нейтральные пары CS1-CS2 в <see cref="SensoryAssociationSystem"/>,
  /// перенос по иерархии «бедный→богатый» при C ≥ γ.</description></item>
  /// <item><description>Вторичное обусловливание — CR порядка ≥2 на CS1 при активации CR на CS2
  /// (CS2 как обусловленный подкрепитель); только для пар вне отношения «бедный⊂богатый».</description></item>
  /// </list>
  /// Активное угасание (RW, λ=0): если после CS в окне τ не пришёл US — ослабление УР с данным Level3.
  /// </summary>
  public sealed class ConditionedReflexFormationService : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly GomeostasSystem _gomeostas;
    private readonly GeneticReflexesSystem _geneticReflexes;
    private readonly ConditionedReflexesSystem _conditionedReflexes;
    private bool _disposed = false;

    #region Инициализация

    private static ConditionedReflexFormationService _instance;

    /// <summary>
    /// Глобальный экземпляр сервиса формирования условных рефлексов
    /// </summary>
    public static ConditionedReflexFormationService Instance => _instance ??
        throw new InvalidOperationException("ConditionedReflexFormationService не инициализирован.");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр сервиса формирования условных рефлексов
    /// </summary>
    public static void InitializeInstance(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes)
    {
      if (_instance != null)
        throw new InvalidOperationException("ConditionedReflexFormationService уже инициализирован.");

      _instance = new ConditionedReflexFormationService(gomeostas, geneticReflexes, conditionedReflexes);
    }

    private ConditionedReflexFormationService(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _geneticReflexes = geneticReflexes ?? throw new ArgumentNullException(nameof(geneticReflexes));
      _conditionedReflexes = conditionedReflexes ?? throw new ArgumentNullException(nameof(conditionedReflexes));
    }

    #endregion

    #region История стимулов

    /// <summary>
    /// Запись о стимуле в истории
    /// </summary>
    public class StimulusRecord
    {
      /// <summary>
      /// Время стимула (пульс)
      /// </summary>
      public int Pulse { get; set; }

      /// <summary>
      /// ID образа восприятия (стимула)
      /// </summary>
      public int StimulusImageId { get; set; }

      /// <summary>
      /// Базовое состояние гомеостаза
      /// </summary>
      public int BaseState { get; set; }

      /// <summary>
      /// ID образа стилей поведения
      /// </summary>
      public int BehaviorStyleImageId { get; set; }

      /// <summary>
      /// ID исходного безусловного рефлекса
      /// </summary>
      public int GeneticReflexId { get; set; }

      /// <summary>
      /// ID тона пускового стимула (для условного стимула — фраза с пульта). 0 — нормальный.
      /// </summary>
      public int ToneId { get; set; }

      /// <summary>
      /// ID настроения пускового стимула (для условного стимула). 0 — нормальное.
      /// </summary>
      public int MoodId { get; set; }
    }

    // Последний безусловный стимул
    private StimulusRecord _lastUnconditionedStimulus = null;

    // Пульс ПРЕДЫДУЩего безусловного стимула (до текущего). Граничит испытания: CS,
    // предъявленные до него, относятся к прошному триалу и не могут быть «со-предъявлены»
    // с текущим подкреплением (между ними уже встал US). Нужно для строгого компаунд-гейта
    // Kamin blocking (см. GetCoPresentedCsImageIds).
    private int _prevUnconditionedPulse = 0;

    // Последний условный стимул
    private StimulusRecord _lastConditionedStimulus = null;

    // Система мотор→стимул ассоциаций (вторичная инициализация из IsidaEngine)
    private MotorStimulusAssociationSystem _motorStimulusAssociations;

    // Последний выполненный мотор — для обучения мотор→стимул связей.
    // Живёт между пульсами (сервис не сбрасывается в ResetStates), в отличие от полей ReflexesActivator.
    private int _lastMotorActionId = 0;
    private int _lastMotorPulse = 0;

    // Мотор уже «поглощён» первым нейтральным CS после него — защита от перехвата несколькими CS.
    private bool _motorStimulusLinkConsumed = false;

    // «Удержанный» мотор-реакция: мотор, зафиксированный SetLastMotorAction в том же пульсе,
    // что и предъявление CS, на котором есть прокачанный УР (реальный порядок ActiveFromPhrase:
    // ExecuteReflexes → RecordStimulus). Пока кандидат на подкрепление (_lastMotorActionId)
    // не потреблён, новый мотор уходит сюда и НЕ затирает кандидата (регресс Случая 23:
    // затирание кандидата рвало транзитивную цепь ровно на «известном» стимуле).
    private int _heldMotorActionId = 0;
    private int _heldMotorPulse = 0;

    // Состояние цепочки у-рефлексов (реплей виртуальных стимулов).
    // Живёт между пульсами (сервис не сбрасывается в ResetStates), в отличие от полей ReflexesActivator.
    private int _chainTriggerImageId = 0;       // первый CS (триггер цепочки)
    private int _chainStep = 0;                 // индекс шага (1 = первый у-рефлекс сработал)
    private int _chainActivationPulse = 0;      // пульс активации первого у-рефлекса цепочки
    // Уже отреплейнутые стимулы текущей цепочки — защита от цикла (мотор2→С6 не даёт повторить С6).
    private readonly HashSet<int> _chainReplayedStimuli = new HashSet<int>();


    /// <summary>Ожидается ли US после последнего CS (для активного угасания).</summary>
    private bool _pendingCsAwaitingUs;

    private int _pendingCsPulse;
    private int _pendingCsStimulusImageId;
    private int _pendingCsToneId;
    private int _pendingCsMoodId;

    /// <summary>
    /// CS, предъявленные в текущем испытании (в окне τ от последнего стимула).
    /// Нужны, чтобы отличить классический Kamин blocking (компаунд: два CS предъявлены
    /// СОВМЕСТНО перед одним US) от раздельных испытаний. Без этой истории конкурентный
    /// слой неизбежно считается по всему реестру и блокирует раздельное обучение,
    /// делая невозможным «несколько разных стимулов на одну реакцию».
    /// </summary>
    private readonly List<StimulusRecord> _trialConditionedStimuli = new List<StimulusRecord>();

    /// <summary>
    /// Число неподкреплённых предъявлений CS (ключ: образ/тон/настроение) — накопитель
    /// латентного торможения (Lubov–Moore, 1959): стимул, многократно встреченный без US,
    /// «известная константа среды», обусловливается медленнее. Это природный анти-мусорный
    /// фильтр: он карает НЕинформативный CS, а не «второй CS на тот же US».
    /// </summary>
    private readonly Dictionary<(int Image, int Tone, int Mood), int> _csUnreinforcedPresentations =
        new Dictionary<(int Image, int Tone, int Mood), int>();

    /// <summary>
    /// Добавление стимула в историю
    /// </summary>
    /// <param name="pulse">Время стимула (пульс)</param>
    /// <param name="stimulusImageId">ID образа восприятия (стимула)</param>
    /// <param name="baseState">Базовое состояние гомеостаза</param>
    /// <param name="behaviorStyleImageId">ID образа стилей поведения</param>
    /// <param name="geneticReflexId">ID исходного безусловного рефлекса (0 для условного стимула)</param>
    /// <param name="toneId">ID тона (для условного стимула — фраза с пульта). 0 — по умолчанию.</param>
    /// <param name="moodId">ID настроения (для условного стимула). 0 — по умолчанию.</param>
    /// <param name="crFired">True — CS уже активировал УР (подтверждённое предсказание, угасание не запускается). 
    /// False — CS без срабатывания, при отсутствии US в окне τ применяется активное угасание. False по умолчанию.</param>
    public void RecordStimulus(
        int pulse,
        int stimulusImageId,
        int baseState,
        int behaviorStyleImageId,
        int geneticReflexId = 0,
        int toneId = 0,
        int moodId = 0,
        bool crFired = false)
    {
      int extinguishImageId = 0;
      int extinguishToneId = 0;
      int extinguishMoodId = 0;
      bool doExtinguish = false;

      _lock.EnterWriteLock();
      try
      {
        if (geneticReflexId == 0)
        {
          TryStrengthenNeutralSequentialLink(pulse, stimulusImageId);
          TryStrengthenMotorStimulusLink(pulse, stimulusImageId);
        }

        var record = new StimulusRecord
        {
          Pulse = pulse,
          StimulusImageId = stimulusImageId,
          BaseState = baseState,
          BehaviorStyleImageId = behaviorStyleImageId,
          GeneticReflexId = geneticReflexId,
          ToneId = toneId,
          MoodId = moodId
        };

        if (geneticReflexId > 0)
        {
          // Сдвигаем границу испытаний: прежний US становится «предыдущим».
          _prevUnconditionedPulse = _lastUnconditionedStimulus != null ? _lastUnconditionedStimulus.Pulse : 0;
          _lastUnconditionedStimulus = record;
          TrimTrialCsHistory(pulse);
          if (_pendingCsAwaitingUs &&
              _pendingCsPulse < pulse &&
              pulse - _pendingCsPulse <= _conditionedReflexes.Settings.TimeWindowPulses)
          {
            _pendingCsAwaitingUs = false;
          }
        }
        else
        {
          // Сначала закрываем ПРЕДЫДУЩИЙ pending CS, читаем его ID в локали ДО перезаписи.
          if (_pendingCsAwaitingUs && pulse > _pendingCsPulse)
          {
            extinguishImageId = _pendingCsStimulusImageId;
            extinguishToneId = _pendingCsToneId;
            extinguishMoodId = _pendingCsMoodId;
            _pendingCsAwaitingUs = false;
            doExtinguish = true;
          }

          // Всегда обновляем last CS — от него зависят вторичное обусловливание
          // и сенсорная прекондиция.
          _lastConditionedStimulus = record;
          _trialConditionedStimuli.Add(record);
          TrimTrialCsHistory(pulse);

          // Новый CS помечаем pending ТОЛЬКО если он не подтверждён сработавшим CR.
          if (!crFired)
          {
            _pendingCsAwaitingUs = true;
            _pendingCsPulse = pulse;
            _pendingCsStimulusImageId = stimulusImageId;
            _pendingCsToneId = toneId;
            _pendingCsMoodId = moodId;
          }
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      if (doExtinguish)
      {
        // Pending CS закрыт НОВЫМ CS раньше истечения окна τ. Это НЕ фиксируем как
        // «неподкреплённое предъявление» (латентное торможение здесь не копится):
        //  • US ещё может прийти в этом же окне τ и подкрепить закрытый CS;
        //  • новый CS для закрытого — сенсорный партнёр (латентная пара CS→CS′),
        //    т.е. закрытый CS предсказывает другой значимый стимул и потому ИНФОРМАТИВЕН,
        //    а не «известная константа среды» (Lubov–Moore).
        // Латентное торможение накапливается только по факту полного пролёта окна τ без US
        // (ProcessPendingExtinction) и только для ещё не обусловленного CS.
        // Активное угасание при этом применяется как и раньше.
        _conditionedReflexes.ApplyActiveExtinctionForStimulus(extinguishImageId, extinguishToneId, extinguishMoodId);
      }
    }

    /// <summary>
    /// Убирает из истории текущего испытания CS, вышедшие за окно τ от <paramref name="currentPulse"/>.
    /// </summary>
    /// <param name="currentPulse">Пульс, относительно которого обрезается окно.</param>
    private void TrimTrialCsHistory(int currentPulse)
    {
      int timeWindow = _conditionedReflexes.Settings.TimeWindowPulses;
      _trialConditionedStimuli.RemoveAll(r => r.Pulse < currentPulse - timeWindow);
    }

    /// <summary>
    /// Множество образов CS, предъявленных в окне τ ДО <paramref name="reinforcementPulse"/> —
    /// «совместно предъявленные» стимулы одного испытания. Размер &lt; 2 означает, что
    /// подкрепляемый CS был в испытании один (раздельное обучение), и Kamин blocking
    /// неприменим.
    /// </summary>
    /// <param name="reinforcementPulse">Пульс подкрепления (US или подкрепляющего CS).</param>
    /// <returns>Множество Level3 CS текущего испытания.</returns>
    private HashSet<int> GetCoPresentedCsImageIds(int reinforcementPulse)
    {
      int timeWindow = _conditionedReflexes.Settings.TimeWindowPulses;
      var ids = new HashSet<int>();
      foreach (var r in _trialConditionedStimuli)
      {
        if (r.Pulse > reinforcementPulse) continue;
        if (reinforcementPulse - r.Pulse > timeWindow) continue;
        // Граница испытания: CS, предъявленные до ПРЕДЫДУЩЕГО US, относятся к прошному
        // триалу (между ними уже встал US). Настоящий Kamин блокирует только при
        // совместном предъявлении БЕЗ промежуточного подкрепления (A+AX), поэтому такие
        // CS не считаются со-предъявленными — иначе паттерн CS→US→CS→US впритык ложно
        // сошёл бы за компаунд и затормозил свежий, отдельно обучающийся стимул.
        if (_prevUnconditionedPulse > 0 && r.Pulse <= _prevUnconditionedPulse) continue;
        ids.Add(r.StimulusImageId);
      }
      return ids;
    }

    /// <summary>
    /// Фиксирует факт неподкреплённого предъявления CS (накопитель латентного торможения).
    /// <para>
    /// Латентное торможение (Lubov–Moore, 1959) по смыслу наказывает только НЕинформативный
    /// CS — «известную константу среды», которая ничего не предсказывает. Если на образ уже
    /// есть условный рефлекс, стимул перестал быть константой: он предсказывает US/другой CS.
    /// Для такого CS угасание идёт своим механизмом (<c>ApplyActiveExtinctionForStimulus</c>),
    /// а латентное торможение не накапливается — иначе обусловленный стимул, застрявший чуть
    /// ниже порога γ, сам бы себя хоронил (самоподдерживающаяся ловушка «всегда слабый CS»).
    /// </para>
    /// </summary>
    /// <param name="imageId">Образ-стимул.</param>
    /// <param name="toneId">Тон.</param>
    /// <param name="moodId">Настроение.</param>
    private void RegisterUnreinforcedPresentation(int imageId, int toneId, int moodId)
    {
      if (imageId <= 0)
        return;

      // Фильтр (a): обусловленный CS (есть хотя бы один УР на этот образ) — не «константа
      // среды», латентное торможение на него не копим.
      if (HasConditionedReflexForImage(imageId))
        return;

      var key = (imageId, toneId, moodId);
      int count;
      _csUnreinforcedPresentations.TryGetValue(key, out count);
      _csUnreinforcedPresentations[key] = count + 1;
    }

    /// <summary>
    /// Есть ли на образ-стимул хотя бы один условный рефлекс (любого порядка/тона/настроения).
    /// Такой стимул информативен (что-то предсказывает) и не подлежит латентному торможению.
    /// </summary>
    /// <param name="imageId">Образ-стимул (Level3).</param>
    /// <returns>True, если УР на образ существует.</returns>
    private bool HasConditionedReflexForImage(int imageId)
    {
      if (imageId <= 0)
        return false;

      foreach (var r in _conditionedReflexes.GetAllConditionedReflexes())
      {
        if (r.Level3 == imageId)
          return true;
      }
      return false;
    }

    /// <summary>
    /// CS получил подкрепление — латентное торможение плавно снимается (накопитель
    /// уменьшается вдвое, а не обнуляется мгновенно). Мгновенный сброс превращал бы LI
    /// в одно-пробный штраф: достаточно одного случайного «CS без US», чтобы стимул
    /// навсегда стал «известной константой среды». Половинчатый распад сохраняет и
    /// тормозящий эффект для хронически шумовых CS, и обратимость для полезного стимула.
    /// </summary>
    /// <param name="imageId">Образ-стимул.</param>
    /// <param name="toneId">Тон.</param>
    /// <param name="moodId">Настроение.</param>
    private void DecayLatentInhibition(int imageId, int toneId, int moodId)
    {
      if (imageId <= 0)
        return;

      var key = (imageId, toneId, moodId);
      int count;
      if (!_csUnreinforcedPresentations.TryGetValue(key, out count))
        return;

      count /= 2;
      if (count <= 0)
        _csUnreinforcedPresentations.Remove(key);
      else
        _csUnreinforcedPresentations[key] = count;
    }

    /// <summary>
    /// Множитель скорости обучения из латентного торможения: f = max(min, 1/(1+S·N)),
    /// где N — число неподкреплённых предъявлений CS. 1 — торможения нет.
    /// </summary>
    /// <param name="conditionedStimulus">Запись подкрепляемого CS.</param>
    /// <returns>Множитель ∈ [LatentInhibitionMinFactor..1].</returns>
    private float ComputeLatentInhibitionFactor(StimulusRecord conditionedStimulus)
    {
      var settings = _conditionedReflexes.Settings;
      float strength = Math.Min(1f, Math.Max(0f, settings.LatentInhibitionStrength));
      if (strength <= 0f)
        return 1f;

      int count;
      if (!_csUnreinforcedPresentations.TryGetValue(
              (conditionedStimulus.StimulusImageId, conditionedStimulus.ToneId, conditionedStimulus.MoodId),
              out count) || count <= 0)
        return 1f;

      float factor = 1f / (1f + strength * count);
      float floor = Math.Min(1f, Math.Max(0f, settings.LatentInhibitionMinFactor));
      return Math.Max(floor, factor);
    }

    /// <summary>
    /// Если окно τ после CS истекло без US — активное угасание (RW, λ=0).
    /// </summary>
    internal void ProcessPendingExtinction(int currentPulse)
    {
      int imageId = 0;
      int toneId = 0;
      int moodId = 0;
      bool shouldExtinguish = false;

      _lock.EnterWriteLock();
      try
      {
        if (!_pendingCsAwaitingUs)
          return;

        int timeWindow = _conditionedReflexes.Settings.TimeWindowPulses;
        if (currentPulse <= _pendingCsPulse + timeWindow)
          return;

        imageId = _pendingCsStimulusImageId;
        toneId = _pendingCsToneId;
        moodId = _pendingCsMoodId;
        _pendingCsAwaitingUs = false;
        shouldExtinguish = true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      if (shouldExtinguish)
      {
        RegisterUnreinforcedPresentation(imageId, toneId, moodId);
        _conditionedReflexes.ApplyActiveExtinctionForStimulus(imageId, toneId, moodId);
      }
    }

    /// <summary>
    /// Укрепляет CS₁→CS₂ при нейтральной последовательной паре в окне τ.
    /// </summary>
    private void TryStrengthenNeutralSequentialLink(int currentPulse, int currentImageId)
    {
      if (_lastConditionedStimulus == null || !SensoryAssociationSystem.IsInitialized)
        return;

      int prevPulse = _lastConditionedStimulus.Pulse;
      int prevImageId = _lastConditionedStimulus.StimulusImageId;
      int timeWindow = _conditionedReflexes.Settings.TimeWindowPulses;

      if (prevPulse >= currentPulse)
        return;

      if (currentPulse > prevPulse + timeWindow)
        return;

      if (prevImageId == currentImageId)
        return;

      if (_lastUnconditionedStimulus != null &&
          _lastUnconditionedStimulus.Pulse > prevPulse &&
          _lastUnconditionedStimulus.Pulse < currentPulse)
        return;

      SensoryAssociationSystem.Instance.StrengthenLink(prevImageId, currentImageId);
    }

    /// <summary>
    /// Устанавливает ссылку на систему мотор→стимул ассоциаций (вторичная инициализация).
    /// Вызывается из IsidaEngine после инициализации MotorStimulusAssociationSystem.
    /// </summary>
    /// <param name="system">Система мотор→стимул ассоциаций.</param>
    /// <exception cref="ArgumentNullException">Передана null-ссылка.</exception>
    internal void SetMotorStimulusAssociations(MotorStimulusAssociationSystem system)
    {
      _motorStimulusAssociations = system ?? throw new ArgumentNullException(nameof(system));
    }

    /// <summary>
    /// Фиксирует выполненный мотор как «последний» для обучения мотор→стимул связей.
    /// Вызывается из ReflexesActivator при штатном выполнении рефлекса (ExecuteReflexes),
    /// НЕ при разыгрывании звена цепочки — прогон цепочки не должен обучаться.
    /// Если мотор приходит в том же пульсе, что и последнее предъявление CS, а кандидат
    /// на подкрепление ещё не потреблён — мотор НЕ затирает кандидата, а уходит в
    /// «удерживаемый» (heldMotor). Это позволяет первому звену усилиться до того,
    /// как второй мотор станет кандидатом для следующего CS (регресс Случая 23).
    /// </summary>
    /// <param name="motorActionId">ID моторного действия.</param>
    /// <param name="pulse">Пульс выполнения мотора.</param>
    internal void SetLastMotorAction(int motorActionId, int pulse)
    {
      _lock.EnterWriteLock();
      try
      {
        // Если есть живой непотреблённый кандидат и мотор-реакция пришла в пределах окна τ
        // от него — не затираем кандидата, а удерживаем мотор для следующего CS. Иначе —
        // штатная замена кандидата (первый мотор или кандидат уже подкреплён/остыл).
        if (_lastMotorActionId > 0 &&
            !_motorStimulusLinkConsumed &&
            pulse <= _lastMotorPulse + _conditionedReflexes.Settings.TimeWindowPulses)
        {
          _heldMotorActionId = motorActionId;
          _heldMotorPulse = pulse;
          return;
        }

        // Иначе — штатная замена кандидата.
        _lastMotorActionId = motorActionId;
        _lastMotorPulse = pulse;
        _motorStimulusLinkConsumed = false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Укрепляет мотор→стимул связь: если последний выполненный мотор сработал в окне τ
    /// до текущего нейтрального CS, усиливает связь MotorId → StimulusImageId.
    /// Усиливает связь только для ПЕРВОГО нейтрального CS после мотора.
    /// Вызывается из RecordStimulus() при geneticReflexId == 0.
    /// </summary>
    /// <param name="currentPulse">Пульс текущего стимула.</param>
    /// <param name="currentImageId">Образ текущего стимула.</param>
    private void TryStrengthenMotorStimulusLink(int currentPulse, int currentImageId)
    {
      if (_lastMotorActionId <= 0 || _motorStimulusAssociations == null)
        return;

      // Мотор уже связан с первым CS после него — второй и последующие CS
      // в том же окне не должны «перехватывать» мотор.
      if (_motorStimulusLinkConsumed)
        return;

      int timeWindow = _conditionedReflexes.Settings.TimeWindowPulses;
      // currentPulse <= _lastMotorPulse — намеренная защита от ложной связи «мотор → собственный
      // триггер»: в ActiveFromPhrase ExecuteReflexes (SetLastMotorAction) и RecordStimulus идут
      // в одном пульсе, поэтому CS того же пульса связывать нельзя.
      if (currentPulse <= _lastMotorPulse || currentPulse - _lastMotorPulse > timeWindow)
        return;

      _motorStimulusAssociations.StrengthenLink(_lastMotorActionId, currentImageId);
      _motorStimulusLinkConsumed = true;

      // После подкрепления кандидата продвигаем удержанный мотор-реакцию в кандидаты,
      // чтобы следующий CS (CS₃) мог усилить связь M₂→CS₃ (регресс Случая 23).
      if (_heldMotorActionId > 0)
      {
        _lastMotorActionId = _heldMotorActionId;
        _lastMotorPulse = _heldMotorPulse;
        _motorStimulusLinkConsumed = false;
        _heldMotorActionId = 0;
        _heldMotorPulse = 0;
      }
    }

    /// <summary>
    /// Получает последние стимулы в пределах временного окна
    /// </summary>
    public List<StimulusRecord> GetRecentStimuli(int currentPulse, int timeWindowPulses)
    {
      _lock.EnterReadLock();
      try
      {
        var recentStimuli = new List<StimulusRecord>();

        // Проверяем последний условный стимул
        if (_lastConditionedStimulus != null && (currentPulse - _lastConditionedStimulus.Pulse) <= timeWindowPulses)
          recentStimuli.Add(_lastConditionedStimulus);

        // Проверяем последний безусловный стимул
        if (_lastUnconditionedStimulus != null && (currentPulse - _lastUnconditionedStimulus.Pulse) <= timeWindowPulses)
          recentStimuli.Add(_lastUnconditionedStimulus);

        return recentStimuli.OrderBy(r => r.Pulse).ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Формирование условных рефлексов

    /// <summary>
    /// Проверяет временные корреляции между стимулами
    /// </summary>
    internal void CheckTemporalCorrelations(int currentPulse, bool authoritativeMode = false)
    {
      if (_lastUnconditionedStimulus == null || _lastConditionedStimulus == null)
        return;

      // Получаем настройки системы
      var settings = _conditionedReflexes.Settings;
      int timeWindow = settings.TimeWindowPulses;

      // Проверяем, находятся ли оба стимула в пределах временного окна
      bool unconditionedInWindow = (currentPulse - _lastUnconditionedStimulus.Pulse) <= timeWindow;
      bool conditionedInWindow = (currentPulse - _lastConditionedStimulus.Pulse) <= timeWindow;

      if (unconditionedInWindow && conditionedInWindow)
      {
        // Проверяем, что условный стимул предшествовал безусловному
        if (_lastConditionedStimulus.Pulse < _lastUnconditionedStimulus.Pulse)
        {
          // Проверяем, можно ли создать/усилить условный рефлекс
          ProcessConditionedAssociation(
              _lastConditionedStimulus,
              _lastUnconditionedStimulus,
              currentPulse,
              authoritativeMode);
        }
      }

      // Удаляет протухшие УР; сенсорные связи CS→CS по-прежнему точатся η
      _conditionedReflexes.ApplyDecay();
      if (SensoryAssociationSystem.IsInitialized)
        SensoryAssociationSystem.Instance.ApplyDecay();
      if (_motorStimulusAssociations != null)
        _motorStimulusAssociations.ApplyDecay();
    }

    /// <summary>
    /// Обрабатывает ассоциацию между условным и безусловным стимулом
    /// </summary>
    private void ProcessConditionedAssociation(
        StimulusRecord conditionedStimulus,
        StimulusRecord unconditionedStimulus,
        int currentPulse,
        bool authoritativeMode = false)
    {
      try
      {
        // Конкурентный слой (ΣV / Kamin blocking): если тот же US уже предсказывается
        // другим CS, СОВМЕСТНО предъявленным в этом же испытании, подкрепление подавляется.
        // Авторитарная запись оператора от подавления защищена — это явное подтверждение.
        float suppression = authoritativeMode
            ? 0f
            : ComputeCompetitionSuppression(
                conditionedStimulus, unconditionedStimulus.GeneticReflexId, 0, unconditionedStimulus.Pulse);
        if (suppression >= 1f)
        {
          Logger.Info(
              $"Обучение CS (Level3={conditionedStimulus.StimulusImageId}) заблокировано конкурентом " +
              $"от UR {unconditionedStimulus.GeneticReflexId}");
          return;
        }

        // Латентное торможение (Lubov–Moore): «известный» CS, долго висевший без US,
        // обусловливается медленнее. Работает и при раздельных испытаниях, поэтому и есть
        // основной анти-мусорный фильтр после отключения междуиспытательного blocking.
        float latentFactor = authoritativeMode ? 1f : ComputeLatentInhibitionFactor(conditionedStimulus);

        var existingReflexes = _conditionedReflexes.GetAllConditionedReflexes()
            .Where(r => r.Level3 == conditionedStimulus.StimulusImageId)
            .ToList();

        foreach (var existingReflex in existingReflexes)
        {
          if (existingReflex.Level1 == conditionedStimulus.BaseState &&
              existingReflex.Level2.OrderBy(x => x).SequenceEqual(GetCurrentStyleIds().OrderBy(x => x)) &&
              existingReflex.ToneId == conditionedStimulus.ToneId &&
              existingReflex.MoodId == conditionedStimulus.MoodId)
          {
            if (suppression > 0f || latentFactor < 1f)
            {
              _conditionedReflexes.StrengthenAssociationWithRate(
                  existingReflex.Id,
                  GetEffectiveLearningRate(suppression, latentFactor, existingReflex.Order));
              Logger.Info($"Усилен (скорректирован конкурентом/латентным торможением) условный рефлекс ID={existingReflex.Id}");
            }
            else
            {
              _conditionedReflexes.StrengthenAssociation(existingReflex.Id);
              Logger.Info($"Усилен условный рефлекс ID={existingReflex.Id}");
            }
            DecayLatentInhibition(
                conditionedStimulus.StimulusImageId, conditionedStimulus.ToneId, conditionedStimulus.MoodId);
            return;
          }
        }

        List<int> reflexStyles = new List<int>();

        if (unconditionedStimulus.GeneticReflexId > 0)
        {
          var geneticReflex = _geneticReflexes.GetAllGeneticReflexesList()
              .FirstOrDefault(r => r.Id == unconditionedStimulus.GeneticReflexId);

          if (geneticReflex != null)
            reflexStyles = geneticReflex.Level2?.ToList() ?? new List<int>();
        }
        else
        {
          // Если нет ID безусловного рефлекса, берем текущие стили
          reflexStyles = GetCurrentStyleIds();
        }

        var (newReflexId, warnings) = _conditionedReflexes.AddConditionedReflex(
            level1: conditionedStimulus.BaseState,
            level2: reflexStyles,
            level3: conditionedStimulus.StimulusImageId,
            sourceGeneticReflexId: unconditionedStimulus.GeneticReflexId,
            authoritativeMod: authoritativeMode,
            toneId: conditionedStimulus.ToneId,
            moodId: conditionedStimulus.MoodId);

        if (newReflexId > 0)
        {
          Logger.Info($"Создан условный рефлекс ID={newReflexId} от безусловного {unconditionedStimulus.GeneticReflexId}");
          if (suppression > 0f || latentFactor < 1f)
            _conditionedReflexes.StrengthenAssociationWithRate(
                newReflexId, GetEffectiveLearningRate(suppression, latentFactor, order: 1));
          DecayLatentInhibition(
              conditionedStimulus.StimulusImageId, conditionedStimulus.ToneId, conditionedStimulus.MoodId);
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    /// <summary>
    /// Скорость обучения с учётом конкурентного подавления и латентного торможения:
    /// α_eff = (α / K(order)) · (1 − suppression) · latentFactor.
    /// </summary>
    /// <param name="suppression">Доля подавления конкурирующим CS (0..1).</param>
    /// <param name="latentFactor">Множитель латентного торможения (0..1).</param>
    /// <param name="order">Порядок УР (для понижающего коэффициента K).</param>
    /// <returns>Эффективная скорость обучения ∈ [0..1].</returns>
    private float GetEffectiveLearningRate(float suppression, float latentFactor, int order)
    {
      float reduction = _conditionedReflexes.GetReductionCoefficientForOrder(order);
      float rate = (_conditionedReflexes.Settings.LearningRate / reduction)
          * (1f - suppression)
          * Math.Min(1f, Math.Max(0f, latentFactor));
      return Math.Min(1f, Math.Max(0f, rate));
    }

    /// <summary>
    /// Вычисляет долю подавления подкрепления текущего CS конкурирующими CS в модели
    /// Рескорла–Вагнера с ΣV (Kamin blocking). Конкуренты — другие активируемые УР с тем же
    /// источником подкрепления в тех же условиях (Level1/Level2/Tone/Mood). Их суммарная
    /// предсказательная сила ΣV нормируется на β: suppression = ΣV/β · коэффициент.
    /// Для первичного обусловливания конкурентами считаются CR₁ того же UR
    /// (<paramref name="sourceConditionedReflexId"/> = 0); для вторичного — CR того же
    /// родительского CR (<paramref name="sourceConditionedReflexId"/> &gt; 0).
    /// Kamин blocking по классике действует только при **совместном** предъявлении CS
    /// (компаунд): если <see cref="ConditionedReflexesSystem.ConditionedReflexSettings.CompetitiveLearningRequiresCompound"/>
    /// истинно, подавление считается лишь по конкурентам, реально предъявленным в окне τ
    /// до <paramref name="reinforcementPulse"/>; раздельные испытания учатся независимо
    /// (spacing effect). При ложном — прежний междуиспытательный режим по всему реестру.
    /// Возвращает 0, если конкурентный слой отключён или конкурентов нет.
    /// </summary>
    /// <param name="conditionedStimulus">Подкрепляемый CS.</param>
    /// <param name="sourceGeneticReflexId">Источник подкрепления — безусловный рефлекс (0 для вторичных).</param>
    /// <param name="sourceConditionedReflexId">Родительский УР для вторичного обусловливания (0 для первичных).</param>
    /// <param name="reinforcementPulse">Пульс подкрепления (US или подкрепляющего CS).</param>
    /// <returns>Доля подавления ∈ [0..1].</returns>
    private float ComputeCompetitionSuppression(
        StimulusRecord conditionedStimulus,
        int sourceGeneticReflexId,
        int sourceConditionedReflexId,
        int reinforcementPulse)
    {
      var settings = _conditionedReflexes.Settings;
      if (!settings.EnableCompetitiveLearning)
        return 0f;

      if (sourceGeneticReflexId <= 0 && sourceConditionedReflexId <= 0)
        return 0f;

      // Компаунд-гейт: Kamин blocking — про совместное предъявление. Если в текущем
      // испытании был только один CS, конкурировать не с чем → подавления нет.
      HashSet<int> coPresented = null;
      if (settings.CompetitiveLearningRequiresCompound)
      {
        coPresented = GetCoPresentedCsImageIds(reinforcementPulse);
        if (coPresented.Count < 2)
          return 0f;
      }

      float beta = settings.MaxAssociationStrength > 1e-6f ? settings.MaxAssociationStrength : 1f;
      var styleIds = GetCurrentStyleIds();

      float sumV = 0f;
      foreach (var r in _conditionedReflexes.GetAllConditionedReflexes())
      {
        bool isCompetitor = sourceConditionedReflexId > 0
            ? r.SourceConditionedReflexId == sourceConditionedReflexId
            : r.SourceConditionedReflexId == 0 && r.SourceGeneticReflexId == sourceGeneticReflexId;
        if (!isCompetitor) continue;
        if (r.Level1 != conditionedStimulus.BaseState) continue;
        if (r.ToneId != conditionedStimulus.ToneId || r.MoodId != conditionedStimulus.MoodId) continue;
        if (!r.Level2.OrderBy(x => x).SequenceEqual(styleIds.OrderBy(x => x))) continue;
        // Сам подкрепляемый CS конкурентом не является.
        if (r.Level3 == conditionedStimulus.StimulusImageId) continue;
        // В компаунд-режиме конкурентом считается только CS, предъявленный в этом же испытании.
        if (coPresented != null && !coPresented.Contains(r.Level3)) continue;
        if (!r.CanBeActivated()) continue;

        sumV += r.AssociationStrength;
      }

      if (sumV <= 0f)
        return 0f;

      float coeff = Math.Min(1f, Math.Max(0f, settings.CompetitionSuppressionCoefficient));
      float predicted = Math.Min(1f, sumV / beta);
      return Math.Min(1f, predicted * coeff);
    }

    /// <summary>
    /// Получает ID текущих активных стилей поведения
    /// </summary>
    private List<int> GetCurrentStyleIds()
    {
      var currentStyles = AppGlobalState.ActiveStyles;
      return currentStyles.Select(s => s.Id).ToList();
    }

    /// <summary>
    /// Проверяет возможность формирования вторичного условного рефлекса.
    /// Вызывается когда стимул активировал существующий у-рефлекс (CS₂ как обусловленный подкрепитель).
    /// Если перед ним (в окне τ) был другой CS₁ и пара не относится к сенсорной прекондиции,
    /// для CS₁ создаётся/усиливается CR порядка ≥2 (exact match, не иерархия).
    /// </summary>
    /// <param name="currentPulse">Текущий пульс</param>
    /// <param name="reinforcingStimulusImageId">ID образа стимула, активировавшего условный рефлекс</param>
    /// <param name="baseState">Базовое состояние гомеостаза</param>
    /// <param name="behaviorStyleImageId">ID образа стилей поведения</param>
    /// <param name="activatedConditionedReflexId">ID условного рефлекса, который был активирован</param>
    /// <param name="authoritativeMode">Флаг авторитарной записи</param>
    /// <param name="reinforcingToneId">Тон стимула, активировавшего рефлекс</param>
    /// <param name="reinforcingMoodId">Настроение стимула, активировавшего рефлекс</param>
    internal void CheckSecondaryConditioning(
        int currentPulse,
        int reinforcingStimulusImageId,
        int baseState,
        int behaviorStyleImageId,
        int activatedConditionedReflexId,
        bool authoritativeMode,
        int reinforcingToneId,
        int reinforcingMoodId)
    {
      if (_lastConditionedStimulus == null)
        return;

      // Проверяем, что родительский условный рефлекс достаточно силён
      var parentReflex = _conditionedReflexes.GetConditionedReflexById(activatedConditionedReflexId);
      if (parentReflex == null || !parentReflex.CanBeActivated())
        return;

      // Не создаём рефлексы порядка выше третичного
      if (parentReflex.Order >= 3)
        return;

      var settings = _conditionedReflexes.Settings;
      int timeWindow = settings.TimeWindowPulses;

      bool previousCSInWindow = (currentPulse - _lastConditionedStimulus.Pulse) <= timeWindow;

      if (!previousCSInWindow)
        return;

      // Предыдущий CS должен предшествовать текущему подкрепляющему стимулу
      if (_lastConditionedStimulus.Pulse >= currentPulse)
        return;

      // Убеждаемся, что предыдущий CS — это другой стимул (не тот же самый)
      if (_lastConditionedStimulus.StimulusImageId == reinforcingStimulusImageId)
        return;

      // Пара «бедный CSₐ ⊂ богатый CSᵦ» — зона сенсорной прекондиции (связь + иерархия), не вторичный CR.
      if (_conditionedReflexes.IsSensoryPreconditioningPair(
              _lastConditionedStimulus.StimulusImageId,
              reinforcingStimulusImageId))
        return;

      bool reinforced = ProcessSecondaryConditionedAssociation(
          _lastConditionedStimulus,
          parentReflex,
          authoritativeMode,
          currentPulse);

      // CSₐ получил вторичное подкрепление (активацией CR на CSᵦ) — снимаем у него
      // ожидание US без активного угасания. Иначе при записи следующего стимула CSₐ
      // закрывается как «CS без подкрепления» и только что созданный CR₂ активно
      // угашается (регрессия «CR₂ не активируется»).
      if (reinforced)
        ConfirmPendingCsReinforced();
    }

    /// <summary>
    /// Вторичное обусловливание: CR на CSₐ, подкреплённый активацией CR на последующем CSᵦ.
    /// Активация — exact match по Level3; иерархия и SensoryAssociationSystem не задействуются.
    /// </summary>
    /// <returns>True, если CR₂ создан или усилен (CSₐ фактически подкреплён).</returns>
    private bool ProcessSecondaryConditionedAssociation(
        StimulusRecord conditionedStimulus,
        ConditionedReflexesSystem.ConditionedReflex parentConditionedReflex,
        bool authoritativeMode,
        int reinforcementPulse)
    {
      try
      {
        // Конкурентный слой (ΣV / Kamин blocking) для вторичного обусловливания:
        // если тот же источник подкрепления уже предсказывается другим активируемым CR,
        // СОВМЕСТНО предъявленным в этом испытании, подкрепление шумового CS₂ подавляется.
        float suppression = authoritativeMode
            ? 0f
            : ComputeCompetitionSuppression(
                conditionedStimulus,
                sourceGeneticReflexId: 0,
                sourceConditionedReflexId: parentConditionedReflex.Id,
                reinforcementPulse: reinforcementPulse);

        float latentFactor = authoritativeMode ? 1f : ComputeLatentInhibitionFactor(conditionedStimulus);

        var existingReflexes = _conditionedReflexes.GetAllConditionedReflexes()
            .Where(r => r.Level3 == conditionedStimulus.StimulusImageId)
            .ToList();

        foreach (var existingReflex in existingReflexes)
        {
          if (existingReflex.Level1 == conditionedStimulus.BaseState &&
              existingReflex.Level2.OrderBy(x => x).SequenceEqual(GetCurrentStyleIds().OrderBy(x => x)) &&
              existingReflex.ToneId == conditionedStimulus.ToneId &&
              existingReflex.MoodId == conditionedStimulus.MoodId)
          {
            if (suppression > 0f || latentFactor < 1f)
            {
              _conditionedReflexes.StrengthenAssociationWithRate(
                  existingReflex.Id,
                  GetEffectiveLearningRate(suppression, latentFactor, existingReflex.Order));
            }
            else
            {
              _conditionedReflexes.StrengthenAssociation(existingReflex.Id);
            }
            DecayLatentInhibition(
                conditionedStimulus.StimulusImageId, conditionedStimulus.ToneId, conditionedStimulus.MoodId);
            Logger.Info($"Усилен вторичный условный рефлекс ID={existingReflex.Id} " +
                       $"(порядок {existingReflex.Order}) от условного {parentConditionedReflex.Id}");
            return true;
          }
        }

        if (suppression >= 1f)
        {
          Logger.Info(
              $"Вторичное обучение CS (Level3={conditionedStimulus.StimulusImageId}) заблокировано конкурентом " +
              $"от условного {parentConditionedReflex.Id}");
          return false;
        }

        List<int> reflexStyles = GetCurrentStyleIds();

        var (newReflexId, warnings) = _conditionedReflexes.AddConditionedReflex(
            level1: conditionedStimulus.BaseState,
            level2: reflexStyles,
            level3: conditionedStimulus.StimulusImageId,
            sourceGeneticReflexId: parentConditionedReflex.SourceGeneticReflexId,
            authoritativeMod: authoritativeMode,
            toneId: conditionedStimulus.ToneId,
            moodId: conditionedStimulus.MoodId,
            sourceConditionedReflexId: parentConditionedReflex.Id);

        if (newReflexId > 0)
        {
          int newOrder = parentConditionedReflex.Order + 1;
          Logger.Info($"Создан условный рефлекс {newOrder}-го порядка ID={newReflexId} " +
                     $"от условного {parentConditionedReflex.Id} " +
                     $"(безусловный источник: {parentConditionedReflex.SourceGeneticReflexId})");
          if (suppression > 0f || latentFactor < 1f)
            _conditionedReflexes.StrengthenAssociationWithRate(
                newReflexId, GetEffectiveLearningRate(suppression, latentFactor, newOrder));
          DecayLatentInhibition(
              conditionedStimulus.StimulusImageId, conditionedStimulus.ToneId, conditionedStimulus.MoodId);
        }

        return true;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return false;
      }
    }

    /// <summary>
    /// Снимает у текущего pending-CS ожидание US без активного угасания.
    /// Вызывается, когда CS получил вторичное подкрепление (активацию CR на последующем CS).
    /// </summary>
    private void ConfirmPendingCsReinforced()
    {
      _lock.EnterWriteLock();
      try
      {
        _pendingCsAwaitingUs = false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Цепочки у-рефлексов (реплей виртуальных стимулов)

    /// <summary>
    /// Записывает активацию у-рефлекса в контексте цепочки.
    /// Вызывается из <c>ReflexesActivator.ExecuteReflexes</c> после успешной активации у-рефлекса.
    /// Первый вызов для данного триггера устанавливает <c>_chainTriggerImageId</c> и <c>_chainStep = 1</c>.
    /// </summary>
    /// <param name="conditionedReflexId">ID у-рефлекса, который только что сработал.</param>
    /// <param name="triggerStimulusId">ID стимула, который его активировал (первый CS цепочки).</param>
    internal void RecordConditionedReflexActivation(int conditionedReflexId, int triggerStimulusId)
    {
      _lock.EnterWriteLock();
      try
      {
        // Первый шаг цепочки: сбрасываем HashSet отреплейнутых стимулов и фиксируем триггер.
        // Все последующие шаги (step > 1) не перезаписывают триггер — он сохраняется от первого вызова.
        if (_chainStep == 0)
        {
          _chainReplayedStimuli.Clear();
          _chainReplayedStimuli.Add(triggerStimulusId);
        }
        _chainTriggerImageId = triggerStimulusId;
        _chainStep = 1;
        _chainActivationPulse = AppGlobalState.Lifetime;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Проверяет, есть ли цепочка, и возвращает стимул для реплея (второй CS цепочки).
    /// true — есть следующий шаг, false — цепочка завершена или не начата.
    /// </summary>
    /// <param name="nextStimulusId">ID стимула для реплея (второй CS).</param>
    /// <returns>True, если цепочка продолжается.</returns>
    internal bool TryGetNextChainStimulus(out int nextStimulusId)
    {
      nextStimulusId = 0;
      if (_chainTriggerImageId <= 0 || _chainStep <= 0)
        return false;

      // Таймаут шага: если прошло слишком много пульсов — цепочка мертва.
      int timeout = _conditionedReflexes.Settings.ConditionedReflexChainStepTimeout;
      if (timeout > 0 && AppGlobalState.Lifetime - _chainActivationPulse > timeout)
      {
        _chainStep = 0;
        return false;
      }

      // Ищем активируемые стимулы для последнего выполненного мотора.
      if (_motorStimulusAssociations == null || _lastMotorActionId <= 0)
      {
        _chainStep = 0;
        return false;
      }

      var activatable = _motorStimulusAssociations.GetActivatableStimuli(_lastMotorActionId);
      if (activatable == null || activatable.Count == 0)
      {
        _chainStep = 0;
        return false;
      }

      // Берём первый активируемый стимул, ещё не отреплейнутый в этой цепочке (защита от цикла).
      int chosen = 0;
      foreach (var s in activatable)
      {
        if (!_chainReplayedStimuli.Contains(s))
        {
          chosen = s;
          break;
        }
      }
      if (chosen == 0)
      {
        // Все активируемые стимулы уже использованы — цепочка завершена.
        _chainStep = 0;
        return false;
      }

      nextStimulusId = chosen;
      _chainReplayedStimuli.Add(chosen);
      _chainStep++;

      // Проверяем, не достигли ли макс. длины цепочки.
      int maxLen = _conditionedReflexes.Settings.ConditionedReflexMaxChainLength;
      if (maxLen > 0 && _chainStep >= maxLen)
      {
        _chainStep = 0;
      }

      return true;
    }

    /// <summary>
    /// Сбрасывает состояние цепочки (вызывается при необходимости извне, например при смене условий).
    /// </summary>
    internal void ResetChainState()
    {
      _lock.EnterWriteLock();
      try
      {
        _chainTriggerImageId = 0;
        _chainStep = 0;
        _chainActivationPulse = 0;
        _chainReplayedStimuli.Clear();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Управление жизненным циклом рефлексов

    /// <summary>
    /// Сброс истории стимулов
    /// </summary>
    public void ResetHistory()
    {
      _lock.EnterWriteLock();
      try
      {
        _lastUnconditionedStimulus = null;
        _prevUnconditionedPulse = 0;
        _lastConditionedStimulus = null;
        _pendingCsAwaitingUs = false;
        _pendingCsPulse = 0;
        _pendingCsStimulusImageId = 0;
        _pendingCsToneId = 0;
        _pendingCsMoodId = 0;

        // Сброс состояния мотора (страховка при выгрузке/сбросе тестов).
        _lastMotorActionId = 0;
        _lastMotorPulse = 0;
        _motorStimulusLinkConsumed = false;
        _heldMotorActionId = 0;
        _heldMotorPulse = 0;

        // Сброс состояния цепочки у-рефлексов.
        _chainTriggerImageId = 0;
        _chainStep = 0;
        _chainActivationPulse = 0;

        // Сброс истории CS текущего испытания и накопителя латентного торможения:
        // оба живут только в памяти сервиса (не персистятся) и при сбросе движка/теста
        // обязаны обнуляться, иначе следующий прогон стартует с чужим «знакомством со средой».
        _trialConditionedStimuli.Clear();
        _csUnreinforcedPresentations.Clear();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом ConditionedReflexFormationService
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        // Сброс истории под блокировкой до Dispose — иначе EnterWriteLock на уничтоженном ReaderWriterLockSlim.
        ResetHistory();
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
        _instance = null;
      }
    }

    #endregion
  }
}