using System.Collections;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

[AddComponentMenu("Journey/War Gesture Video Director")]
public class WarGestureVideoDirector : MonoBehaviour
{
    [Header("Generated War Video")]
    public PoeVideoManager poeVideoManager;
    public VideoPlayer fallbackVideoPlayer;
    public VideoClip fallbackVideoClip;
    public GameObject fallbackVideoDisplay;
    public int generatedVideoDurationSeconds = 6;
    public string nextSceneName = "3";
    public float sceneFadeDuration = 0.65f;
    public float generationTimeoutSeconds = 210f;
    public float playbackTimeoutSeconds = 35f;

    [TextArea(2, 5)]
    public string generatedVideoPromptTemplate =
        "Create a cinematic first-person wartime surrender memory. The viewer stands inside a ruined battlefield checkpoint with bodily immediacy and uninterrupted subjective camera continuity. " +
        "Prompt seed: {PROMPT_SEED}. Emotional tone: {OUTCOME_TONE}. Mood tags: {TAGS}. " +
        "Keep both raised human hands or an equivalent surrender posture visible near the lower foreground. Show broken sandbags, concrete barricades, drifting ash, smoke, dim siren light, debris, and distant mechanized soldier silhouettes pressing psychological threat from the mid-ground. " +
        "Keep the environment human-scale, grounded, oppressive, and physically believable. Silent. No subtitles. No captions. No on-screen text. No typography. No science fiction. No fantasy. No hospital imagery.";

    [TextArea(2, 5)]
    public string primedVideoPrompt =
        "Create a cinematic first-person wartime surrender memory. The viewer stands inside a ruined battlefield checkpoint with both raised hands visible in the lower foreground. " +
        "Show broken sandbags, concrete barricades, drifting ash, smoke, dim siren light, distant mechanized soldier silhouettes, and the constant bodily pressure of surrender under threat. " +
        "Keep the camera grounded, oppressive, human-scale, and emotionally coherent from start to finish. Silent. No subtitles. No captions. No on-screen text. No typography. No science fiction. No fantasy. No hospital imagery.";

    [Header("Preload")]
    public bool preGenerateWarVideoOnSceneEnter = false;
    public float preGenerateDelaySeconds = 0.35f;
    public bool useSharedPreparedWarClipForScenePlayback = false;

    [Header("Prompt Capture")]
    public float promptCaptureDurationSeconds = 10f;
    public float scene2MinigameMinimumPlaySeconds = 10f;

    [Header("ElevenLabs Music")]
    public ElevenLabsMusicPro elevenLabsMusic;
    public int musicDurationVideoThreeSeconds = 10;

    [TextArea(2, 4)]
    public string promptForVideoThree = "More emotional and evolving music for the ending video.";

    [Header("War Gesture")]
    [Range(0.0f, 1.0f)]
    public float raisedHandYThreshold = 0.58f;
    public float holdSecondsRequired = 1.0f;
    public float quickComplianceSeconds = 4.0f;
    public float surrenderTimeoutSeconds = 20.0f;
    public float interactionUnlockDelaySeconds = 30f;
    public bool debugLog = false;
    public float fingerExtendedTolerance = 0.02f;
    public float raisedWristSlack = 0.18f;
    public float minimumHandSeparation = 0.12f;

    [Header("Gesture Reminder Audio")]
    [Tooltip("Looping reminder audio that plays while both hands are NOT raised.")]
    public AudioSource noHandsRaisedLoopAudio;

    private enum WarSequenceState
    {
        AwaitingGesture,
        WaitingForPlaybackGate,
        GeneratingVideo,
        Transitioning,
        Completed,
    }

