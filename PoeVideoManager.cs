using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

[AddComponentMenu("Journey/Poe Video Manager")]
public class PoeVideoManager : MonoBehaviour
{
    private const string ForcedPoeModel = "veo-3.1-lite";

    public enum VideoBackendMode
    {
        Auto,
        Poe,
        LocalLtx,
    }

    [Header("Video Provider")]
    public VideoBackendMode backendMode = VideoBackendMode.Poe;
    public string apiBaseUrl = "https://api.poe.com/v1";
    public string apiKeyEnvironmentVariable = "POE_API_KEY";
    public string localApiKeyFileName = "poe_api_key.txt";
    public string model = ForcedPoeModel;
    public string resolution = "720p";
    public bool silentMode = true;
    public int defaultDurationSeconds = 4;
    public float requestTimeoutSeconds = 900f;
    public bool verboseDiagnostics = true;

    [Header("Local LTX")]
    public string localLtxInstallRoot = @"C:\Users\Dicky\Desktop\LTX2";
    public int localLtxFrameRate = 8;
    public int localLtxInferenceSteps = 8;
    public bool localLtxEnableFp8 = true;
    public bool localLtxEnhancePrompt = false;
    public bool localNormalizeForUnityPlayback = true;
    public string localFfmpegPath = "";
    public LocalLtxServiceMode localLtxServiceMode = LocalLtxServiceMode.DirectProcess;
    public string localLtxServiceUrl = "http://127.0.0.1:7861";
    public int localLtxServiceStartupTimeoutSeconds = 180;
    public bool localLtxServiceWarmModelsOnStartup = true;
    public string localLtxServiceScriptPath = "";

    [Header("Playback")]
    public VideoPlayer vPlayer;
    public GameObject videoDisplayRoot;
    public GameObject generatingOverlay;
    public Text currentPromptLabel;
    public float videoFadeDuration = 0.45f;
    public bool stretchVideoToFullscreen = true;
    public bool hideVideoDisplayAfterPlayback = true;

    [Header("Runtime Overlay")]
    public int runtimeOverlaySortingOrder = 4200;
    public Color runtimeBackdropColor = new Color(0f, 0f, 0f, 0.92f);
    public Color runtimeGeneratingPanelColor = new Color(0.03f, 0.06f, 0.08f, 0.9f);
    public Color runtimeGeneratingAccentColor = new Color(0.52f, 0.9f, 1f, 1f);
    [TextArea(1, 3)]
    public string generatingOverlayTitle = "Generating video";

    private IVideoGenerationProvider provider;
    private VideoBackendMode activeBackendMode;
    private CanvasGroup videoCanvasGroup;
    private RectTransform videoRectTransform;
    private RawImage sceneVideoImage;
    private bool isGenerating;
    private bool isPlayingGeneratedVideo;
    private bool playbackFinishedSignal;
    private Coroutine activePlaybackCoroutine;
    private Coroutine generatingStatusCoroutine;

    private Canvas runtimeCanvas;
    private GameObject runtimeVideoDisplayRoot;
    private CanvasGroup runtimeVideoCanvasGroup;
    private RectTransform runtimeVideoRectTransform;
    private RawImage runtimeVideoImage;
    private GameObject runtimeGeneratingOverlay;
    private CanvasGroup runtimeGeneratingCanvasGroup;
    private Text runtimeGeneratingTitleText;
    private Text runtimeGeneratingStatusText;
    private RenderTexture ownedRuntimeRenderTexture;

