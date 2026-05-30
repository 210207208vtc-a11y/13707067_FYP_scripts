using System.Collections;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using UnityEngine.SceneManagement;

[AddComponentMenu("Journey/Hunger Transition Gesture Controller")]
public class DoubleHandGestureController : MonoBehaviour
{
    private static readonly Color BlindFlashColor = Color.white;

    [Header("Gather Gesture")]
    [Tooltip("Distance below which both palms count as gathered.")]
    public float handsTogetherThreshold = 0.35f;

    [Tooltip("How long the player must hold the gather pose.")]
    public float holdTimeRequired = 2.0f;

    [Tooltip("Scene transition target.")]
    public string nextSceneName = "2";

    public float sceneFadeDuration = 0.65f;
    public bool debugLogDistance = false;

    [Header("Semantic Timing")]
    public float decisiveTransitionSeconds = 5.0f;
    public float interactionUnlockDelaySeconds = 30f;

    [Header("Scene 2 Insertion Audio (after Scene 1 completion)")]
    [Tooltip("When scene 1 gesture is completed, this audio will play and block the scene transition until it finishes.")]
    public AudioSource scene1CompletionInsertionAudio;

    [Header("Blindness Flash")]
    [Tooltip("Maximum white overlay opacity reached while the insertion audio plays.")]
    [Range(0f, 1f)]
    public float blindnessMaxAlpha = 1f;

    private float handsTogetherTimer;
    private float interactionElapsed;
    private bool hasTriggered;
    private bool transitionStarted;
    private bool gestureInputEnabled = true;
    private bool pendingHandsClose;
    private float pendingHandsDistance;
    private bool semanticHandDetected;
    private float pendingCameraDepthZ;
    private float lastSemanticHandDetectionTime = -10f;
    private readonly object dataLock = new object();
    private GestureHintOverlay hintOverlay;
    private InteractionGateController interactionGate;
    private float semanticCaptionStableTimer;
    private string pendingSemanticCaption = string.Empty;
    private string currentSemanticCaption = string.Empty;
    private const float SemanticCaptionStabilitySeconds = 0.08f;
    private const float SemanticHandDetectionTimeoutSeconds = 0.25f;

    private void Start()
    {
        interactionGate = InteractionGateController.GetOrCreate("SceneInteractionGate", interactionUnlockDelaySeconds);

        hintOverlay = GestureHintOverlay.CreateForScene("HungerTransitionHintOverlay");
        hintOverlay.Configure(
            GestureHintOverlay.HintKind.BothHandsGather,
            "Gather The Last Resources",
            "Bring both hands together and hold to enter the next crisis",
            GestureHintOverlay.OverlayTheme.HungerEmber,
            GestureHintOverlay.IconStyle.Ritual);
        hintOverlay.SetProgress(0f);
        hintOverlay.SetHintVisible(false, true);
        hintOverlay.SetSemanticCaptionVisible(false, true);
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
    }

    private void OnDestroy()
    {
        if (interactionGate != null)
        {
            interactionGate.Unlocked -= HandleInteractionUnlocked;
        }
    }

