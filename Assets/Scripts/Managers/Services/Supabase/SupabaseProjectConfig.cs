using UnityEngine;

namespace MultiplayFishing.Core
{
    [CreateAssetMenu(fileName = "SupabaseProjectConfig", menuName = "Fishing/Supabase Project Config")]
    public class SupabaseProjectConfig : ScriptableObject
    {
        [SerializeField] private string projectUrl;
        [SerializeField] private string publishableKey;
        [SerializeField] private string accessToken;

        public string ProjectUrl => projectUrl != null ? projectUrl.Trim().TrimEnd('/') : "";
        public string PublishableKey => publishableKey != null ? publishableKey.Trim() : "";
        public string AccessToken => accessToken != null ? accessToken.Trim() : "";

        public bool HasRequiredValues =>
            !string.IsNullOrWhiteSpace(ProjectUrl) &&
            !string.IsNullOrWhiteSpace(PublishableKey);
    }
}
