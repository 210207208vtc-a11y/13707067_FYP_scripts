using UnityEngine;

[AddComponentMenu("Journey/Ending Video Preparation Coordinator")]
public class EndingVideoPreparationCoordinator : MonoBehaviour
{
    private static EndingVideoPreparationCoordinator instance;

    [SerializeField] private bool verboseLogging = true;

    private PoeVideoManager backgroundVideoManager;
    private Coroutine activePreparationCoroutine;
    private int activePreparationJourneyId = -1;

    public static EndingVideoPreparationCoordinator Instance => EnsureInstance();

    public static EndingVideoPreparationCoordinator EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        GameObject existing = GameObject.Find("EndingVideoPreparationCoordinator");
        if (existing != null)
        {
            instance = existing.GetComponent<EndingVideoPreparationCoordinator>();
            if (instance != null)
            {
                return instance;
            }
        }

        GameObject root = new GameObject("EndingVideoPreparationCoordinator");
        instance = root.AddComponent<EndingVideoPreparationCoordinator>();
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

    public bool BeginPreparationIfNeeded(string triggerSceneName, string detail = "")
    {
        JourneySessionManager session = JourneySessionManager.Instance;
        if (session == null)
        {
            return false;
        }

        if (activePreparationCoroutine != null && activePreparationJourneyId != session.JourneyId)
        {
            StopCoroutine(activePreparationCoroutine);
            activePreparationCoroutine = null;
            activePreparationJourneyId = -1;
        }

        if (activePreparationCoroutine != null || session.EndingVideoPreparationState == EndingVideoPreparationState.Generating)
        {
            return false;
        }

        EndingPromptPackage package = JourneyPromptComposer.BuildEndingPromptPackage(session);
        if (package == null || package.segmentSpecs == null || package.segmentSpecs.Count == 0)
        {
            session.SetEndingVideoPreparationState(EndingVideoPreparationState.Failed, error: "Ending prompt package could not be built.");
            return false;
        }

        session.SetEndingPromptPackage(package);
        session.SetEndingVideoPreparationState(EndingVideoPreparationState.Generating);
        session.LogEvent(triggerSceneName, "ending_video_preparation_started", detail, package.finalEndingKey);

        EnsureBackgroundVideoManager();
        if (backgroundVideoManager == null)
        {
            session.SetEndingVideoPreparationState(EndingVideoPreparationState.Failed, error: "Background video manager could not be created.");
            return false;
        }
        
        activePreparationJourneyId = session.JourneyId;
        activePreparationCoroutine = StartCoroutine(GenerateEndingSequence(package, activePreparationJourneyId));

        if (verboseLogging)
        {
            Debug.Log("[EndingVideoPreparationCoordinator] Ending multi-clip generation started.");
        }

        return true;
    }

    private System.Collections.IEnumerator GenerateEndingSequence(EndingPromptPackage package, int journeyId)
    {
        JourneySessionManager session = JourneySessionManager.Instance;
        if (session == null)
        {
            activePreparationCoroutine = null;
            activePreparationJourneyId = -1;
            yield break;
        }

        int successCount = 0;
        for (int i = 0; i < package.segmentSpecs.Count; i++)
        {
            EndingVideoSegmentSpec spec = package.segmentSpecs[i];
            if (spec == null || string.IsNullOrWhiteSpace(spec.prompt))
            {
                continue;
            }

            VideoGenerationResult segmentResult = null;
            bool started = backgroundVideoManager.StartBackgroundGeneration(
                spec.prompt,
                spec.durationSeconds,
                result => segmentResult = result,
                false);

            if (!started)
            {
                session.LogEvent("3", "ending_video_segment_failed", "Video manager busy", spec.segmentKey);
                yield return new WaitForSecondsRealtime(0.5f);
                i--;
                continue;
            }

            while (segmentResult == null)
            {
                if (JourneySessionManager.Instance == null || !JourneySessionManager.Instance.IsCurrentJourney(journeyId))
                {
                    activePreparationCoroutine = null;
                    activePreparationJourneyId = -1;
                    yield break;
                }

                yield return null;
            }

            if (session.IsCurrentJourney(journeyId) && segmentResult.IsSuccess)
            {
                successCount++;
                session.AddEndingVideoClip(new EndingVideoClipRecord
                {
                    segmentKey = spec.segmentKey,
                    displayTitle = spec.displayTitle,
                    playablePath = segmentResult.playableUrlOrPath,
                    durationSeconds = spec.durationSeconds,
                });
                session.LogEvent("3", "ending_video_segment_completed", spec.displayTitle, spec.segmentKey);
            }
            else
            {
                string error = string.IsNullOrWhiteSpace(segmentResult.error) ? "Unknown segment generation failure." : segmentResult.error;
                session.LogEvent("3", "ending_video_segment_failed", error, spec.segmentKey);
            }
        }

        if (!session.IsCurrentJourney(journeyId))
        {
            activePreparationCoroutine = null;
            activePreparationJourneyId = -1;
            yield break;
        }

        if (successCount > 0)
        {
            session.SetEndingVideoPreparationState(EndingVideoPreparationState.Ready);
            session.LogEvent("3", "ending_video_preparation_completed", $"clips:{successCount}", package.finalEndingKey);
        }
        else
        {
            session.SetEndingVideoPreparationState(EndingVideoPreparationState.Failed, error: "No ending clips were generated successfully.");
            session.LogEvent("3", "ending_video_preparation_failed", "No ending clips were generated successfully.");
        }

        activePreparationCoroutine = null;
        activePreparationJourneyId = -1;
    }

    private void EnsureBackgroundVideoManager()
    {
        if (backgroundVideoManager != null)
        {
            return;
        }

        GameObject managerObject = new GameObject("EndingVideoPoeManager");
        managerObject.transform.SetParent(transform, false);
        backgroundVideoManager = managerObject.AddComponent<PoeVideoManager>();
        backgroundVideoManager.generatingOverlayTitle = "Preparing ending";
        backgroundVideoManager.verboseDiagnostics = true;
        backgroundVideoManager.model = "veo-3.1-lite";
        backgroundVideoManager.resolution = "720p";
        backgroundVideoManager.defaultDurationSeconds = 4;
        backgroundVideoManager.localLtxFrameRate = Mathf.Clamp(backgroundVideoManager.localLtxFrameRate, 8, 8);
        backgroundVideoManager.localLtxInferenceSteps = Mathf.Clamp(backgroundVideoManager.localLtxInferenceSteps, 8, 10);
        backgroundVideoManager.requestTimeoutSeconds = Mathf.Max(backgroundVideoManager.requestTimeoutSeconds, 720f);
    }
}