    public void OnHandLandmarkDetected(HandLandmarkerResult result)
    {
        if (hasTriggered || result.handLandmarks == null)
        {
            return;
        }

        if (!gestureInputEnabled)
        {
            return;
        }

        if (result.handLandmarks.Count == 0)
        {
            semanticHandDetected = false;
            lock (dataLock)
            {
                pendingHandsClose = false;
                pendingHandsDistance = 0f;
                pendingCameraDepthZ = 0f;
            }
            return;
        }

        semanticHandDetected = true;

        if (result.handLandmarks.Count < 2)
        {
            float semanticDepth = 0f;
            int singleHandIndex = ResolvePreferredHandIndex(result);
            if (singleHandIndex >= 0 &&
                singleHandIndex < result.handLandmarks.Count &&
                result.handLandmarks[singleHandIndex].landmarks != null &&
                result.handLandmarks[singleHandIndex].landmarks.Count > 9)
            {
                semanticDepth = result.handLandmarks[singleHandIndex].landmarks[9].z;
            }

            lock (dataLock)
            {
                pendingHandsClose = false;
                pendingHandsDistance = 0f;
                pendingCameraDepthZ = semanticDepth;
            }
            return;
        }

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
                if (string.IsNullOrWhiteSpace(label))
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

        int semanticIndex = ResolvePreferredHandIndex(result);
        if (semanticIndex >= 0 &&
            semanticIndex < result.handLandmarks.Count &&
            result.handLandmarks[semanticIndex].landmarks != null &&
            result.handLandmarks[semanticIndex].landmarks.Count > 9)
        {
            pendingCameraDepthZ = result.handLandmarks[semanticIndex].landmarks[9].z;
        }

        if (leftIndex < 0 || rightIndex < 0 ||
            leftIndex >= result.handLandmarks.Count ||
            rightIndex >= result.handLandmarks.Count)
        {
            lock (dataLock)
            {
                pendingHandsClose = false;
                pendingHandsDistance = 0f;
            }
            return;
        }

        var leftLandmarks = result.handLandmarks[leftIndex];
        var rightLandmarks = result.handLandmarks[rightIndex];
        if (leftLandmarks.landmarks == null || rightLandmarks.landmarks == null ||
            leftLandmarks.landmarks.Count <= 9 || rightLandmarks.landmarks.Count <= 9)
        {
            lock (dataLock)
            {
                pendingHandsClose = false;
            }
            return;
        }

        NormalizedLandmark leftPalm = leftLandmarks.landmarks[9];
        NormalizedLandmark rightPalm = rightLandmarks.landmarks[9];
        Vector3 leftPosition = new Vector3(leftPalm.x, leftPalm.y, leftPalm.z);
        Vector3 rightPosition = new Vector3(rightPalm.x, rightPalm.y, rightPalm.z);
        float distance = Vector3.Distance(leftPosition, rightPosition);

        lock (dataLock)
        {
            pendingHandsDistance = distance;
            pendingHandsClose = distance < handsTogetherThreshold;
            pendingCameraDepthZ = semanticIndex >= 0 &&
                                  semanticIndex < result.handLandmarks.Count &&
                                  result.handLandmarks[semanticIndex].landmarks != null &&
                                  result.handLandmarks[semanticIndex].landmarks.Count > 9
                ? result.handLandmarks[semanticIndex].landmarks[9].z
                : pendingCameraDepthZ;
        }
    }

    private void Update()
    {
        if (hasTriggered)
        {
            return;
        }

        if (!gestureInputEnabled)
        {
            handsTogetherTimer = 0f;
            semanticHandDetected = false;
            hintOverlay?.SetProgress(0f);
            hintOverlay?.SetHintVisible(false);
            hintOverlay?.SetSemanticCaptionVisible(false, true);
            hintOverlay?.SetSemanticCaptionPaused(true);
            hintOverlay?.ClearSemanticCaption();
            pendingSemanticCaption = string.Empty;
            currentSemanticCaption = string.Empty;
            semanticCaptionStableTimer = 0f;
            return;
        }

        if (semanticHandDetected)
        {
            lastSemanticHandDetectionTime = Time.unscaledTime;
        }

        bool semanticVisible = semanticHandDetected &&
                               (Time.unscaledTime - lastSemanticHandDetectionTime) <= SemanticHandDetectionTimeoutSeconds;
        if (!semanticVisible)
        {
            semanticHandDetected = false;
            lock (dataLock)
            {
                pendingCameraDepthZ = 0f;
            }
            hintOverlay?.SetSemanticCaptionVisible(false, true);
            hintOverlay?.SetSemanticCaptionPaused(true);
            hintOverlay?.ClearSemanticCaption();
            pendingSemanticCaption = string.Empty;
            currentSemanticCaption = string.Empty;
            semanticCaptionStableTimer = 0f;
        }
        else
        {
            lastSemanticHandDetectionTime = Time.unscaledTime;
            hintOverlay?.SetSemanticCaptionVisible(true, true);
            hintOverlay?.SetSemanticCaptionPaused(false);
            UpdateScene1SemanticCaptionStable();
        }

        bool shouldShowHintUi = interactionGate != null && interactionGate.IsInteractionUnlocked;
        hintOverlay?.SetHintVisible(shouldShowHintUi);

        interactionElapsed += Time.deltaTime;

        bool handsClose;
        float lastDistance;
        lock (dataLock)
        {
            handsClose = pendingHandsClose;
            lastDistance = pendingHandsDistance;
        }

        if (debugLogDistance)
        {
            Debug.Log($"[DoubleHandGestureController] hands distance={lastDistance:F3}, close={handsClose}");
        }

        if (!handsClose)
        {
            handsTogetherTimer = 0f;
            hintOverlay?.SetProgress(0f);
            return;
        }

        handsTogetherTimer += Time.deltaTime;
        hintOverlay?.SetProgress(handsTogetherTimer / Mathf.Max(holdTimeRequired, 0.01f));

        if (handsTogetherTimer >= holdTimeRequired)
        {
            TriggerSceneTransition(lastDistance);
        }
    }

