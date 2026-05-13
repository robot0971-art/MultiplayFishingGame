using MultiplayFishing.Data.Models;

namespace MultiplayFishing.Core
{
    public interface ICaughtFishSyncService
    {
        bool IsConfigured { get; }

        void SaveCaughtFish(string playerName, string fishId, float length, FishDataSO fishData);
    }
}