    public bool IsGenerating => isGenerating;
    public bool IsPlayingGeneratedVideo => isPlayingGeneratedVideo;
    public VideoBackendMode ActiveBackendMode => activeBackendMode;
    public string LastPrompt { get; private set; } = string.Empty;
    public string LastPlayableUrl { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;
    public string DiagnosticsSummary { get; private set; } = string.Empty;
    public VideoGenerationStatus LastStatus { get; private set; } = VideoGenerationStatus.ProviderError;

    private void Awake()
    {
        activeBackendMode = ResolveBackendMode();
        provider = CreateProvider(activeBackendMode);
        Debug.Log($"[PoeVideoManager] Using backend: {activeBackendMode}");

        if (vPlayer == null)
        {
            vPlayer = GetComponent<VideoPlayer>();
        }

        if (vPlayer != null)
        {
            vPlayer.loopPointReached += HandlePlaybackFinished;
        }

        CacheVideoDisplayReferences();
        EnsureRuntimeOverlay();
        EnsureVideoOutputTarget();
        RefreshPromptLabels();
        SetVideoDisplayVisible(false, true);
        SetGeneratingOverlay(false);
    }

    private void OnDestroy()
    {
        if (vPlayer != null)
        {
            vPlayer.loopPointReached -= HandlePlaybackFinished;
        }

        if (generatingStatusCoroutine != null)
        {
            StopCoroutine(generatingStatusCoroutine);
            generatingStatusCoroutine = null;
        }

        if (ownedRuntimeRenderTexture != null)
        {
            ownedRuntimeRenderTexture.Release();
            Destroy(ownedRuntimeRenderTexture);
        }

        if (runtimeCanvas != null)
        {
            Destroy(runtimeCanvas.gameObject);
        }
    }

    public void StartAIGeneration(string prompt)
    {
        StartAIGeneration(prompt, null);
    }

    public void StartAIGeneration(string prompt, int? durationSecondsOverride)
    {
        if (isGenerating || string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        LastPrompt = prompt.Trim();
        LastPlayableUrl = string.Empty;
        LastError = string.Empty;
        DiagnosticsSummary = string.Empty;
        playbackFinishedSignal = false;
        RefreshPromptLabels();

        StartCoroutine(GenerateVideo(LastPrompt, durationSecondsOverride));
    }

    public IEnumerator GenerateAndPlayVideoFlow(string prompt, int durationSeconds, Action<bool> onCompleted = null)
    {
        if (isGenerating || isPlayingGeneratedVideo || string.IsNullOrWhiteSpace(prompt))
        {
            onCompleted?.Invoke(false);
            yield break;
        }

        StartAIGeneration(prompt, durationSeconds);

        while (isGenerating)
        {
            yield return null;
        }

        if (LastStatus != VideoGenerationStatus.Success)
        {
            onCompleted?.Invoke(false);
            yield break;
        }

        while (!playbackFinishedSignal)
        {
            yield return null;
        }

        onCompleted?.Invoke(true);
    }

    public bool StartBackgroundGeneration(string prompt, int? durationSecondsOverride, Action<VideoGenerationResult> onCompleted, bool showOverlay = false)
    {
        if (isGenerating || string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        LastPrompt = prompt.Trim();
        LastPlayableUrl = string.Empty;
        LastError = string.Empty;
        DiagnosticsSummary = string.Empty;
        RefreshPromptLabels();
        StartCoroutine(GenerateVideoBackgroundRequest(LastPrompt, durationSecondsOverride, onCompleted, showOverlay));
        return true;
    }

    public IEnumerator PlayPreparedVideoFlow(string playableUrlOrPath, Action<bool> onCompleted = null)
    {
        if (isPlayingGeneratedVideo || string.IsNullOrWhiteSpace(playableUrlOrPath))
        {
            onCompleted?.Invoke(false);
            yield break;
        }

        playbackFinishedSignal = false;
        yield return StartCoroutine(PrepareAndPlayVideo(playableUrlOrPath));

        if (!isPlayingGeneratedVideo && !playbackFinishedSignal)
        {
            onCompleted?.Invoke(false);
            yield break;
        }

        while (!playbackFinishedSignal)
        {
            yield return null;
        }

        onCompleted?.Invoke(true);
    }

    public void RunPoeDiagnosticsFromInspector()
    {
        if (isGenerating)
        {
            return;
        }

        StartCoroutine(RunPoeDiagnostics());
    }

    public void StopPlaybackAndHide()
    {
        if (activePlaybackCoroutine != null)
        {
            StopCoroutine(activePlaybackCoroutine);
            activePlaybackCoroutine = null;
        }

        playbackFinishedSignal = false;
        isPlayingGeneratedVideo = false;

        if (vPlayer != null)
        {
            vPlayer.Stop();
        }

        SetVideoDisplayVisible(false, true);
        SetGeneratingOverlay(false);
    }

    private IEnumerator GenerateVideo(string promptText, int? durationSecondsOverride)
    {
        isGenerating = true;
        LastStatus = VideoGenerationStatus.ProviderError;
        SetGeneratingOverlay(true);
        SetVideoDisplayVisible(false, true);

        string sceneName = SceneManager.GetActiveScene().name;
        JourneySessionManager session = JourneySessionManager.Instance;
        session?.LogEvent(sceneName, "api_request_started", promptText);

        VideoGenerationRequest request = BuildRequest(promptText, durationSecondsOverride);

        VideoGenerationResult result = null;
        yield return provider.GenerateVideo(request, generated => result = generated);

        if (result != null && result.IsSuccess)
        {
            LastStatus = result.status;
            LastPlayableUrl = result.playableUrlOrPath ?? string.Empty;
            LastError = string.Empty;
            session?.LogEvent(sceneName, "api_request_completed", result.playableUrlOrPath, result.status.ToString());
            yield return StartCoroutine(PrepareAndPlayVideo(result.playableUrlOrPath));
        }
        else
        {
            string error = result != null ? result.error : "Provider returned no result.";
            LastStatus = result != null ? result.status : VideoGenerationStatus.ProviderError;
            LastError = error ?? string.Empty;
            LastPlayableUrl = string.Empty;
            Debug.LogError("[PoeVideoManager] Video generation failed: " + error);
            if (verboseDiagnostics && result != null && !string.IsNullOrWhiteSpace(result.rawResponse))
            {
                Debug.LogError("[PoeVideoManager] Provider raw response:\n" + result.rawResponse);
            }
            session?.LogEvent(sceneName, "api_request_failed", error, result != null ? result.status.ToString() : "Unknown");
            SetGeneratingOverlay(false);
        }

        isGenerating = false;
    }

    private IEnumerator GenerateVideoBackgroundRequest(string promptText, int? durationSecondsOverride, Action<VideoGenerationResult> onCompleted, bool showOverlay)
    {
        isGenerating = true;
        LastStatus = VideoGenerationStatus.ProviderError;
        if (showOverlay)
        {
            SetGeneratingOverlay(true);
        }

        string sceneName = SceneManager.GetActiveScene().name;
        JourneySessionManager session = JourneySessionManager.Instance;
        session?.LogEvent(sceneName, "api_request_started", "background_generation");

        VideoGenerationRequest request = BuildRequest(promptText, durationSecondsOverride);
        VideoGenerationResult result = null;
        yield return provider.GenerateVideo(request, generated => result = generated);

        if (result != null && result.IsSuccess)
        {
            LastStatus = result.status;
            LastPlayableUrl = result.playableUrlOrPath ?? string.Empty;
            LastError = string.Empty;
            session?.LogEvent(sceneName, "api_request_completed", result.playableUrlOrPath, result.status.ToString());
        }
        else
        {
            string error = result != null ? result.error : "Provider returned no result.";
            LastStatus = result != null ? result.status : VideoGenerationStatus.ProviderError;
            LastPlayableUrl = string.Empty;
            LastError = error ?? string.Empty;
            session?.LogEvent(sceneName, "api_request_failed", error, result != null ? result.status.ToString() : "Unknown");
        }

        if (showOverlay)
        {
            SetGeneratingOverlay(false);
        }

        isGenerating = false;
        onCompleted?.Invoke(result);
    }

    private IEnumerator RunPoeDiagnostics()
    {
        isGenerating = true;

        IPoeDiagnosticsProvider diagnosticsProvider = provider as IPoeDiagnosticsProvider;
        if (diagnosticsProvider == null)
        {
            Debug.LogError("[PoeVideoManager] Provider does not support diagnostics.");
            isGenerating = false;
            yield break;
        }

        PoeDiagnosticsResult diagnostics = null;
        yield return diagnosticsProvider.RunDiagnostics(BuildRequest("diagnostic prompt", null), result => diagnostics = result);

        if (diagnostics == null)
        {
            Debug.LogError("[PoeVideoManager] Diagnostics returned no result.");
            isGenerating = false;
            yield break;
        }

        string sampleModels = diagnostics.sampleModels != null && diagnostics.sampleModels.Count > 0
            ? string.Join(", ", diagnostics.sampleModels.GetRange(0, Mathf.Min(8, diagnostics.sampleModels.Count)).ToArray())
            : "none";

        DiagnosticsSummary =
            $"keyFormatValid={diagnostics.keyFormatValid}\n" +
            $"modelsEndpointReachable={diagnostics.modelsEndpointReachable}\n" +
            $"targetModelListed={diagnostics.targetModelListed}\n" +
            $"sampleModels={sampleModels}\n" +
            $"error={diagnostics.error}";

        Debug.Log(
            "[PoeVideoManager] Poe diagnostics\n" +
            DiagnosticsSummary);

        if (verboseDiagnostics && !string.IsNullOrWhiteSpace(diagnostics.modelsRawResponse))
        {
            Debug.Log("[PoeVideoManager] Poe /models raw response: " + diagnostics.modelsRawResponse);
        }

        isGenerating = false;
    }

    private IEnumerator PrepareAndPlayVideo(string playableUrlOrPath)
    {
        if (vPlayer == null)
        {
            FailPlayback("VideoPlayer is not assigned.");
            yield break;
        }

        string normalizedUrl = NormalizePlayableUrl(playableUrlOrPath);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            FailPlayback("Playable URL was empty.");
            yield break;
        }

        EnsureRuntimeOverlay();
        EnsureFullscreenDisplay();
        EnsureVideoOutputTarget();

        playbackFinishedSignal = false;
        isPlayingGeneratedVideo = false;

        vPlayer.Stop();
        vPlayer.source = VideoSource.Url;
        vPlayer.url = normalizedUrl;
        vPlayer.isLooping = false;
        vPlayer.Prepare();

        float prepareStart = Time.realtimeSinceStartup;
        while (!vPlayer.isPrepared)
        {
            if (Time.realtimeSinceStartup - prepareStart > Mathf.Max(20f, requestTimeoutSeconds * 0.25f))
            {
                FailPlayback("Video prepare timed out before playback.");
                yield break;
            }

            yield return null;
        }

        SetGeneratingOverlay(false);
        SetVideoDisplayVisible(true, true);
        SetDisplayAlpha(0f);

        vPlayer.Play();
        isPlayingGeneratedVideo = true;

        if (activePlaybackCoroutine != null)
        {
            StopCoroutine(activePlaybackCoroutine);
        }

        activePlaybackCoroutine = StartCoroutine(FadeVideoCanvas(1f));
        yield return activePlaybackCoroutine;
        activePlaybackCoroutine = null;
    }

    private void HandlePlaybackFinished(VideoPlayer source)
    {
        if (!isPlayingGeneratedVideo)
        {
            return;
        }

        isPlayingGeneratedVideo = false;
        playbackFinishedSignal = true;

        if (hideVideoDisplayAfterPlayback)
        {
            if (activePlaybackCoroutine != null)
            {
                StopCoroutine(activePlaybackCoroutine);
            }

            activePlaybackCoroutine = StartCoroutine(HideVideoAfterPlayback());
        }
    }

    private IEnumerator HideVideoAfterPlayback()
    {
        yield return FadeVideoCanvas(0f);
        SetVideoDisplayVisible(false, true);
        activePlaybackCoroutine = null;
    }

    private IEnumerator FadeVideoCanvas(float targetAlpha)
    {
        float duration = Mathf.Max(0.01f, videoFadeDuration);
        float startAlpha = GetDisplayAlpha();
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetDisplayAlpha(Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration));
            yield return null;
        }

        SetDisplayAlpha(targetAlpha);
    }