    private WarSequenceState currentState = WarSequenceState.AwaitingGesture;
    private float bothHandsRaisedTimer;
    private float interactionElapsed;
    private bool pendingBothHandsRaised;
    private bool handPresenceDetected;
    private readonly object dataLock = new object();
    private bool outcomeLocked;
    private string lockedOutcomeKey = string.Empty;
    private GestureHintOverlay hintOverlay;
    private GenerationRitualOverlay ritualOverlay;
    private bool primedGenerationStarted;
    private bool primedGenerationCompleted;
    private VideoGenerationResult primedGenerationResult;
    private bool noHandsAudioPlaying;
    private Scene2WaitingMinigameController waitingMinigameController;
    private InteractionGateController interactionGate;
    private PromptCaptureWindow promptCaptureWindow;
    private float captureRaisedDuration;
    private float captureDefiantDuration;
    private string frozenPromptSeed = "AmbivalenceSeed";
    private bool promptSeedFrozen;
    private bool backgroundGenerationStarted;
    private bool isWaitingForGeneratedClip;
    private bool generatedClipReady;
    private string generatedClipPlayablePath = string.Empty;
    private const float GenerationRetryDelaySeconds = 1.5f;
    private float semanticCaptionStableTimer;
    private string pendingSemanticCaption = string.Empty;
    private string currentSemanticCaption = string.Empty;
    private const float SemanticCaptionStabilitySeconds = 0.2f;

    private void Start()
    {
        generatedVideoDurationSeconds = Mathf.Clamp(generatedVideoDurationSeconds, 3, 6);
        promptCaptureWindow = new PromptCaptureWindow(promptCaptureDurationSeconds);

        if (fallbackVideoPlayer == null)
        {
            fallbackVideoPlayer = GetComponent<VideoPlayer>();
        }

        if (fallbackVideoPlayer != null)
        {
            fallbackVideoPlayer.loopPointReached += OnFallbackVideoFinished;
            fallbackVideoPlayer.Stop();
        }

        EnsureSceneVideoManager();
        interactionGate = InteractionGateController.GetOrCreate("SceneInteractionGate", interactionUnlockDelaySeconds);

        SetFallbackDisplayActive(false);

        hintOverlay = GestureHintOverlay.CreateForScene("WarGestureHintOverlay");
        hintOverlay.Configure(
            GestureHintOverlay.HintKind.BothHandsRaise,
            "Raise Both Hands",
            "Hold the surrender gesture to trigger the war vision",
            GestureHintOverlay.OverlayTheme.WarCommand,
            GestureHintOverlay.IconStyle.Directive);
        hintOverlay.SetCustomGestureTexture(Resources.Load<Texture>("hands up"), false);
        hintOverlay.SetProgress(0f);
        hintOverlay.SetHintVisible(false, true);
        hintOverlay.SetSemanticCaptionVisible(true, true);
        hintOverlay.SetSemanticCaptionPaused(true);
        hintOverlay.ClearSemanticCaption();

        if (interactionGate != null)
        {
            interactionGate.Unlocked += HandleInteractionUnlocked;
            if (interactionGate.IsInteractionUnlocked)
            {
                HandleInteractionUnlocked();
            }
        }

        ritualOverlay = GenerationRitualOverlay.CreateForScene("WarGenerationRitualOverlay");
        ritualOverlay.Configure(GenerationRitualOverlay.RitualTheme.War, "Please wait");
        ritualOverlay.SetVisible(false, true);

        JourneySessionManager.Instance?.LogEvent(SceneManager.GetActiveScene().name, "scene2_started", "awaiting_surrender_gesture");

        if (preGenerateWarVideoOnSceneEnter)
        {
            StartCoroutine(BeginPrimedWarVisionGenerationAfterDelay());
        }

        SetNoHandsRaisedAudio(false);
    }

    private void OnDestroy()
    {
        if (fallbackVideoPlayer != null)
        {
            fallbackVideoPlayer.loopPointReached -= OnFallbackVideoFinished;
        }

        SetNoHandsRaisedAudio(false);

        if (waitingMinigameController != null && waitingMinigameController.IsLoaded)
        {
            waitingMinigameController.ForceCleanup();
        }

        if (interactionGate != null)
        {
            interactionGate.Unlocked -= HandleInteractionUnlocked;
        }
    }

