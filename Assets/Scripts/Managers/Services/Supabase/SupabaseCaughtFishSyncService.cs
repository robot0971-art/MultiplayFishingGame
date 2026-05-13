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
            string url = $"{config.ProjectUrl}/rest/v1/caught_fish";
            string body = BuildCaughtFishJson(playerName, fishId, length, fishData);
            byte[] payload = Encoding.UTF8.GetBytes(body);

            using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", config.PublishableKey);
            request.SetRequestHeader("Authorization", $"Bearer {config.AccessToken}");
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
    }
}