    private void CacheVideoDisplayReferences()
    {
        if (videoDisplayRoot == null)
        {
            return;
        }

        if (videoCanvasGroup == null)
        {
            videoCanvasGroup = videoDisplayRoot.GetComponent<CanvasGroup>();
            if (videoCanvasGroup == null)
            {
                videoCanvasGroup = videoDisplayRoot.AddComponent<CanvasGroup>();
            }
        }

        if (videoRectTransform == null)
        {
            videoRectTransform = videoDisplayRoot.GetComponent<RectTransform>();
        }

        if (sceneVideoImage == null)
        {
            sceneVideoImage = videoDisplayRoot.GetComponent<RawImage>();
        }
    }

    private void EnsureRuntimeOverlay()
    {
        if (runtimeCanvas != null && runtimeVideoDisplayRoot != null && runtimeGeneratingOverlay != null)
        {
            return;
        }

        Font builtinFont = LoadBuiltinFont();

        GameObject canvasObject = new GameObject("PoeVideoRuntimeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        runtimeCanvas = canvasObject.GetComponent<Canvas>();
        runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        runtimeCanvas.sortingOrder = runtimeOverlaySortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject videoRoot = new GameObject("GeneratedVideoRuntimeRoot", typeof(RectTransform), typeof(CanvasGroup));
        videoRoot.transform.SetParent(canvasObject.transform, false);
        runtimeVideoDisplayRoot = videoRoot;
        runtimeVideoRectTransform = videoRoot.GetComponent<RectTransform>();
        runtimeVideoCanvasGroup = videoRoot.GetComponent<CanvasGroup>();
        Stretch(runtimeVideoRectTransform);

        RawImage videoBackdrop = CreateBox("VideoBackdrop", runtimeVideoRectTransform, runtimeBackdropColor);
        Stretch(videoBackdrop.rectTransform);
        videoBackdrop.raycastTarget = false;

        runtimeVideoImage = CreateBox("GeneratedVideoImage", runtimeVideoRectTransform, Color.white);
        Stretch(runtimeVideoImage.rectTransform);
        runtimeVideoImage.texture = Texture2D.blackTexture;
        runtimeVideoImage.raycastTarget = false;

        GameObject generatingRoot = new GameObject("GeneratingOverlayRuntimeRoot", typeof(RectTransform), typeof(CanvasGroup));
        generatingRoot.transform.SetParent(canvasObject.transform, false);
        runtimeGeneratingOverlay = generatingRoot;
        runtimeGeneratingCanvasGroup = generatingRoot.GetComponent<CanvasGroup>();
        Stretch(generatingRoot.GetComponent<RectTransform>());

        RawImage generatingBackdrop = CreateBox("GeneratingBackdrop", generatingRoot.GetComponent<RectTransform>(), new Color(0f, 0f, 0f, 0.78f));
        Stretch(generatingBackdrop.rectTransform);
        generatingBackdrop.raycastTarget = false;

        RawImage panel = CreateBox("GeneratingPanel", generatingRoot.GetComponent<RectTransform>(), runtimeGeneratingPanelColor);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(700f, 170f);
        panel.raycastTarget = false;

        RawImage accent = CreateBox("Accent", panelRect, runtimeGeneratingAccentColor);
        RectTransform accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(0f, 5f);
        accent.raycastTarget = false;

        runtimeGeneratingTitleText = CreateText("Title", panelRect, builtinFont, generatingOverlayTitle, 34, FontStyle.Bold, TextAnchor.UpperCenter, Color.white);
        RectTransform titleRect = runtimeGeneratingTitleText.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -28f);
        titleRect.sizeDelta = new Vector2(760f, 44f);

        runtimeGeneratingStatusText = CreateText("Status", panelRect, builtinFont, "Preparing request...", 24, FontStyle.Normal, TextAnchor.UpperCenter, new Color(0.87f, 0.94f, 0.98f, 1f));
        RectTransform statusRect = runtimeGeneratingStatusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.5f, 1f);
        statusRect.anchorMax = new Vector2(0.5f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.anchoredPosition = new Vector2(0f, -88f);
        statusRect.sizeDelta = new Vector2(620f, 38f);

        runtimeVideoDisplayRoot.SetActive(false);
        runtimeGeneratingOverlay.SetActive(false);
    }

