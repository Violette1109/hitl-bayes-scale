using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BOforUnity.Scripts;
using QuestionnaireToolkit.Scripts;
using UnityEngine;

namespace BOforUnity.Examples
{
    public class FittsLawConditionManager : MonoBehaviour, IQuestionnaireOptimizationBridge
    {
        public enum ConditionMode
        {
            [InspectorName("HITL MOBO")]
            AdaptiveBo,
            Static,
            Random
        }

        [Header("References")]
        public FittsLawTask fittsLawTask;
        public QTQuestionnaireManager questionnaireManager;
        public BoForUnityManager iterationSettingsSource;

        [Header("Condition")]
        public ConditionMode conditionMode = ConditionMode.AdaptiveBo;
        public bool setConditionIdFromMode = true;
        public bool startOnAwake = true;
        public string userId = "-1";
        public string conditionId = "HITL MOBO";
        public string groupId = "-1";

        [Header("Iterations")]
        public bool readIterationsFromSource = true;
        [Min(0)] public int samplingIterations = 3;
        [Min(0)] public int optimizationIterations = 2;
        public bool includeFinalDesignRound = true;
        [Min(0f)] public float nextRoundDelaySeconds = 0.25f;

        [Header("Baseline CSV Capture")]
        public bool captureBaselineCsv = false;
        public string baselineCsvUserId = "-1";
        [Min(1)] public int baselineCsvScale = 5;
        public string baselineCsvGroupId = "baseline";

        private readonly Dictionary<string, List<float>> _currentObjectiveValues =
            new Dictionary<string, List<float>>(StringComparer.OrdinalIgnoreCase);

        private int _currentRound;
        private int _baseRoundCount;
        private int _totalRoundCount;
        private bool _started;
        private bool _advanceQueued;
        private bool _runtimeUserFolderReserved;
        private bool _baselineBlockActive;
        private bool _baselineBlockCompletionNotified;
        private int _lastBaselineCsvRoundWritten;

        public bool UsesExternalIterationSignal
        {
            get
            {
                BoForUnityManager source = ResolveIterationSettingsSource();
                return conditionMode == ConditionMode.AdaptiveBo &&
                       source != null &&
                       source.UsesExternalIterationSignal;
            }
        }

        public bool EnablePriorRatingHints
        {
            get
            {
                BoForUnityManager source = ResolveIterationSettingsSource();
                return conditionMode == ConditionMode.AdaptiveBo &&
                       source != null &&
                       source.EnablePriorRatingHints;
            }
        }

        public float PriorRatingHintAlpha
        {
            get
            {
                BoForUnityManager source = ResolveIterationSettingsSource();
                return conditionMode == ConditionMode.AdaptiveBo && source != null
                    ? source.PriorRatingHintAlpha
                    : 0f;
            }
        }

        public string UserId
        {
            get
            {
                BoForUnityManager source = ResolveIterationSettingsSource();
                return conditionMode == ConditionMode.AdaptiveBo && source != null
                    ? source.UserId
                    : ResolveContextValue(userId);
            }
        }

        public string ConditionId
        {
            get
            {
                BoForUnityManager source = ResolveIterationSettingsSource();
                return conditionMode == ConditionMode.AdaptiveBo && source != null
                    ? source.ConditionId
                    : ResolveContextValue(GetConfiguredConditionId());
            }
        }

        public string GroupId
        {
            get
            {
                BoForUnityManager source = ResolveIterationSettingsSource();
                return conditionMode == ConditionMode.AdaptiveBo && source != null
                    ? source.GroupId
                    : ResolveContextValue(groupId);
            }
        }

        public string ScaleForQuestionnaireCsv =>
            conditionMode == ConditionMode.AdaptiveBo && iterationSettingsSource != null
                ? iterationSettingsSource.ScaleForQuestionnaireCsv
                : ResolveContextValue(GetConfiguredConditionId());

        public string SamplingRoundsForQuestionnaireCsv =>
            conditionMode == ConditionMode.AdaptiveBo && iterationSettingsSource != null
                ? iterationSettingsSource.SamplingRoundsForQuestionnaireCsv
                : ResolveContextValue(groupId);

        public bool WarmStartForQuestionnaireCsv =>
            conditionMode == ConditionMode.AdaptiveBo &&
            iterationSettingsSource != null &&
            iterationSettingsSource.WarmStartForQuestionnaireCsv;

        public bool RandomForQuestionnaireCsv =>
            iterationSettingsSource != null
                ? iterationSettingsSource.RandomForQuestionnaireCsv
                : conditionMode == ConditionMode.Random;

