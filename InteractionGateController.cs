using System;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Journey/Interaction Gate Controller")]
public class InteractionGateController : MonoBehaviour
{
    [SerializeField] private float unlockDelaySeconds = 30f;

    private float elapsedSeconds;
    private bool isInteractionUnlocked;

    public float UnlockDelaySeconds => unlockDelaySeconds;
    public float ElapsedSeconds => elapsedSeconds;
    public bool IsInteractionUnlocked => isInteractionUnlocked;
    public float RemainingLockedSeconds => Mathf.Max(0f, unlockDelaySeconds - elapsedSeconds);

    public event Action Unlocked;

    public static InteractionGateController GetOrCreate(string objectName, float delaySeconds)
    {
        InteractionGateController existing = FindObjectOfType<InteractionGateController>();
        if (existing != null)
        {
            existing.Configure(delaySeconds);
            return existing;
        }

        GameObject gateObject = new GameObject(objectName);
        InteractionGateController controller = gateObject.AddComponent<InteractionGateController>();
        controller.Configure(delaySeconds);
        return controller;
    }

    private void Update()
    {
        if (isInteractionUnlocked)
        {
            return;
        }

        elapsedSeconds += Time.deltaTime;
        if (elapsedSeconds >= unlockDelaySeconds)
        {
            elapsedSeconds = unlockDelaySeconds;
            isInteractionUnlocked = true;
            Unlocked?.Invoke();
        }
    }

    public void Configure(float delaySeconds)
    {
        unlockDelaySeconds = Mathf.Max(0f, delaySeconds);
        if (elapsedSeconds >= unlockDelaySeconds)
        {
            isInteractionUnlocked = true;
        }
    }
}