    private void EnsureFullscreenDisplay()
    {
        CacheVideoDisplayReferences();

        if (stretchVideoToFullscreen)
        {
            Stretch(videoRectTransform);
            Stretch(runtimeVideoRectTransform);
        }
    }

    private void EnsureVideoOutputTarget()
    {
        if (vPlayer == null)
        {
            return;
        }

        RenderTexture targetTexture = vPlayer.targetTexture;
        if (targetTexture == null)
        {
            int width = 1920;
            int height = 1080;

            string normalizedResolution = (resolution ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedResolution == "720p")
            {
                width = 1280;
                height = 720;
            }

            if (ownedRuntimeRenderTexture == null || ownedRuntimeRenderTexture.width != width || ownedRuntimeRenderTexture.height != height)
            {
                if (ownedRuntimeRenderTexture != null)
                {
                    ownedRuntimeRenderTexture.Release();
                    Destroy(ownedRuntimeRenderTexture);
                }

                ownedRuntimeRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                ownedRuntimeRenderTexture.name = "PoeGeneratedVideoRT";
                ownedRuntimeRenderTexture.Create();
            }

            targetTexture = ownedRuntimeRenderTexture;
        }

        vPlayer.renderMode = VideoRenderMode.RenderTexture;
        vPlayer.targetTexture = targetTexture;
        ApplyVideoTexture(targetTexture);
    }