    public void OnHandLandmarkDetected(HandLandmarkerResult result)
    {
        waitingMinigameController?.OnHandLandmarkDetected(result);

        bool bothRaised = false;
        handPresenceDetected = result.handLandmarks != null && result.handLandmarks.Count > 0;
        if (result.handLandmarks != null && result.handLandmarks.Count > 0)
        {
            int leftIndex = -1;
            int rightIndex = -1;

            if (result.handedness != null && result.handedness.Count == result.handLandmarks.Count)
            {
                for (int i = 0; i < result.handedness.Count; i++)
                {
                    var cls = result.handedness[i];
                    if (cls.categories == null || cls.categories.Count == 0)
                    {
                        continue;
                    }

                    string label = cls.categories[0].categoryName ?? cls.categories[0].displayName;
                    if (string.IsNullOrEmpty(label))
                    {
                        continue;
                    }

                    label = label.ToLowerInvariant();
                    if (label.Contains("left"))
                    {
                        leftIndex = i;
                    }
                    else if (label.Contains("right"))
                    {
                        rightIndex = i;
                    }
                }
            }

            if ((leftIndex == -1 || rightIndex == -1) && result.handLandmarks.Count >= 2)
            {
                leftIndex = 0;
                rightIndex = 1;
            }

            bool leftRaised = IsRaisedOpenHand(result, leftIndex);
            bool rightRaised = IsRaisedOpenHand(result, rightIndex);
            bothRaised = leftRaised && rightRaised;

            if (debugLog)
            {
                Debug.Log($"[WarGestureVideoDirector] leftRaised={leftRaised}, rightRaised={rightRaised}, bothRaised={bothRaised}");
            }
        }

        lock (dataLock)
        {
            pendingBothHandsRaised = bothRaised;
        }
    }

    private void Update()
    {
        bool isRaised;
        lock (dataLock)
        {
            isRaised = pendingBothHandsRaised;
        }

        hintOverlay?.SetSemanticCaptionPaused(!handPresenceDetected);
        if (handPresenceDetected)
        {
            UpdateWarSemanticCaption(isRaised);
        }

        UpdatePromptCapture(isRaised);

        if (currentState == WarSequenceState.Completed || currentState == WarSequenceState.Transitioning)
        {
            SetNoHandsRaisedAudio(false);
            return;
        }

        if (currentState == WarSequenceState.WaitingForPlaybackGate || isWaitingForGeneratedClip)
        {
            bool gateSatisfied = HasSatisfiedPlaybackGate();
            if (!isWaitingForGeneratedClip && generatedClipReady && gateSatisfied && !string.IsNullOrWhiteSpace(generatedClipPlayablePath))
            {
                StartCoroutine(PlayGeneratedClipAndAdvance(generatedClipPlayablePath));
                return;
            }

            ritualOverlay?.SetVisible(true, true);
            ritualOverlay?.SetIntensity(gateSatisfied ? 0.25f : Mathf.Clamp01(GetPlaybackGateProgress01()));
            return;
        }

        if (interactionGate != null && !interactionGate.IsInteractionUnlocked)
        {
            bothHandsRaisedTimer = 0f;
            hintOverlay?.SetProgress(0f);
            SetNoHandsRaisedAudio(false);
            return;
        }

        if (!handPresenceDetected)
        {
            bothHandsRaisedTimer = 0f;
            hintOverlay?.SetProgress(0f);
            SetNoHandsRaisedAudio(false);
            return;
        }

        interactionElapsed += Time.deltaTime;
        SetNoHandsRaisedAudio(!isRaised);

        if (!isRaised)
        {
            bothHandsRaisedTimer = 0f;
            hintOverlay?.SetProgress(0f);
        }
        else
        {
            bothHandsRaisedTimer += Time.deltaTime;
            hintOverlay?.SetProgress(bothHandsRaisedTimer / Mathf.Max(holdSecondsRequired, 0.01f));

            if (debugLog)
            {
                Debug.Log($"[WarGestureVideoDirector] surrender hold {bothHandsRaisedTimer:F2}s / {holdSecondsRequired:F2}s");
            }

            if (bothHandsRaisedTimer >= holdSecondsRequired)
            {
                string outcomeKey = interactionElapsed <= quickComplianceSeconds ? "Submission" : "Hesitation";
                TriggerWarVision(outcomeKey, "surrender_gesture");
                return;
            }
        }

        if (interactionElapsed >= surrenderTimeoutSeconds)
        {
            TriggerWarVision("Resistance", "surrender_timeout");
        }
    }

