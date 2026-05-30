using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BOforUnity;
using BOforUnity.Scripts;
using QuestionnaireToolkit.Scripts;

public class ExperimentConfig : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // User ID Panel UI
    // ─────────────────────────────────────────────
    [Header("User ID Panel UI")]
    [Tooltip("在 Config Panel 前先顯示的 User ID 輸入面板")]
    public GameObject userIdPanel;

    [Tooltip("讓實驗者輸入 ID 的 TMP_InputField")]
    public TMP_InputField userIdInputField;

    [Tooltip("User ID 面板上的 Continue 按鈕")]
    public Button userIdContinueBtn;

    // ─────────────────────────────────────────────
    // Config Panel UI
    // ─────────────────────────────────────────────
    [Header("Config Panel UI")]
    public GameObject configPanel;
    public Button scale5Btn;
    public Button scale20Btn;
    public Button scale100Btn;
    public Button rounds10Btn;
    public Button rounds15Btn;
    public Toggle warmStartToggle;
    
    // Pascal v1.4.2 隨機組分配開關
    public Toggle randomAllocationToggle;
    public Toggle optimizedToggle;
    public Button startBtn;

    // ─────────────────────────────────────────────
    // Data-Driven References
    // ─────────────────────────────────────────────
    [Header("Dynamic References (Data-Driven)")]
    [Tooltip("請輸入妳在 BoManager 裡設定的問卷 Objective Key (例: mental_demand)")]
    public string targetObjectiveKey = "mental_demand";

    [Tooltip("當選擇 10 輪 Sampling 時，對應的 Optimization 輪數")]
    public int optimizationRoundsFor10 = 5;

    [Tooltip("當選擇 15 輪 Sampling 時，對應的 Optimization 輪數")]
    public int optimizationRoundsFor15 = 0;

    [Header("Manager References")]
    public BoForUnityManager boManager;

    // 🟢 已改成通用的 MonoBehaviour 防止編譯紅字
    private MonoBehaviour _fittsLawConditionManager;

    private const int BaselineRoundsPerScale = 10;
    private static readonly int[] BaselineScales = { 5, 20, 100 };

    private readonly Color _selectedColor = new Color(0.498f, 0.467f, 0.867f);
    private readonly Color _defaultColor  = new Color(0.9f, 0.9f, 0.9f);

    private static int    _likertMax         = 5;
    private static int    _samplingRounds    = 10;
    private static bool   _warmStart         = false;
    private static bool   _randomAllocation  = false;
    private static bool   _optimized         = false;
    private static bool   _experimentStarted = false;
    private static string _userId            = ""; 
    private static bool   _baselinePhaseStarted = false;
    private static bool   _baselinePhaseCompleted = false;
    private static int[]  _baselineScaleOrder = new int[0];
    private static int    _baselineScaleIndex = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _likertMax         = 5;
        _samplingRounds    = 10;
        _warmStart         = false;
        _randomAllocation  = false;
        _optimized         = false;
        _experimentStarted = false;
        _userId            = ""; 
        _baselinePhaseStarted = false;
        _baselinePhaseCompleted = false;
        _baselineScaleOrder = new int[0];
        _baselineScaleIndex = 0;
    }

    // ─────────────────────────────────────────────
    // Awake
    // ─────────────────────────────────────────────
    void Awake()
    {
        if (!_experimentStarted)
        {
            userIdPanel.SetActive(!_baselinePhaseStarted && !_baselinePhaseCompleted);
            configPanel.SetActive(_baselinePhaseCompleted);
            boManager.welcomePanel.SetActive(false);
            boManager.nextButton.SetActive(false);

            if (boManager.pythonStarter != null)
                boManager.pythonStarter.enabled = false;
        }
        else
        {
            userIdPanel.SetActive(false);
            configPanel.SetActive(false);
            ApplyConfig();
        }
    }

    // ─────────────────────────────────────────────
    // Start
    // ─────────────────────────────────────────────
    void Start()
    {
        if (_experimentStarted) return;

        userIdContinueBtn.onClick.AddListener(OnUserIdContinueClicked);

        if (!string.IsNullOrEmpty(_userId) && userIdInputField != null)
            userIdInputField.text = _userId;

        scale5Btn  .onClick.AddListener(() => { SetScale(5);   HighlightScale(scale5Btn);   });
        scale20Btn .onClick.AddListener(() => { SetScale(20);  HighlightScale(scale20Btn);  });
        scale100Btn.onClick.AddListener(() => { SetScale(100); HighlightScale(scale100Btn); });
        
        rounds10Btn.onClick.AddListener(() => {
            if (_randomAllocation) return;
            SetRounds(10); 
            HighlightRounds(rounds10Btn);
            ConfigureRound10Mode();
        });
        
        rounds15Btn.onClick.AddListener(() => {
            if (_randomAllocation) return;
            SetRounds(15); 
            HighlightRounds(rounds15Btn);
            ConfigureRound15Mode();
        });
        
        warmStartToggle.onValueChanged.AddListener(val => _warmStart = val);
        randomAllocationToggle.onValueChanged.AddListener(OnRandomAllocationChanged);
        if (optimizedToggle != null)
        {
            optimizedToggle.onValueChanged.AddListener(val => _optimized = val);
        }
        startBtn.onClick.AddListener(OnStartClicked);

        _warmStart = warmStartToggle.isOn;
        _randomAllocation = randomAllocationToggle.isOn;
        _optimized = optimizedToggle != null && optimizedToggle.isOn;

        HighlightScale(scale5Btn);
        if (_randomAllocation)
        {
            HighlightRounds(rounds15Btn);
            rounds10Btn.interactable = false;
            rounds15Btn.interactable = false;
            ConfigureRandomMode();
            SetConditionManagerMode("Random");
        }
        else
        {
            HighlightRounds(rounds10Btn);
            ConfigureRound10Mode();
        }
    }

    void OnUserIdContinueClicked()
    {
        string trimmed = userIdInputField != null ? userIdInputField.text.Trim() : "";

        if (string.IsNullOrEmpty(trimmed))
        {
            Debug.LogWarning("[ExperimentConfig] User ID 不能空白，請輸入後再繼續。");
            return;
        }

        _userId = trimmed;
        boManager.userId = _userId;

        Debug.Log($"[ExperimentConfig] User ID 已設定為原始字串：'{_userId}'");

        UpdateBaselineDataPaths();

        if (_baselinePhaseCompleted)
            ShowPhase2ConfigPanel();
        else
            StartBaselinePhase();
    }

    void OnRandomAllocationChanged(bool isOn)
    {
        _randomAllocation = isOn;
        if (isOn)
        {
            _samplingRounds = 5;
            HighlightRounds(rounds15Btn);
            rounds10Btn.interactable = false;
            rounds15Btn.interactable = false;

            ConfigureRandomMode();
            SetConditionManagerMode("Random");
        }
        else
        {
            EnableBoRuntimeComponentsForConfig();
            rounds10Btn.interactable = true;
            rounds15Btn.interactable = true;
            _samplingRounds = 10;
            HighlightRounds(rounds10Btn);

            // Restore default for Round10 mode
            ConfigureRound10Mode();
            SetConditionManagerMode("AdaptiveBo");
        }
    }

    void SetScale(int val)
    {
        _likertMax = val;
        ApplyLikertScaleConfiguration();
        UpdateBaselineDataPaths();
        if (_randomAllocation)
            ClearBaselineDataPaths();
    }

    void SetRounds(int samplingVal) { _samplingRounds = samplingVal; }

    private void ConfigureRound10Mode()
    {
        boManager.numSamplingIterations = 0;
        boManager.numOptimizationIterations = Mathf.Max(0, optimizationRoundsFor10);
        boManager.enableFinalDesignRound = true;
        boManager.warmStart = true;
        boManager.useInitialDataAsPrior = true;
        _warmStart = true;
        SetWarmStartToggleSilently(true);
        UpdateBaselineDataPaths();
        SyncConditionManagerFinalDesignRound();
    }

    private void ConfigureRound15Mode()
    {
        boManager.numSamplingIterations = 5;
        boManager.numOptimizationIterations = Mathf.Max(0, optimizationRoundsFor15);
        boManager.enableFinalDesignRound = true;
        boManager.warmStart = false;
        boManager.useInitialDataAsPrior = true;
        _warmStart = false;
        SetWarmStartToggleSilently(false);
        UpdateBaselineDataPaths();
        SyncConditionManagerFinalDesignRound();
    }

    private void ConfigureRandomMode()
    {
        boManager.numSamplingIterations = 5;
        boManager.numOptimizationIterations = 0;
        boManager.enableFinalDesignRound = false;
        boManager.warmStart = false;
        boManager.useInitialDataAsPrior = false;
        _warmStart = false;
        SetWarmStartToggleSilently(false);
        ClearBaselineDataPaths();
        SyncConditionManagerFinalDesignRound();
    }

    private void SetWarmStartToggleSilently(bool isOn)
    {
        if (warmStartToggle == null)
            return;

        warmStartToggle.SetIsOnWithoutNotify(isOn);
    }

    void HighlightScale(Button selected)
    {
        SetButtonColor(scale5Btn,   _defaultColor);
        SetButtonColor(scale20Btn,  _defaultColor);
        SetButtonColor(scale100Btn, _defaultColor);
        SetButtonColor(selected,    _selectedColor);
    }

    void HighlightRounds(Button selected)
    {
        SetButtonColor(rounds10Btn, _defaultColor);
        SetButtonColor(rounds15Btn, _defaultColor);
        SetButtonColor(selected,    _selectedColor);
    }

    void SetButtonColor(Button btn, Color color)
    {
        var colors = btn.colors;
        colors.normalColor   = color;
        colors.selectedColor = color;
        btn.colors = colors;
    }

    void OnStartClicked()
    {
        _experimentStarted = true;
        if (!ShouldUseRandomCondition)
            EnableBoRuntimeComponentsForConfig();

        ApplyConfig();

        if (ShouldUseRandomCondition)
        {
            configPanel.SetActive(false);

            if (boManager.welcomePanel != null)
                boManager.welcomePanel.SetActive(false);

            if (boManager.nextButton != null)
                boManager.nextButton.SetActive(false);

            // 🟢 改用反射安全呼叫 StartConfiguredCondition() 方法
            var conditionManager = ResolveFittsLawConditionManager();
            if (conditionManager != null)
            {
                var method = conditionManager.GetType().GetMethod("StartConfiguredCondition");
                if (method != null)
                {
                    method.Invoke(conditionManager, null);
                }
                else
                {
                    Debug.LogWarning("[ExperimentConfig] 找不到 StartConfiguredCondition 方法。");
                }
            }
            return;
        }

        if (boManager.pythonStarter != null)
            boManager.pythonStarter.enabled = true;

        configPanel.SetActive(false);
        boManager.welcomePanel.SetActive(true);

        if (boManager.initialized)
            boManager.nextButton.SetActive(true);
    }

    // ─────────────────────────────────────────────
    // ApplyConfig
    // ─────────────────────────────────────────────
    void ApplyConfig()
    {
        if (!string.IsNullOrEmpty(_userId))
            boManager.userId = _userId;

        // 🟢 改用反射安全設定 ConditionMode 列舉
        var conditionManager = ResolveFittsLawConditionManager();
        if (conditionManager != null)
        {
            try
            {
                var userIdField = conditionManager.GetType().GetField("userId");
                if (userIdField != null && !string.IsNullOrEmpty(boManager.userId))
                {
                    userIdField.SetValue(conditionManager, boManager.userId);
                }

                var conditionIdField = conditionManager.GetType().GetField("conditionId");
                if (conditionIdField != null)
                {
                    conditionIdField.SetValue(conditionManager, _likertMax.ToString());
                }

                var groupIdField = conditionManager.GetType().GetField("groupId");
                if (groupIdField != null)
                {
                    groupIdField.SetValue(conditionManager, _randomAllocation ? "random" : _samplingRounds.ToString());
                }

                var setConditionIdFromModeField = conditionManager.GetType().GetField("setConditionIdFromMode");
                if (setConditionIdFromModeField != null)
                {
                    setConditionIdFromModeField.SetValue(conditionManager, false);
                }

                var readIterationsFromSourceField = conditionManager.GetType().GetField("readIterationsFromSource");
                if (readIterationsFromSourceField != null)
                {
                    readIterationsFromSourceField.SetValue(conditionManager, true);
                }

                var method = conditionManager.GetType().GetMethod("SetConditionMode");
                if (method != null)
                {
                    // 動態獲取內部的 ConditionMode 列舉類型
                    var enumType = conditionManager.GetType().GetNestedType("ConditionMode");
                    if (enumType != null)
                    {
                        var enumValue = System.Enum.Parse(enumType, ShouldUseRandomCondition ? "Random" : "AdaptiveBo");
                        method.Invoke(conditionManager, new object[] { enumValue });
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ExperimentConfig] 動態設定 ConditionMode 失敗: {ex.Message}");
            }
        }

        // 自動對照組 Condition ID 編碼
        boManager.conditionId = _likertMax.ToString();

        // Group ID follows sampling rounds unless random allocation is enabled.
        boManager.groupId = _randomAllocation ? "random" : _samplingRounds.ToString();

        UnityEngine.Debug.Log(
            $"[ApplyConfig] UserID={_userId}, ConditionID={boManager.conditionId}, GroupID={boManager.groupId}, RandomAllocation={_randomAllocation}"
        );

        ApplyLikertScaleConfiguration();

        // ==========================================================
        // 🟢 【雙勾勾動態連動】控制 FittsLawTask 內部隨機開關與隨機種子固定開關
        // ==========================================================
        MonoBehaviour[] allComponents = FindObjectsOfType<MonoBehaviour>();
        foreach (var comp in allComponents)
        {
            if (comp != null && comp.GetType().Name == "FittsLawTask")
            {
                try
                {
                    // 1. 控制隨機抽樣主開關 (randomizeDesignParametersOnBegin)
                    var fieldRandomize = comp.GetType().GetField("randomizeDesignParametersOnBegin", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (fieldRandomize != null)
                    {
                        fieldRandomize.SetValue(comp, _randomAllocation);
                        Debug.Log($"[ExperimentConfig 連動] 成功將 FittsLawTask 內部的 randomizeDesignParametersOnBegin 設為: {_randomAllocation}");
                    }

                    // 2. 控制確定性隨機種子開關 (useDeterministicRandomDesignSeed)
                    var fieldDeterministic = comp.GetType().GetField("useDeterministicRandomDesignSeed", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (fieldDeterministic != null)
                    {
                        fieldDeterministic.SetValue(comp, _randomAllocation);
                        Debug.Log($"[ExperimentConfig 連動] 成功將 FittsLawTask 內部的 useDeterministicRandomDesignSeed 設為: {_randomAllocation}");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ExperimentConfig] 連動 FittsLawTask 隨機雙勾勾時發生例外：{ex.Message}");
                }
                break; 
            }
        }

        // =========================================
        // RANDOM ALLOCATION & ITERATIONS
        // =========================================
        if (_randomAllocation)
        {
            ConfigureRandomMode();
        }
        else if (_samplingRounds == 15)
        {
            ConfigureRound15Mode();
        }
        else
        {
            // Round10 mode: 0 sampling + 5 optimization + final design
            ConfigureRound10Mode();
        }

        boManager.questionnaireScaleForCsv = _likertMax.ToString();
        boManager.questionnaireSamplingRoundsForCsv = boManager.groupId;
        boManager.questionnaireRandomForCsv = _randomAllocation;
        boManager.questionnaireOptimisedForCsv = _optimized;

        boManager.totalIterations = boManager.numSamplingIterations + boManager.numOptimizationIterations;
    }

    private bool ShouldUseRandomCondition => _randomAllocation;

    private void StartBaselinePhase()
    {
        _baselinePhaseStarted = true;
        _baselinePhaseCompleted = false;
        _baselineScaleIndex = 0;
        _baselineScaleOrder = CreateShuffledBaselineScaleOrder();

        userIdPanel.SetActive(false);
        configPanel.SetActive(false);
        if (boManager.welcomePanel != null)
            boManager.welcomePanel.SetActive(false);
        if (boManager.nextButton != null)
            boManager.nextButton.SetActive(false);
        if (boManager.pythonStarter != null)
            boManager.pythonStarter.enabled = false;

        Debug.Log("[ExperimentConfig] Starting baseline phase with scale order: " + FormatScaleOrder(_baselineScaleOrder));
        StartNextBaselineBlock();
    }

    private void StartNextBaselineBlock()
    {
        if (_baselineScaleOrder == null || _baselineScaleOrder.Length == 0)
            _baselineScaleOrder = CreateShuffledBaselineScaleOrder();

        if (_baselineScaleIndex >= _baselineScaleOrder.Length)
        {
            CompleteBaselinePhase();
            return;
        }

        int scale = _baselineScaleOrder[_baselineScaleIndex];
        _likertMax = scale;
        _samplingRounds = BaselineRoundsPerScale;
        ApplyLikertScaleConfiguration();
        UpdateBaselineDataPaths();

        boManager.userId = _userId;
        boManager.conditionId = scale.ToString();
        boManager.groupId = "baseline";
        boManager.numSamplingIterations = BaselineRoundsPerScale;
        boManager.numOptimizationIterations = 0;
        boManager.totalIterations = BaselineRoundsPerScale;
        boManager.enableFinalDesignRound = false;
        boManager.warmStart = false;
        boManager.useInitialDataAsPrior = false;
        boManager.questionnaireScaleForCsv = scale.ToString();
        boManager.questionnaireSamplingRoundsForCsv = "baseline";
        boManager.questionnaireRandomForCsv = true;
        boManager.questionnaireOptimisedForCsv = false;

        var conditionManager = ResolveFittsLawConditionManager();
        if (conditionManager == null)
        {
            Debug.LogError("[ExperimentConfig] Cannot start baseline block because FittsLawConditionManager was not found.");
            return;
        }

        try
        {
            var configureMethod = conditionManager.GetType().GetMethod(
                "ConfigureBaselineBlock",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var startMethod = conditionManager.GetType().GetMethod(
                "StartConfiguredCondition",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (configureMethod == null || startMethod == null)
            {
                Debug.LogError("[ExperimentConfig] FittsLawConditionManager is missing baseline phase methods.");
                return;
            }

            configureMethod.Invoke(conditionManager, new object[] { _userId, scale, BaselineRoundsPerScale });
            startMethod.Invoke(conditionManager, null);
            Debug.Log($"[ExperimentConfig] Baseline block started: UserID={_userId}, Scale={scale}, Rounds={BaselineRoundsPerScale}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ExperimentConfig] Failed to start baseline block: {ex.Message}");
        }
    }

    public void OnBaselineBlockCompleted(int completedScale)
    {
        if (!_baselinePhaseStarted || _baselinePhaseCompleted)
            return;

        if (_baselineScaleOrder != null &&
            _baselineScaleIndex < _baselineScaleOrder.Length &&
            _baselineScaleOrder[_baselineScaleIndex] != completedScale)
        {
            Debug.LogWarning(
                $"[ExperimentConfig] Baseline completion scale mismatch. Expected {_baselineScaleOrder[_baselineScaleIndex]}, got {completedScale}."
            );
        }

        _baselineScaleIndex++;
        StartNextBaselineBlock();
    }

    private void CompleteBaselinePhase()
    {
        _baselinePhaseStarted = false;
        _baselinePhaseCompleted = true;
        _baselineScaleIndex = 0;

        Debug.Log("[ExperimentConfig] Baseline phase completed. Showing Phase 2 config panel.");
        ShowPhase2ConfigPanel();
    }

    private void ShowPhase2ConfigPanel()
    {
        EnableBoRuntimeComponentsForConfig();

        _likertMax = 5;
        _samplingRounds = 10;
        _randomAllocation = false;
        _warmStart = true;
        _optimized = optimizedToggle != null && optimizedToggle.isOn;

        if (randomAllocationToggle != null)
            randomAllocationToggle.SetIsOnWithoutNotify(false);

        if (rounds10Btn != null)
            rounds10Btn.interactable = true;
        if (rounds15Btn != null)
            rounds15Btn.interactable = true;

        HighlightScale(scale5Btn);
        HighlightRounds(rounds10Btn);
        SetConditionManagerMode("AdaptiveBo");
        ConfigureRound10Mode();
        ApplyLikertScaleConfiguration();

        userIdPanel.SetActive(false);
        configPanel.SetActive(true);
        if (boManager.welcomePanel != null)
            boManager.welcomePanel.SetActive(false);
        if (boManager.nextButton != null)
            boManager.nextButton.SetActive(false);
        if (boManager.pythonStarter != null)
            boManager.pythonStarter.enabled = false;
    }

    private static int[] CreateShuffledBaselineScaleOrder()
    {
        int[] order = (int[])BaselineScales.Clone();
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int tmp = order[i];
            order[i] = order[j];
            order[j] = tmp;
        }

        return order;
    }

    private static string FormatScaleOrder(int[] order)
    {
        if (order == null || order.Length == 0)
            return "";

        string[] values = new string[order.Length];
        for (int i = 0; i < order.Length; i++)
            values[i] = order[i].ToString();

        return string.Join(", ", values);
    }

    private void ApplyLikertScaleConfiguration()
    {
        if (boManager == null)
            return;

        Slider[] allSliders = Resources.FindObjectsOfTypeAll<Slider>();
        foreach (var s in allSliders)
        {
            if (s.gameObject.name == "SliderBar" || s.gameObject.activeInHierarchy)
            {
                s.minValue = 1;
                s.maxValue = _likertMax;
                s.wholeNumbers = true;

                TextMeshProUGUI[] textComponents = s.gameObject.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var txt in textComponents)
                {
                    if (txt != null && (txt.gameObject.name.Equals("score", System.StringComparison.OrdinalIgnoreCase) ||
                                        txt.gameObject.name.Contains("Score")))
                    {
                        txt.text = "score";
                    }
                }

                Text[] oldTexts = s.gameObject.GetComponentsInChildren<Text>(true);
                foreach (var txt in oldTexts)
                {
                    if (txt != null && (txt.gameObject.name.Equals("score", System.StringComparison.OrdinalIgnoreCase) ||
                                        txt.gameObject.name.Contains("Score")))
                    {
                        txt.text = "score";
                    }
                }

                var containerCanvas = s.GetComponentInParent<Canvas>();
                if (containerCanvas != null)
                    Canvas.ForceUpdateCanvases();
            }
        }

        foreach (var obj in boManager.objectives)
        {
            if (obj == null || obj.value == null)
                continue;

            if (obj.key == "aesthetics" || obj.key == "usability")
            {
                obj.value.lowerBound = 1;
                obj.value.upperBound = _likertMax;
            }
        }
    }

    private void EnableBoRuntimeComponentsForConfig()
    {
        if (boManager == null)
            return;

        if (boManager.gameObject != null && !boManager.gameObject.activeSelf)
            boManager.gameObject.SetActive(true);

        boManager.enabled = true;

        Optimizer optimizer = boManager.GetComponent<Optimizer>();
        if (optimizer != null)
            optimizer.enabled = true;

        SocketNetwork socketNetwork = boManager.GetComponent<SocketNetwork>();
        if (socketNetwork != null)
            socketNetwork.enabled = true;

        MainThreadDispatcher dispatcher = boManager.GetComponent<MainThreadDispatcher>();
        if (dispatcher != null)
            dispatcher.enabled = true;

        PythonStarter pythonStarter = boManager.GetComponent<PythonStarter>();
        if (pythonStarter != null)
            pythonStarter.enabled = false;
    }

    /// <summary>
    /// Sets boManager.initialParametersDataPath and initialObjectivesDataPath
    /// based on the current _userId and _likertMax.
    /// Called whenever scale is changed or user ID is confirmed.
    /// </summary>
    private void UpdateBaselineDataPaths()
    {
        if (string.IsNullOrEmpty(_userId)) return;

        string userFolder = LogDataFolderUtility.NormalizeLogFolderToken(_userId);
        boManager.initialParametersDataPath = $"{userFolder}/baseline_{_likertMax}_params.csv";
        boManager.initialObjectivesDataPath = $"{userFolder}/baseline_{_likertMax}_objectives.csv";

        Debug.Log($"[ExperimentConfig] Baseline paths updated: {boManager.initialParametersDataPath}, {boManager.initialObjectivesDataPath}");
    }

    private void ClearBaselineDataPaths()
    {
        boManager.initialParametersDataPath = string.Empty;
        boManager.initialObjectivesDataPath = string.Empty;
        Debug.Log("[ExperimentConfig] Baseline paths cleared for Random mode.");
    }

    private void SetConditionManagerMode(string modeName)
    {
        var conditionManager = ResolveFittsLawConditionManager();
        if (conditionManager == null)
            return;

        try
        {
            var enumType = conditionManager.GetType().GetNestedType("ConditionMode");
            var method = conditionManager.GetType().GetMethod("SetConditionMode");
            if (enumType == null || method == null)
                return;

            var enumValue = System.Enum.Parse(enumType, modeName);
            method.Invoke(conditionManager, new object[] { enumValue });
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ExperimentConfig] 設定 ConditionMode={modeName} 失敗: {ex.Message}");
        }
    }

    private void SyncConditionManagerFinalDesignRound()
    {
        var conditionManager = ResolveFittsLawConditionManager();
        if (conditionManager == null)
            return;

        var includeFinalField = conditionManager.GetType().GetField("includeFinalDesignRound");
        if (includeFinalField != null)
            includeFinalField.SetValue(conditionManager, boManager.enableFinalDesignRound);
    }

    private MonoBehaviour ResolveFittsLawConditionManager()
    {
        if (_fittsLawConditionManager == null)
        {
            MonoBehaviour[] allComponents = FindObjectsOfType<MonoBehaviour>();
            foreach (var comp in allComponents)
            {
                if (comp != null && comp.GetType().Name == "FittsLawConditionManager")
                {
                    _fittsLawConditionManager = comp;
                    break;
                }
            }
        }

        return _fittsLawConditionManager;
    }
}