        public bool OptimisedForQuestionnaireCsv =>
            iterationSettingsSource != null
                ? iterationSettingsSource.OptimisedForQuestionnaireCsv
                : conditionMode == ConditionMode.AdaptiveBo;

        private void Awake()
        {
            ResolveReferences();
            ApplyConditionConfiguration();
        }

        private IEnumerator Start()
        {
            if (!startOnAwake || conditionMode == ConditionMode.AdaptiveBo)
                yield break;

            yield return null;
            if (nextRoundDelaySeconds > 0f)
                yield return new WaitForSecondsRealtime(nextRoundDelaySeconds);

            BeginNextRound();
        }

        private void OnValidate()
        {
            samplingIterations = Mathf.Max(0, samplingIterations);
            optimizationIterations = Mathf.Max(0, optimizationIterations);
            nextRoundDelaySeconds = Mathf.Max(0f, nextRoundDelaySeconds);

            if (setConditionIdFromMode || string.IsNullOrWhiteSpace(conditionId))
            {
                conditionId = GetDefaultConditionIdForMode();
            }
        }

        public void OptimizationStart()
        {
            if (conditionMode == ConditionMode.AdaptiveBo)
            {
                ResolveIterationSettingsSource()?.OptimizationStart();
                return;
            }

            if (captureBaselineCsv)
                AppendBaselineCsvRow();

            QueueNextRound();
        }

        public void SetConditionMode(ConditionMode mode)
        {
            if (conditionMode == mode)
                return;

            conditionMode = mode;
            if (mode != ConditionMode.Random)
            {
                captureBaselineCsv = false;
                _baselineBlockActive = false;
            }
            else if (!_baselineBlockActive)
            {
                readIterationsFromSource = true;
                ResetRoundProgress();
            }
            ResolveReferences();
            ApplyConditionConfiguration();
        }

        public void StartConfiguredCondition()
        {
            if (conditionMode == ConditionMode.AdaptiveBo || _started || _advanceQueued)
                return;
            
            // 🟢 核心修正：允許在正式啟動時重新鎖定正確的 User ID 資料夾
            _runtimeUserFolderReserved = false;
            if (!_started)
            {
                _currentRound = 0;
                _baseRoundCount = 0;
                _totalRoundCount = 0;
                _baselineBlockCompletionNotified = false;
                _lastBaselineCsvRoundWritten = 0;
                _currentObjectiveValues.Clear();
            }

            _advanceQueued = true;
            StartCoroutine(BeginNextRoundAfterDelay());
        }

        public void ConfigureBaselineBlock(string baselineUserId, int scale, int rounds)
        {
            ResolveReferences();

            captureBaselineCsv = true;
            _baselineBlockActive = true;
            _baselineBlockCompletionNotified = false;
            _lastBaselineCsvRoundWritten = 0;

            baselineCsvUserId = ResolveContextValue(baselineUserId);
            baselineCsvScale = Mathf.Max(1, scale);
            conditionMode = ConditionMode.Random;
            setConditionIdFromMode = false;
            userId = baselineCsvUserId;
            conditionId = baselineCsvScale.ToString(CultureInfo.InvariantCulture);
            groupId = ResolveContextValue(baselineCsvGroupId);
            readIterationsFromSource = false;
            samplingIterations = Mathf.Max(1, rounds);
            optimizationIterations = 0;
            includeFinalDesignRound = false;

            _currentRound = 0;
            _baseRoundCount = 0;
            _totalRoundCount = 0;
            _started = false;
            _advanceQueued = false;
            _runtimeUserFolderReserved = false;
            _currentObjectiveValues.Clear();

            BoForUnityManager source = ResolveIterationSettingsSource();
            if (source != null)
            {
                source.userId = userId;
                source.conditionId = conditionId;
                source.groupId = groupId;
                source.numSamplingIterations = samplingIterations;
                source.numOptimizationIterations = 0;
                source.totalIterations = samplingIterations;
                source.enableFinalDesignRound = false;
                source.warmStart = false;
                source.useInitialDataAsPrior = false;
                source.questionnaireScaleForCsv = conditionId;
                source.questionnaireSamplingRoundsForCsv = groupId;
                source.questionnaireRandomForCsv = true;
                source.questionnaireOptimisedForCsv = false;
            }

            ApplyConditionConfiguration();
            PrepareBaselineCsvFiles();
        }

        public void RequestNextIteration()
        {
            if (conditionMode == ConditionMode.AdaptiveBo)
            {
                ResolveIterationSettingsSource()?.RequestNextIteration();
                return;
            }

            QueueNextRound();
        }

