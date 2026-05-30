using UnityEngine;

public class PromptCaptureWindow
{
    private readonly float captureDurationSeconds;
    private float elapsedSeconds;
    private bool isClosed;

    public PromptCaptureWindow(float durationSeconds)
    {
        captureDurationSeconds = Mathf.Max(0.01f, durationSeconds);
    }

    public float CaptureDurationSeconds => captureDurationSeconds;
    public float ElapsedSeconds => elapsedSeconds;
    public bool IsPromptCaptureClosed => isClosed;
    public float Progress01 => Mathf.Clamp01(elapsedSeconds / captureDurationSeconds);

    public bool Advance(float deltaTime)
    {
        if (isClosed)
        {
            return false;
        }

        elapsedSeconds += Mathf.Max(0f, deltaTime);
        if (elapsedSeconds >= captureDurationSeconds)
        {
            elapsedSeconds = captureDurationSeconds;
            isClosed = true;
            return true;
        }

        return false;
    }
}
