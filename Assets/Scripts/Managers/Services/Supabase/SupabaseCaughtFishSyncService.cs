using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using MultiplayFishing.Data.Models;

namespace MultiplayFishing.Core
{
    public sealed class SupabaseCaughtFishSyncService : MonoBehaviour, ICaughtFishSyncService
    {
        [SerializeField] private SupabaseProjectConfig config;
        [SerializeField] private bool logRequests;

        private const string AccessTokenKey = "Supabase.AccessToken";
        private const string RefreshTokenKey = "Supabase.RefreshToken";
        private const string ExpiresAtKey = "Supabase.ExpiresAt";

        public bool IsConfigured => config != null && config.HasRequiredValues;

        private void Awake()
        {
            DIContainer.Register<ICaughtFishSyncService>(this);
        }

        public void SaveCaughtFish(string playerName, string fishId, float length, FishDataSO fishData)
        {
            if (!IsConfigured)
            {
                Debug.LogWarning("[SupabaseCaughtFishSyncService] Supabase config is missing. Skipping remote fish save.");
                return;
            }

            if (string.IsNullOrWhiteSpace(fishId))
            {
                Debug.LogWarning("[SupabaseCaughtFishSyncService] fishId is empty. Skipping remote fish save.");
                return;
            }

            StartCoroutine(PostCaughtFishRoutine(playerName, fishId, length, fishData));
        }

        private IEnumerator PostCaughtFishRoutine(string playerName, string fishId, float length, FishDataSO fishData)
        {
            string accessToken = null;
            yield return EnsureAuthenticatedRoutine(token => accessToken = token);

            if (string.IsNullOrEmpty(accessToken))
            {
                Debug.LogWarning("[SupabaseCaughtFishSyncService] Supabase anonymous auth failed. Skipping remote fish save.");
                yield break;
            }

            string url = $"{config.ProjectUrl}/rest/v1/caught_fish";
            string body = BuildCaughtFishJson(playerName, fishId, length, fishData);
            byte[] payload = Encoding.UTF8.GetBytes(body);

            using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", config.PublishableKey);
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            request.SetRequestHeader("Prefer", "return=minimal");

            if (logRequests)
            {
                Debug.Log($"[SupabaseCaughtFishSyncService] POST {url} {body}");
            }

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[SupabaseCaughtFishSyncService] Saved caught fish: {fishId} ({length:F1}cm).");
                yield break;
            }

            Debug.LogWarning(
                $"[SupabaseCaughtFishSyncService] Failed to save caught fish. " +
                $"Code={request.responseCode}, Error={request.error}, Body={request.downloadHandler.text}");
        }

        private IEnumerator EnsureAuthenticatedRoutine(Action<string> onComplete)
        {
            string savedAccessToken = PlayerPrefs.GetString(AccessTokenKey, "");
            long expiresAt = ParseLong(PlayerPrefs.GetString(ExpiresAtKey, "0"));

            if (!string.IsNullOrEmpty(savedAccessToken) && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expiresAt - 60)
            {
                onComplete?.Invoke(savedAccessToken);
                yield break;
            }

            string refreshToken = PlayerPrefs.GetString(RefreshTokenKey, "");
            if (!string.IsNullOrEmpty(refreshToken))
            {
                bool refreshDone = false;
                string refreshedToken = null;
                yield return RefreshSessionRoutine(refreshToken, token =>
                {
                    refreshedToken = token;
                    refreshDone = true;
                });

                if (refreshDone && !string.IsNullOrEmpty(refreshedToken))
                {
                    onComplete?.Invoke(refreshedToken);
                    yield break;
                }
            }

            yield return SignInAnonymouslyRoutine(onComplete);
        }

        private IEnumerator SignInAnonymouslyRoutine(Action<string> onComplete)
        {
            string url = $"{config.ProjectUrl}/auth/v1/signup";
            byte[] payload = Encoding.UTF8.GetBytes("{\"data\":{}}");

            using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", config.PublishableKey);

            if (logRequests)
            {
                Debug.Log($"[SupabaseCaughtFishSyncService] POST {url} anonymous sign-in");
            }

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[SupabaseCaughtFishSyncService] Anonymous sign-in failed. " +
                    $"Code={request.responseCode}, Error={request.error}, Body={request.downloadHandler.text}");
                onComplete?.Invoke(null);
                yield break;
            }

            SupabaseAuthSession session = JsonUtility.FromJson<SupabaseAuthSession>(request.downloadHandler.text);
            SaveSession(session);
            onComplete?.Invoke(session != null ? session.access_token : null);
        }

        private IEnumerator RefreshSessionRoutine(string refreshToken, Action<string> onComplete)
        {
            string url = $"{config.ProjectUrl}/auth/v1/token?grant_type=refresh_token";
            string body = $"{{\"refresh_token\":\"{EscapeJson(refreshToken)}\"}}";
            byte[] payload = Encoding.UTF8.GetBytes(body);

            using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", config.PublishableKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[SupabaseCaughtFishSyncService] Session refresh failed. " +
                    $"Code={request.responseCode}, Error={request.error}, Body={request.downloadHandler.text}");
                onComplete?.Invoke(null);
                yield break;
            }

            SupabaseAuthSession session = JsonUtility.FromJson<SupabaseAuthSession>(request.downloadHandler.text);
            SaveSession(session);
            onComplete?.Invoke(session != null ? session.access_token : null);
        }

        private static void SaveSession(SupabaseAuthSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.access_token))
            {
                return;
            }

            PlayerPrefs.SetString(AccessTokenKey, session.access_token);
            if (!string.IsNullOrEmpty(session.refresh_token))
            {
                PlayerPrefs.SetString(RefreshTokenKey, session.refresh_token);
            }

            long expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Mathf.Max(60, session.expires_in);
            PlayerPrefs.SetString(ExpiresAtKey, expiresAt.ToString());
            PlayerPrefs.Save();
        }

        private static long ParseLong(string value)
        {
            return long.TryParse(value, out long result) ? result : 0L;
        }

        private static string BuildCaughtFishJson(string playerName, string fishId, float length, FishDataSO fishData)
        {
            string fishName = fishData != null ? fishData.fishName : "";
            string rank = fishData != null ? fishData.rank : "";
            int expReward = fishData != null ? fishData.expReward : 0;
            int sellPrice = fishData != null ? fishData.sellPrice : 0;

            return "{" +
                $"\"player_name\":\"{EscapeJson(playerName)}\"," +
                $"\"fish_id\":\"{EscapeJson(fishId)}\"," +
                $"\"fish_name\":\"{EscapeJson(fishName)}\"," +
                $"\"rank\":\"{EscapeJson(rank)}\"," +
                $"\"length_cm\":{length.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"exp_reward\":{expReward}," +
                $"\"sell_price\":{sellPrice}" +
                "}";
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        [Serializable]
        private sealed class SupabaseAuthSession
        {
            public string access_token = "";
            public string refresh_token = "";
            public int expires_in = 3600;
        }
    }
}
