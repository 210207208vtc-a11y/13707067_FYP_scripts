using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class JourneySessionManager : MonoBehaviour
{
    public static JourneySessionManager Instance { get; private set; }

    [SerializeField] private StoryOutcomeCatalog storyOutcomeCatalog;
    [SerializeField] private string resourcesCatalogPath = "StoryOutcomeCatalog";
    [SerializeField] private bool verboseLogging = true;

    private readonly List<JourneyEventRecord> eventRecords = new List<JourneyEventRecord>();
    private readonly List<GestureSemanticRecord> gestureSemanticRecords = new List<GestureSemanticRecord>();
    private readonly Dictionary<string, SceneOutcomeRecord> sceneOutcomes = new Dictionary<string, SceneOutcomeRecord>(StringComparer.OrdinalIgnoreCase);
    private readonly PromptTagSet promptTagSet = new PromptTagSet();

    private bool isSubscribed;
    private float sessionStartTime;
    private EndingPromptPackage endingPromptPackage;
    private EndingVideoPreparationState endingVideoPreparationState = EndingVideoPreparationState.NotStarted;
    private readonly List<EndingVideoClipRecord> endingVideoClipRecords = new List<EndingVideoClipRecord>();
    private int endingVideoTargetClipCount;
    private string endingVideoPlayablePath = string.Empty;
    private string endingVideoError = string.Empty;
    private int journeyId;
    private int lastJourneyResetFrame = -1;

    public IReadOnlyList<JourneyEventRecord> EventRecords => eventRecords;
    public IReadOnlyList<GestureSemanticRecord> GestureSemanticRecords => gestureSemanticRecords;
    public IReadOnlyDictionary<string, SceneOutcomeRecord> SceneOutcomes => sceneOutcomes;
    public IReadOnlyList<string> AggregatedPromptTags => promptTagSet.Tags;
    public EndingPromptPackage EndingPromptPackage => endingPromptPackage;
    public EndingVideoPreparationState EndingVideoPreparationState => endingVideoPreparationState;
    public IReadOnlyList<EndingVideoClipRecord> EndingVideoClipRecords => endingVideoClipRecords;
    public int EndingVideoTargetClipCount => endingVideoTargetClipCount;
    public int ReadyEndingVideoClipCount => endingVideoClipRecords.Count;
    public string EndingVideoPlayablePath => endingVideoPlayablePath;
    public string EndingVideoError => endingVideoError;
    public bool HasReadyEndingVideo => endingVideoClipRecords.Count > 0;
    public int JourneyId => journeyId;

    public StoryOutcomeCatalog Catalog
    {
        get
        {
            EnsureCatalogLoaded();
            return storyOutcomeCatalog;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        sessionStartTime = Time.unscaledTime;
        DontDestroyOnLoad(gameObject);
        EnsureCatalogLoaded();
        SubscribeToSceneEvents();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            UnsubscribeFromSceneEvents();
            Instance = null;
        }
    }

    public void LogEvent(string sceneName, string eventType, string detail = "", string outcomeKey = "", string semanticKey = "")
    {
        JourneyEventRecord record = new JourneyEventRecord
        {
            sceneName = string.IsNullOrWhiteSpace(sceneName) ? SceneManager.GetActiveScene().name : sceneName,
            eventType = eventType,
            detail = detail,
            outcomeKey = outcomeKey,
            semanticKey = semanticKey,
            sessionTime = Time.unscaledTime - sessionStartTime,
        };

        eventRecords.Add(record);

        if (verboseLogging)
        {
            Debug.Log($"[JourneySession] {record.sceneName} | {record.eventType} | outcome={record.outcomeKey} | semantic={record.semanticKey} | {record.detail}");
        }
    }

    public void RecordGestureSemantic(string sceneName, string gestureKey, IEnumerable<string> semanticTags, float intensity = 0f, float duration = 0f, string detail = "")
    {
        if (string.IsNullOrWhiteSpace(gestureKey))
        {
            return;
        }

        List<string> capturedTags = new List<string>();
        if (semanticTags != null)
        {
            foreach (string tag in semanticTags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    capturedTags.Add(tag.Trim());
                }
            }
        }

        GestureSemanticRecord record = new GestureSemanticRecord
        {
            sceneName = string.IsNullOrWhiteSpace(sceneName) ? SceneManager.GetActiveScene().name : sceneName,
            gestureKey = gestureKey,
            semanticTags = capturedTags,
            intensity = intensity,
            duration = duration,
            detail = detail,
            sessionTime = Time.unscaledTime - sessionStartTime,
        };

        gestureSemanticRecords.Add(record);
        promptTagSet.AddRange(capturedTags);
        LogEvent(record.sceneName, "gesture_semantic", detail, string.Empty, gestureKey);
    }

    public bool TryRecordSceneOutcome(string sceneName, string outcomeKey, string detail = "")
    {
        if (string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(outcomeKey))
        {
            return false;
        }

        if (sceneOutcomes.ContainsKey(sceneName))
        {
            return false;
        }

        EnsureCatalogLoaded();

        StoryOutcomeCatalog.StoryOutcomeDefinition definition = storyOutcomeCatalog.FindSceneOutcome(sceneName, outcomeKey);
        if (definition == null)
        {
            StoryOutcomeCatalog runtimeDefaults = StoryOutcomeCatalog.CreateRuntimeDefault();
            definition = runtimeDefaults.FindSceneOutcome(sceneName, outcomeKey);
        }
        SceneOutcomeRecord record = new SceneOutcomeRecord
        {
            sceneName = sceneName,
            outcomeKey = outcomeKey,
            displayTitle = definition != null ? definition.displayTitle : outcomeKey,
            recapSentence = definition != null ? definition.recapSentence : string.Empty,
            promptTags = definition != null ? new List<string>(definition.promptTags) : new List<string>(),
            sessionTime = Time.unscaledTime - sessionStartTime,
            detail = detail,
        };

        sceneOutcomes[sceneName] = record;
        promptTagSet.AddRange(record.promptTags);

        LogEvent(sceneName, "outcome_locked", detail, outcomeKey);
        return true;
    }

    public bool HasSceneOutcome(string sceneName)
    {
        return !string.IsNullOrWhiteSpace(sceneName) && sceneOutcomes.ContainsKey(sceneName);
    }

    public SceneOutcomeRecord GetSceneOutcome(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return null;
        }

        sceneOutcomes.TryGetValue(sceneName, out SceneOutcomeRecord record);
        return record;
    }

    public void SetEndingPromptPackage(EndingPromptPackage package)
    {
        endingPromptPackage = package;
        if (package != null)
        {
            endingVideoTargetClipCount = package.segmentSpecs != null ? package.segmentSpecs.Count : 0;
            promptTagSet.AddRange(package.aggregatedTags);
            LogEvent("3", "ending_prompt_built", package.finalEndingKey, package.finalEndingKey);
        }
    }

    public void AddEndingVideoClip(EndingVideoClipRecord clipRecord)
    {
        if (clipRecord == null || string.IsNullOrWhiteSpace(clipRecord.playablePath))
        {
            return;
        }

        for (int i = 0; i < endingVideoClipRecords.Count; i++)
        {
            if (string.Equals(endingVideoClipRecords[i].segmentKey, clipRecord.segmentKey, StringComparison.OrdinalIgnoreCase))
            {
                endingVideoClipRecords[i] = clipRecord;
                if (string.IsNullOrWhiteSpace(endingVideoPlayablePath))
                {
                    endingVideoPlayablePath = clipRecord.playablePath;
                }

                LogEvent("3", "ending_video_clip_ready", clipRecord.segmentKey, clipRecord.segmentKey);
                return;
            }
        }

        endingVideoClipRecords.Add(clipRecord);
        if (string.IsNullOrWhiteSpace(endingVideoPlayablePath))
        {
            endingVideoPlayablePath = clipRecord.playablePath;
        }

        LogEvent("3", "ending_video_clip_ready", clipRecord.segmentKey, clipRecord.segmentKey);
    }

    public bool HasEndingVideoClip(string segmentKey)
    {
        if (string.IsNullOrWhiteSpace(segmentKey))
        {
            return false;
        }

        for (int i = 0; i < endingVideoClipRecords.Count; i++)
        {
            if (string.Equals(endingVideoClipRecords[i].segmentKey, segmentKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public EndingVideoClipRecord GetEndingVideoClip(string segmentKey)
    {
        if (string.IsNullOrWhiteSpace(segmentKey))
        {
            return null;
        }

        for (int i = 0; i < endingVideoClipRecords.Count; i++)
        {
            if (string.Equals(endingVideoClipRecords[i].segmentKey, segmentKey, StringComparison.OrdinalIgnoreCase))
            {
                return endingVideoClipRecords[i];
            }
        }

        return null;
    }

    public void SetEndingVideoPreparationState(EndingVideoPreparationState state, string playablePath = "", string error = "")
    {
        endingVideoPreparationState = state;
        if (!string.IsNullOrWhiteSpace(playablePath))
        {
            endingVideoPlayablePath = playablePath;
            if (endingVideoClipRecords.Count == 0)
            {
                endingVideoClipRecords.Add(new EndingVideoClipRecord
                {
                    segmentKey = "legacy-ending",
                    displayTitle = "Legacy Ending Clip",
                    playablePath = playablePath,
                    durationSeconds = 8,
                });
            }
        }

        if (state == EndingVideoPreparationState.NotStarted)
        {
            endingVideoClipRecords.Clear();
            endingVideoTargetClipCount = 0;
            endingVideoPlayablePath = string.Empty;
            endingVideoError = string.Empty;
        }
        else if (state == EndingVideoPreparationState.Failed)
        {
            endingVideoError = error ?? string.Empty;
        }
        else if (state == EndingVideoPreparationState.Ready)
        {
            endingVideoError = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(error))
        {
            endingVideoError = error;
        }
    }

    public void ResetEndingVideoPreparation()
    {
        endingPromptPackage = null;
        endingVideoPreparationState = EndingVideoPreparationState.NotStarted;
        endingVideoClipRecords.Clear();
        endingVideoTargetClipCount = 0;
        endingVideoPlayablePath = string.Empty;
        endingVideoError = string.Empty;
    }

    public bool BeginNewJourneySession(string reason = "")
    {
        if (lastJourneyResetFrame == Time.frameCount)
        {
            return false;
        }

        lastJourneyResetFrame = Time.frameCount;
        journeyId++;
        sessionStartTime = Time.unscaledTime;
        eventRecords.Clear();
        gestureSemanticRecords.Clear();
        sceneOutcomes.Clear();
        promptTagSet.Clear();
        ResetEndingVideoPreparation();

        LogEvent("Test", "journey_reset", reason, journeyId.ToString());
        return true;
    }

    public bool IsCurrentJourney(int expectedJourneyId)
    {
        return expectedJourneyId == journeyId;
    }

    private void SubscribeToSceneEvents()
    {
        if (isSubscribed)
        {
            return;
        }

        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        isSubscribed = true;
    }

    private void UnsubscribeFromSceneEvents()
    {
        if (!isSubscribed)
        {
            return;
        }

        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        isSubscribed = false;
    }

    private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        if (previousScene.IsValid() && !string.IsNullOrEmpty(previousScene.name))
        {
            LogEvent(previousScene.name, "scene_exit", $"to:{nextScene.name}");
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        LogEvent(scene.name, "scene_enter", mode.ToString());
    }

    private void EnsureCatalogLoaded()
    {
        if (storyOutcomeCatalog != null)
        {
            return;
        }

        storyOutcomeCatalog = Resources.Load<StoryOutcomeCatalog>(resourcesCatalogPath);

        if (storyOutcomeCatalog == null)
        {
            storyOutcomeCatalog = StoryOutcomeCatalog.CreateRuntimeDefault();
            Debug.LogWarning("[JourneySession] StoryOutcomeCatalog asset not found. Using runtime defaults.");
        }
    }
}

[Serializable]
public class JourneyEventRecord
{
    public string sceneName;
    public string eventType;
    public string detail;
    public string outcomeKey;
    public string semanticKey;
    public float sessionTime;
}

[Serializable]
public class GestureSemanticRecord
{
    public string sceneName;
    public string gestureKey;
    public List<string> semanticTags = new List<string>();
    public float intensity;
    public float duration;
    public string detail;
    public float sessionTime;
}

[Serializable]
public class SceneOutcomeRecord
{
    public string sceneName;
    public string outcomeKey;
    public string displayTitle;
    public string recapSentence;
    public List<string> promptTags = new List<string>();
    public float sessionTime;
    public string detail;
}

[Serializable]
public class EndingPromptPackage
{
    public string journeyIdentityLine;
    public string hungerRecapLine;
    public string warRecapLine;
    public string medicalRecapLine;
    public string finalEndingKey;
    public string finalEndingTitle;
    public string finalEndingSentence;
    public string finalVideoPrompt;
    public float generatedAtSessionTime;
    public List<string> aggregatedTags = new List<string>();
    public List<EndingVideoSegmentSpec> segmentSpecs = new List<EndingVideoSegmentSpec>();
}

public enum EndingVideoPreparationState
{
    NotStarted,
    Generating,
    Ready,
    Failed,
}

[Serializable]
public class PromptTagSet
{
    [SerializeField] private List<string> tags = new List<string>();
    private readonly HashSet<string> tagLookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Tags => tags;

    public void Clear()
    {
        tags.Clear();
        tagLookup.Clear();
    }

    public void AddRange(IEnumerable<string> values)
    {
        if (values == null)
        {
            return;
        }

        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string trimmed = value.Trim();
            if (tagLookup.Add(trimmed))
            {
                tags.Add(trimmed);
            }
        }
    }
}

[Serializable]
public class EndingVideoSegmentSpec
{
    public string segmentKey;
    public string displayTitle;
    public string prompt;
    public int durationSeconds = 8;
}

[Serializable]
public class EndingVideoClipRecord
{
    public string segmentKey;
    public string displayTitle;
    public string playablePath;
    public int durationSeconds = 8;
}
