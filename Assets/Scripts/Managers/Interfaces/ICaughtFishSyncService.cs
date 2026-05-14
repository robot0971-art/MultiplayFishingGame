using System;
using MultiplayFishing.Data.Models;

namespace MultiplayFishing.Core
{
    public interface ICaughtFishSyncService
    {
        bool IsConfigured { get; }

        void SaveCaughtFish(string playerName, string fishId, float length, FishDataSO fishData);
        void SyncProfileName(string fallbackName, Action<string> onResolved);
        void SaveProfileName(string playerName);
        void SyncCaughtFishToLocal(IUserService userService);
        void MarkCaughtFishSold(InventoryItem item);
        void MarkCaughtFishSold(System.Collections.Generic.IEnumerable<InventoryItem> items);
        void SyncWalletToLocal(IUserService userService);
        void SaveWallet(int gold);
        void SyncEquipmentToLocal(IUserService userService);
        void SaveEquipment(UserSaveData userData);
    }
}