    private void UpdatePromptCapture(bool bothRaised)
    {
        if (promptCaptureWindow == null || promptCaptureWindow.IsPromptCaptureClosed)
        {
            return;
        }

        if (bothRaised)
        {
            captureRaisedDuration += Time.deltaTime;
        }
        else
        {
            captureDefiantDuration += Time.deltaTime;
        }

        bool justClosed = promptCaptureWindow.Advance(Time.deltaTime);
        if (justClosed)
        {
            if (debugLog)
            {
                Debug.Log("[WarGestureVideoDirector] Prompt capture window closed naturally.");
            }

            FreezePromptSeed();
            StartFrozenBackgroundGenerationIfNeeded();
        }
    }

    private void FreezePromptSeed()
    {
        if (promptSeedFrozen)
        {
            return;
        }

        if (captureRaisedDuration >= captureDefiantDuration + 1.25f)
        {
            frozenPromptSeed = "SurrenderSeed";
        }
        else if (captureDefiantDuration >= captureRaisedDuration + 1.25f)
        {
            frozenPromptSeed = "DefianceSeed";
        }
        else
        {
            frozenPromptSeed = "AmbivalenceSeed";
        }

        promptSeedFrozen = true;
        JourneySessionManager.Instance?.LogEvent(SceneManager.GetActiveScene().name, "prompt_seed_frozen", frozenPromptSeed, frozenPromptSeed);

        if (debugLog)
        {
            Debug.Log($"[WarGestureVideoDirector] Prompt seed frozen: {frozenPromptSeed} (raised={captureRaisedDuration:F2}s, defiant={captureDefiantDuration:F2}s)");
        }
    }

    private bool IsRaisedOpenHand(HandLandmarkerResult result, int handIndex)
    {
        if (handIndex < 0 || handIndex >= result.handLandmarks.Count)
        {
            return false;
        }

        var hand = result.handLandmarks[handIndex];
        if (hand.landmarks == null || hand.landmarks.Count <= 9)
        {
            return false;
        }

        var wrist = hand.landmarks[0];
        var palm = hand.landmarks[9];
        var middleTip = hand.landmarks[12];
        var indexTip = hand.landmarks[8];
        var pinkyTip = hand.landmarks[20];
        int extendedFingerCount = CountExtendedFingers(hand);
        bool isOpen = HandGestureDetector.IsOpenHand(hand, 0.14f) || extendedFingerCount >= 2;
        bool highEnough = palm.y < raisedHandYThreshold || wrist.y < raisedHandYThreshold + raisedWristSlack;
        float fingerSpan = Vector2.Distance(new Vector2(indexTip.x, indexTip.y), new Vector2(pinkyTip.x, pinkyTip.y));
        float wristToMiddle = Vector2.Distance(new Vector2(wrist.x, wrist.y), new Vector2(middleTip.x, middleTip.y));
        bool readableRaisedPose = fingerSpan >= minimumHandSeparation || wristToMiddle >= minimumHandSeparation;

        return isOpen && highEnough && readableRaisedPose;
    }

