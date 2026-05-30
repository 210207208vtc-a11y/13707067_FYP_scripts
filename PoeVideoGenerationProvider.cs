using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class PoeVideoGenerationProvider : IVideoGenerationProvider, IPoeDiagnosticsProvider
{
    private const string DefaultApiKeyFileName = "poe_api_key.txt";
    private const float PollIntervalSeconds = 2.5f;
    private const int ContentDownloadRetryCount = 8;
    private static readonly Regex IdFieldPattern = new Regex(@"""id""\s*:\s*""([^""\\]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ErrorMessagePattern = new Regex(@"""message""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex StatusFieldPattern = new Regex(@"""status""\s*:\s*""([^""\\]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerator GenerateVideo(VideoGenerationRequest request, Action<VideoGenerationResult> onCompleted)
    {
        VideoGenerationResult result = new VideoGenerationResult
        {
            status = VideoGenerationStatus.ProviderError,
        };

        ResolvedApiKeyInfo keyInfo = ResolveApiKeyInfo(request);
        string apiKey = keyInfo.value;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            result.status = VideoGenerationStatus.MissingApiKey;
            result.error = BuildMissingApiKeyMessage(request);
            onCompleted?.Invoke(result);
            yield break;
        }

        if (!LooksLikePoeApiKey(apiKey))
        {
            result.status = VideoGenerationStatus.MissingApiKey;
            result.error = "Poe API key format is invalid. Expected a key starting with 'sk-poe-'. " + BuildApiKeySourceSuffix(keyInfo.source);
            onCompleted?.Invoke(result);
            yield break;
        }

        string createUrl = NormalizeBaseUrl(request.baseUrl) + "/videos";
        string createResponse = string.Empty;
        string providerError = string.Empty;
        string videoId = string.Empty;
        string resolvedModel = NormalizeModelId(request.model);
        string resolvedSize = ResolveOutputSize(request);
        int resolvedSeconds = Mathf.Clamp(request.durationSeconds, 4, 8);
        string payload = BuildCreatePayload(request, resolvedModel, resolvedSize);
        string requestSummary = $"model={resolvedModel}, size={resolvedSize}, seconds={resolvedSeconds}, scene={request.sceneName}";

        using (UnityWebRequest createRequest = CreateJsonPostRequest(createUrl, payload, apiKey, request.timeoutSeconds))
        {
            yield return createRequest.SendWebRequest();

            createResponse = createRequest.downloadHandler != null ? createRequest.downloadHandler.text : string.Empty;
            result.rawResponse = createResponse;

            if (createRequest.result != UnityWebRequest.Result.Success)
            {
                providerError = BuildRequestError(createRequest, createResponse);
                result.status = ResolveRequestFailureStatus(createRequest, createResponse);
                result.error = AppendApiKeySourceIfNeeded(providerError, keyInfo.source) + $" Request: {requestSummary}";
                Debug.LogWarning($"[PoeVideoGenerationProvider] Video create failed. {requestSummary}");
                onCompleted?.Invoke(result);
                yield break;
            }

            videoId = ExtractId(createResponse);
            if (string.IsNullOrWhiteSpace(videoId))
            {
                providerError = "Poe video response did not include a video id.";
                result.status = VideoGenerationStatus.InvalidResponse;
                result.error = providerError + $" Request: {requestSummary}";
                onCompleted?.Invoke(result);
                yield break;
            }
        }

        result.providerVideoId = videoId;

        string statusResponse = string.Empty;
        string currentStatus = string.Empty;
        float startTime = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - startTime < Mathf.Max(30f, request.timeoutSeconds))
        {
            using (UnityWebRequest statusRequest = UnityWebRequest.Get($"{NormalizeBaseUrl(request.baseUrl)}/videos/{videoId}"))
            {
                statusRequest.timeout = Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(30f, request.timeoutSeconds)));
                statusRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
                yield return statusRequest.SendWebRequest();

                statusResponse = statusRequest.downloadHandler != null ? statusRequest.downloadHandler.text : string.Empty;
                result.rawResponse = createResponse + "\n---\n" + statusResponse;

                if (statusRequest.result != UnityWebRequest.Result.Success)
                {
                    result.status = ResolveRequestFailureStatus(statusRequest, statusResponse);
                    result.error = AppendApiKeySourceIfNeeded(BuildRequestError(statusRequest, statusResponse), keyInfo.source);
                    onCompleted?.Invoke(result);
                    yield break;
                }

                currentStatus = ExtractStatus(statusResponse);
                if (string.Equals(currentStatus, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (string.Equals(currentStatus, "error", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(currentStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(currentStatus, "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    result.status = VideoGenerationStatus.ProviderError;
                    result.error = ExtractProviderError(statusResponse);
                    if (string.IsNullOrWhiteSpace(result.error))
                    {
                        result.error = $"Poe returned terminal status '{currentStatus}'.";
                    }

                    onCompleted?.Invoke(result);
                    yield break;
                }
            }

            yield return new WaitForSecondsRealtime(PollIntervalSeconds);
        }

        if (!string.Equals(currentStatus, "completed", StringComparison.OrdinalIgnoreCase))
        {
            result.status = VideoGenerationStatus.NetworkError;
            result.error = "Timed out waiting for Poe video generation to complete.";
            onCompleted?.Invoke(result);
            yield break;
        }

        string downloadedFilePath = string.Empty;
        string contentError = string.Empty;
        string contentRawResponse = string.Empty;
        yield return DownloadCompletedVideoContent(apiKey, request, videoId, path => downloadedFilePath = path, error => contentError = error, raw => contentRawResponse = raw);

        if (string.IsNullOrWhiteSpace(downloadedFilePath))
        {
            result.status = VideoGenerationStatus.ProviderError;
            result.error = string.IsNullOrWhiteSpace(contentError)
                ? "Video completed, but downloading content failed."
                : contentError;
            result.rawResponse = string.IsNullOrWhiteSpace(result.rawResponse)
                ? contentRawResponse
                : result.rawResponse + "\n---\n" + contentRawResponse;
            onCompleted?.Invoke(result);
            yield break;
        }

        result.status = VideoGenerationStatus.Success;
        result.playableUrlOrPath = downloadedFilePath;
        onCompleted?.Invoke(result);
    }

    public IEnumerator RunDiagnostics(VideoGenerationRequest request, Action<PoeDiagnosticsResult> onCompleted)
    {
        PoeDiagnosticsResult diagnostics = new PoeDiagnosticsResult();
        ResolvedApiKeyInfo keyInfo = ResolveApiKeyInfo(request);
        string apiKey = keyInfo.value;
        diagnostics.keyFormatValid = LooksLikePoeApiKey(apiKey);

        using (UnityWebRequest webRequest = UnityWebRequest.Get(NormalizeBaseUrl(request.baseUrl) + "/models"))
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
            }

            webRequest.timeout = Mathf.Max(1, Mathf.CeilToInt(request.timeoutSeconds));
            yield return webRequest.SendWebRequest();

            diagnostics.modelsRawResponse = webRequest.downloadHandler != null ? webRequest.downloadHandler.text : string.Empty;

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                diagnostics.modelsEndpointReachable = false;
                diagnostics.error = string.IsNullOrWhiteSpace(webRequest.error)
                    ? $"HTTP {webRequest.responseCode}"
                    : $"{webRequest.error} (HTTP {webRequest.responseCode})";
                onCompleted?.Invoke(diagnostics);
                yield break;
            }

            diagnostics.modelsEndpointReachable = true;
            diagnostics.sampleModels = ExtractModelIds(diagnostics.modelsRawResponse);
            string targetModel = NormalizeModelId(request.model);
            diagnostics.targetModelListed = diagnostics.sampleModels.Exists(modelId => string.Equals(modelId, targetModel, System.StringComparison.OrdinalIgnoreCase));
        }

        onCompleted?.Invoke(diagnostics);
    }

    public static bool LooksLikePoeApiKey(string apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey) && apiKey.Trim().StartsWith("sk-poe-", StringComparison.Ordinal);
    }

    public static string ResolveApiKey(VideoGenerationRequest request)
    {
        return ResolveApiKeyInfo(request).value;
    }

    private static ResolvedApiKeyInfo ResolveApiKeyInfo(VideoGenerationRequest request)
    {
        string apiKey = Environment.GetEnvironmentVariable(request.apiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return new ResolvedApiKeyInfo(apiKey.Trim(), "environment variable " + request.apiKeyEnvironmentVariable);
        }

        foreach (string filePath in GetCandidateApiKeyPaths(request))
        {
            if (!File.Exists(filePath))
            {
                continue;
            }

            string fileValue = File.ReadAllText(filePath).Trim();
            if (!string.IsNullOrWhiteSpace(fileValue))
            {
                return new ResolvedApiKeyInfo(fileValue, filePath);
            }
        }

        return new ResolvedApiKeyInfo(string.Empty, "not found");
    }

    private static UnityWebRequest CreateJsonPostRequest(string url, string payload, string apiKey, float timeoutSeconds)
    {
        UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        byte[] body = Encoding.UTF8.GetBytes(payload);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(60f, timeoutSeconds)));
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);
        return request;
    }

    private static string BuildCreatePayload(VideoGenerationRequest request, string resolvedModel, string resolvedSize)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("{");
        builder.Append("\"model\":\"").Append(EscapeJson(resolvedModel)).Append("\",");
        builder.Append("\"prompt\":\"").Append(EscapeJson(request.prompt)).Append("\",");
        builder.Append("\"seconds\":").Append(Mathf.Clamp(request.durationSeconds, 4, 8)).Append(",");
        builder.Append("\"size\":\"").Append(EscapeJson(resolvedSize)).Append("\"");
        builder.Append("}");
        return builder.ToString();
    }

    private static string ResolveOutputSize(VideoGenerationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.size))
        {
            return request.size.Trim();
        }

        switch ((request.resolution ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "720p":
                return "1280x720";
            case "1080p":
            default:
                return "1920x1080";
        }
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "https://api.poe.com/v1";
        }

        return baseUrl.TrimEnd('/');
    }

    private static string BuildMissingApiKeyMessage(VideoGenerationRequest request)
    {
        List<string> searchPaths = GetCandidateApiKeyPaths(request);
        return
            $"Missing API key. Set environment variable {request.apiKeyEnvironmentVariable} " +
            $"or create {ResolveApiKeyFallbackFileName(request)} in one of these locations: {string.Join(" | ", searchPaths)}";
    }

    private static string AppendApiKeySourceIfNeeded(string error, string source)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return error;
        }

        if (error.IndexOf("incorrect api key", StringComparison.OrdinalIgnoreCase) >= 0 ||
            error.IndexOf("invalid_api_key", StringComparison.OrdinalIgnoreCase) >= 0 ||
            error.IndexOf("authentication_error", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return error + " " + BuildApiKeySourceSuffix(source);
        }

        return error;
    }

    private static string BuildApiKeySourceSuffix(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        return $"Resolved API key source: {source}.";
    }

    private readonly struct ResolvedApiKeyInfo
    {
        public readonly string value;
        public readonly string source;

        public ResolvedApiKeyInfo(string value, string source)
        {
            this.value = value;
            this.source = source;
        }
    }

    private static List<string> GetCandidateApiKeyPaths(VideoGenerationRequest request)
    {
        string fallbackFileName = ResolveApiKeyFallbackFileName(request);
        List<string> paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(fallbackFileName))
        {
            paths.Add(Path.Combine(Application.persistentDataPath, fallbackFileName));
        }

        if (Application.isEditor)
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (!string.IsNullOrWhiteSpace(projectRoot))
                {
                    paths.Add(Path.Combine(projectRoot, fallbackFileName));
                    paths.Add(Path.Combine(projectRoot, "UserSettings", fallbackFileName));
                }
            }
            catch
            {
            }

            try
            {
                string currentDirectory = Directory.GetCurrentDirectory();
                if (!string.IsNullOrWhiteSpace(currentDirectory))
                {
                    paths.Add(Path.Combine(currentDirectory, fallbackFileName));

                    string parentDirectory = Directory.GetParent(currentDirectory)?.FullName;
                    if (!string.IsNullOrWhiteSpace(parentDirectory))
                    {
                        paths.Add(Path.Combine(parentDirectory, fallbackFileName));
                    }
                }
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackFileName))
        {
            paths.Add(Path.Combine(Application.streamingAssetsPath, fallbackFileName));
        }

        List<string> uniquePaths = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string normalizedPath = path.Replace('\\', '/');
            if (seen.Add(normalizedPath))
            {
                uniquePaths.Add(path);
            }
        }

        return uniquePaths;
    }

    private static string ResolveApiKeyFallbackFileName(VideoGenerationRequest request)
    {
        if (request != null && !string.IsNullOrWhiteSpace(request.apiKeyFallbackFileName))
        {
            return request.apiKeyFallbackFileName.Trim();
        }

        return DefaultApiKeyFileName;
    }

    private static string NormalizeModelId(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return "veo-3.1-lite";
        }

        string trimmed = model.Trim();
        string lowered = trimmed.ToLowerInvariant();

        switch (lowered)
        {
            case "veo-3.1-lite":
            case "veo-v3.1-lite":
            case "veo3.1lite":
                return "veo-3.1-lite";
            case "veo-3.1-fast":
            case "veo-v3.1-fast":
            case "veo3.1fast":
                return "veo-3.1-lite";
            default:
                return trimmed;
        }
    }

    private static bool IsRetryableModelError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.IndexOf("model does not support video generation", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("was not found", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("does not have access", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("could not initiate video generation", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("http 500", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("internal server error", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("server got itself in trouble", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("http 502", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("http 503", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("http 504", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ExtractId(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return string.Empty;
        }

        Match match = IdFieldPattern.Match(rawResponse);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string ExtractStatus(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return string.Empty;
        }

        Match match = StatusFieldPattern.Match(rawResponse);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string ExtractProviderError(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return string.Empty;
        }

        Match match = ErrorMessagePattern.Match(rawResponse);
        if (!match.Success)
        {
            return string.Empty;
        }

        string escaped = match.Groups[1].Value.Replace("\\/", "/");
        return Regex.Unescape(escaped).Trim();
    }

    private static List<string> ExtractModelIds(string rawResponse)
    {
        List<string> ids = new List<string>();
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return ids;
        }

        foreach (Match match in IdFieldPattern.Matches(rawResponse))
        {
            string id = match.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static VideoGenerationStatus ResolveRequestFailureStatus(UnityWebRequest request, string rawResponse)
    {
        string providerError = ExtractProviderError(rawResponse);
        return string.IsNullOrWhiteSpace(providerError)
            ? VideoGenerationStatus.NetworkError
            : VideoGenerationStatus.ProviderError;
    }

    private static string BuildRequestError(UnityWebRequest request, string rawResponse)
    {
        string providerError = ExtractProviderError(rawResponse);
        if (!string.IsNullOrWhiteSpace(providerError))
        {
            return providerError;
        }

        string responseSnippet = string.IsNullOrWhiteSpace(rawResponse) ? string.Empty : rawResponse.Trim();
        if (responseSnippet.Length > 240)
        {
            responseSnippet = responseSnippet.Substring(0, 237) + "...";
        }

        string baseError = string.IsNullOrWhiteSpace(request.error)
            ? $"HTTP {request.responseCode}"
            : $"{request.error} (HTTP {request.responseCode})";

        return string.IsNullOrWhiteSpace(responseSnippet)
            ? baseError
            : baseError + " | Response: " + responseSnippet;
    }

    private static string SaveVideoBytes(string videoId, byte[] bytes, string extension)
    {
        if (bytes == null || bytes.Length == 0)
        {
            throw new InvalidOperationException("Downloaded video content was empty.");
        }

        string directory = Path.Combine(Application.persistentDataPath, "GeneratedVideos");
        Directory.CreateDirectory(directory);

        string safeId = string.IsNullOrWhiteSpace(videoId) ? Guid.NewGuid().ToString("N") : SanitizeFileName(videoId);
        string filePath = Path.Combine(directory, safeId + extension);
        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }

    private static string ResolveOutputExtension(string contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            string lowered = contentType.ToLowerInvariant();
            if (lowered.Contains("webm"))
            {
                return ".webm";
            }

            if (lowered.Contains("quicktime"))
            {
                return ".mov";
            }
        }

        return ".mp4";
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static IEnumerator DownloadCompletedVideoContent(
        string apiKey,
        VideoGenerationRequest request,
        string videoId,
        Action<string> onPath,
        Action<string> onError,
        Action<string> onRawResponse)
    {
        onPath?.Invoke(string.Empty);
        onError?.Invoke(string.Empty);
        onRawResponse?.Invoke(string.Empty);

        string contentUrl = $"{NormalizeBaseUrl(request.baseUrl)}/videos/{videoId}/content";
        string lastError = string.Empty;
        string lastRawResponse = string.Empty;

        for (int attempt = 1; attempt <= ContentDownloadRetryCount; attempt++)
        {
            using (UnityWebRequest contentRequest = UnityWebRequest.Get(contentUrl))
            {
                contentRequest.timeout = Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(120f, request.timeoutSeconds)));
                contentRequest.downloadHandler = new DownloadHandlerBuffer();
                contentRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
                yield return contentRequest.SendWebRequest();

                lastRawResponse = contentRequest.downloadHandler != null ? contentRequest.downloadHandler.text : string.Empty;

                if (contentRequest.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        string downloadedFilePath = SaveVideoBytes(videoId, contentRequest.downloadHandler.data, ResolveOutputExtension(contentRequest.GetResponseHeader("Content-Type")));
                        onPath?.Invoke(downloadedFilePath);
                        onRawResponse?.Invoke(lastRawResponse);
                        yield break;
                    }
                    catch (Exception ex)
                    {
                        lastError = "Failed to save generated video locally: " + ex.Message;
                        break;
                    }
                }

                lastError = BuildRequestError(contentRequest, lastRawResponse);
                bool shouldRetry = ShouldRetryContentDownload(contentRequest.responseCode, attempt);
                if (!shouldRetry)
                {
                    break;
                }

                float retryDelay = Mathf.Min(12f, 1.4f * attempt);
                Debug.LogWarning($"[PoeVideoGenerationProvider] Video content download failed on attempt {attempt}/{ContentDownloadRetryCount}. Retrying in {retryDelay:F1}s.");
                yield return new WaitForSecondsRealtime(retryDelay);
            }
        }

        onError?.Invoke(lastError);
        onRawResponse?.Invoke(lastRawResponse);
    }

    private static bool ShouldRetryContentDownload(long responseCode, int attempt)
    {
        if (attempt >= ContentDownloadRetryCount)
        {
            return false;
        }

        return responseCode == 0 ||
               responseCode == 202 ||
               responseCode == 404 ||
               responseCode == 429 ||
               responseCode == 500 ||
               responseCode == 502 ||
               responseCode == 503 ||
               responseCode == 504;
    }
}
