using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.EventSystems;

[RequireComponent(typeof(AudioSource))]
public class Scene3VoiceAndVideoController : MonoBehaviour
{
    [Header("Video Playback")]
    public VideoPlayer videoPlayer;
    public VideoClip firstVideoClip;
    public VideoClip secondVideoClip;

    [Header("Intro Audio")]
    public AudioClip introVoiceClip;

    [Header("Volume UI")]
    public Text volumeText;
    public TMP_Text volumeTextTMP;

    [Header("Voice Threshold")]
    [Range(0.001f, 1f)]
    public float volumeThreshold = 0.1f;
    public float requiredLoudSeconds = 1.0f;
    public int sampleSize = 1024;

    [Header("Microphone")]
    public int microphoneDeviceIndex = 0;
    public int microphoneBufferSeconds = 10;
    public int microphoneSampleRate = 44100;
    public float microphoneStartTimeoutSeconds = 2.0f;
    public bool useClipDataForVolume = true;
    public float debugLogIntervalSeconds = 0.5f;

    [Header("Medical Outcome")]
    public float despairTimeoutSeconds = 20.0f;
    public float apotheosisTimeWindowSeconds = 4.0f;
    public float apotheosisVolumeMultiplier = 1.75f;
    public float interactionUnlockDelaySeconds = 30f;
    public float promptCaptureDurationSeconds = 10f;
    public float scene3MinigameMinimumPlaySeconds = 10f;
    public string nextSceneName = "4";
    public float sceneFadeDuration = 0.65f;
    public int generatedMedicalVideoDurationSeconds = 8;
    public string scene3MinigameScenePath = "fypmng2";

    [Header("Second Phase Playback")]
    public bool preferGeneratedSecondPhaseVideo = true;

    [Header("Voice Red Filter")]
    public bool enableVoiceRedFilter = true;
    [Range(0f, 1f)]
    public float redFilterMaxAlpha = 0.6f;
    public float redFilterResponseMultiplier = 1.6f;
    public float redFilterSmoothing = 4.5f;
    public Color redFilterColor = new Color(0.82f, 0.08f, 0.08f, 0f);

    private AudioSource micAudioSource;
    private string micDevice;
    private float loudTimer;
    private bool hasSwitchedToSecondView;
    private bool medicalOutcomeLocked;
    private float interactionElapsed;
    private float peakVolume;
    private float lastDebugLogTime;
    private GestureHintOverlay hintOverlay;
    private GenerationRitualOverlay ritualOverlay;
    private bool isTransitioning;
    private string lockedOutcomeKey = string.Empty;
    private PoeVideoManager generatedVideoManager;
    private bool microphoneAvailable;
    private bool isGeneratingMedicalClip;
    private Canvas redFilterCanvas;
    private Image redFilterImage;
    private Canvas volumeStatusCanvas;
    private TMP_Text runtimeVolumeStatusText;
    private float currentRedFilterAlpha;
    private CameraGestureControl cameraGestureControl;
    private float currentVolume;
    private InteractionGateController interactionGate;
    private PromptCaptureWindow promptCaptureWindow;
    private ScenePlaybackGateTimer playbackGateTimer;
    private bool promptSeedFrozen;
    private string frozenPromptSeed = "SilenceSeed";
    private float captureSilenceDuration;
    private float capturePleaDuration;
    private float captureAscensionDuration;
    private bool generatedClipReady;
    private string generatedClipPlayablePath = string.Empty;
    private bool backgroundGenerationStarted;
    private bool isPlayingGeneratedClip;
    private bool hasStartedFallbackSecondClip;
    private Scene3MinigamePointerBridge minigamePointerBridge;
    private Scene loadedMinigameScene;
    private bool minigameSceneLoaded;
    private Camera minigameOverlayCamera;
    private EventSystem[] scene3EventSystems;
    private const string Scene3MinigameLayerName = "Scene3Minigame";
    private const float GenerationRetryDelaySeconds = 1.5f;

    public float LiveVolume => currentVolume;
    public bool HasLiveMicrophoneInput => microphoneAvailable && micAudioSource != null && micAudioSource.clip != null;
    public float NormalizedLiveVolume => Mathf.Clamp01(currentVolume / Mathf.Max(volumeThreshold, 0.001f));