    private void TriggerWarVision(string outcomeKey, string detail)
    {
        if (currentState != WarSequenceState.AwaitingGesture)
        {
            return;
        }

        LockWarOutcome(outcomeKey, detail);
        lockedOutcomeKey = string.IsNullOrWhiteSpace(lockedOutcomeKey) ? outcomeKey : lockedOutcomeKey;
        currentState = WarSequenceState.WaitingForPlaybackGate;

        JourneySessionManager.Instance?.LogEvent(SceneManager.GetActiveScene().name, "gesture_confirmed", "surrender");
        JourneySessionManager.Instance?.RecordGestureSemantic(
            SceneManager.GetActiveScene().name,
            "war_surrender",
            BuildWarSemanticTags(outcomeKey),
            intensity: Mathf.Clamp01(bothHandsRaisedTimer / Mathf.Max(holdSecondsRequired, 0.01f)),
            duration: interactionElapsed,
            detail: detail);

        hintOverlay?.SetHintVisible(false);
        hintOverlay?.SetSemanticCaptionVisible(false, true);
        hintOverlay?.SetSemanticCaptionPaused(true);
        ritualOverlay?.Configure(GenerationRitualOverlay.RitualTheme.War, "Please wait");
        ritualOverlay?.SetIntensity(0.15f);
        ritualOverlay?.SetVisible(true, true);
        SetNoHandsRaisedAudio(false);

        if (debugLog)
        {
            Debug.Log($"[WarGestureVideoDirector] TriggerWarVision outcome={outcomeKey}, detail={detail}, promptSeedFrozen={promptSeedFrozen}");
        }

        if (!promptSeedFrozen)
        {
            FreezePromptSeed();
        }

        StartCoroutine(EnsureWaitingMinigameLoaded());
        StartFrozenBackgroundGenerationIfNeeded();
    }

    private void StartFrozenBackgroundGenerationIfNeeded()
    {
        if (backgroundGenerationStarted)
        {
            if (debugLog)
            {
                Debug.Log("[WarGestureVideoDirector] Background generation already started.");
            }

            return;
        }

        if (!promptSeedFrozen)
        {
            if (debugLog)
            {
                Debug.Log("[WarGestureVideoDirector] Background generation blocked: prompt seed not frozen yet.");
            }

            return;
        }

        if (poeVideoManager == null)
        {
            Debug.LogWarning("[WarGestureVideoDirector] Background generation blocked: PoeVideoManager is missing.");
            return;
        }

        if (debugLog)
        {
            Debug.Log($"[WarGestureVideoDirector] Starting frozen background generation with seed={frozenPromptSeed}");
        }

        backgroundGenerationStarted = true;
        StartCoroutine(GenerateWarVisionInBackground());
    }

    private IEnumerator GenerateWarVisionInBackground()
    {
        while (true)
        {
            if (!primedGenerationStarted)
            {
                if (debugLog)
                {
                    Debug.Log("[WarGestureVideoDirector] Requesting primed war vision generation.");
                }

                StartPrimedWarVisionGeneration(BuildGeneratedVideoPrompt());
            }

            if (!primedGenerationStarted)
            {
                Debug.LogWarning("[WarGestureVideoDirector] Primed generation did not start. Retrying.");
                yield return new WaitForSecondsRealtime(GenerationRetryDelaySeconds);
                continue;
            }

            while (!primedGenerationCompleted)
            {
                yield return null;
            }

            if (primedGenerationResult != null && primedGenerationResult.IsSuccess && !string.IsNullOrWhiteSpace(primedGenerationResult.playableUrlOrPath))
            {
                if (debugLog)
                {
                    Debug.Log($"[WarGestureVideoDirector] Generated war clip ready: {primedGenerationResult.playableUrlOrPath}");
                }

                generatedClipReady = true;
                generatedClipPlayablePath = primedGenerationResult.playableUrlOrPath;
                JourneySessionManager.Instance?.AddEndingVideoClip(new EndingVideoClipRecord
                {
                    segmentKey = "war-memory",
                    displayTitle = "War Memory",
                    playablePath = generatedClipPlayablePath,
                    durationSeconds = generatedVideoDurationSeconds,
                });
                yield break;
            }

            Debug.LogWarning($"[WarGestureVideoDirector] Primed generation failed. status={primedGenerationResult?.status.ToString() ?? "null"}, error={primedGenerationResult?.error ?? "unknown"}");
            primedGenerationStarted = false;
            primedGenerationCompleted = false;
            primedGenerationResult = null;
            poeVideoManager.StopPlaybackAndHide();
            yield return new WaitForSecondsRealtime(GenerationRetryDelaySeconds);
        }
    }

