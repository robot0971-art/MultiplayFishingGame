using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
        [SerializeField] private bool syncRemoteCaughtFishOnStart = true;

        private const string AccessTokenKey = "Supabase.AccessToken";
        private const string RefreshTokenKey = "Supabase.RefreshToken";
        private const string ExpiresAtKey = "Supabase.ExpiresAt";

        public bool IsConfigured => config != null && config.HasRequiredValues;

        private void Awake()
        {
            DIContainer.Register<ICaughtFishSyncService>(this);
        }

        private void Start()
        {
            if (!syncRemoteCaughtFishOnStart)
            {
                return;
            }

            StartCoroutine(SyncCaughtFishWhenUserServiceReadyRoutine());
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

        public void SyncCaughtFishToLocal(IUserService userService)
        {
            if (!IsConfigured)
            {
                Debug.LogWarning("[SupabaseCaughtFishSyncService] Supabase config is missing. Skipping remote fish sync.");
                return;
            }

            if (userService == null)
            {
                Debug.LogWarning("[SupabaseCaughtFishSyncService] UserService is missing. Skipping remote fish sync.");
                return;
            }

            StartCoroutine(SyncCaughtFishToLocalRoutine(userService));
        }

        public void MarkCaughtFishSold(InventoryItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.remoteId))
            {
                return;
            }

            StartCoroutine(MarkCaughtFishSoldRoutine(new[] { item }));
        }

        public void MarkCaughtFishSold(IEnumerable<InventoryItem> items)
        {
            if (items == null)
            {
                return;
            }

            List<InventoryItem> remoteItems = new List<InventoryItem>();
            foreach (InventoryItem item in items)
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.remoteId))
                {
                    remoteItems.Add(item);
                }
            }

            if (remoteItems.Count == 0)
            {
                return;
            }

            StartCoroutine(MarkCaughtFishSoldRoutine(remoteItems));
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

        private IEnumerator SyncCaughtFishToLocalRoutine(IUserService userService)
        {
            string accessToken = null;
            yield return EnsureAuthenticatedRoutine(token => accessToken = token);

            if (string.IsNullOrEmpty(accessToken))
            {
                Debug.LogWarning("[SupabaseCaughtFishSyncService] Supabase anonymous auth failed. Skipping remote fish sync.");
                yield break;
            }

            string url = $"{config.ProjectUrl}/rest/v1/caught_fish?select=id,fish_id,length_cm,caught_at&sold_at=is.null&order=caught_at.desc";
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("apikey", config.PublishableKey);
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

            if (logRequests)
            {
                Debug.Log($"[SupabaseCaughtFishSyncService] GET {url}");
            }

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[SupabaseCaughtFishSyncService] Failed to sync caught fish. " +
                    $"Code={request.responseCode}, Error={request.error}, Body={request.downloadHandler.text}");
                yield break;
            }

            List<CaughtFishRow> rows = ParseCaughtFishRows(request.downloadHandler.text);
            int mergedCount = 0;
            foreach (CaughtFishRow row in rows)
            {
                if (userService.MergeFishFromRemote(row.id, row.fish_id, row.length_cm, ParseTimestamp(row.caught_at)))
                {
                    mergedCount++;
                }
            }

            if (mergedCount > 0)
            {
                userService.SaveRemoteMerge();
            }

            Debug.Log($"[SupabaseCaughtFishSyncService] Synced {rows.Count} remote caught fish rows. Merged {mergedCount} new local items.");
        }

        private IEnumerator MarkCaughtFishSoldRoutine(IEnumerable<InventoryItem> items)
        {
            string accessToken = null;
            yield return EnsureAuthenticatedRoutine(token => accessToken = token);

            if (string.IsNullOrEmpty(accessToken))
            {
                Debug.LogWarning("[SupabaseCaughtFishSyncService] Supabase anonymous auth failed. Skipping sold fish sync.");
                yield break;
            }

            foreach (InventoryItem item in items)
            {
                string url = $"{config.ProjectUrl}/rest/v1/caught_fish?id=eq.{UnityWebRequest.EscapeURL(item.remoteId)}";
                string body = $"{{\"sold_at\":\"{DateTimeOffset.UtcNow:O}\",\"sold_price\":{GetSellPrice(item.fishId)}}}";
                byte[] payload = Encoding.UTF8.GetBytes(body);

                using UnityWebRequest request = new UnityWebRequest(url, "PATCH");
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", config.PublishableKey);
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.SetRequestHeader("Prefer", "return=minimal");

                if (logRequests)
                {
                    Debug.Log($"[SupabaseCaughtFishSyncService] PATCH {url} {body}");
                }

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    continue;
                }

                Debug.LogWarning(
                    $"[SupabaseCaughtFishSyncService] Failed to mark fish sold. " +
                    $"RemoteId={item.remoteId}, Code={request.responseCode}, Error={request.error}, Body={request.downloadHandler.text}");
            }
        }

        private IEnumerator SyncCaughtFishWhenUserServiceReadyRoutine()
        {
            const float timeoutSeconds = 5f;
            float elapsed = 0f;

            while (elapsed < timeoutSeconds)
            {
                if (DIContainer.TryResolve(out IUserService userService))
                {
                    SyncCaughtFishToLocal(userService);
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            Debug.LogWarning("[SupabaseCaughtFishSyncService] UserService was not ready before startup sync timed out.");
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

        private static List<CaughtFishRow> ParseCaughtFishRows(string json)
        {
            List<CaughtFishRow> rows = new List<CaughtFishRow>();
            if (string.IsNullOrWhiteSpace(json) || json == "[]")
            {
                return rows;
            }

            string wrappedJson = "{\"rows\":" + json + "}";
            CaughtFishRowsWrapper wrapper = JsonUtility.FromJson<CaughtFishRowsWrapper>(wrappedJson);
            if (wrapper?.rows != null)
            {
                rows.AddRange(wrapper.rows);
            }

            return rows;
        }

        private static long ParseTimestamp(string value)
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset timestamp))
            {
                return timestamp.ToUnixTimeSeconds();
            }

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static int GetSellPrice(string fishId)
        {
            if (DIContainer.TryResolve(out IDataService dataService))
            {
                FishDataSO fishData = dataService.GetFishData(fishId);
                if (fishData != null)
                {
                    return fishData.sellPrice;
                }
            }

            return 0;
        }

        [Serializable]
        private sealed class SupabaseAuthSession
        {
            public string access_token = "";
            public string refresh_token = "";
            public int expires_in = 3600;
        }

        [Serializable]
        private sealed class CaughtFishRowsWrapper
        {
            public CaughtFishRow[] rows = Array.Empty<CaughtFishRow>();
        }

        [Serializable]
        private sealed class CaughtFishRow
        {
            public string id = "";
            public string fish_id = "";
            public float length_cm = 0f;
            public string caught_at = "";
        }
    }
}
