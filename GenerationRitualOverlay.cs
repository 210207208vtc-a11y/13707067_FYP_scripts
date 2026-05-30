using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class GenerationRitualOverlay : MonoBehaviour
{
    public enum RitualTheme
    {
        Hunger,
        War,
        Medical,
    }

    [SerializeField] private RitualTheme ritualTheme = RitualTheme.War;

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private RectTransform panelRoot;
    private Text statusText;
    private RawImage panelBackground;
    private RawImage accentLine;
    private RawImage leftGlyph;
    private RawImage rightGlyph;
    private RawImage centerPulse;
    private RectTransform leftGlyphRect;
    private RectTransform rightGlyphRect;
    private RectTransform centerPulseRect;
    private Color baseColor;
    private Color peakColor;
    private float intensity;
    private bool isVisible;
    private Font cachedFont;

    public static GenerationRitualOverlay CreateForScene(string objectName)
    {
        GenerationRitualOverlay existing = FindObjectOfType<GenerationRitualOverlay>();
        if (existing != null)
        {
            return existing;
        }

        GameObject overlayObject = new GameObject(objectName);
        return overlayObject.AddComponent<GenerationRitualOverlay>();
    }

    private void Awake()
    {
        cachedFont = LoadBuiltinFont();
        BuildRuntimeUi();
        Configure(RitualTheme.War, "Please wait");
        SetVisible(false, true);
    }

    private void Update()
    {
        if (canvasGroup == null)
        {
            return;
        }

        float fadeTarget = isVisible ? 1f : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, fadeTarget, Time.unscaledDeltaTime * 3f);

        float timeValue = Time.unscaledTime;
        Color accent = Color.Lerp(baseColor, peakColor, intensity);

        if (panelBackground != null)
        {
            panelBackground.color = new Color(panelBackground.color.r, panelBackground.color.g, panelBackground.color.b, isVisible ? 0.72f : 0f);
        }

        if (accentLine != null)
        {
            accentLine.color = new Color(accent.r, accent.g, accent.b, 0.85f);
        }

        switch (ritualTheme)
        {
            case RitualTheme.Hunger:
                AnimateHunger(timeValue, accent);
                break;
            case RitualTheme.Medical:
                AnimateMedical(timeValue, accent);
                break;
            default:
                AnimateWar(timeValue, accent);
                break;
        }
    }

    public void Configure(RitualTheme theme, string status)
    {
        ritualTheme = theme;
        if (statusText != null)
        {
            statusText.text = string.IsNullOrWhiteSpace(status) ? "Please wait" : status;
        }

        ApplyTheme();
    }

    public void SetStatus(string status)
    {
        if (statusText != null)
        {
            statusText.text = string.IsNullOrWhiteSpace(status) ? "Please wait" : status;
        }
    }

    public void SetIntensity(float normalizedIntensity)
    {
        intensity = Mathf.Clamp01(normalizedIntensity);
    }

    public void SetVisible(bool visible, bool instant = false)
    {
        isVisible = visible;
        if (instant && canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
        }
    }

    private void BuildRuntimeUi()
    {
        canvas = FindOrCreateCanvas();

        GameObject rootObject = new GameObject("GenerationRitualOverlay");
        rootObject.transform.SetParent(canvas.transform, false);
        panelRoot = rootObject.AddComponent<RectTransform>();
        panelRoot.anchorMin = new Vector2(0.5f, 0f);
        panelRoot.anchorMax = new Vector2(0.5f, 0f);
        panelRoot.pivot = new Vector2(0.5f, 0f);
        panelRoot.anchoredPosition = new Vector2(0f, 124f);
        panelRoot.sizeDelta = new Vector2(620f, 110f);

        panelBackground = CreateBox("Background", panelRoot, new Color(0.02f, 0.03f, 0.05f, 0.72f));
        Stretch(panelBackground.rectTransform, 0f);

        accentLine = CreateBox("Accent", panelRoot, Color.white);
        accentLine.rectTransform.anchorMin = new Vector2(0f, 1f);
        accentLine.rectTransform.anchorMax = new Vector2(1f, 1f);
        accentLine.rectTransform.pivot = new Vector2(0.5f, 1f);
        accentLine.rectTransform.anchoredPosition = Vector2.zero;
        accentLine.rectTransform.sizeDelta = new Vector2(0f, 3f);

        statusText = CreateText("Status", panelRoot, "Please wait", 24, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        statusText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        statusText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        statusText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        statusText.rectTransform.anchoredPosition = new Vector2(0f, 12f);
        statusText.rectTransform.sizeDelta = new Vector2(480f, 34f);

        leftGlyph = CreateBox("LeftGlyph", panelRoot, Color.white);
        leftGlyphRect = leftGlyph.rectTransform;
        leftGlyphRect.anchorMin = new Vector2(0.5f, 0.5f);
        leftGlyphRect.anchorMax = new Vector2(0.5f, 0.5f);
        leftGlyphRect.pivot = new Vector2(0.5f, 0.5f);
        leftGlyphRect.anchoredPosition = new Vector2(-220f, -12f);
        leftGlyphRect.sizeDelta = new Vector2(88f, 8f);

        rightGlyph = CreateBox("RightGlyph", panelRoot, Color.white);
        rightGlyphRect = rightGlyph.rectTransform;
        rightGlyphRect.anchorMin = new Vector2(0.5f, 0.5f);
        rightGlyphRect.anchorMax = new Vector2(0.5f, 0.5f);
        rightGlyphRect.pivot = new Vector2(0.5f, 0.5f);
        rightGlyphRect.anchoredPosition = new Vector2(220f, -12f);
        rightGlyphRect.sizeDelta = new Vector2(88f, 8f);

        centerPulse = CreateBox("CenterPulse", panelRoot, Color.white);
        centerPulseRect = centerPulse.rectTransform;
        centerPulseRect.anchorMin = new Vector2(0.5f, 0.5f);
        centerPulseRect.anchorMax = new Vector2(0.5f, 0.5f);
        centerPulseRect.pivot = new Vector2(0.5f, 0.5f);
        centerPulseRect.anchoredPosition = new Vector2(0f, -12f);
        centerPulseRect.sizeDelta = new Vector2(46f, 10f);

        canvasGroup = rootObject.AddComponent<CanvasGroup>();
    }

    private Canvas FindOrCreateCanvas()
    {
        GameObject canvasObject = GameObject.Find("GenerationRitualCanvas");
        Canvas existingCanvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;
        if (existingCanvas != null)
        {
            return existingCanvas;
        }

        GameObject createdCanvasObject = new GameObject("GenerationRitualCanvas");
        Canvas createdCanvas = createdCanvasObject.AddComponent<Canvas>();
        createdCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        createdCanvas.sortingOrder = 4450;

        CanvasScaler scaler = createdCanvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        createdCanvasObject.AddComponent<GraphicRaycaster>();
        return createdCanvas;
    }

    private void ApplyTheme()
    {
        switch (ritualTheme)
        {
            case RitualTheme.Hunger:
                panelBackground.color = new Color(0.08f, 0.05f, 0.03f, 0.72f);
                baseColor = new Color(0.83f, 0.58f, 0.28f, 0.95f);
                peakColor = new Color(1f, 0.84f, 0.55f, 1f);
                break;
            case RitualTheme.Medical:
                panelBackground.color = new Color(0.02f, 0.08f, 0.1f, 0.72f);
                baseColor = new Color(0.38f, 0.86f, 0.95f, 0.95f);
                peakColor = new Color(0.92f, 0.98f, 0.64f, 1f);
                break;
            default:
                panelBackground.color = new Color(0.09f, 0.03f, 0.03f, 0.72f);
                baseColor = new Color(0.92f, 0.3f, 0.3f, 0.95f);
                peakColor = new Color(1f, 0.75f, 0.42f, 1f);
                break;
        }
    }

    private void AnimateHunger(float timeValue, Color accent)
    {
        float contraction = Mathf.Lerp(0f, 60f, intensity) + Mathf.Sin(timeValue * 2.6f) * 6f;
        leftGlyphRect.anchoredPosition = new Vector2(-210f + contraction, -12f);
        rightGlyphRect.anchoredPosition = new Vector2(210f - contraction, -12f);
        centerPulseRect.sizeDelta = new Vector2(Mathf.Lerp(40f, 120f, intensity), 10f);
        leftGlyph.color = accent;
        rightGlyph.color = accent;
        centerPulse.color = new Color(accent.r, accent.g, accent.b, 0.78f);
    }

    private void AnimateWar(float timeValue, Color accent)
    {
        float sweep = Mathf.Sin(timeValue * 5.2f) * 18f;
        leftGlyphRect.anchoredPosition = new Vector2(-220f + sweep, -12f);
        rightGlyphRect.anchoredPosition = new Vector2(220f - sweep, -12f);
        leftGlyphRect.sizeDelta = new Vector2(Mathf.Lerp(88f, 160f, intensity), 8f);
        rightGlyphRect.sizeDelta = new Vector2(Mathf.Lerp(88f, 160f, intensity), 8f);
        centerPulseRect.sizeDelta = new Vector2(40f + Mathf.Abs(sweep), 10f);
        leftGlyph.color = new Color(accent.r, accent.g, accent.b, 0.82f);
        rightGlyph.color = new Color(accent.r, accent.g, accent.b, 0.82f);
        centerPulse.color = new Color(accent.r, accent.g, accent.b, 0.48f + intensity * 0.42f);
    }

    private void AnimateMedical(float timeValue, Color accent)
    {
        float pulse = Mathf.Abs(Mathf.Sin(timeValue * 4.8f));
        float width = Mathf.Lerp(52f, 180f, Mathf.Clamp01((pulse * 0.55f) + (intensity * 0.7f)));
        centerPulseRect.sizeDelta = new Vector2(width, 12f);
        leftGlyphRect.sizeDelta = new Vector2(18f, Mathf.Lerp(12f, 44f, pulse * 0.8f + intensity * 0.2f));
        rightGlyphRect.sizeDelta = new Vector2(18f, Mathf.Lerp(12f, 44f, pulse * 0.8f + intensity * 0.2f));
        leftGlyphRect.anchoredPosition = new Vector2(-120f, -12f);
        rightGlyphRect.anchoredPosition = new Vector2(120f, -12f);
        leftGlyph.color = accent;
        rightGlyph.color = accent;
        centerPulse.color = new Color(accent.r, accent.g, accent.b, 0.52f + intensity * 0.36f);
    }

    private static RawImage CreateBox(string name, RectTransform parent, Color color)
    {
        GameObject boxObject = new GameObject(name);
        boxObject.transform.SetParent(parent, false);
        RawImage image = boxObject.AddComponent<RawImage>();
        image.texture = Texture2D.whiteTexture;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private Text CreateText(string name, RectTransform parent, string value, int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        Text text = textObject.AddComponent<Text>();
        text.font = cachedFont;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
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