    private IEnumerator PlayGeneratedClipAndAdvance(string playablePath)
    {
        if (isWaitingForGeneratedClip)
        {
            yield break;
        }

        isWaitingForGeneratedClip = true;
        currentState = WarSequenceState.GeneratingVideo;
        yield return PauseWaitingMinigame();

        bool playbackCompleted = false;
        bool playbackSuccess = false;
        StartCoroutine(poeVideoManager.PlayPreparedVideoFlow(
            playablePath,
            success =>
            {
                playbackSuccess = success;
                playbackCompleted = true;
            }));

        while (!playbackCompleted)
        {
            yield return null;
        }

        isWaitingForGeneratedClip = false;
        ritualOverlay?.SetVisible(false);

        if (playbackSuccess)
        {
            yield return TransitionToNextScene();
            yield break;
        }

        currentState = WarSequenceState.WaitingForPlaybackGate;
    }

    private int CountExtendedFingers(NormalizedLandmarks hand)
    {
        if (hand.landmarks == null || hand.landmarks.Count < 21)
        {
            return 0;
        }

        int extended = 0;
        if (IsFingerExtended(hand, 8, 6)) extended++;
        if (IsFingerExtended(hand, 12, 10)) extended++;
        if (IsFingerExtended(hand, 16, 14)) extended++;
        if (IsFingerExtended(hand, 20, 18)) extended++;
        return extended;
    }

    private bool IsFingerExtended(NormalizedLandmarks hand, int tipIndex, int pipIndex)
    {
        if (hand.landmarks == null || hand.landmarks.Count <= Mathf.Max(tipIndex, pipIndex))
        {
            return false;
        }

        return hand.landmarks[tipIndex].y < hand.landmarks[pipIndex].y - fingerExtendedTolerance;
    }

    private IEnumerator EnsureWaitingMinigameLoaded()
    {
        if (waitingMinigameController == null)
        {
            waitingMinigameController = GetComponent<Scene2WaitingMinigameController>();
            if (waitingMinigameController == null)
            {
                waitingMinigameController = gameObject.AddComponent<Scene2WaitingMinigameController>();
            }
        }

        if (!waitingMinigameController.IsLoaded)
        {
            yield return waitingMinigameController.LoadMinigame();
        }
    }

    private IEnumerator PauseWaitingMinigame()
    {
        if (waitingMinigameController != null && waitingMinigameController.IsLoaded)
        {
            yield return waitingMinigameController.PauseAndUnloadMinigame();
        }
    }

    private void OnFallbackVideoFinished(VideoPlayer source)
    {
        StartCoroutine(TransitionToNextScene());
    }

    private IEnumerator TransitionToNextScene()
    {
        if (currentState == WarSequenceState.Transitioning || currentState == WarSequenceState.Completed)
        {
            yield break;
        }

        currentState = WarSequenceState.Transitioning;
        SetFallbackDisplayActive(false);

        if (elevenLabsMusic != null && !string.IsNullOrWhiteSpace(promptForVideoThree))
        {
            elevenLabsMusic.prompt = promptForVideoThree;
            elevenLabsMusic.duration = musicDurationVideoThreeSeconds;
        }

        JourneySessionManager.Instance?.LogEvent(SceneManager.GetActiveScene().name, "scene_complete", "war_sequence_finished");
        yield return SceneTransitionFader.FadeToSceneRoutine(nextSceneName, sceneFadeDuration, sceneFadeDuration);
        currentState = WarSequenceState.Completed;
    }

    private string BuildGeneratedVideoPrompt()
    {
        string tags;
        string tone;

        switch (frozenPromptSeed)
        {
            case "SurrenderSeed":
                tags = "raised hands, surrender, obedience, fear";
                tone = "immediate obedience, fear, and collapse under command";
                break;
            case "DefianceSeed":
                tags = "refusal, tension, guarded posture, resistance";
                tone = "defiance, pressure, and refusal under threat";
                break;
            default:
                tags = "hesitation, ambiguity, fear, unstable resolve";
                tone = "conflict, hesitation, and fear before surrender";
                break;
        }

        return generatedVideoPromptTemplate
            .Replace("{PROMPT_SEED}", frozenPromptSeed)
            .Replace("{OUTCOME_TONE}", tone)
            .Replace("{TAGS}", tags);
    }

