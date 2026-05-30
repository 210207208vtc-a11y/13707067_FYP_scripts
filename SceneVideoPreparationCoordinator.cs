using UnityEngine;

[AddComponentMenu("Journey/Scene Video Preparation Coordinator")]
public class SceneVideoPreparationCoordinator : MonoBehaviour
{
    private static SceneVideoPreparationCoordinator instance;

    [SerializeField] private bool verboseLogging = true;

    private PoeVideoManager backgroundVideoManager;
    private bool warPreparationStarted;
    private bool warPreparationCompleted;
    private bool warPreparationSucceeded;
    private string warPreparedPlayablePath = string.Empty;
    private int warPreparationJourneyId = -1;

    private const string GenericWarPrompt =
        "Create a grounded first-person wartime surrender memory. The viewer stands in the center of a ruined battlefield checkpoint. " +
        "Keep both raised human hands visible near the lower foreground. Show broken sandbags, concrete barricades, drifting ash, smoke, dim siren light, " +
        "and distant mechanized soldier silhouettes. Human-scale realism only. No science fiction. No fantasy. No hospital imagery. No subtitles. No text. Silent.";

    public static SceneVideoPreparationCoordinator Instance => EnsureInstance();

    public bool WarPreparationStarted => warPreparationStarted;
    public bool WarPreparationCompleted => warPreparationCompleted;
    public bool WarPreparationSucceeded => warPreparationSucceeded;
    public bool IsWarPreparationInProgress => warPreparationStarted && !warPreparationCompleted;

    public void ResetForJourney(int journeyId)
    {
        warPreparationStarted = false;
        warPreparationCompleted = false;
        warPreparationSucceeded = false;
        warPreparedPlayablePath = string.Empty;
        warPreparationJourneyId = journeyId;
    }

    public static SceneVideoPreparationCoordinator EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        GameObject existing = GameObject.Find("SceneVideoPreparationCoordinator");
        if (existing != null)
        {
            instance = existing.GetComponent<SceneVideoPreparationCoordinator>();
            if (instance != null)
            {
                return instance;
            }
        }

        GameObject root = new GameObject("SceneVideoPreparationCoordinator");
        instance = root.AddComponent<SceneVideoPreparationCoordinator>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureBackgroundVideoManager();
    }

    public void BeginWarPreparationIfNeeded()
    {
        if (warPreparationStarted || warPreparationCompleted)
        {
            return;
        }

        EnsureBackgroundVideoManager();
        if (backgroundVideoManager == null)
        {
            return;
        }

        JourneySessionManager session = JourneySessionManager.Instance;
        int requestJourneyId = session != null ? session.JourneyId : warPreparationJourneyId;
        warPreparationJourneyId = requestJourneyId;

        warPreparationStarted = backgroundVideoManager.StartBackgroundGeneration(
            GenericWarPrompt,
            3,
            result =>
            {
                JourneySessionManager currentSession = JourneySessionManager.Instance;
                if (currentSession != null && !currentSession.IsCurrentJourney(requestJourneyId))
                {
                    if (verboseLogging)
                    {
                        Debug.Log("[SceneVideoPreparationCoordinator] Discarded stale war clip from a previous journey.");
                    }

                    return;
                }

                warPreparationCompleted = true;
                warPreparationSucceeded = result != null && result.IsSuccess && !string.IsNullOrWhiteSpace(result.playableUrlOrPath);
                warPreparedPlayablePath = warPreparationSucceeded ? result.playableUrlOrPath : string.Empty;

                if (verboseLogging)
                {
                    Debug.Log(warPreparationSucceeded
                        ? "[SceneVideoPreparationCoordinator] Generic war clip prepared."
                        : "[SceneVideoPreparationCoordinator] Generic war clip preparation failed.");
                }
            },
            false);

        if (verboseLogging && warPreparationStarted)
        {
            Debug.Log("[SceneVideoPreparationCoordinator] Generic war clip preparation started.");
        }
    }

    public bool TryGetPreparedWarClip(out string playablePath)
    {
        playablePath = warPreparedPlayablePath;
        JourneySessionManager session = JourneySessionManager.Instance;
        bool currentJourney = session == null || session.IsCurrentJourney(warPreparationJourneyId);
        return currentJourney && warPreparationCompleted && warPreparationSucceeded && !string.IsNullOrWhiteSpace(warPreparedPlayablePath);
    }

    private void EnsureBackgroundVideoManager()
    {
        if (backgroundVideoManager != null)
        {
            return;
        }

        GameObject managerObject = new GameObject("ScenePreparationVideoManager");
        managerObject.transform.SetParent(transform, false);
        managerObject.AddComponent<UnityEngine.Video.VideoPlayer>();
        backgroundVideoManager = managerObject.AddComponent<PoeVideoManager>();
        backgroundVideoManager.generatingOverlayTitle = "Please wait";
        backgroundVideoManager.verboseDiagnostics = true;
        backgroundVideoManager.hideVideoDisplayAfterPlayback = false;
        backgroundVideoManager.resolution = "720p";
        backgroundVideoManager.defaultDurationSeconds = 3;
        backgroundVideoManager.localLtxFrameRate = 8;
        backgroundVideoManager.localLtxInferenceSteps = 8;
        backgroundVideoManager.requestTimeoutSeconds = Mathf.Max(backgroundVideoManager.requestTimeoutSeconds, 720f);
    }
}
