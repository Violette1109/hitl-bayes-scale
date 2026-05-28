using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BOforUnity;
using BOforUnity.Examples;

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

    private FittsLawConditionManager _fittsLawConditionManager;

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
        });
        
        rounds15Btn.onClick.AddListener(() => {
            if (_randomAllocation) return;
            SetRounds(15); 
            HighlightRounds(rounds15Btn); 
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

        userIdPanel.SetActive(false);
        configPanel.SetActive(true);
    }

    void OnRandomAllocationChanged(bool isOn)
    {
        _randomAllocation = isOn;
        if (isOn)
        {
            _samplingRounds = 15;
            HighlightRounds(rounds15Btn);
            rounds10Btn.interactable = false;
            rounds15Btn.interactable = false;
        }
        else
        {
            rounds10Btn.interactable = true;
            rounds15Btn.interactable = true;
            _samplingRounds = 10;
            HighlightRounds(rounds10Btn);
        }
    }

    void SetScale(int val)          { _likertMax      = val; }
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

        if (ShouldUseRandomCondition())
        {
            configPanel.SetActive(false);

            if (boManager.welcomePanel != null)
                boManager.welcomePanel.SetActive(false);

            if (boManager.nextButton != null)
                boManager.nextButton.SetActive(false);

            ResolveFittsLawConditionManager()?.StartConfiguredCondition();
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

        FittsLawConditionManager conditionManager = ResolveFittsLawConditionManager();
        if (conditionManager != null)
        {
            conditionManager.SetConditionMode(
                ShouldUseRandomCondition()
                    ? FittsLawConditionManager.ConditionMode.Random
                    : FittsLawConditionManager.ConditionMode.AdaptiveBo
            );
        }

        // 自動對照組 Condition ID 編碼
        if (_likertMax == 5) boManager.conditionId = "5";
        else if (_likertMax == 20) boManager.conditionId = "20";
        else if (_likertMax == 100) boManager.conditionId = "100";

        // 自動對照組 Group ID 編碼
        if (_samplingRounds == 10) boManager.groupId = "10";
        else if (_samplingRounds == 15) boManager.groupId = "15";

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
                    // 這裡用 System.StringComparison.OrdinalIgnoreCase 做到大小寫防呆
                    // 只有當這個 Text 物件的名字叫 "score" 或 "scoretext" 時才蓋台！
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
            boManager.numSamplingIterations = 15;
            boManager.numOptimizationIterations = 0;
            boManager.enableFinalDesignRound = false;
        }
        else
        {
            boManager.numSamplingIterations = _samplingRounds;
            boManager.numOptimizationIterations = (_samplingRounds == 15) ? optimizationRoundsFor15 : optimizationRoundsFor10;
            boManager.enableFinalDesignRound = true;
        }

        boManager.warmStart = _warmStart;
        boManager.questionnaireScaleForCsv = _likertMax.ToString();
        boManager.questionnaireSamplingRoundsForCsv = _samplingRounds.ToString();
        boManager.questionnaireRandomForCsv = _randomAllocation;
        boManager.questionnaireOptimisedForCsv = _optimized;

        boManager.totalIterations = _warmStart
            ? boManager.numOptimizationIterations
            : boManager.numSamplingIterations + boManager.numOptimizationIterations;

        if (_warmStart)
        {
            boManager.initialParametersDataPath = "warmstart_params.csv";
            boManager.initialObjectivesDataPath = "warmstart_objectives.csv";
        }
    }

    bool ShouldUseRandomCondition()
    {
        return _randomAllocation;
    }

    FittsLawConditionManager ResolveFittsLawConditionManager()
    {
        if (_fittsLawConditionManager == null)
            _fittsLawConditionManager = FindObjectOfType<FittsLawConditionManager>();

        return _fittsLawConditionManager;
    }
}