    private void EnsureSceneVideoManager()
    {
        if (poeVideoManager == null)
        {
            GameObject managerObject = new GameObject("Scene2WarVideoManager");
            managerObject.transform.SetParent(transform, false);
            managerObject.AddComponent<VideoPlayer>();
            poeVideoManager = managerObject.AddComponent<PoeVideoManager>();
            poeVideoManager.generatingOverlayTitle = "Please wait";
            poeVideoManager.hideVideoDisplayAfterPlayback = false;
            poeVideoManager.videoFadeDuration = 0.2f;
            poeVideoManager.verboseDiagnostics = true;
        }

        if (poeVideoManager.ActiveBackendMode == PoeVideoManager.VideoBackendMode.LocalLtx)
        {
            generatedVideoDurationSeconds = 3;
            poeVideoManager.resolution = "720p";
            poeVideoManager.defaultDurationSeconds = 3;
            poeVideoManager.localLtxFrameRate = Mathf.Clamp(poeVideoManager.localLtxFrameRate, 8, 8);
            poeVideoManager.localLtxInferenceSteps = Mathf.Clamp(poeVideoManager.localLtxInferenceSteps, 8, 10);
            poeVideoManager.requestTimeoutSeconds = Mathf.Max(poeVideoManager.requestTimeoutSeconds, 720f);
        }
        else
        {
            generatedVideoDurationSeconds = 4;
            poeVideoManager.model = "veo-3.1-lite";
            poeVideoManager.resolution = "720p";
            poeVideoManager.defaultDurationSeconds = 4;
            poeVideoManager.requestTimeoutSeconds = Mathf.Max(poeVideoManager.requestTimeoutSeconds, generationTimeoutSeconds + 30f);
        }

        poeVideoManager.enabled = true;
        poeVideoManager.StopPlaybackAndHide();
    }

    private IEnumerator BeginPrimedWarVisionGenerationAfterDelay()
    {
        if (preGenerateDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(preGenerateDelaySeconds);
        }

        if (!promptSeedFrozen)
        {
            yield break;
        }

        StartFrozenBackgroundGenerationIfNeeded();
    }

    private void StartPrimedWarVisionGeneration(string prompt)
    {
        if (poeVideoManager == null || primedGenerationStarted || string.IsNullOrWhiteSpace(prompt))
        {
            if (debugLog)
            {
                Debug.Log($"[WarGestureVideoDirector] StartPrimedWarVisionGeneration skipped. manager={(poeVideoManager != null)}, started={primedGenerationStarted}, promptEmpty={string.IsNullOrWhiteSpace(prompt)}");
            }

            return;
        }

        primedGenerationResult = null;
        primedGenerationCompleted = false;

        primedGenerationStarted = poeVideoManager.StartBackgroundGeneration(
            prompt.Trim(),
            generatedVideoDurationSeconds,
            result =>
            {
                primedGenerationResult = result;
                primedGenerationCompleted = true;

                if (debugLog)
                {
                    Debug.Log($"[WarGestureVideoDirector] Background generation callback. success={result != null && result.IsSuccess}, status={result?.status.ToString() ?? "null"}");
                }
            },
            false);

        if (debugLog)
        {
            Debug.Log($"[WarGestureVideoDirector] StartBackgroundGeneration returned {primedGenerationStarted}.");
        }
    }

    private void SetFallbackDisplayActive(bool isActive)
    {
        if (fallbackVideoDisplay != null)
        {
            fallbackVideoDisplay.SetActive(isActive);
        }
    }