    private void SetVideoDisplayVisible(bool visible, bool instant)
    {
        CacheVideoDisplayReferences();
        EnsureRuntimeOverlay();

        if (videoDisplayRoot != null)
        {
            videoDisplayRoot.SetActive(visible);
        }

        if (runtimeVideoDisplayRoot != null)
        {
            runtimeVideoDisplayRoot.SetActive(visible);
        }

        float alpha = visible ? 1f : 0f;
        if (instant)
        {
            SetDisplayAlpha(alpha);
        }

        if (!visible && !instant && activePlaybackCoroutine != null)
        {
            StopCoroutine(activePlaybackCoroutine);
            activePlaybackCoroutine = null;
        }
    }

    private void SetGeneratingOverlay(bool visible)
    {
        EnsureRuntimeOverlay();
        RefreshPromptLabels();

        if (generatingOverlay != null)
        {
            generatingOverlay.SetActive(visible);
        }

        if (runtimeGeneratingOverlay != null)
        {
            runtimeGeneratingOverlay.SetActive(visible);
        }

        if (runtimeGeneratingCanvasGroup != null)
        {
            runtimeGeneratingCanvasGroup.alpha = visible ? 1f : 0f;
        }

        if (visible)
        {
            if (generatingStatusCoroutine != null)
            {
                StopCoroutine(generatingStatusCoroutine);
            }

            generatingStatusCoroutine = StartCoroutine(AnimateGeneratingStatus());
        }
        else if (generatingStatusCoroutine != null)
        {
            StopCoroutine(generatingStatusCoroutine);
            generatingStatusCoroutine = null;
        }
    }

