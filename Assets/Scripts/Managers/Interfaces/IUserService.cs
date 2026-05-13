using System;
using MultiplayFishing.Data.Models;

namespace MultiplayFishing.Core
{
    public enum ShopItemType
    {
        Rod,
        Bait
    }

    public interface IUserService
    {
        UserSaveData UserData { get; }
        event Action OnDataChanged;
        
        void AddFish(string fishId, float length);
        bool MergeFishFromRemote(string remoteId, string fishId, float length, long caughtTime);
        void SaveRemoteMerge();

        void SellFish(string instanceId);
        void SellAllFish();

        bool BuyItem(ShopItemType itemType, string itemId);

        bool EquipRod(string rodId);
        bool EquipBait(string baitId);
        void UnequipRod();
        void UnequipBait();

        bool IsRodOwned(string rodId);
        bool IsBaitOwned(string baitId);

        void Save();
        void Load();
    }
}