    private List<string> BuildWarSemanticTags(string outcomeKey)
    {
        List<string> tags = new List<string> { "surrender" };
        switch (outcomeKey)
        {
            case "Submission":
                tags.Add("obedience");
                tags.Add("fear");
                break;
            case "Hesitation":
                tags.Add("hesitation");
                tags.Add("uncertainty");
                break;
            default:
                tags.Add("defiance");
                tags.Add("resistance");
                break;
        }

        return tags;
    }

    private void LockWarOutcome(string outcomeKey, string detail)
    {
        if (outcomeLocked)
        {
            return;
        }

        outcomeLocked = JourneySessionManager.Instance?.TryRecordSceneOutcome("2", outcomeKey, detail) ?? true;
        lockedOutcomeKey = outcomeKey;
    }

    private void SetNoHandsRaisedAudio(bool shouldPlay)
    {
        if (noHandsRaisedLoopAudio == null)
        {
            return;
        }

        if (shouldPlay)
        {
            if (noHandsAudioPlaying)
            {
                return;
            }

            noHandsRaisedLoopAudio.loop = true;
            noHandsRaisedLoopAudio.Play();
            noHandsAudioPlaying = true;
            return;
        }

        if (!noHandsAudioPlaying)
        {
            return;
        }

        noHandsRaisedLoopAudio.Stop();
        noHandsAudioPlaying = false;
    }

    private void HandleInteractionUnlocked()
    {
        hintOverlay?.SetHintVisible(true);
    }

    private void UpdateWarSemanticCaption(bool bothRaised)
    {
        string targetCaption = BuildWarPromptPreview(bothRaised);
        if (!string.Equals(targetCaption, pendingSemanticCaption))
        {
            pendingSemanticCaption = targetCaption;
            semanticCaptionStableTimer = 0f;
            return;
        }

        if (string.Equals(currentSemanticCaption, targetCaption))
        {
            return;
        }

        semanticCaptionStableTimer += Time.deltaTime;
        if (semanticCaptionStableTimer >= SemanticCaptionStabilitySeconds)
        {
            currentSemanticCaption = targetCaption;
            hintOverlay?.SetSemanticCaption(currentSemanticCaption);
        }
    }

    private string BuildWarPromptPreview(bool bothRaised)
    {
        float raise01 = Mathf.Clamp01(bothHandsRaisedTimer / Mathf.Max(holdSecondsRequired, 0.01f));
        float time01 = Mathf.Clamp01(interactionElapsed / Mathf.Max(surrenderTimeoutSeconds, 0.01f));
        bool leaningHesitant = interactionElapsed > quickComplianceSeconds * 0.8f && interactionElapsed < surrenderTimeoutSeconds * 0.85f;

        string posturePhrase = bothRaised
            ? raise01 > 0.7f
                ? "raised hands, surrender, exposure"
                : "lifting hands, uncertainty, compliance"
            : time01 > 0.6f
                ? "tense body, refusal, threat"
                : "guarded posture, oscillation, pressure";

        string environmentPhrase = raise01 > 0.55f || bothRaised
            ? "ash, smoke, barricades, mechanical threat"
            : "checkpoint, siren haze, drifting ash, collapsing certainty";

        if (bothRaised && !leaningHesitant)
        {
            return "submission, obedience, panic, surrender, " + environmentPhrase + ", " + posturePhrase;
        }

        if (bothRaised || leaningHesitant)
        {
            return "hesitation, uncertainty, fear, unstable resolve, " + environmentPhrase + ", " + posturePhrase;
        }

        return "defiance, resistance, panic, guarded body, " + environmentPhrase + ", " + posturePhrase;
    }

    private bool HasSatisfiedPlaybackGate()
    {
        return waitingMinigameController != null && waitingMinigameController.HasMetMinimumPlayTime(scene2MinigameMinimumPlaySeconds);
    }

    private float GetPlaybackGateProgress01()
    {
        if (waitingMinigameController == null)
        {
            return 0f;
        }

        return Mathf.Clamp01(waitingMinigameController.ActivePlaySeconds / Mathf.Max(scene2MinigameMinimumPlaySeconds, 0.01f));
    }
}