    private void Start()
    {
        cameraGestureControl = Camera.main != null ? Camera.main.GetComponent<CameraGestureControl>() : null;
        generatedMedicalVideoDurationSeconds = Mathf.Clamp(generatedMedicalVideoDurationSeconds, 4, 6);
        promptCaptureWindow = new PromptCaptureWindow(promptCaptureDurationSeconds);

        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
        }

        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached += OnVideoFinished;
        }

        if (videoPlayer != null && firstVideoClip != null)
        {
            videoPlayer.clip = firstVideoClip;
            videoPlayer.Play();
        }

        micAudioSource = GetComponent<AudioSource>();
        micAudioSource.loop = true;
        micAudioSource.playOnAwake = false;
        micAudioSource.mute = false;
        micAudioSource.volume = 1f;

        if (introVoiceClip != null)
        {
            micAudioSource.PlayOneShot(introVoiceClip);
        }

        interactionGate = InteractionGateController.GetOrCreate("SceneInteractionGate", interactionUnlockDelaySeconds);
        playbackGateTimer = GetComponent<ScenePlaybackGateTimer>();
        if (playbackGateTimer == null)
        {
            playbackGateTimer = gameObject.AddComponent<ScenePlaybackGateTimer>();
        }
        playbackGateTimer.Configure(scene3MinigameMinimumPlaySeconds);
        playbackGateTimer.ResetTimer();
        scene3EventSystems = GetComponentsInScene<EventSystem>(SceneManager.GetActiveScene(), scene3MinigameScenePath);

        hintOverlay = GestureHintOverlay.CreateForScene("MedicalVoiceHintOverlay");
        hintOverlay.Configure(
            GestureHintOverlay.HintKind.VoicePulse,
            "Speak Louder",
            "Sustain your voice to awaken the system",
            GestureHintOverlay.OverlayTheme.MedicalSanctuary,
            GestureHintOverlay.IconStyle.Ritual);
        hintOverlay.SetProgress(0f);
        hintOverlay.SetHintVisible(false, true);
        hintOverlay.SetSemanticCaptionVisible(true, true);
        hintOverlay.SetSemanticCaption("Silence");

        if (interactionGate != null)
        {
            interactionGate.Unlocked += HandleInteractionUnlocked;
            if (interactionGate.IsInteractionUnlocked)
            {
                HandleInteractionUnlocked();
            }
        }

        ritualOverlay = GenerationRitualOverlay.CreateForScene("MedicalGenerationRitualOverlay");
        ritualOverlay.Configure(GenerationRitualOverlay.RitualTheme.Medical, "Please wait");
        ritualOverlay.SetIntensity(0.1f);
        ritualOverlay.SetVisible(false, true);

        if (enableVoiceRedFilter)
        {
            EnsureVoiceRedFilter();
            SetVoiceRedFilterAlpha(0f, true);
        }

        StartCoroutine(StartMicrophoneRoutine());
        JourneySessionManager.Instance?.LogEvent(SceneManager.GetActiveScene().name, "scene3_started", "medical_sequence_ready");
    }

    public void OnHandLandmarkDetected(Mediapipe.Tasks.Vision.HandLandmarker.HandLandmarkerResult result)
    {
        if (cameraGestureControl == null)
        {
            CameraGestureControl[] controls = FindObjectsOfType<CameraGestureControl>(true);
            if (controls.Length > 0)
            {
                cameraGestureControl = controls[0];
            }
        }

        cameraGestureControl?.OnHandLandmarkDetected(result);
        UpdateScene3MinigamePointer(result);
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoFinished;
        }

        if (!string.IsNullOrEmpty(micDevice))
        {
            Microphone.End(micDevice);
        }

        if (redFilterCanvas != null)
        {
            Destroy(redFilterCanvas.gameObject);
        }

        if (volumeStatusCanvas != null)
        {
            Destroy(volumeStatusCanvas.gameObject);
        }

        if (interactionGate != null)
        {
            interactionGate.Unlocked -= HandleInteractionUnlocked;
        }

        if (minigamePointerBridge != null)
        {
            minigamePointerBridge.ClearPointer();
        }
    }

    private void Update()
    {
        bool hasReadableMicrophone = micAudioSource != null && micAudioSource.clip != null;
        float volume = hasReadableMicrophone ? GetCurrentVolume() : 0f;
        currentVolume = volume;
        peakVolume = Mathf.Max(peakVolume, volume);

        if (debugLogIntervalSeconds > 0f && Time.unscaledTime - lastDebugLogTime >= debugLogIntervalSeconds)
        {
            lastDebugLogTime = Time.unscaledTime;
            bool isRecording = microphoneAvailable && !string.IsNullOrEmpty(micDevice) && Microphone.IsRecording(micDevice);
            int position = !string.IsNullOrEmpty(micDevice) ? Microphone.GetPosition(micDevice) : 0;
            Debug.Log($"[Scene3VoiceAndVideoController] recording={isRecording}, pos={position}, volume={volume:F4}");
        }

        UpdateVolumeLabels(volume);
        UpdateVoiceRedFilter(volume);
        UpdateMedicalSemanticCaption(volume);
        UpdatePromptCapture(volume);

        if (!hasSwitchedToSecondView)
        {
            UpdateFirstPhase(volume);
            return;
        }

        UpdateSecondPhase(volume);
    }

    private void UpdatePromptCapture(float volume)
    {
        if (promptCaptureWindow == null || promptCaptureWindow.IsPromptCaptureClosed)
        {
            return;
        }

        if (volume < volumeThreshold * 0.35f)
        {
            captureSilenceDuration += Time.deltaTime;
        }
        else if (volume >= volumeThreshold * apotheosisVolumeMultiplier)
        {
            captureAscensionDuration += Time.deltaTime;
        }
        else
        {
            capturePleaDuration += Time.deltaTime;
        }

        bool justClosed = promptCaptureWindow.Advance(Time.deltaTime);
        if (justClosed)
        {
            Debug.Log("[Scene3VoiceAndVideoController] Prompt capture window closed naturally.");
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

        if (captureAscensionDuration >= capturePleaDuration && captureAscensionDuration >= captureSilenceDuration)
        {
            frozenPromptSeed = "AscensionSeed";
        }
        else if (capturePleaDuration >= captureSilenceDuration)
        {
            frozenPromptSeed = "PleaSeed";
        }
        else
        {
            frozenPromptSeed = "SilenceSeed";
        }

        promptSeedFrozen = true;
        JourneySessionManager.Instance?.LogEvent(SceneManager.GetActiveScene().name, "prompt_seed_frozen", frozenPromptSeed, frozenPromptSeed);
        Debug.Log($"[Scene3VoiceAndVideoController] Prompt seed frozen: {frozenPromptSeed} (silence={captureSilenceDuration:F2}s, plea={capturePleaDuration:F2}s, ascension={captureAscensionDuration:F2}s)");
    }

    private void UpdateFirstPhase(float volume)
    {
        if (interactionGate != null && !interactionGate.IsInteractionUnlocked)
        {
            loudTimer = 0f;
            hintOverlay?.SetProgress(0f);
            return;
        }

        interactionElapsed += Time.deltaTime;
        hintOverlay?.SetProgress(volumeThreshold > 0f ? volume / volumeThreshold : 0f);

        if (volume >= volumeThreshold)
        {
            loudTimer += Time.deltaTime;
        }
        else
        {
            loudTimer = 0f;
        }

        if (interactionElapsed >= despairTimeoutSeconds)
        {
            hintOverlay?.SetHintVisible(false);
            LockMedicalOutcome("Despair", "voice_timeout", Mathf.Clamp01(peakVolume / Mathf.Max(volumeThreshold, 0.001f)));
            SwitchToSecondView();
            return;
        }

        if (loudTimer >= requiredLoudSeconds)
        {
            string outcomeKey = IsApotheosis(volume) ? "Apotheosis" : "Supplication";
            hintOverlay?.SetHintVisible(false);
            LockMedicalOutcome(outcomeKey, "voice_threshold_reached", Mathf.Clamp01(peakVolume / Mathf.Max(volumeThreshold * apotheosisVolumeMultiplier, 0.001f)));
            SwitchToSecondView();
        }
    }

    private void UpdateSecondPhase(float volume)
    {
        if (isPlayingGeneratedClip || isTransitioning)
        {
            return;
        }

        if (playbackGateTimer != null)
        {
            if (!HasMetScene3PlaybackGate())
            {
                ritualOverlay?.SetVisible(true, true);
                ritualOverlay?.SetIntensity(Mathf.Clamp01(GetScene3PlaybackGateProgressSeconds() / Mathf.Max(scene3MinigameMinimumPlaySeconds, 0.01f)));
                return;
            }
        }

        if (generatedClipReady && !string.IsNullOrWhiteSpace(generatedClipPlayablePath))
        {
            StartCoroutine(PlayGeneratedClipAndTransition(generatedClipPlayablePath));
            return;
        }

        if (!preferGeneratedSecondPhaseVideo)
        {
            if (videoPlayer != null && secondVideoClip != null)
            {
                if (!hasStartedFallbackSecondClip)
                {
                    hasStartedFallbackSecondClip = true;
                    ritualOverlay?.SetVisible(false);
                    videoPlayer.clip = secondVideoClip;
                    videoPlayer.Play();
                }

                return;
            }

            StartCoroutine(TransitionToNextScene());
            return;
        }

        JourneySessionManager session = JourneySessionManager.Instance;
        EndingVideoPreparationState state = session != null ? session.EndingVideoPreparationState : EndingVideoPreparationState.NotStarted;
        bool showRitual = isGeneratingMedicalClip || state == EndingVideoPreparationState.Generating || (playbackGateTimer != null && !HasMetScene3PlaybackGate());

        ritualOverlay?.SetVisible(showRitual);
        if (showRitual)
        {
            float normalizedVoice = Mathf.Clamp01(volume / Mathf.Max(volumeThreshold * 1.5f, 0.001f));
            float pulse = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.6f)) * 0.22f;
            ritualOverlay?.SetIntensity(Mathf.Clamp01(0.18f + normalizedVoice * 0.72f + pulse));
        }
        else
        {
            ritualOverlay?.SetIntensity(0.08f);
        }
    }

    private void UpdateVolumeLabels(float volume)
    {
        EnsureRuntimeVolumeStatus();

        float normalized = Mathf.Clamp01(NormalizedLiveVolume);
        string semanticState =
            volume < volumeThreshold * 0.35f ? "Silence" :
            volume >= volumeThreshold * apotheosisVolumeMultiplier ? "Ascension" :
            "Plea";
        string label = $"Voice {Mathf.RoundToInt(normalized * 100f)}%  {semanticState}";

        if (volumeText != null)
        {
            volumeText.text = $"Volume: {Mathf.RoundToInt(normalized * 100f)}%";
            volumeText.enabled = true;
        }

        if (volumeTextTMP != null)
        {
            volumeTextTMP.text = label;
            volumeTextTMP.enabled = true;
            volumeTextTMP.color = Color.Lerp(new Color(0.85f, 0.9f, 1f, 0.92f), new Color(1f, 0.3f, 0.3f, 1f), normalized);
        }

        if (runtimeVolumeStatusText != null)
        {
            runtimeVolumeStatusText.text = label;
            runtimeVolumeStatusText.color = Color.Lerp(new Color(0.9f, 0.95f, 1f, 0.98f), new Color(1f, 0.35f, 0.35f, 1f), normalized);
        }
    }

    private void UpdateVoiceRedFilter(float volume)
    {
        if (!enableVoiceRedFilter)
        {
            return;
        }

        EnsureVoiceRedFilter();
        if (redFilterImage == null)
        {
            return;
        }

        float normalized = Mathf.Clamp01(volume / Mathf.Max(volumeThreshold * redFilterResponseMultiplier, 0.001f));
        float targetAlpha = normalized * redFilterMaxAlpha;
        currentRedFilterAlpha = Mathf.Lerp(currentRedFilterAlpha, targetAlpha, Time.unscaledDeltaTime * Mathf.Max(0.01f, redFilterSmoothing));
        SetVoiceRedFilterAlpha(currentRedFilterAlpha, false);
    }

    private void EnsureVoiceRedFilter()
    {
        if (redFilterCanvas != null && redFilterImage != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("Scene3VoiceRedFilterCanvas");
        redFilterCanvas = canvasObject.AddComponent<Canvas>();
        redFilterCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        redFilterCanvas.sortingOrder = 4300;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>().enabled = false;

        GameObject imageObject = new GameObject("VoiceRedFilter");
        imageObject.transform.SetParent(canvasObject.transform, false);
        redFilterImage = imageObject.AddComponent<Image>();
        redFilterImage.raycastTarget = false;

        RectTransform rect = redFilterImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void EnsureRuntimeVolumeStatus()
    {
        if (runtimeVolumeStatusText != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("Scene3RuntimeVolumeStatusCanvas");
        volumeStatusCanvas = canvasObject.AddComponent<Canvas>();
        volumeStatusCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        volumeStatusCanvas.sortingOrder = 4600;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>().enabled = false;

        GameObject labelObject = new GameObject("Scene3RuntimeVolumeStatus");
        labelObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = labelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 48f);
        rect.sizeDelta = new Vector2(820f, 80f);

        runtimeVolumeStatusText = labelObject.AddComponent<TextMeshProUGUI>();
        runtimeVolumeStatusText.alignment = TextAlignmentOptions.Center;
        runtimeVolumeStatusText.fontSize = 34f;
        runtimeVolumeStatusText.enableWordWrapping = false;
        runtimeVolumeStatusText.text = "Voice 0%  Silence";
        runtimeVolumeStatusText.color = new Color(0.9f, 0.95f, 1f, 0.98f);
    }

    private void SetVoiceRedFilterAlpha(float alpha, bool instant)
    {
        if (redFilterImage == null)
        {
            return;
        }

        Color color = redFilterColor;
        color.a = Mathf.Clamp01(alpha);
        redFilterImage.color = color;

        if (redFilterCanvas != null)
        {
            redFilterCanvas.gameObject.SetActive(instant || color.a > 0.001f);
        }
    }

    private IEnumerator StartMicrophoneRoutine()
    {
        microphoneAvailable = false;
        micDevice = string.Empty;

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            Debug.Log("[Scene3VoiceAndVideoController] Requesting microphone authorization.");
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            Debug.LogWarning("[Scene3VoiceAndVideoController] Microphone authorization denied or unavailable.");
            yield break;
        }

        string[] devices = Microphone.devices;
        Debug.Log($"[Scene3VoiceAndVideoController] Microphone devices: {(devices != null && devices.Length > 0 ? string.Join(", ", devices) : "<none>")}");

        if (devices == null || devices.Length == 0)
        {
            Debug.LogWarning("[Scene3VoiceAndVideoController] No microphone device found.");
            yield break;
        }

        List<int> candidateIndices = new List<int>();
        if (microphoneDeviceIndex >= 0 && microphoneDeviceIndex < devices.Length)
        {
            candidateIndices.Add(microphoneDeviceIndex);
        }

        for (int i = 0; i < devices.Length; i++)
        {
            if (!candidateIndices.Contains(i))
            {
                candidateIndices.Add(i);
            }
        }

        foreach (int index in candidateIndices)
        {
            string candidateDevice = devices[index];
            Debug.Log($"[Scene3VoiceAndVideoController] Trying microphone device: {candidateDevice}");

            if (!string.IsNullOrEmpty(micDevice))
            {
                Microphone.End(micDevice);
            }

            micDevice = candidateDevice;
            micAudioSource.clip = Microphone.Start(micDevice, true, microphoneBufferSeconds, microphoneSampleRate);

            if (micAudioSource.clip == null)
            {
                Debug.LogWarning($"[Scene3VoiceAndVideoController] Microphone.Start returned null for device '{micDevice}'.");
                continue;
            }

            float startTime = Time.realtimeSinceStartup;
            bool started = false;
            while (Time.realtimeSinceStartup - startTime < microphoneStartTimeoutSeconds)
            {
                if (Microphone.GetPosition(micDevice) > 0)
                {
                    started = true;
                    break;
                }

                yield return null;
            }

            if (!started)
            {
                Debug.LogWarning($"[Scene3VoiceAndVideoController] Microphone device '{micDevice}' did not start within {microphoneStartTimeoutSeconds:F1}s.");
                Microphone.End(micDevice);
                micAudioSource.clip = null;
                continue;
            }

            micAudioSource.Play();
            microphoneAvailable = true;
            Debug.Log($"[Scene3VoiceAndVideoController] Microphone active on device '{micDevice}'.");
            yield break;
        }

        microphoneAvailable = false;
        micDevice = string.Empty;
        Debug.LogWarning("[Scene3VoiceAndVideoController] Failed to initialize any microphone device.");
    }

    private float GetCurrentVolume()
    {
        float[] data = new float[sampleSize];

        if (useClipDataForVolume)
        {
            int position = !string.IsNullOrEmpty(micDevice) ? Microphone.GetPosition(micDevice) : 0;
            int clipSamples = micAudioSource.clip.samples;
            if (clipSamples <= 0)
            {
                return 0f;
            }

            int start = position - sampleSize;
            if (start < 0)
            {
                start += clipSamples;
            }

            start %= clipSamples;

            if (start + sampleSize <= clipSamples)
            {
                micAudioSource.clip.GetData(data, start);
            }
            else
            {
                micAudioSource.GetOutputData(data, 0);
            }
        }
        else
        {
            micAudioSource.GetOutputData(data, 0);
        }

        float sum = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i] * data[i];
        }

        float rms = Mathf.Sqrt(sum / data.Length);
        return float.IsNaN(rms) ? 0f : rms;
    }

    private bool IsApotheosis(float currentVolume)
    {
        return interactionElapsed <= apotheosisTimeWindowSeconds
            || peakVolume >= volumeThreshold * apotheosisVolumeMultiplier
            || currentVolume >= volumeThreshold * apotheosisVolumeMultiplier;
    }

    private void SwitchToSecondView()
    {
        if (hasSwitchedToSecondView)
        {
            return;
        }

        hasSwitchedToSecondView = true;
        hintOverlay?.SetHintVisible(false);
        hintOverlay?.SetSemanticCaption(BuildLockedMedicalCaption());
        JourneySessionManager.Instance?.LogEvent(SceneManager.GetActiveScene().name, "gesture_confirmed", "voice_threshold_or_timeout");
        JourneySessionManager.Instance?.RecordGestureSemantic(
            SceneManager.GetActiveScene().name,
            "medical_second_phase",
            BuildSecondPhaseSemanticTags(),
            intensity: Mathf.Clamp01(peakVolume / Mathf.Max(volumeThreshold * 1.5f, 0.001f)),
            duration: interactionElapsed,
            detail: lockedOutcomeKey);

        if (playbackGateTimer != null)
        {
            playbackGateTimer.ResetTimer();
            playbackGateTimer.Begin();
        }

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }

        Debug.Log($"[Scene3VoiceAndVideoController] Switching to second view. promptSeedFrozen={promptSeedFrozen}, outcome={lockedOutcomeKey}");
        if (!promptSeedFrozen)
        {
            FreezePromptSeed();
        }

        StartFrozenBackgroundGenerationIfNeeded();
    }

    private void OnVideoFinished(VideoPlayer source)
    {
        if (preferGeneratedSecondPhaseVideo || source == null || source.clip != secondVideoClip || !hasSwitchedToSecondView)
        {
            return;
        }

        hintOverlay?.SetHintVisible(false);
        JourneySessionManager.Instance?.LogEvent(SceneManager.GetActiveScene().name, "scene_complete", "medical_sequence_finished");
        StartCoroutine(TransitionToNextScene());
    }

    private void LockMedicalOutcome(string outcomeKey, string detail, float normalizedIntensity)
    {
        if (medicalOutcomeLocked)
        {
            return;
        }

        medicalOutcomeLocked = JourneySessionManager.Instance?.TryRecordSceneOutcome("3", outcomeKey, detail) ?? true;
        lockedOutcomeKey = outcomeKey;
        JourneySessionManager.Instance?.RecordGestureSemantic(
            SceneManager.GetActiveScene().name,
            "medical_invocation",
            BuildMedicalSemanticTags(outcomeKey),
            intensity: Mathf.Clamp01(normalizedIntensity),
            duration: interactionElapsed,
            detail: detail);
    }

    private List<string> BuildMedicalSemanticTags(string outcomeKey)
    {
        List<string> tags = new List<string> { "medical", "invocation" };
        switch (outcomeKey)
        {
            case "Apotheosis":
                tags.Add("medical-transcendence");
                tags.Add("becoming-god");
                tags.Add("ascension");
                break;
            case "Supplication":
                tags.Add("supplication");
                tags.Add("plea");
                tags.Add("prayer");
                break;
            default:
                tags.Add("faith-failure");
                tags.Add("helplessness");
                tags.Add("silence");
                break;
        }

        return tags;
    }

    private List<string> BuildSecondPhaseSemanticTags()
    {
        List<string> tags = new List<string> { "medical", "witnessing", "ritual-wait" };
        switch (lockedOutcomeKey)
        {
            case "Apotheosis":
                tags.Add("machine-pulse");
                tags.Add("transcendence");
                break;
            case "Supplication":
                tags.Add("plea");
                tags.Add("forgiveness");
                break;
            default:
                tags.Add("plague");
                tags.Add("despair");
                break;
        }

        return tags;
    }

    private IEnumerator TransitionToNextScene()
    {
        if (isTransitioning)
        {
            yield break;
        }

        isTransitioning = true;
        isGeneratingMedicalClip = false;
        ritualOverlay?.SetVisible(false);
        yield return SceneTransitionFader.FadeToSceneRoutine(nextSceneName, sceneFadeDuration, sceneFadeDuration);
    }

    private void StartFrozenBackgroundGenerationIfNeeded()
    {
        if (backgroundGenerationStarted)
        {
            Debug.Log("[Scene3VoiceAndVideoController] Background generation already started.");
            return;
        }

        if (!promptSeedFrozen)
        {
            Debug.Log("[Scene3VoiceAndVideoController] Background generation blocked: prompt seed not frozen yet.");
            return;
        }

        Debug.Log($"[Scene3VoiceAndVideoController] Starting frozen background generation with seed={frozenPromptSeed}");
        backgroundGenerationStarted = true;
        if (!minigameSceneLoaded)
        {
            Debug.Log("[Scene3VoiceAndVideoController] Starting scene3 minigame load from prompt freeze.");
            StartCoroutine(EnsureScene3MinigameLoaded());
        }
        StartCoroutine(GenerateMedicalSecondPhaseVideoInBackground());
    }

    private IEnumerator GenerateMedicalSecondPhaseVideoInBackground()
    {
        ritualOverlay?.Configure(GenerationRitualOverlay.RitualTheme.Medical, "Please wait");
        ritualOverlay?.SetVisible(true, true);
        isGeneratingMedicalClip = true;

        while (true)
        {
            PoeVideoManager manager = EnsureGeneratedVideoManager();
            JourneySessionManager session = JourneySessionManager.Instance;
            int journeyId = session != null ? session.JourneyId : -1;
            VideoGenerationResult generatedResult = null;
            bool generationCompleted = false;
            bool started = manager.StartBackgroundGeneration(
                BuildMedicalGeneratedVideoPrompt(),
                generatedMedicalVideoDurationSeconds,
                result =>
                {
                    generatedResult = result;
                    generationCompleted = true;
                },
                false);

            if (!started)
            {
                Debug.LogWarning("[Scene3VoiceAndVideoController] Scene3 background generation did not start. Retrying.");
                yield return new WaitForSecondsRealtime(GenerationRetryDelaySeconds);
                continue;
            }

            while (!generationCompleted)
            {
                if (JourneySessionManager.Instance != null && !JourneySessionManager.Instance.IsCurrentJourney(journeyId))
                {
                    isGeneratingMedicalClip = false;
                    yield break;
                }

                yield return null;
            }

            if (generatedResult != null && generatedResult.IsSuccess)
            {
                Debug.Log($"[Scene3VoiceAndVideoController] Generated medical clip ready: {generatedResult.playableUrlOrPath}");
                generatedClipReady = true;
                generatedClipPlayablePath = generatedResult.playableUrlOrPath;
                JourneySessionManager.Instance?.AddEndingVideoClip(new EndingVideoClipRecord
                {
                    segmentKey = "medical-memory",
                    displayTitle = "Medical Memory",
                    playablePath = generatedClipPlayablePath,
                    durationSeconds = generatedMedicalVideoDurationSeconds,
                });

                EndingVideoPreparationCoordinator.Instance?.BeginPreparationIfNeeded(SceneManager.GetActiveScene().name, "scene3_generated_medical_memory");
                isGeneratingMedicalClip = false;
                yield break;
            }

            Debug.LogWarning($"[Scene3VoiceAndVideoController] Scene3 generation failed. status={generatedResult?.status.ToString() ?? "null"}, error={generatedResult?.error ?? "unknown"}");
            yield return new WaitForSecondsRealtime(GenerationRetryDelaySeconds);
        }
    }

    private IEnumerator PlayGeneratedClipAndTransition(string playablePath)
    {
        if (isTransitioning || isPlayingGeneratedClip)
        {
            yield break;
        }

        isPlayingGeneratedClip = true;
        generatedClipReady = false;
        bool playbackCompleted = false;
        bool playbackSuccess = false;

        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
        }

        ritualOverlay?.SetVisible(false);

        PoeVideoManager manager = EnsureGeneratedVideoManager();
        StartCoroutine(manager.PlayPreparedVideoFlow(
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

        if (playbackSuccess)
        {
            yield return TransitionToNextScene();
        }
        else
        {
            generatedClipReady = true;
        }

        isPlayingGeneratedClip = false;
    }

    private PoeVideoManager EnsureGeneratedVideoManager()
    {
        if (generatedVideoManager != null)
        {
            return generatedVideoManager;
        }

        GameObject managerObject = new GameObject("Scene3GeneratedMedicalVideoManager");
        managerObject.transform.SetParent(transform, false);
        managerObject.AddComponent<VideoPlayer>();
        generatedVideoManager = managerObject.AddComponent<PoeVideoManager>();
        generatedVideoManager.generatingOverlayTitle = "Please wait";
        generatedVideoManager.hideVideoDisplayAfterPlayback = false;
        generatedVideoManager.videoFadeDuration = 0.2f;
        generatedVideoManager.verboseDiagnostics = true;

        if (generatedVideoManager.ActiveBackendMode == PoeVideoManager.VideoBackendMode.LocalLtx)
        {
            generatedMedicalVideoDurationSeconds = Mathf.Clamp(generatedMedicalVideoDurationSeconds, 4, 4);
            generatedVideoManager.resolution = "720p";
            generatedVideoManager.defaultDurationSeconds = 4;
            generatedVideoManager.localLtxFrameRate = Mathf.Clamp(generatedVideoManager.localLtxFrameRate, 8, 8);
            generatedVideoManager.localLtxInferenceSteps = Mathf.Clamp(generatedVideoManager.localLtxInferenceSteps, 8, 10);
            generatedVideoManager.requestTimeoutSeconds = Mathf.Max(generatedVideoManager.requestTimeoutSeconds, 720f);
        }
        else
        {
            generatedMedicalVideoDurationSeconds = 4;
            generatedVideoManager.model = "veo-3.1-lite";
            generatedVideoManager.resolution = "720p";
            generatedVideoManager.defaultDurationSeconds = 4;
        }

        return generatedVideoManager;
    }

    private string BuildMedicalGeneratedVideoPrompt()
    {
        string tags;
        string sentence;

        switch (frozenPromptSeed)
        {
            case "AscensionSeed":
                tags = "transcendence, machine-pulse, medical-ai";
                sentence = "A first-person medical transcendence memory where healing becomes synthetic godhood.";
                break;
            case "PleaSeed":
                tags = "prayer, plea, supplication";
                sentence = "A first-person ritual healing memory where prayer, plea, and forgiveness collide.";
                break;
            default:
                tags = "silence, plague, helplessness";
                sentence = "A first-person plague memory where helplessness, silence, and failed faith consume the body.";
                break;
        }

        return
            "Create a " + generatedMedicalVideoDurationSeconds + "-second first-person cinematic medical memory with uninterrupted subjective camera continuity. " +
            "Prompt seed: " + frozenPromptSeed + ". Tags: " + tags + ". " +
            sentence + " " +
            "Show ritual-medical architecture, pulse rings, scanner light, bodily vulnerability, and a clear progression from fear toward collapse, prayer, or synthetic transcendence. " +
            "Keep the space human-scale, tactile, atmospheric, and emotionally legible, with visible body implication and transformation happening close to the viewer. " +
            "Silent. No subtitles. No captions. No on-screen text. No typography.";
    }

    private void HandleInteractionUnlocked()
    {
        hintOverlay?.SetHintVisible(true);
    }

    private IEnumerator EnsureScene3MinigameLoaded()
    {
        if (minigameSceneLoaded || string.IsNullOrWhiteSpace(scene3MinigameScenePath))
        {
            yield break;
        }

        SetBehaviourArrayEnabled(scene3EventSystems, false);

        Scene existingScene = SceneManager.GetSceneByName(scene3MinigameScenePath);
        if (existingScene.IsValid() && existingScene.isLoaded)
        {
            loadedMinigameScene = existingScene;
            Debug.Log("[Scene3VoiceAndVideoController] Minigame scene already loaded.");
        }
        else
        {
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(scene3MinigameScenePath, LoadSceneMode.Additive);
            if (loadOperation == null)
            {
                yield break;
            }

            while (!loadOperation.isDone)
            {
                yield return null;
            }
            loadedMinigameScene = SceneManager.GetSceneByName(scene3MinigameScenePath);
        }

        if (!loadedMinigameScene.IsValid() || !loadedMinigameScene.isLoaded)
        {
            Debug.LogWarning("[Scene3VoiceAndVideoController] Minigame scene failed to load.");
            yield break;
        }

        Debug.Log("[Scene3VoiceAndVideoController] Minigame scene loaded successfully.");

        foreach (GameObject rootObject in loadedMinigameScene.GetRootGameObjects())
        {
            Camera minigameCamera = rootObject.GetComponent<Camera>();
            if (minigameCamera != null)
            {
                ConfigureMinigameOverlayCamera(minigameCamera);
            }

            AudioListener minigameListener = rootObject.GetComponent<AudioListener>();
            if (minigameListener != null)
            {
                minigameListener.enabled = false;
            }

            EventSystem loadedEventSystem = rootObject.GetComponent<EventSystem>();
            if (loadedEventSystem != null)
            {
                loadedEventSystem.enabled = false;
                BaseInputModule[] modules = loadedEventSystem.GetComponents<BaseInputModule>();
                for (int i = 0; i < modules.Length; i++)
                {
                    if (modules[i] != null)
                    {
                        modules[i].enabled = false;
                    }
                }
            }

            if (rootObject.name == "Directional Light" || rootObject.name == "EventSystem")
            {
                rootObject.SetActive(false);
            }
        }

        PlayerMovement playerMovement = null;
        int minigameLayer = LayerMask.NameToLayer(Scene3MinigameLayerName);
        foreach (GameObject rootObject in loadedMinigameScene.GetRootGameObjects())
        {
            if (rootObject.name == "player" || rootObject.name == "ECGwire" || rootObject.name == "GameManager")
            {
                SetLayerRecursively(rootObject, minigameLayer);
            }

            if (playerMovement == null)
            {
                playerMovement = rootObject.GetComponentInChildren<PlayerMovement>(true);
            }
        }

        if (playerMovement != null)
        {
            minigamePointerBridge = GetComponent<Scene3MinigamePointerBridge>();
            if (minigamePointerBridge == null)
            {
                minigamePointerBridge = gameObject.AddComponent<Scene3MinigamePointerBridge>();
            }

            if (minigameOverlayCamera != null)
            {
                playerMovement.movementCamera = minigameOverlayCamera;
            }

            playerMovement.EnsureVisiblePresentation();
            minigamePointerBridge.Bind(playerMovement, minigameOverlayCamera);
            Debug.Log($"[Scene3VoiceAndVideoController] Minigame pointer bridge bound to PlayerMovement. camera={(minigameOverlayCamera != null ? minigameOverlayCamera.name : "null")}");
        }
        else
        {
            Debug.LogWarning("[Scene3VoiceAndVideoController] PlayerMovement not found in minigame scene.");
        }

        minigameSceneLoaded = true;
        SetBehaviourArrayEnabled(scene3EventSystems, true);
    }

    private void ConfigureMinigameOverlayCamera(Camera overlayCamera)
    {
        if (overlayCamera == null)
        {
            return;
        }

        minigameOverlayCamera = overlayCamera;
        minigameOverlayCamera.clearFlags = CameraClearFlags.Depth;
        minigameOverlayCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        minigameOverlayCamera.depth = (Camera.main != null ? Camera.main.depth : 0f) + 1f;
        Debug.Log($"[Scene3VoiceAndVideoController] Configured minigame overlay camera {minigameOverlayCamera.name} with depth {minigameOverlayCamera.depth}.");

        int overlayLayer = LayerMask.NameToLayer(Scene3MinigameLayerName);
        if (overlayLayer >= 0)
        {
            minigameOverlayCamera.cullingMask = 1 << overlayLayer;
        }
    }

    private void SetLayerRecursively(GameObject rootObject, int layer)
    {
        if (rootObject == null || layer < 0)
        {
            return;
        }

        rootObject.layer = layer;
        SpriteRenderer spriteRenderer = rootObject.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
            spriteRenderer.color = Color.white;
            spriteRenderer.sortingOrder = 1200;
        }
        for (int i = 0; i < rootObject.transform.childCount; i++)
        {
            SetLayerRecursively(rootObject.transform.GetChild(i).gameObject, layer);
        }
    }

    private void UpdateScene3MinigamePointer(Mediapipe.Tasks.Vision.HandLandmarker.HandLandmarkerResult result)
    {
        if (minigamePointerBridge == null)
        {
            return;
        }

        if (result.handLandmarks == null || result.handLandmarks.Count == 0)
        {
            minigamePointerBridge.ClearPointer();
            return;
        }

        var hand = result.handLandmarks[0];
        if (hand.landmarks == null || hand.landmarks.Count < 9)
        {
            minigamePointerBridge.ClearPointer();
            return;
        }

        Vector2 indexTipPosition = HandGestureDetector.GetIndexTipPosition(hand);
        minigamePointerBridge.SetNormalizedPointer(indexTipPosition);
    }

    private bool HasMetScene3PlaybackGate()
    {
        if (minigamePointerBridge != null)
        {
            return minigamePointerBridge.ActivePlaySeconds >= scene3MinigameMinimumPlaySeconds;
        }

        return playbackGateTimer == null || playbackGateTimer.IsSatisfied;
    }

    private float GetScene3PlaybackGateProgressSeconds()
    {
        if (minigamePointerBridge != null)
        {
            return minigamePointerBridge.ActivePlaySeconds;
        }

        return playbackGateTimer != null ? playbackGateTimer.ElapsedSeconds : 0f;
    }

    private T[] GetComponentsInScene<T>(Scene scene, string excludedSceneName) where T : Behaviour
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.name == excludedSceneName)
        {
            return new T[0];
        }

        List<T> results = new List<T>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null)
            {
                continue;
            }

            T[] found = roots[i].GetComponentsInChildren<T>(true);
            if (found != null && found.Length > 0)
            {
                results.AddRange(found);
            }
        }

        return results.ToArray();
    }

    private void SetBehaviourArrayEnabled<T>(T[] behaviours, bool enabled) where T : Behaviour
    {
        if (behaviours == null)
        {
            return;
        }

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
            {
                behaviours[i].enabled = enabled;
            }
        }
    }

    private void UpdateMedicalSemanticCaption(float volume)
    {
        if (hasSwitchedToSecondView)
        {
            hintOverlay?.SetSemanticCaption(BuildMedicalPromptPreview(lockedOutcomeKey));
            return;
        }

        string caption;
        if (volume < volumeThreshold * 0.35f)
        {
            caption = "Despair";
        }
        else if (volume >= volumeThreshold * apotheosisVolumeMultiplier ||
                 ((interactionGate == null || interactionGate.IsInteractionUnlocked) &&
                  interactionElapsed <= apotheosisTimeWindowSeconds &&
                  volume >= volumeThreshold))
        {
            caption = "Apotheosis";
        }
        else
        {
            caption = "Supplication";
        }

        hintOverlay?.SetSemanticCaption(BuildMedicalPromptPreview(caption));
    }

    private string BuildLockedMedicalCaption()
    {
        switch (lockedOutcomeKey)
        {
            case "Apotheosis":
                return "Ascension";
            case "Supplication":
                return "Plea";
            case "Despair":
                return "Despair";
            default:
                return "Silence";
        }
    }

    private string BuildMedicalPromptPreview(string outcomeKey)
    {
        float normalizedVolume = Mathf.Clamp01(NormalizedLiveVolume);
        float pleaBias = promptCaptureDurationSeconds > 0f ? Mathf.Clamp01(capturePleaDuration / Mathf.Max(promptCaptureDurationSeconds, 0.01f)) : 0f;
        float silenceBias = promptCaptureDurationSeconds > 0f ? Mathf.Clamp01(captureSilenceDuration / Mathf.Max(promptCaptureDurationSeconds, 0.01f)) : 0f;
        float ascensionBias = promptCaptureDurationSeconds > 0f ? Mathf.Clamp01(captureAscensionDuration / Mathf.Max(promptCaptureDurationSeconds, 0.01f)) : 0f;

        string bodyPhrase = normalizedVolume > 1.2f
            ? "surge, transformation, force"
            : normalizedVolume > 0.65f
                ? "strain, fear, appeal"
                : "fragility, exposure, collapse";

        string lightPhrase = ascensionBias > silenceBias && ascensionBias > pleaBias
            ? "scanner light, pulse rings, transcendence"
            : pleaBias > silenceBias
                ? "ritual light, prayer, searching"
                : "clinical light, plague, dread";

        switch (outcomeKey)
        {
            case "Apotheosis":
                return "ascension, machine pulse, synthetic godhood, " + bodyPhrase + ", " + lightPhrase;
            case "Supplication":
                return "supplication, prayer, forgiveness, vulnerability, " + bodyPhrase + ", " + lightPhrase;
            default:
                return "silence, helplessness, failed faith, despair, " + bodyPhrase + ", " + lightPhrase;
        }
    }
}