    private IEnumerator AnimateGeneratingStatus()
    {
        string[] steps =
        {
            "Please wait",
            "Generating video",
            "Preparing playback",
        };

        int stepIndex = 0;
        int dotCount = 0;

        while (runtimeGeneratingOverlay != null && runtimeGeneratingOverlay.activeSelf)
        {
            if (runtimeGeneratingTitleText != null)
            {
                runtimeGeneratingTitleText.text = string.IsNullOrWhiteSpace(generatingOverlayTitle)
                    ? "AI is generating your vision"
                    : generatingOverlayTitle;
            }

            if (runtimeGeneratingStatusText != null)
            {
                runtimeGeneratingStatusText.text = steps[stepIndex] + new string('.', dotCount);
            }

            RefreshPromptLabels();
            yield return new WaitForSecondsRealtime(0.45f);

            dotCount++;
            if (dotCount > 3)
            {
                dotCount = 0;
                stepIndex = (stepIndex + 1) % steps.Length;
            }
        }
    }

    private void RefreshPromptLabels()
    {
        string prompt = string.IsNullOrWhiteSpace(LastPrompt) ? "Awaiting prompt." : LastPrompt.Trim();

        if (currentPromptLabel != null)
        {
            currentPromptLabel.text = string.Empty;
        }
    }

