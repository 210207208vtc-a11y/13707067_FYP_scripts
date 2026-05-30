using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class GestureHintOverlay : MonoBehaviour
{
    public enum OverlayTheme
    {
        NeutralBlue,
        HungerEmber,
        WarCommand,
        MedicalSanctuary,
    }

    public enum IconStyle
    {
        Minimal,
        Directive,
        Ritual,
    }

    public enum HintKind
    {
        BothHandsRaise,
        BothHandsGather,
        VoicePulse,
    }

    [SerializeField] private bool showTextLabels = false;
    [SerializeField] private HintKind hintKind = HintKind.BothHandsRaise;
    [SerializeField] private OverlayTheme theme = OverlayTheme.NeutralBlue;
    [SerializeField] private IconStyle iconStyle = IconStyle.Minimal;
    [SerializeField] private string title = "Raise Both Hands";
    [SerializeField] private string subtitle = "Hold the gesture to continue";
    [SerializeField] private bool visibleOnStart = true;

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private CanvasGroup semanticCaptionCanvasGroup;
    private RectTransform panelRoot;
    private Text titleText;
    private Text subtitleText;
    private Text semanticCaptionText;
    private RawImage panelBackground;
    private RawImage edgeGlow;
    private RawImage pulseFill;
    private RectTransform pulseFillRect;
    private RawImage leftHand;
    private RawImage rightHand;
    private RectTransform leftHandRect;
    private RectTransform rightHandRect;
    private RawImage leftArrow;
    private RawImage rightArrow;
    private RectTransform leftArrowRect;
    private RectTransform rightArrowRect;
    private RawImage[] voiceBars;
    private RectTransform[] voiceBarRects;

    private float progress;
    private bool isVisible;
    private bool semanticCaptionVisible = true;
    private Font cachedFont;
    private Color baseAccentColor;
    private Color peakAccentColor;
    private Texture leftHandTexture;
    private Texture rightHandTexture;
    private Texture customGestureTexture;
    private bool useCustomGestureTexture;
    private string semanticCaptionTargetText = string.Empty;
    private float semanticCaptionRevealChars;
    private bool semanticCaptionPaused;

    private const float SemanticCaptionRevealSpeed = 12f;

    public static GestureHintOverlay CreateForScene(string objectName)
    {
        GestureHintOverlay existing = FindObjectOfType<GestureHintOverlay>();
        if (existing != null)
        {
            return existing;
        }

        GameObject overlayObject = new GameObject(objectName);
        return overlayObject.AddComponent<GestureHintOverlay>();
    }

    private void Awake()
    {
        cachedFont = LoadBuiltinFont();
        LoadHandTextures();
        BuildRuntimeUi();
        SetVisible(visibleOnStart, true);
    }

    private void Update()
    {
        if (panelRoot == null)
        {
            return;
        }

        float t = Time.unscaledTime;
        float fadeTarget = isVisible ? 1f : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, fadeTarget, Time.unscaledDeltaTime * 3f);
        if (semanticCaptionCanvasGroup != null)
        {
            float captionFadeTarget = semanticCaptionVisible ? 1f : 0f;
            semanticCaptionCanvasGroup.alpha = Mathf.MoveTowards(semanticCaptionCanvasGroup.alpha, captionFadeTarget, Time.unscaledDeltaTime * 4f);
        }

        UpdateSemanticCaptionReveal();

        Color accent = Color.Lerp(baseAccentColor, peakAccentColor, progress);
        pulseFill.color = accent;

        if (hintKind == HintKind.VoicePulse)
        {
            AnimateVoicePulse(t, accent);
        }
        else
        {
            AnimateHandGesture(t, accent);
        }
    }

    public void Configure(HintKind kind, string newTitle, string newSubtitle, OverlayTheme newTheme = OverlayTheme.NeutralBlue, IconStyle newIconStyle = IconStyle.Minimal)
    {
        hintKind = kind;
        theme = newTheme;
        iconStyle = newIconStyle;
        title = newTitle;
        subtitle = newSubtitle;

        if (titleText != null)
        {
            titleText.text = title;
        }

        if (subtitleText != null)
        {
            subtitleText.text = subtitle;
        }

        ApplyTheme();
        ApplyIconStyle();
        ApplyHintVisibility();
        ApplyTextVisibility();
    }

    public void SetProgress(float normalizedProgress)
    {
        progress = Mathf.Clamp01(normalizedProgress);

        if (pulseFillRect != null)
        {
            Vector2 size = pulseFillRect.sizeDelta;
            size.x = Mathf.Lerp(24f, 220f, progress);
            pulseFillRect.sizeDelta = size;
        }
    }

    public void SetVisible(bool shouldBeVisible, bool instant = false)
    {
        isVisible = shouldBeVisible;
        if (instant && canvasGroup != null)
        {
            canvasGroup.alpha = shouldBeVisible ? 1f : 0f;
        }
    }

    public void SetHintVisible(bool shouldBeVisible, bool instant = false)
    {
        SetVisible(shouldBeVisible, instant);
    }

    public void SetSemanticCaption(string text)
    {
        string nextText = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        if (string.Equals(semanticCaptionTargetText, nextText))
        {
            return;
        }

        semanticCaptionTargetText = nextText;
        semanticCaptionRevealChars = 0f;
        if (semanticCaptionPaused)
        {
            semanticCaptionRevealChars = semanticCaptionTargetText.Length;
        }
        ApplySemanticCaptionReveal();
    }

    public void ClearSemanticCaption()
    {
        SetSemanticCaption(string.Empty);
    }

    public void SetSemanticCaptionVisible(bool visible, bool instant = false)
    {
        semanticCaptionVisible = visible;
        if (instant && semanticCaptionCanvasGroup != null)
        {
            semanticCaptionCanvasGroup.alpha = visible ? 1f : 0f;
        }
    }

    public void SetSemanticCaptionPaused(bool paused)
    {
        semanticCaptionPaused = paused;
        if (semanticCaptionPaused)
        {
            if (!string.IsNullOrEmpty(semanticCaptionTargetText) && semanticCaptionRevealChars <= 0f)
            {
                semanticCaptionRevealChars = semanticCaptionTargetText.Length;
            }
            SnapSemanticCaptionToWordBoundary();
        }
    }

    public void SetCustomGestureTexture(Texture texture, bool mirrorOnBothHands = true)
    {
        customGestureTexture = texture;
        useCustomGestureTexture = customGestureTexture != null;

        if (!useCustomGestureTexture)
        {
            ApplyHandTextures();
            ApplyIconStyle();
            return;
        }

        if (leftHand != null)
        {
            leftHand.texture = customGestureTexture;
            leftHand.gameObject.SetActive(true);
        }

        if (rightHand != null)
        {
            rightHand.texture = mirrorOnBothHands ? customGestureTexture : rightHandTexture;
            rightHand.gameObject.SetActive(mirrorOnBothHands);
        }

        if (leftArrow != null)
        {
            leftArrow.gameObject.SetActive(false);
        }

        if (rightArrow != null)
        {
            rightArrow.gameObject.SetActive(false);
        }

        if (leftHandRect != null)
        {
            leftHandRect.sizeDelta = new Vector2(120f, 120f);
        }

        if (rightHandRect != null)
        {
            rightHandRect.sizeDelta = new Vector2(120f, 120f);
        }
    }

    private void BuildRuntimeUi()
    {
        canvas = FindDedicatedCanvas();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("GestureHintCanvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4500;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        GameObject rootObject = new GameObject("GestureHintOverlay");
        rootObject.transform.SetParent(canvas.transform, false);

        panelRoot = rootObject.AddComponent<RectTransform>();
        panelRoot.anchorMin = new Vector2(0.5f, 0f);
        panelRoot.anchorMax = new Vector2(0.5f, 0f);
        panelRoot.pivot = new Vector2(0.5f, 0f);
        panelRoot.anchoredPosition = new Vector2(0f, 28f);
        panelRoot.sizeDelta = new Vector2(660f, 172f);

        panelBackground = CreateBox("Background", panelRoot, Color.clear);
        Stretch(panelBackground.rectTransform, 0f);

        edgeGlow = CreateBox("EdgeGlow", panelRoot, Color.clear);
        edgeGlow.rectTransform.anchorMin = new Vector2(0f, 1f);
        edgeGlow.rectTransform.anchorMax = new Vector2(1f, 1f);
        edgeGlow.rectTransform.pivot = new Vector2(0.5f, 1f);
        edgeGlow.rectTransform.anchoredPosition = Vector2.zero;
        edgeGlow.rectTransform.sizeDelta = new Vector2(0f, 2f);

        canvasGroup = rootObject.AddComponent<CanvasGroup>();

        titleText = CreateText("Title", panelRoot, title, 28, FontStyle.Bold, TextAnchor.UpperCenter, new Color(0.96f, 0.97f, 0.99f, 1f));
        titleText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        titleText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        titleText.rectTransform.pivot = new Vector2(0.5f, 1f);
        titleText.rectTransform.anchoredPosition = new Vector2(0f, -18f);
        titleText.rectTransform.sizeDelta = new Vector2(520f, 34f);

        subtitleText = CreateText("Subtitle", panelRoot, subtitle, 17, FontStyle.Normal, TextAnchor.UpperCenter, new Color(0.75f, 0.82f, 0.9f, 0.95f));
        subtitleText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        subtitleText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        subtitleText.rectTransform.pivot = new Vector2(0.5f, 1f);
        subtitleText.rectTransform.anchoredPosition = new Vector2(0f, -56f);
        subtitleText.rectTransform.sizeDelta = new Vector2(540f, 28f);

        RawImage pulseTrack = CreateBox("PulseTrack", panelRoot, new Color(1f, 1f, 1f, 0.08f));
        pulseTrack.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        pulseTrack.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        pulseTrack.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        pulseTrack.rectTransform.anchoredPosition = new Vector2(0f, 24f);
        pulseTrack.rectTransform.sizeDelta = new Vector2(228f, 6f);

        pulseFill = CreateBox("PulseFill", panelRoot, new Color(0.8f, 0.9f, 1f, 0.95f));
        pulseFillRect = pulseFill.rectTransform;
        pulseFillRect.anchorMin = new Vector2(0.5f, 0f);
        pulseFillRect.anchorMax = new Vector2(0.5f, 0f);
        pulseFillRect.pivot = new Vector2(0.5f, 0.5f);
        pulseFillRect.anchoredPosition = new Vector2(0f, 24f);
        pulseFillRect.sizeDelta = new Vector2(24f, 6f);

        leftHand = CreateBox("LeftHand", panelRoot, new Color(0.94f, 0.96f, 1f, 0.82f));
        leftHandRect = leftHand.rectTransform;
        leftHandRect.anchorMin = new Vector2(0.5f, 0f);
        leftHandRect.anchorMax = new Vector2(0.5f, 0f);
        leftHandRect.pivot = new Vector2(0.5f, 0.5f);
        leftHandRect.anchoredPosition = new Vector2(-84f, 72f);
        leftHandRect.sizeDelta = new Vector2(70f, 70f);

        rightHand = CreateBox("RightHand", panelRoot, new Color(0.94f, 0.96f, 1f, 0.82f));
        rightHandRect = rightHand.rectTransform;
        rightHandRect.anchorMin = new Vector2(0.5f, 0f);
        rightHandRect.anchorMax = new Vector2(0.5f, 0f);
        rightHandRect.pivot = new Vector2(0.5f, 0.5f);
        rightHandRect.anchoredPosition = new Vector2(84f, 72f);
        rightHandRect.sizeDelta = new Vector2(70f, 70f);

        leftArrow = CreateBox("LeftArrow", panelRoot, new Color(0.74f, 0.86f, 1f, 0.88f));
        leftArrowRect = leftArrow.rectTransform;
        leftArrowRect.anchorMin = new Vector2(0.5f, 0f);
        leftArrowRect.anchorMax = new Vector2(0.5f, 0f);
        leftArrowRect.pivot = new Vector2(0.5f, 0.5f);
        leftArrowRect.anchoredPosition = new Vector2(-84f, 112f);
        leftArrowRect.sizeDelta = new Vector2(10f, 34f);

        rightArrow = CreateBox("RightArrow", panelRoot, new Color(0.74f, 0.86f, 1f, 0.88f));
        rightArrowRect = rightArrow.rectTransform;
        rightArrowRect.anchorMin = new Vector2(0.5f, 0f);
        rightArrowRect.anchorMax = new Vector2(0.5f, 0f);
        rightArrowRect.pivot = new Vector2(0.5f, 0.5f);
        rightArrowRect.anchoredPosition = new Vector2(84f, 112f);
        rightArrowRect.sizeDelta = new Vector2(10f, 34f);

        voiceBars = new RawImage[5];
        voiceBarRects = new RectTransform[5];
        for (int i = 0; i < voiceBars.Length; i++)
        {
            RawImage bar = CreateBox("VoiceBar" + i, panelRoot, new Color(0.76f, 0.88f, 1f, 0.9f));
            RectTransform barRect = bar.rectTransform;
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(-44f + (i * 22f), 64f);
            barRect.sizeDelta = new Vector2(12f, 24f);
            voiceBars[i] = bar;
            voiceBarRects[i] = barRect;
        }

        GameObject captionObject = new GameObject("SemanticCaptionRoot");
        captionObject.transform.SetParent(canvas.transform, false);
        RectTransform captionRoot = captionObject.AddComponent<RectTransform>();
        captionRoot.anchorMin = new Vector2(0.5f, 0f);
        captionRoot.anchorMax = new Vector2(0.5f, 0f);
        captionRoot.pivot = new Vector2(0.5f, 0f);
        captionRoot.anchoredPosition = new Vector2(0f, 8f);
        captionRoot.sizeDelta = new Vector2(1480f, 42f);
        semanticCaptionCanvasGroup = captionObject.AddComponent<CanvasGroup>();

        semanticCaptionText = CreateText(
            "SemanticCaption",
            captionRoot,
            string.Empty,
            22,
            FontStyle.Normal,
            TextAnchor.MiddleCenter,
            new Color(0.95f, 0.97f, 0.99f, 0.96f));
        Stretch(semanticCaptionText.rectTransform, 0f);
        semanticCaptionText.horizontalOverflow = HorizontalWrapMode.Overflow;
        semanticCaptionText.verticalOverflow = VerticalWrapMode.Truncate;
        semanticCaptionText.resizeTextForBestFit = true;
        semanticCaptionText.resizeTextMinSize = 16;
        semanticCaptionText.resizeTextMaxSize = 22;

        ApplyTheme();
        ApplyIconStyle();
        ApplyHandTextures();
        ApplyHintVisibility();
        ApplyTextVisibility();
        SetSemanticCaptionVisible(true, true);
    }

    private void UpdateSemanticCaptionReveal()
    {
        if (semanticCaptionText == null)
        {
            return;
        }

        if (semanticCaptionPaused)
        {
            return;
        }

        if (semanticCaptionRevealChars >= semanticCaptionTargetText.Length)
        {
            return;
        }

        semanticCaptionRevealChars = Mathf.Min(
            semanticCaptionTargetText.Length,
            semanticCaptionRevealChars + (Time.unscaledDeltaTime * SemanticCaptionRevealSpeed));
        ApplySemanticCaptionReveal();
    }

    private void ApplySemanticCaptionReveal()
    {
        if (semanticCaptionText == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(semanticCaptionTargetText))
        {
            semanticCaptionText.text = string.Empty;
            return;
        }

        int charCount = Mathf.Clamp(Mathf.CeilToInt(semanticCaptionRevealChars), 0, semanticCaptionTargetText.Length);
        semanticCaptionText.text = semanticCaptionTargetText.Substring(0, charCount);
    }


    private void SnapSemanticCaptionToWordBoundary()
    {
        if (string.IsNullOrEmpty(semanticCaptionTargetText))
        {
            return;
        }

        int charCount = Mathf.Clamp(Mathf.CeilToInt(semanticCaptionRevealChars), 0, semanticCaptionTargetText.Length);
        if (charCount <= 0 || charCount >= semanticCaptionTargetText.Length)
        {
            ApplySemanticCaptionReveal();
            return;
        }

        while (charCount < semanticCaptionTargetText.Length && !IsSemanticCaptionBoundary(semanticCaptionTargetText[charCount]))
        {
            charCount++;
        }

        semanticCaptionRevealChars = charCount;
        ApplySemanticCaptionReveal();
    }

    private static bool IsSemanticCaptionBoundary(char character)
    {
        return character == ',' || character == ' ' || character == '.' || character == ';' || character == '|';
    }

    private Canvas FindDedicatedCanvas()
    {
        GameObject canvasObject = GameObject.Find("GestureHintCanvas");
        if (canvasObject == null)
        {
            return null;
        }

        Canvas foundCanvas = canvasObject.GetComponent<Canvas>();
        if (foundCanvas == null || !foundCanvas.isActiveAndEnabled)
        {
            return null;
        }

        RectTransform rectTransform = foundCanvas.GetComponent<RectTransform>();
        if (rectTransform != null && rectTransform.lossyScale.sqrMagnitude < 0.01f)
        {
            return null;
        }

        return foundCanvas;
    }

    private void ApplyHintVisibility()
    {
        bool handsVisible = hintKind == HintKind.BothHandsRaise || hintKind == HintKind.BothHandsGather;
        bool voiceVisible = hintKind == HintKind.VoicePulse;

        SetGraphicActive(leftHand, handsVisible);
        SetGraphicActive(rightHand, handsVisible);
        SetGraphicActive(leftArrow, handsVisible);
        SetGraphicActive(rightArrow, handsVisible);

        if (voiceBars != null)
        {
            for (int i = 0; i < voiceBars.Length; i++)
            {
                SetGraphicActive(voiceBars[i], voiceVisible);
            }
        }
    }

    private void ApplyTextVisibility()
    {
        if (titleText != null)
        {
            titleText.enabled = showTextLabels;
        }

        if (subtitleText != null)
        {
            subtitleText.enabled = showTextLabels;
        }
    }

    private void AnimateHandGesture(float timeValue, Color accent)
    {
        if (hintKind == HintKind.BothHandsGather)
        {
            float gatherOffset = Mathf.Lerp(0f, 42f, progress) + Mathf.Sin(timeValue * 3.4f) * 5f;
            leftHandRect.anchoredPosition = new Vector2(-84f + gatherOffset, 72f);
            rightHandRect.anchoredPosition = new Vector2(84f - gatherOffset, 72f);
            leftArrowRect.anchoredPosition = new Vector2(-126f + gatherOffset * 0.7f, 72f);
            rightArrowRect.anchoredPosition = new Vector2(126f - gatherOffset * 0.7f, 72f);

            float gatherArrowAlpha = iconStyle == IconStyle.Ritual ? 0.78f : 0.68f;
            leftArrow.color = new Color(accent.r, accent.g, accent.b, gatherArrowAlpha + progress * 0.16f);
            rightArrow.color = new Color(accent.r, accent.g, accent.b, gatherArrowAlpha + progress * 0.16f);
            leftHand.color = new Color(0.94f, 0.96f, 1f, 0.72f + progress * 0.24f);
            rightHand.color = new Color(0.94f, 0.96f, 1f, 0.72f + progress * 0.24f);
            return;
        }

        if (useCustomGestureTexture)
        {
            float customLift = Mathf.Sin(timeValue * 4.2f) * 4f + (progress * 8f);
            leftHandRect.anchoredPosition = new Vector2(0f, 74f + customLift);
            leftHand.color = new Color(1f, 1f, 1f, 0.82f + progress * 0.18f);
            if (rightHand != null)
            {
                rightHand.gameObject.SetActive(false);
            }
            return;
        }

        float styleLift = iconStyle == IconStyle.Directive ? 13f : 10f;
        float lift = Mathf.Sin(timeValue * 3.8f) * 7f + (progress * styleLift);
        leftHandRect.anchoredPosition = new Vector2(-84f, 72f + lift);
        rightHandRect.anchoredPosition = new Vector2(84f, 72f + lift);
        leftArrowRect.anchoredPosition = new Vector2(-84f, 112f + Mathf.Sin(timeValue * 3.8f + 0.1f) * 9f);
        rightArrowRect.anchoredPosition = new Vector2(84f, 112f + Mathf.Sin(timeValue * 3.8f + 0.45f) * 9f);

        float arrowAlpha = iconStyle == IconStyle.Directive ? 0.82f : 0.68f;
        leftArrow.color = new Color(accent.r, accent.g, accent.b, arrowAlpha + progress * 0.18f);
        rightArrow.color = new Color(accent.r, accent.g, accent.b, arrowAlpha + progress * 0.18f);
        leftHand.color = new Color(0.94f, 0.96f, 1f, 0.74f + progress * 0.26f);
        rightHand.color = new Color(0.94f, 0.96f, 1f, 0.74f + progress * 0.26f);
    }

    private void AnimateVoicePulse(float timeValue, Color accent)
    {
        for (int i = 0; i < voiceBarRects.Length; i++)
        {
            float wave = Mathf.Abs(Mathf.Sin((timeValue * 4.5f) + (i * 0.45f)));
            float height = Mathf.Lerp(18f, 54f, Mathf.Clamp01((wave * 0.55f) + (progress * 0.7f)));
            voiceBarRects[i].sizeDelta = new Vector2(12f, height);
            voiceBars[i].color = new Color(accent.r, accent.g, accent.b, 0.45f + (wave * 0.18f) + (progress * 0.28f));
        }
    }

    private void ApplyTheme()
    {
        switch (theme)
        {
            case OverlayTheme.HungerEmber:
                baseAccentColor = new Color(0.92f, 0.62f, 0.26f, 0.95f);
                peakAccentColor = new Color(1f, 0.88f, 0.62f, 1f);
                break;
            case OverlayTheme.WarCommand:
                baseAccentColor = new Color(0.95f, 0.36f, 0.36f, 0.95f);
                peakAccentColor = new Color(1f, 0.82f, 0.5f, 1f);
                break;
            case OverlayTheme.MedicalSanctuary:
                baseAccentColor = new Color(0.42f, 0.88f, 0.96f, 0.95f);
                peakAccentColor = new Color(0.94f, 0.95f, 0.62f, 1f);
                break;
            default:
                baseAccentColor = new Color(0.55f, 0.75f, 0.95f, 0.95f);
                peakAccentColor = new Color(0.94f, 0.84f, 0.52f, 1f);
                break;
        }

        if (panelBackground != null)
        {
            panelBackground.color = Color.clear;
        }

        if (edgeGlow != null)
        {
            edgeGlow.color = Color.clear;
        }
    }

    private void ApplyIconStyle()
    {
        if (leftHandRect != null && rightHandRect != null)
        {
            Vector2 handSize;
            switch (iconStyle)
            {
                case IconStyle.Directive:
                    handSize = new Vector2(78f, 78f);
                    break;
                case IconStyle.Ritual:
                    handSize = new Vector2(84f, 84f);
                    break;
                default:
                    handSize = new Vector2(70f, 70f);
                    break;
            }
            leftHandRect.sizeDelta = handSize;
            rightHandRect.sizeDelta = handSize;
        }

        if (leftArrowRect != null && rightArrowRect != null)
        {
            Vector2 arrowSize;
            if (hintKind == HintKind.BothHandsGather)
            {
                arrowSize = iconStyle == IconStyle.Ritual ? new Vector2(34f, 10f) : new Vector2(42f, 12f);
            }
            else
            {
                arrowSize = iconStyle == IconStyle.Directive ? new Vector2(14f, 42f) : new Vector2(10f, 34f);
            }
            leftArrowRect.sizeDelta = arrowSize;
            rightArrowRect.sizeDelta = arrowSize;
        }

        if (voiceBarRects != null)
        {
            float width = iconStyle == IconStyle.Ritual ? 14f : 12f;
            for (int i = 0; i < voiceBarRects.Length; i++)
            {
                Vector2 size = voiceBarRects[i].sizeDelta;
                size.x = width;
                voiceBarRects[i].sizeDelta = size;
            }
        }
    }

    private void LoadHandTextures()
    {
        leftHandTexture = Resources.Load<Texture>("left");
        rightHandTexture = Resources.Load<Texture>("right");
    }

    private void ApplyHandTextures()
    {
        if (leftHand != null && leftHandTexture != null)
        {
            leftHand.texture = useCustomGestureTexture && customGestureTexture != null ? customGestureTexture : leftHandTexture;
        }

        if (rightHand != null && rightHandTexture != null)
        {
            rightHand.texture = useCustomGestureTexture && customGestureTexture != null ? customGestureTexture : rightHandTexture;
        }
    }

    private static RawImage CreateBox(string name, RectTransform parent, Color color)
    {
        GameObject boxObject = new GameObject(name);
        boxObject.transform.SetParent(parent, false);
        RawImage image = boxObject.AddComponent<RawImage>();
        image.texture = Texture2D.whiteTexture;
        image.color = color;
        return image;
    }

    private Text CreateText(string name, RectTransform parent, string value, int fontSize, FontStyle style, TextAnchor anchor, Color color)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.font = cachedFont;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void Stretch(RectTransform rectTransform, float inset)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = new Vector2(inset, inset);
        rectTransform.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetGraphicActive(Graphic graphic, bool active)
    {
        if (graphic != null)
        {
            graphic.gameObject.SetActive(active);
        }
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