        public void SubmitQuestionnaireObjectiveValue(string headerName, string rawValue, string sourceName)
        {
            if (conditionMode == ConditionMode.AdaptiveBo)
            {
                ResolveIterationSettingsSource()?.SubmitQuestionnaireObjectiveValue(headerName, rawValue, sourceName);
                return;
            }

            if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return;

            string objectiveKey = ResolveObjectiveKey(headerName);
            if (string.IsNullOrWhiteSpace(objectiveKey))
                return;

            if (!_currentObjectiveValues.TryGetValue(objectiveKey, out List<float> values))
            {
                values = new List<float>();
                _currentObjectiveValues[objectiveKey] = values;
            }

            values.Add(value);
        }

        public bool TryGetObjectiveAverage(string objectiveKey, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(objectiveKey) ||
                !_currentObjectiveValues.TryGetValue(objectiveKey.Trim(), out List<float> values) ||
                values == null ||
                values.Count == 0)
            {
                return false;
            }

            double sum = 0.0;
            int count = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (!IsFinite(values[i]))
                    continue;

                sum += values[i];
                count++;
            }

            if (count == 0)
                return false;

            value = (float)(sum / count);
            return true;
        }

        public void SetPriorSliderRatingHint(string questionKey, float sliderValue)
        {
            if (conditionMode == ConditionMode.AdaptiveBo)
                ResolveIterationSettingsSource()?.SetPriorSliderRatingHint(questionKey, sliderValue);
        }

        public bool TryGetPriorSliderRatingHint(string questionKey, out float sliderValue)
        {
            sliderValue = 0f;
            BoForUnityManager source = ResolveIterationSettingsSource();
            return conditionMode == ConditionMode.AdaptiveBo &&
                   source != null &&
                   source.TryGetPriorSliderRatingHint(questionKey, out sliderValue);
        }

        public void RemovePriorSliderRatingHint(string questionKey)
        {
            if (conditionMode == ConditionMode.AdaptiveBo)
                ResolveIterationSettingsSource()?.RemovePriorSliderRatingHint(questionKey);
        }

        public void SetPriorLinearScaleRatingHint(string questionKey, string answerValue)
        {
            if (conditionMode == ConditionMode.AdaptiveBo)
                ResolveIterationSettingsSource()?.SetPriorLinearScaleRatingHint(questionKey, answerValue);
        }

        public bool TryGetPriorLinearScaleRatingHint(string questionKey, out string answerValue)
        {
            answerValue = null;
            BoForUnityManager source = ResolveIterationSettingsSource();
            return conditionMode == ConditionMode.AdaptiveBo &&
                   source != null &&
                   source.TryGetPriorLinearScaleRatingHint(questionKey, out answerValue);
        }

        public void RemovePriorLinearScaleRatingHint(string questionKey)
        {
            if (conditionMode == ConditionMode.AdaptiveBo)
                ResolveIterationSettingsSource()?.RemovePriorLinearScaleRatingHint(questionKey);
        }

        private void ResolveReferences()
        {
            if (fittsLawTask == null)
                fittsLawTask = FindSceneObject<FittsLawTask>();

            if (questionnaireManager == null)
                questionnaireManager = FindSceneObject<QTQuestionnaireManager>();

            if (iterationSettingsSource == null)
                ResolveIterationSettingsSource();
        }

        private BoForUnityManager ResolveIterationSettingsSource()
        {
            if (conditionMode != ConditionMode.AdaptiveBo && iterationSettingsSource != null)
            {
                if (iterationSettingsSource.gameObject == null ||
                    !iterationSettingsSource.gameObject.activeInHierarchy)
                {
                    return iterationSettingsSource;
                }

                BoForUnityManager preferredSource = FindPreferredIterationSettingsSource();
                if (preferredSource != null &&
                    preferredSource != iterationSettingsSource &&
                    GetIterationSettingsSourceScore(preferredSource) >
                    GetIterationSettingsSourceScore(iterationSettingsSource))
                {
                    iterationSettingsSource = preferredSource;
                }

                return iterationSettingsSource;
            }

            BoForUnityManager preferred = FindPreferredIterationSettingsSource();
            if (preferred != null)
                iterationSettingsSource = preferred;

            return iterationSettingsSource;
        }

        private static BoForUnityManager FindPreferredIterationSettingsSource()
        {
            BoForUnityManager best = null;
            int bestScore = int.MinValue;
            foreach (BoForUnityManager candidate in Resources.FindObjectsOfTypeAll<BoForUnityManager>())
            {
                if (candidate == null ||
                    candidate.gameObject == null ||
                    !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                int score = GetIterationSettingsSourceScore(candidate);
                if (best == null || score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private static int GetIterationSettingsSourceScore(BoForUnityManager source)
        {
            int score = Mathf.Max(0, source.currentIteration);
            if (source.optimizationRunning)
                score += 1000;
            if (source.simulationRunning)
                score += 500;
            if (source.initialized)
                score += 250;
            if (source.hasNewDesignParameterValues)
                score += 100;
            if (source.gameObject.activeInHierarchy)
                score += 50;

            return score;
        }

        private void ApplyConditionConfiguration()
        {
            BoForUnityManager source = ResolveIterationSettingsSource();
            if (conditionMode == ConditionMode.AdaptiveBo)
            {
                if (fittsLawTask != null)
                {
                    fittsLawTask.SetRuntimeDesignParameterSource(source);
                    fittsLawTask.startOnAwake = true;
                    fittsLawTask.ensureBoManagerInScene = true;
                    fittsLawTask.waitForBoEvaluationStart = true;
                    fittsLawTask.startBoOptimizationAfterResults = true;
                    fittsLawTask.queueNextExternalSignalIteration = true;
                    fittsLawTask.readDesignParametersFromBo = true;
                    fittsLawTask.writeObjectivesToBo = true;
                    fittsLawTask.randomizeDesignParametersOnBegin = false;
                    fittsLawTask.restartWithKey = false;
                }

                SyncAdaptiveConditionIdToSource();
                return;
            }

            RefreshIterationCounts();
            SyncContextToReferencedComponents();
            source = ResolveIterationSettingsSource();
            if (fittsLawTask != null)
            {
                fittsLawTask.SetRuntimeDesignParameterSource(source);
                fittsLawTask.restartWithKey = false;
            }

            DisableBoRuntimeForBaseline();

            if (fittsLawTask == null)
                return;

            fittsLawTask.startOnAwake = false;
            fittsLawTask.ensureBoManagerInScene = false;
            fittsLawTask.configureBoManagerForFittsTask = false;
            fittsLawTask.waitForBoEvaluationStart = false;
            fittsLawTask.startBoOptimizationAfterResults = false;
            fittsLawTask.queueNextExternalSignalIteration = false;
            fittsLawTask.readDesignParametersFromBo = false;
            fittsLawTask.writeObjectivesToBo = false;
            fittsLawTask.writeDetailedAppLogCsv = true;
            fittsLawTask.randomizeDesignParametersOnBegin = conditionMode == ConditionMode.Random;
        }

        private void SyncAdaptiveConditionIdToSource()
        {
            if (!setConditionIdFromMode)
                return;

            conditionId = ResolveContextValue(GetDefaultConditionIdForMode());

            BoForUnityManager source = ResolveIterationSettingsSource();
            if (source != null)
                source.conditionId = conditionId;

            if (questionnaireManager != null)
                questionnaireManager.contextConditionId = conditionId;
        }

        private void DisableBoRuntimeForBaseline()
        {
            BoForUnityManager source = ResolveIterationSettingsSource();
            if (source != null)
                iterationSettingsSource = source;

            foreach (BoForUnityManager candidate in Resources.FindObjectsOfTypeAll<BoForUnityManager>())
            {
                if (candidate == null ||
                    candidate.gameObject == null ||
                    !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                DisableBoComponents(candidate);
            }
        }

        private static void DisableBoComponents(BoForUnityManager manager)
        {
            if (manager == null)
                return;

            manager.enabled = false;

            Optimizer optimizer = manager.GetComponent<Optimizer>();
            if (optimizer != null)
                optimizer.enabled = false;

            PythonStarter pythonStarter = manager.GetComponent<PythonStarter>();
            if (pythonStarter != null)
                pythonStarter.enabled = false;

            SocketNetwork socketNetwork = manager.GetComponent<SocketNetwork>();
            if (socketNetwork != null)
                socketNetwork.enabled = false;

            MainThreadDispatcher dispatcher = manager.GetComponent<MainThreadDispatcher>();
            if (dispatcher != null)
                dispatcher.enabled = false;
        }

        private void RefreshIterationCounts()
        {
            int sourceSampling = samplingIterations;
            int sourceOptimization = optimizationIterations;

            BoForUnityManager source = ResolveIterationSettingsSource();
            if (readIterationsFromSource && source != null)
            {
                sourceSampling = Mathf.Max(0, source.numSamplingIterations);
                sourceOptimization = Mathf.Max(0, source.numOptimizationIterations);
            }

            samplingIterations = sourceSampling;
            optimizationIterations = sourceOptimization;
            _baseRoundCount = Mathf.Max(1, samplingIterations + optimizationIterations);
            _totalRoundCount = _baseRoundCount + (includeFinalDesignRound ? 1 : 0);

            if (source != null)
            {
                source.numSamplingIterations = samplingIterations;
                source.numOptimizationIterations = optimizationIterations;
                source.totalIterations = _baseRoundCount;
            }
        }

        private void ResetRoundProgress()
        {
            _currentRound = 0;
            _baseRoundCount = 0;
            _totalRoundCount = 0;
            _started = false;
            _advanceQueued = false;
            _runtimeUserFolderReserved = false;
            _baselineBlockCompletionNotified = false;
            _lastBaselineCsvRoundWritten = 0;
            _currentObjectiveValues.Clear();
        }

        private void SyncContextToReferencedComponents()
        {
            conditionId = ResolveContextValue(GetConfiguredConditionId());
            groupId = ResolveContextValue(groupId);
            EnsureUniqueRuntimeUserFolder();

            BoForUnityManager source = ResolveIterationSettingsSource();
            if (source != null)
            {
                source.userId = userId;
                source.conditionId = conditionId;
                source.groupId = groupId;
            }

            if (questionnaireManager != null)
            {
                questionnaireManager.contextUserId = userId;
                questionnaireManager.contextConditionId = conditionId;
                questionnaireManager.contextGroupId = groupId;
            }
        }

        private void EnsureUniqueRuntimeUserFolder()
        {
            if (_runtimeUserFolderReserved)
                return;

            if (_baselineBlockActive)
            {
                userId = ResolveContextValue(userId);
                _runtimeUserFolderReserved = true;
                return;
            }

            string requestedUserId = ResolveContextValue(userId);
            string normalizedRequestedUserId = LogDataFolderUtility.NormalizeLogFolderToken(requestedUserId);
            userId = LogDataFolderUtility.GetOrCreateUserFolderTokenForCondition(
                LogDataFolderUtility.StreamingAssetsLogRoot,
                requestedUserId,
                conditionId,
                allowExistingRequestedUserFolder: true,
                allowExistingConditionFolder: false
            );
            _runtimeUserFolderReserved = true;

            if (!string.Equals(normalizedRequestedUserId, userId, StringComparison.Ordinal))
            {
                Debug.Log(
                    $"FittsLawConditionManager: user log folder '{normalizedRequestedUserId}' already exists. " +
                    $"Using '{userId}' for this condition run."
                );
            }
        }

        private void QueueNextRound()
        {
            if (!_started || _advanceQueued)
                return;

            _advanceQueued = true;
            StartCoroutine(BeginNextRoundAfterDelay());
        }

        private IEnumerator BeginNextRoundAfterDelay()
        {
            if (nextRoundDelaySeconds > 0f)
                yield return new WaitForSecondsRealtime(nextRoundDelaySeconds);
            else
                yield return null;

            _advanceQueued = false;
            BeginNextRound();
        }

        private void BeginNextRound()
        {
            RefreshIterationCounts();
            SyncContextToReferencedComponents();

            if (_currentRound >= _totalRoundCount)
            {
                Debug.Log(
                    $"FittsLawConditionManager: condition '{conditionId}' completed {_totalRoundCount} rounds."
                );
                if (_baselineBlockActive || captureBaselineCsv)
                    NotifyBaselineBlockCompleted();
                return;
            }

            _started = true;
            _currentRound++;
            _currentObjectiveValues.Clear();

            string phase = GetPhaseForRound(_currentRound);
            BoForUnityManager source = ResolveIterationSettingsSource();
            if (source != null && !string.IsNullOrEmpty(source.userId) && source.userId != "-1")
            {
                this.userId = source.userId;
            }
            RefreshIterationCounts();
            SyncContextToReferencedComponents();

            if (fittsLawTask == null)
            {
                Debug.LogError("FittsLawConditionManager: FittsLawTask reference is missing.");
                return;
            }

            fittsLawTask.SetRuntimeDesignParameterSource(source);
            fittsLawTask.SetManualLogContext(_currentRound, phase, userId, conditionId, groupId);
            fittsLawTask.BeginTask();
        }

        private void PrepareBaselineCsvFiles()
        {
            string[] parameterHeaders = CollectBaselineParameterKeys();
            string[] objectiveHeaders = CollectBaselineObjectiveKeys();
            if (parameterHeaders.Length == 0 || objectiveHeaders.Length == 0)
            {
                Debug.LogError("FittsLawConditionManager: baseline CSV headers could not be resolved.");
                return;
            }

            Directory.CreateDirectory(GetBaselineCsvDirectory());
            File.WriteAllText(
                GetBaselineParametersCsvPath(),
                BuildSemicolonCsvLine(parameterHeaders) + Environment.NewLine,
                Encoding.UTF8);
            File.WriteAllText(
                GetBaselineObjectivesCsvPath(),
                BuildSemicolonCsvLine(objectiveHeaders) + Environment.NewLine,
                Encoding.UTF8);
        }

        private void AppendBaselineCsvRow()
        {
            if (!_baselineBlockActive || _currentRound <= 0 || _lastBaselineCsvRoundWritten == _currentRound)
                return;

            string[] parameterKeys = CollectBaselineParameterKeys();
            string[] objectiveKeys = CollectBaselineObjectiveKeys();
            if (parameterKeys.Length == 0 || objectiveKeys.Length == 0)
            {
                Debug.LogError("FittsLawConditionManager: baseline CSV row skipped because headers are missing.");
                return;
            }

            string[] parameterValues = new string[parameterKeys.Length];
            for (int i = 0; i < parameterKeys.Length; i++)
            {
                if (!TryGetParameterValue(parameterKeys[i], out float value))
                {
                    value = 0f;
                    Debug.LogWarning($"FittsLawConditionManager: baseline parameter '{parameterKeys[i]}' was missing; writing 0.");
                }

                parameterValues[i] = FormatCsvFloat(value);
            }

            string[] objectiveValues = new string[objectiveKeys.Length];
            for (int i = 0; i < objectiveKeys.Length; i++)
            {
                if (!TryGetObjectiveValue(objectiveKeys[i], out float value))
                {
                    value = GetObjectiveFallbackValue(objectiveKeys[i]);
                    Debug.LogWarning(
                        $"FittsLawConditionManager: baseline objective '{objectiveKeys[i]}' was missing; writing fallback {FormatCsvFloat(value)}."
                    );
                }

                objectiveValues[i] = FormatCsvFloat(value);
            }

            Directory.CreateDirectory(GetBaselineCsvDirectory());
            AppendSemicolonCsvRow(GetBaselineParametersCsvPath(), parameterValues);
            AppendSemicolonCsvRow(GetBaselineObjectivesCsvPath(), objectiveValues);
            _lastBaselineCsvRoundWritten = _currentRound;
        }

        private void NotifyBaselineBlockCompleted()
        {
            if (_baselineBlockCompletionNotified)
                return;

            _baselineBlockCompletionNotified = true;
            captureBaselineCsv = false;
            _baselineBlockActive = false;
            _started = false;
            _advanceQueued = false;

            foreach (MonoBehaviour component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (component == null ||
                    component.gameObject == null ||
                    !component.gameObject.scene.IsValid() ||
                    component.GetType().Name != "ExperimentConfig")
                {
                    continue;
                }

                try
                {
                    var method = component.GetType().GetMethod(
                        "OnBaselineBlockCompleted",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    if (method == null)
                        continue;

                    object[] args = method.GetParameters().Length == 1
                        ? new object[] { baselineCsvScale }
                        : null;
                    method.Invoke(component, args);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"FittsLawConditionManager: baseline completion callback failed: {ex.Message}");
                    return;
                }
            }

            Debug.LogWarning("FittsLawConditionManager: baseline block completed, but ExperimentConfig callback was not found.");
        }

        private string GetBaselineCsvDirectory()
        {
            string folder = LogDataFolderUtility.NormalizeLogFolderToken(baselineCsvUserId);
            return Path.Combine(Application.streamingAssetsPath, "BOData", "InitData", folder);
        }

        private string GetBaselineParametersCsvPath()
        {
            return Path.Combine(GetBaselineCsvDirectory(), $"baseline_{baselineCsvScale}_params.csv");
        }

        private string GetBaselineObjectivesCsvPath()
        {
            return Path.Combine(GetBaselineCsvDirectory(), $"baseline_{baselineCsvScale}_objectives.csv");
        }

        private string[] CollectBaselineParameterKeys()
        {
            List<string> keys = new List<string>();
            BoForUnityManager source = ResolveIterationSettingsSource();
            if (source != null && source.parameters != null)
            {
                for (int i = 0; i < source.parameters.Count; i++)
                {
                    ParameterEntry parameter = source.parameters[i];
                    if (parameter != null)
                        TryAddCsvKey(keys, parameter.key);
                }
            }

            if (keys.Count == 0 && fittsLawTask != null)
            {
                TryAddCsvKey(keys, fittsLawTask.xFontSizeParameterKey);
                TryAddCsvKey(keys, fittsLawTask.buttonSizeParameterKey);
                TryAddCsvKey(keys, fittsLawTask.buttonDistanceParameterKey);
                TryAddCsvKey(keys, fittsLawTask.buttonHueParameterKey);
                TryAddCsvKey(keys, fittsLawTask.buttonSaturationParameterKey);
            }

            return keys.ToArray();
        }

        private string[] CollectBaselineObjectiveKeys()
        {
            List<string> keys = new List<string>();
            BoForUnityManager source = ResolveIterationSettingsSource();
            if (source != null && source.objectives != null)
            {
                for (int i = 0; i < source.objectives.Count; i++)
                {
                    ObjectiveEntry objective = source.objectives[i];
                    if (objective != null)
                        TryAddCsvKey(keys, objective.key);
                }
            }

            if (keys.Count == 0 && fittsLawTask != null)
            {
                TryAddCsvKey(keys, fittsLawTask.aestheticsObjectiveKey);
                TryAddCsvKey(keys, fittsLawTask.speedObjectiveKey);
                TryAddCsvKey(keys, fittsLawTask.accuracyObjectiveKey);
                TryAddCsvKey(keys, fittsLawTask.usabilityObjectiveKey);
            }

            return keys.ToArray();
        }

        private static void TryAddCsvKey(List<string> keys, string key)
        {
            if (keys == null || string.IsNullOrWhiteSpace(key))
                return;

            string trimmed = key.Trim();
            if (!keys.Contains(trimmed))
                keys.Add(trimmed);
        }

        private bool TryGetParameterValue(string key, out float value)
        {
            value = 0f;
            BoForUnityManager source = ResolveIterationSettingsSource();
            if (source != null && source.parameters != null)
            {
                for (int i = 0; i < source.parameters.Count; i++)
                {
                    ParameterEntry parameter = source.parameters[i];
                    if (parameter == null ||
                        parameter.value == null ||
                        !string.Equals(parameter.key, key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    value = parameter.value.Value;
                    if (IsFinite(value))
                        return true;
                }
            }

            return TryGetTaskParameterValue(key, out value);
        }

        private bool TryGetTaskParameterValue(string key, out float value)
        {
            value = 0f;
            if (fittsLawTask == null || string.IsNullOrWhiteSpace(key))
                return false;

            if (string.Equals(key, fittsLawTask.xFontSizeParameterKey, StringComparison.Ordinal))
                value = fittsLawTask.xFontSizePixels;
            else if (string.Equals(key, fittsLawTask.buttonSizeParameterKey, StringComparison.Ordinal))
                value = fittsLawTask.circleSizePixels;
            else if (string.Equals(key, fittsLawTask.buttonDistanceParameterKey, StringComparison.Ordinal))
                value = fittsLawTask.circleDistancePixels;
            else if (string.Equals(key, fittsLawTask.buttonHueParameterKey, StringComparison.Ordinal))
                value = fittsLawTask.buttonHue;
            else if (string.Equals(key, fittsLawTask.buttonSaturationParameterKey, StringComparison.Ordinal))
                value = fittsLawTask.buttonSaturation;
            else
                return false;

            return IsFinite(value);
        }

        private bool TryGetObjectiveValue(string key, out float value)
        {
            value = 0f;
            if (fittsLawTask != null)
            {
                if (string.Equals(key, fittsLawTask.speedObjectiveKey, StringComparison.Ordinal))
                {
                    value = fittsLawTask.taskCompletionTimeMs;
                    return IsFinite(value);
                }

                if (string.Equals(key, fittsLawTask.accuracyObjectiveKey, StringComparison.Ordinal))
                {
                    value = fittsLawTask.accuracyDistancePixels;
                    return IsFinite(value);
                }
            }

            if (TryGetObjectiveAverage(key, out value))
                return true;

            return TryGetSourceObjectiveValue(key, out value);
        }

        private bool TryGetSourceObjectiveValue(string key, out float value)
        {
            value = 0f;
            if (!TryFindObjectiveEntry(key, out ObjectiveEntry objective) ||
                objective.value == null ||
                objective.value.values == null ||
                objective.value.values.Count == 0)
            {
                return false;
            }

            int measureCount = Mathf.Max(1, objective.value.numberOfSubMeasures);
            int startIndex = Mathf.Max(0, objective.value.values.Count - measureCount);
            double sum = 0.0;
            int count = 0;
            for (int i = startIndex; i < objective.value.values.Count; i++)
            {
                float candidate = objective.value.values[i];
                if (!IsFinite(candidate))
                    return false;

                sum += candidate;
                count++;
            }

            if (count == 0)
                return false;

            value = (float)(sum / count);
            return true;
        }

        private float GetObjectiveFallbackValue(string key)
        {
            if (TryFindObjectiveEntry(key, out ObjectiveEntry objective) &&
                objective.value != null &&
                IsFinite(objective.value.lowerBound) &&
                IsFinite(objective.value.upperBound))
            {
                return (objective.value.lowerBound + objective.value.upperBound) * 0.5f;
            }

            return 0f;
        }

        private bool TryFindObjectiveEntry(string key, out ObjectiveEntry objective)
        {
            objective = null;
            BoForUnityManager source = ResolveIterationSettingsSource();
            if (source == null || source.objectives == null || string.IsNullOrWhiteSpace(key))
                return false;

            for (int i = 0; i < source.objectives.Count; i++)
            {
                ObjectiveEntry candidate = source.objectives[i];
                if (candidate == null || !string.Equals(candidate.key, key, StringComparison.Ordinal))
                    continue;

                objective = candidate;
                return true;
            }

            return false;
        }

        private static void AppendSemicolonCsvRow(string path, string[] values)
        {
            using (var writer = new StreamWriter(path, true, Encoding.UTF8))
            {
                writer.WriteLine(BuildSemicolonCsvLine(values));
            }
        }

        private static string BuildSemicolonCsvLine(string[] cells)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0)
                    builder.Append(';');
                builder.Append(EscapeSemicolonCsvCell(cells[i]));
            }

            return builder.ToString();
        }

        private static string EscapeSemicolonCsvCell(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            bool mustQuote =
                value.IndexOf(';') >= 0 ||
                value.IndexOf('"') >= 0 ||
                value.IndexOf('\n') >= 0 ||
                value.IndexOf('\r') >= 0;

            if (!mustQuote)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string FormatCsvFloat(float value)
        {
            if (!IsFinite(value))
                return "0";

            return Math.Round(value, 3, MidpointRounding.AwayFromZero)
                .ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string GetPhaseForRound(int round)
        {
            if (includeFinalDesignRound && round > _baseRoundCount)
                return "finaldesign";

            return round <= samplingIterations ? "sampling" : "optimization";
        }

        private string GetConfiguredConditionId()
        {
            return setConditionIdFromMode ? GetDefaultConditionIdForMode() : conditionId;
        }

        private string GetDefaultConditionIdForMode()
        {
            if (conditionMode == ConditionMode.Random)
                return "random";

            if (conditionMode == ConditionMode.Static)
                return "static";

            return "HITL MOBO";
        }

        private string ResolveObjectiveKey(string headerName)
        {
            if (fittsLawTask == null || string.IsNullOrWhiteSpace(headerName))
                return null;

            if (MatchesObjectiveHeader(headerName, fittsLawTask.aestheticsObjectiveKey))
                return fittsLawTask.aestheticsObjectiveKey;

            if (MatchesObjectiveHeader(headerName, fittsLawTask.usabilityObjectiveKey))
                return fittsLawTask.usabilityObjectiveKey;

            return null;
        }

        private static bool MatchesObjectiveHeader(string headerName, string objectiveKey)
        {
            if (string.IsNullOrWhiteSpace(headerName) || string.IsNullOrWhiteSpace(objectiveKey))
                return false;

            string source = headerName.Trim();
            string key = objectiveKey.Trim();
            int start = 0;
            while (start < source.Length)
            {
                int idx = source.IndexOf(key, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return false;

                int end = idx + key.Length;
                if (IsObjectiveHeaderBoundary(source, idx) && IsObjectiveHeaderBoundary(source, end))
                    return true;

                start = idx + 1;
            }

            return false;
        }

        private static bool IsObjectiveHeaderBoundary(string text, int boundaryIndex)
        {
            if (boundaryIndex <= 0 || boundaryIndex >= text.Length)
                return true;

            char left = text[boundaryIndex - 1];
            char right = text[boundaryIndex];
            if (!char.IsLetterOrDigit(left) || !char.IsLetterOrDigit(right))
                return true;

            if (char.IsLetter(left) && char.IsLetter(right) && char.IsLower(left) && char.IsUpper(right))
                return true;

            if (char.IsLetter(left) && char.IsDigit(right))
                return true;
            if (char.IsDigit(left) && char.IsLetter(right))
                return true;

            return false;
        }

        private static T FindSceneObject<T>() where T : UnityEngine.Object
        {
            foreach (T candidate in Resources.FindObjectsOfTypeAll<T>())
            {
                if (candidate is Component component)
                {
                    if (component.gameObject != null && component.gameObject.scene.IsValid())
                        return candidate;
                }
            }

            return null;
        }

        private static string ResolveContextValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-1" : value.Trim();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