    private void ApplyVideoTexture(Texture texture)
    {
        if (runtimeVideoImage != null)
        {
            runtimeVideoImage.texture = texture;
        }

        if (sceneVideoImage != null)
        {
            sceneVideoImage.texture = texture;
        }
    }

    private void SetDisplayAlpha(float alpha)
    {
        if (videoCanvasGroup != null)
        {
            videoCanvasGroup.alpha = alpha;
        }

        if (runtimeVideoCanvasGroup != null)
        {
            runtimeVideoCanvasGroup.alpha = alpha;
        }
    }

    private float GetDisplayAlpha()
    {
        if (runtimeVideoCanvasGroup != null)
        {
            return runtimeVideoCanvasGroup.alpha;
        }

        if (videoCanvasGroup != null)
        {
            return videoCanvasGroup.alpha;
        }

        return 0f;
    }

    private void FailPlayback(string error)
    {
        LastStatus = VideoGenerationStatus.ProviderError;
        LastError = error ?? "Unknown playback error.";
        LastPlayableUrl = string.Empty;
        playbackFinishedSignal = false;
        isPlayingGeneratedVideo = false;
        Debug.LogError("[PoeVideoManager] " + LastError);
        SetGeneratingOverlay(false);
        SetVideoDisplayVisible(false, true);
    }

    private string NormalizePlayableUrl(string playableUrlOrPath)
    {
        if (string.IsNullOrWhiteSpace(playableUrlOrPath))
        {
            return string.Empty;
        }

        if (playableUrlOrPath.Contains("://"))
        {
            return playableUrlOrPath;
        }

        if (System.IO.File.Exists(playableUrlOrPath))
        {
            return new Uri(playableUrlOrPath).AbsoluteUri;
        }

        return playableUrlOrPath;
    }

    private VideoGenerationRequest BuildRequest(string promptText, int? durationSecondsOverride)
    {
        JourneySessionManager session = JourneySessionManager.Instance;
        VideoBackendMode selectedBackend = activeBackendMode == VideoBackendMode.Auto ? ResolveBackendMode() : activeBackendMode;
        string effectiveResolution = ResolveEffectiveResolution(selectedBackend, resolution);
        int durationSeconds = NormalizeDurationSeconds(durationSecondsOverride ?? defaultDurationSeconds, selectedBackend);

        return new VideoGenerationRequest
        {
            prompt = promptText,
            sceneName = SceneManager.GetActiveScene().name,
            model = model,
            baseUrl = apiBaseUrl,
            resolution = effectiveResolution,
            size = ResolveOutputSize(effectiveResolution),
            silent = silentMode,
            durationSeconds = durationSeconds,
            timeoutSeconds = ResolveEffectiveTimeoutSeconds(selectedBackend, durationSeconds),
            apiKeyEnvironmentVariable = apiKeyEnvironmentVariable,
            apiKeyFallbackFileName = localApiKeyFileName,
            promptTags = session != null ? new System.Collections.Generic.List<string>(session.AggregatedPromptTags) : new System.Collections.Generic.List<string>(),
            localInstallRoot = localLtxInstallRoot,
            localFrameRate = ResolveEffectiveLocalFrameRate(selectedBackend),
            localInferenceSteps = ResolveEffectiveLocalInferenceSteps(selectedBackend),
            localEnableFp8 = localLtxEnableFp8,
            localEnhancePrompt = localLtxEnhancePrompt,
            localNormalizeForUnityPlayback = localNormalizeForUnityPlayback,
            localFfmpegPath = localFfmpegPath,
            localServiceMode = localLtxServiceMode,
            localServiceUrl = localLtxServiceUrl,
            localServiceStartupTimeoutSeconds = localLtxServiceStartupTimeoutSeconds,
            localServiceWarmModelsOnStartup = localLtxServiceWarmModelsOnStartup,
            localServiceScriptPath = localLtxServiceScriptPath,
        };
    }

