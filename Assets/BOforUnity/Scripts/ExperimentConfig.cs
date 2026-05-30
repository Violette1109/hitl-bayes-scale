using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BOforUnity;

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

    private readonly Color _selectedColor = new Color(0.498f, 0.467f, 0.867f);
    private readonly Color _defaultColor  = new Color(0.9f, 0.9f, 0.9f);

    private static int    _likertMax         = 5;
    private static int    _samplingRounds    = 10;
    private static bool   _warmStart         = false;
    private static bool   _randomAllocation  = false;
    private static bool   _optimized         = false;
    private static bool   _experimentStarted = false;
    private static string _userId            = ""; 

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
    }

    // ─────────────────────────────────────────────
    // Awake
    // ─────────────────────────────────────────────
    void Awake()
    {
        if (!_experimentStarted)
        {
            userIdPanel.SetActive(true);
            configPanel.SetActive(false);
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
            boManager.numSamplingIterations = 0;
            boManager.numOptimizationIterations = 5;
            boManager.enableFinalDesignRound = true;
            UpdateBaselineDataPaths();
        });
        
        rounds15Btn.onClick.AddListener(() => {
            if (_randomAllocation) return;
            SetRounds(15); 
            HighlightRounds(rounds15Btn);
            boManager.numSamplingIterations = 5;
            boManager.numOptimizationIterations = 0;
            boManager.enableFinalDesignRound = true;
            UpdateBaselineDataPaths();
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
        HighlightRounds(rounds10Btn);
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

        userIdPanel.SetActive(false);
        configPanel.SetActive(true);
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

            boManager.numSamplingIterations = 5;
            boManager.numOptimizationIterations = 0;
            boManager.enableFinalDesignRound = false;

            // Do NOT load baseline data for random mode
            boManager.initialParametersDataPath = "";
            boManager.initialObjectivesDataPath = "";

            // Set conditionMode = Random on FittsLawConditionManager via reflection
            var conditionManager = ResolveFittsLawConditionManager();
            if (conditionManager != null)
            {
                try
                {
                    var enumType = conditionManager.GetType().GetNestedType("ConditionMode");
                    if (enumType != null)
                    {
                        var method = conditionManager.GetType().GetMethod("SetConditionMode");
                        if (method != null)
                        {
                            var enumValue = System.Enum.Parse(enumType, "Random");
                            method.Invoke(conditionManager, new object[] { enumValue });
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ExperimentConfig] 設定 Random ConditionMode 失敗: {ex.Message}");
                }
            }
        }
        else
        {
            rounds10Btn.interactable = true;
            rounds15Btn.interactable = true;
            _samplingRounds = 10;
            HighlightRounds(rounds10Btn);

            // Restore default for Round10 mode
            boManager.numSamplingIterations = 0;
            boManager.numOptimizationIterations = 5;
            boManager.enableFinalDesignRound = true;
            UpdateBaselineDataPaths();
        }
    }

    void SetScale(int val)
    {
        _likertMax = val;
        UpdateBaselineDataPaths();
    }

    void SetRounds(int samplingVal) { _samplingRounds = samplingVal; }

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

        // ─────────────────────────────────────────────
        // 動態調整問卷 Slider 上限 ＆ 精準鎖定分數 Placeholder
        // ─────────────────────────────────────────────
        int updatedSlidersCount = 0;
        Slider[] allSliders = Resources.FindObjectsOfTypeAll<Slider>();
        foreach (var s in allSliders)
        {
            if (s.gameObject.name == "SliderBar" || s.gameObject.activeInHierarchy)
            {
                // 1. 設定滑桿基本邊界
                s.minValue = 1;
                s.maxValue = _likertMax;
                s.wholeNumbers = true;

                // 🟢 2. 【精準狙擊】只去尋找名字叫 "score" 的那個中央數值顯示組件
                TextMeshProUGUI[] textComponents = s.gameObject.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var txt in textComponents)
                {
                    if (txt != null && (txt.gameObject.name.Equals("score", System.StringComparison.OrdinalIgnoreCase) || 
                                        txt.gameObject.name.Contains("Score")))
                    {
                        txt.text = "score";
                    }
                }

                // 備用防禦：舊版 UI Text 相同邏輯精準鎖定
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
                {
                    Canvas.ForceUpdateCanvases();
                }
                
                updatedSlidersCount++;
            }
        }

        // 遍歷 Objectives，同步改寫問卷後台數據邊界
        foreach (var obj in boManager.objectives)
        {
            if (obj.key == "aesthetics" || obj.key == "usability")
            {
                obj.value.lowerBound = 1;         
                obj.value.upperBound = _likertMax;  
            }
        }

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
            boManager.numSamplingIterations = 5;
            boManager.numOptimizationIterations = 0;
            boManager.enableFinalDesignRound = false;
            // Do NOT load baseline data for random mode
            boManager.initialParametersDataPath = "";
            boManager.initialObjectivesDataPath = "";
        }
        else if (_samplingRounds == 15)
        {
            boManager.numSamplingIterations = 5;
            boManager.numOptimizationIterations = 0;
            boManager.enableFinalDesignRound = true;
            UpdateBaselineDataPaths();
        }
        else
        {
            // Round10 mode: 0 sampling + 5 optimization + final design
            boManager.numSamplingIterations = 0;
            boManager.numOptimizationIterations = 5;
            boManager.enableFinalDesignRound = true;
            UpdateBaselineDataPaths();
        }

        boManager.warmStart = _warmStart;
        boManager.questionnaireScaleForCsv = _likertMax.ToString();
        boManager.questionnaireSamplingRoundsForCsv = boManager.groupId;
        boManager.questionnaireRandomForCsv = _randomAllocation;
        boManager.questionnaireOptimisedForCsv = _optimized;

        boManager.totalIterations = _warmStart
            ? boManager.numOptimizationIterations
            : boManager.numSamplingIterations + boManager.numOptimizationIterations;

        if (boManager.enableFinalDesignRound)
            boManager.totalIterations += 1;

        if (_warmStart)
        {
            boManager.initialParametersDataPath = "warmstart_params.csv";
            boManager.initialObjectivesDataPath = "warmstart_objectives.csv";
        }
    }

    private bool ShouldUseRandomCondition => _randomAllocation;

    /// <summary>
    /// Sets boManager.initialParametersDataPath and initialObjectivesDataPath
    /// based on the current _userId and _likertMax.
    /// Called whenever scale is changed or user ID is confirmed.
    /// </summary>
    private void UpdateBaselineDataPaths()
    {
        if (string.IsNullOrEmpty(_userId)) return;

        boManager.initialParametersDataPath = $"{_userId}/baseline_{_likertMax}_params.csv";
        boManager.initialObjectivesDataPath = $"{_userId}/baseline_{_likertMax}_objectives.csv";

        Debug.Log($"[ExperimentConfig] Baseline paths updated: {boManager.initialParametersDataPath}, {boManager.initialObjectivesDataPath}");
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