    private void TriggerSceneTransition(float lastDistance)
    {
        if (hasTriggered)
        {
            return;
        }

        SceneTimeManager sceneTimeManager = FindObjectOfType<SceneTimeManager>();
        if (sceneTimeManager != null)
        {
            sceneTimeManager.ForceLockCurrentOutcome("scene_transition");
        }

        string urgencyTag = interactionElapsed <= decisiveTransitionSeconds ? "decisive_transition" : "hesitant_transition";
        List<string> semanticTags = new List<string>
        {
            "hunger",
            "gathering",
            "survival-clutch",
            urgencyTag,
        };

        JourneySessionManager session = JourneySessionManager.Instance;
        string sceneName = SceneManager.GetActiveScene().name;
        session?.RecordGestureSemantic(
            sceneName,
            "gather_survival",
            semanticTags,
            intensity: 1f - Mathf.Clamp01(lastDistance / Mathf.Max(handsTogetherThreshold, 0.001f)),
            duration: interactionElapsed,
            detail: urgencyTag);
        session?.LogEvent(sceneName, "gesture_confirmed", "gather_survival", semanticKey: "gather_survival");

        hintOverlay?.SetHintVisible(false);
        hasTriggered = true;

        if (!transitionStarted)
        {
            transitionStarted = true;
            StartCoroutine(PlayInsertAudioThenTransitionRoutine());
        }
    }

    public void SetGestureInputEnabled(bool enabled)
    {
        gestureInputEnabled = enabled;
        if (!enabled)
        {
            handsTogetherTimer = 0f;
            interactionElapsed = 0f;
            hintOverlay?.SetProgress(0f);
        }
    }

    private void HandleInteractionUnlocked()
    {
        hintOverlay?.SetHintVisible(true, true);
    }

    private void UpdateScene1SemanticCaptionStable()
    {
        float cameraDepthZ;
        lock (dataLock)
        {
            cameraDepthZ = pendingCameraDepthZ;
        }

        string targetCaption = BuildScene1DepthCaption(cameraDepthZ);
        if (!string.Equals(targetCaption, pendingSemanticCaption))
        {
            pendingSemanticCaption = targetCaption;
            semanticCaptionStableTimer = 0f;
            if (string.IsNullOrEmpty(currentSemanticCaption))
            {
                currentSemanticCaption = targetCaption;
                hintOverlay?.SetSemanticCaption(currentSemanticCaption);
            }
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

    private string BuildScene1DepthCaption(float handDepthZ)
    {
        if (debugLogDistance)
        {
            Debug.Log($"[DoubleHandGestureController] camera depth z={handDepthZ:F3}");
        }

        if (handDepthZ <= -0.22f)
        {
            return "collapse, pressure, hunger";
        }

        if (handDepthZ <= -0.12f)
        {
            return "closing in, need, survival";
        }

        if (handDepthZ <= -0.04f)
        {
            return "reaching, want, uncertainty";
        }

        if (handDepthZ <= 0.06f)
        {
            return "searching, hunger, distance";
        }

        return "fading, restraint, emptiness";
    }

    private int ResolvePreferredHandIndex(HandLandmarkerResult result)
    {
        if (result.handLandmarks == null || result.handLandmarks.Count == 0)
        {
            return -1;
        }

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
                if (!string.IsNullOrWhiteSpace(label) && label.ToLowerInvariant().Contains("left"))
                {
                    return i;
                }
            }
        }

        return 0;
    }

    private IEnumerator PlayInsertAudioThenTransitionRoutine()
    {
        float targetAlpha = Mathf.Clamp01(blindnessMaxAlpha);

        if (scene1CompletionInsertionAudio != null && scene1CompletionInsertionAudio.clip != null)
        {
            scene1CompletionInsertionAudio.loop = false;
            scene1CompletionInsertionAudio.Play();

            float expectedDuration = Mathf.Max(0.01f, scene1CompletionInsertionAudio.clip.length);
            if (targetAlpha > 0f)
            {
                StartCoroutine(SceneTransitionFader.FadeOverlayRoutine(BlindFlashColor, targetAlpha, expectedDuration));
            }

            float timeout = expectedDuration + 1.0f;
            float start = Time.unscaledTime;
            while (scene1CompletionInsertionAudio.isPlaying && (Time.unscaledTime - start) < timeout)
            {
                yield return null;
            }
        }

        if (targetAlpha > 0f)
        {
            SceneTransitionFader.SetOverlayImmediate(BlindFlashColor, targetAlpha, preserveOnLoad: true);
        }

        yield return SceneTransitionFader.FadeToSceneRoutine(nextSceneName, sceneFadeDuration, sceneFadeDuration);
    }
}