    private IVideoGenerationProvider CreateProvider(VideoBackendMode selectedBackend)
    {
        switch (selectedBackend)
        {
            case VideoBackendMode.LocalLtx:
                return new LocalLtxVideoGenerationProvider();
            case VideoBackendMode.Poe:
            default:
                return new PoeVideoGenerationProvider();
        }
    }

    private VideoBackendMode ResolveBackendMode()
    {
        if (backendMode == VideoBackendMode.Poe || backendMode == VideoBackendMode.LocalLtx)
        {
            return backendMode;
        }

        return LocalLtxVideoGenerationProvider.IsInstallationAvailable(localLtxInstallRoot)
            ? VideoBackendMode.LocalLtx
            : VideoBackendMode.Poe;
    }

    private static int NormalizeDurationSeconds(int durationSeconds, VideoBackendMode backendMode)
    {
        int[] supported = backendMode == VideoBackendMode.LocalLtx
            ? new[] { 3, 4, 6 }
            : new[] { 4, 6, 8 };
        int best = supported[0];
        int bestDistance = Math.Abs(durationSeconds - best);

        for (int i = 1; i < supported.Length; i++)
        {
            int distance = Math.Abs(durationSeconds - supported[i]);
            if (distance < bestDistance)
            {
                best = supported[i];
                bestDistance = distance;
            }
        }

        return best;
    }

    private static string ResolveOutputSize(string configuredResolution)
    {
        if (string.IsNullOrWhiteSpace(configuredResolution))
        {
            return "1920x1080";
        }

        switch (configuredResolution.Trim().ToLowerInvariant())
        {
            case "720p":
                return "1280x720";
            case "1080p":
                return "1920x1080";
            default:
                return configuredResolution;
        }
    }

    private static string ResolveEffectiveResolution(VideoBackendMode backendMode, string configuredResolution)
    {
        string normalized = string.IsNullOrWhiteSpace(configuredResolution)
            ? string.Empty
            : configuredResolution.Trim().ToLowerInvariant();

        if (backendMode == VideoBackendMode.LocalLtx)
        {
            if (string.IsNullOrWhiteSpace(normalized) ||
                normalized == "1080p" ||
                normalized == "1920x1080")
            {
                return "720p";
            }
        }

        return string.IsNullOrWhiteSpace(configuredResolution) ? "1080p" : configuredResolution;
    }

    private int ResolveEffectiveLocalFrameRate(VideoBackendMode backendMode)
    {
        if (backendMode == VideoBackendMode.LocalLtx)
        {
            return Mathf.Clamp(localLtxFrameRate, 8, 12);
        }

        return localLtxFrameRate;
    }

    private int ResolveEffectiveLocalInferenceSteps(VideoBackendMode backendMode)
    {
        if (backendMode == VideoBackendMode.LocalLtx)
        {
            return Mathf.Clamp(localLtxInferenceSteps, 6, 12);
        }

        return localLtxInferenceSteps;
    }

    private float ResolveEffectiveTimeoutSeconds(VideoBackendMode backendMode, int durationSeconds)
    {
        if (backendMode == VideoBackendMode.LocalLtx)
        {
            return Mathf.Max(requestTimeoutSeconds, 600f + durationSeconds * 45f);
        }

        return Mathf.Max(requestTimeoutSeconds, 120f);
    }

    private static void Stretch(RectTransform rectTransform)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }

    private static RawImage CreateBox(string name, RectTransform parent, Color color)
    {
        GameObject boxObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        boxObject.transform.SetParent(parent, false);
        RawImage image = boxObject.GetComponent<RawImage>();
        image.texture = Texture2D.whiteTexture;
        image.color = color;
        return image;
    }

    private static Text CreateText(string name, RectTransform parent, Font font, string value, int fontSize, FontStyle style, TextAnchor anchor, Color color)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static Font LoadBuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return font;
    }
}
