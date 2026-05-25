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
    public Button startBtn;

    // ─────────────────────────────────────────────
    // Data-Driven References
    // ─────────────────────────────────────────────
    [Header("Dynamic References (Data-Driven)")]
    [Tooltip("直接拖入受試者評分用的 Slider，不再用名字字串去撈")]
    public Slider evaluationSlider;

    [Tooltip("請輸入妳在 BoManager 裡設定的問卷 Objective Key (例: mental_demand)")]
    public string targetObjectiveKey = "mental_demand";

    [Tooltip("當選擇 10 輪 Sampling 時，對應的 Optimization 輪數")]
    public int optimizationRoundsFor10 = 5;

    [Tooltip("當選擇 15 輪 Sampling 時，對應的 Optimization 輪數")]
    public int optimizationRoundsFor15 = 0;

    [Header("Manager References")]
    public BoForUnityManager boManager;

    private readonly Color _selectedColor = new Color(0.498f, 0.467f, 0.867f);
    private readonly Color _defaultColor  = new Color(0.9f, 0.9f, 0.9f);

    private static int    _likertMax         = 5;
    private static int    _samplingRounds    = 10;
    private static bool   _warmStart         = false;
    private static bool   _randomAllocation  = false;
    private static bool   _experimentStarted = false;
    private static string _userId            = ""; // 直接儲存原始輸入

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _likertMax         = 5;
        _samplingRounds    = 10;
        _warmStart         = false;
        _randomAllocation  = false;
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
            // 先顯示 User ID Panel，其餘全隱藏
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

        // User ID panel 監聽
        userIdContinueBtn.onClick.AddListener(OnUserIdContinueClicked);

        // 若已有值（場景重載時）先填回去
        if (!string.IsNullOrEmpty(_userId) && userIdInputField != null)
            userIdInputField.text = _userId;

        // Config panel buttons 監聽
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
        startBtn.onClick.AddListener(OnStartClicked);

        _warmStart = warmStartToggle.isOn;
        _randomAllocation = randomAllocationToggle.isOn;

        HighlightScale(scale5Btn);
        HighlightRounds(rounds10Btn);
    }

    // ─────────────────────────────────────────────
    // User ID Continue 處理（直接讀取字串）
    // ─────────────────────────────────────────────
    void OnUserIdContinueClicked()
    {
        string trimmed = userIdInputField != null ? userIdInputField.text.Trim() : "";

        if (string.IsNullOrEmpty(trimmed))
        {
            Debug.LogWarning("[ExperimentConfig] User ID 不能空白，請輸入後再繼續。");
            return;
        }

        // 🟢 直接使用受試者輸入的原始字串，不再進行任何加密處理
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
    // ─────────────────────────────────────────────
    // ApplyConfig (完全體：同時支援多個問卷 Slider 與多個 Objective 邊界連動)
    // ─────────────────────────────────────────────
    void ApplyConfig()
    {
        if (!string.IsNullOrEmpty(_userId))
            boManager.userId = _userId;

        // 自動對照組 Condition ID 編碼 (1-5->1, 1-20->2, 1-100->3)
        if (_likertMax == 5) boManager.conditionId = "1";
        else if (_likertMax == 20) boManager.conditionId = "2";
        else if (_likertMax == 100) boManager.conditionId = "3";

        // 自動對照組 Group ID 編碼 (10 輪->1, 15 輪->2)
        if (_samplingRounds == 10) boManager.groupId = "1";
        else if (_samplingRounds == 15) boManager.groupId = "2";

        UnityEngine.Debug.Log(
            $"[ApplyConfig] UserID={_userId}, ConditionID={boManager.conditionId}, GroupID={boManager.groupId}, RandomAllocation={_randomAllocation}"
        );

        // 🟢 【關鍵修改 1】動態撈取問卷中「所有」名為 "SliderBar" 或正在顯示的 Slider 元件
        // 這樣不論畫面是有 3 個還是 5 個 Slider，全部都會集體被切換刻度！
        int updatedSlidersCount = 0;
        Slider[] allSliders = Resources.FindObjectsOfTypeAll<Slider>();
        
        foreach (var s in allSliders)
        {
            // 檢查是否為問卷中的滑桿（通常名字包含 SliderBar 或 Slider）
            if (s.gameObject.name == "SliderBar" || s.gameObject.activeInHierarchy)
            {
                s.minValue = 1;
                s.maxValue = _likertMax;
                s.wholeNumbers = true;
                s.value = (_likertMax + 1) / 2; // 預設拉到正中間
                updatedSlidersCount++;
            }
        }
        
        Debug.Log($"[ExperimentConfig] 已成功動態調整畫面上 {updatedSlidersCount} 個問卷 Slider 的最大值為: {_likertMax}");

        // 🟢 【關鍵修改 2】遍歷 Objectives，同步改寫 aesthetics 與 usability 的後台數據邊界
        int updatedObjectivesCount = 0;
        foreach (var obj in boManager.objectives)
        {
            if (obj.key == "aesthetics" || obj.key == "usability")
            {
                obj.value.lowerBound = 1;         
                obj.value.upperBound = _likertMax;  
                updatedObjectivesCount++;
                
                Debug.Log($"[ExperimentConfig] 已動態更新 Objective '{obj.key}' 的 UpperBound 為: {_likertMax}");
            }
        }

        if (updatedObjectivesCount < 2)
        {
            Debug.LogWarning($"[ExperimentConfig] 警告：預期更新 2 個問卷目標，但實際上只成功更新了 {updatedObjectivesCount} 個。請檢查 BoManager 裡的 Key 名稱是否完全對齊 'aesthetics' 與 'usability'。");
        }

        // =========================================
        // RANDOM ALLOCATION & ITERATIONS (Pascal v1.4.2 邏輯)
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
        boManager.questionnaireOptimisedForCsv = !_randomAllocation;

        boManager.totalIterations = _warmStart
            ? boManager.numOptimizationIterations
            : boManager.numSamplingIterations + boManager.numOptimizationIterations;

        if (_warmStart)
        {
            boManager.initialParametersDataPath = "warmstart_params.csv";
            boManager.initialObjectivesDataPath = "warmstart_objectives.csv";
        }
    }
}