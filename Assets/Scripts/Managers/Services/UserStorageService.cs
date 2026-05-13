using MultiplayFishing.Data.Models;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MultiplayFishing.Core
{
    public class UserStorageService : IUserService
    {
        private UserSaveData userData = new UserSaveData();
        private readonly string savePath;
        private readonly IDataService dataService;
        private ICaughtFishSyncService caughtFishSyncService;

        public UserSaveData UserData => userData;
        public event Action OnDataChanged;

        public UserStorageService(IDataService dataService)
        {
            this.dataService = dataService;
            savePath = Path.Combine(Application.persistentDataPath, "UserData.json");
            Load();
        }

        public void AddFish(string fishId, float length)
        {
            EnsureUserData();

            // 1. 인벤토리 추가
            userData.AddToInventory(fishId, length);
            
            // 2. 경험치 획득 및 레벨업 체크
            var fishInfo = dataService.GetFishData(fishId);
            if (fishInfo != null)
            {
                bool levelUp = userData.AddExp(fishInfo.expReward);
                Debug.Log($"[UserStorageService] Gained {fishInfo.expReward} EXP.");
                if (levelUp) Debug.Log($"<color=yellow><b>[LEVEL UP!]</b></color> Tier {userData.currentTier}");
            }
            
            Save();
            OnDataChanged?.Invoke(); // UI에 알림
        }

        public bool MergeFishFromRemote(string remoteId, string fishId, float length, long caughtTime)
        {
            EnsureUserData();

            if (string.IsNullOrWhiteSpace(fishId))
            {
                return false;
            }

            bool alreadyExists = !string.IsNullOrWhiteSpace(remoteId)
                ? userData.inventory.Exists(item => item.remoteId == remoteId)
                : userData.inventory.Exists(item =>
                    item.fishId == fishId &&
                    Mathf.Abs(item.length - length) < 0.01f &&
                    item.caughtTime == caughtTime);

            if (alreadyExists)
            {
                return false;
            }

            InventoryItem remoteItem = new InventoryItem(fishId, length)
            {
                remoteId = remoteId,
                caughtTime = caughtTime
            };
            userData.inventory.Add(remoteItem);
            userData.UpdateEncyclopedia(fishId, length);
            return true;
        }

        public void SellFish(string instanceId)
        {
            InventoryItem item = userData.inventory.Find(x => x.instanceId == instanceId);
            if (item != null)
            {
                var fishInfo = dataService.GetFishData(item.fishId);
                if (fishInfo != null)
                {
                    userData.gold += fishInfo.sellPrice;
                    userData.inventory.Remove(item);
                    SyncSoldFish(item);
                    Debug.Log($"[UserStorageService] Sold {fishInfo.fishName}. Current Gold: {userData.gold}");
                    Save();
                    OnDataChanged?.Invoke(); // UI에 알림
                }
            }
        }

        public void SellAllFish()
        {
            if (userData.inventory.Count == 0) return;

            int totalGain = 0;
            List<InventoryItem> soldItems = new List<InventoryItem>(userData.inventory);
            foreach (var item in userData.inventory)
            {
                var fishInfo = dataService.GetFishData(item.fishId);
                if (fishInfo != null)
                {
                    totalGain += fishInfo.sellPrice;
                }
            }

            userData.gold += totalGain;
            userData.inventory.Clear();
            SyncSoldFish(soldItems);
            
            Debug.Log($"[UserStorageService] Bulk sold all fish. Gained {totalGain}G. Total Gold: {userData.gold}");
            
            Save();
            OnDataChanged?.Invoke();
        }

        public bool BuyItem(ShopItemType itemType, string itemId)
        {
            EnsureUserData();

            int price = 0;

            if (itemType == ShopItemType.Rod)
            {
                if (IsRodOwned(itemId))
                {
                    Debug.LogWarning($"[UserStorageService] Rod {itemId} already owned.");
                    return false;
                }
                var rodData = dataService.GetRodData(itemId);
                if (rodData == null) return false;
                price = rodData.price;
            }
            else if (itemType == ShopItemType.Bait)
            {
                if (IsBaitOwned(itemId))
                {
                    Debug.LogWarning($"[UserStorageService] Bait {itemId} already owned.");
                    return false;
                }
                var baitData = dataService.GetBaitData(itemId);
                if (baitData == null) return false;
                price = baitData.price;
            }

            if (userData.gold < price)
            {
                Debug.Log($"[UserStorageService] Not enough gold. Need {price}, have {userData.gold}.");
                return false;
            }

            userData.gold -= price;

            if (itemType == ShopItemType.Rod)
                userData.ownedRodIds.Add(itemId);
            else
                userData.ownedBaitIds.Add(itemId);

            Debug.Log($"[UserStorageService] Purchased {itemType} {itemId}. Gold left: {userData.gold}");
            Save();
            OnDataChanged?.Invoke();
            return true;
        }

        public bool EquipRod(string rodId)
        {
            EnsureUserData();

            if (!IsRodOwned(rodId)) return false;
            userData.equippedRodId = rodId;
            Debug.Log($"[UserStorageService] Equipped rod: {rodId}");
            Save();
            OnDataChanged?.Invoke();
            return true;
        }

        public bool EquipBait(string baitId)
        {
            EnsureUserData();

            if (!IsBaitOwned(baitId)) return false;
            userData.equippedBaitId = baitId;
            Debug.Log($"[UserStorageService] Equipped bait: {baitId}");
            Save();
            OnDataChanged?.Invoke();
            return true;
        }

        public void UnequipRod()
        {
            EnsureUserData();

            userData.equippedRodId = "";
            Debug.Log($"[UserStorageService] Unequipped rod.");
            Save();
            OnDataChanged?.Invoke();
        }

        public void UnequipBait()
        {
            EnsureUserData();

            userData.equippedBaitId = "";
            Debug.Log($"[UserStorageService] Unequipped bait.");
            Save();
            OnDataChanged?.Invoke();
        }

        public bool IsRodOwned(string rodId)
        {
            EnsureUserData();
            return userData.ownedRodIds.Contains(rodId);
        }

        public bool IsBaitOwned(string baitId)
        {
            EnsureUserData();
            return userData.ownedBaitIds.Contains(baitId);
        }

        public void Save()
        {
            EnsureUserData();
            string json = JsonUtility.ToJson(userData, true);
            File.WriteAllText(savePath, json);
        }

        public void Load()
        {
            if (File.Exists(savePath))
            {
                string json = File.ReadAllText(savePath);
                userData = JsonUtility.FromJson<UserSaveData>(json);
                EnsureUserData();
                CleanGhostData();
            }
            else
            {
                userData = new UserSaveData();
            }
            EnsureDefaultItems();
        }

        private void EnsureDefaultItems()
        {
            EnsureUserData();

            if (!userData.ownedRodIds.Contains("rod_basic"))
            {
                userData.ownedRodIds.Add("rod_basic");
                Debug.Log("[UserStorageService] 기본 낚싯대가 지급되었습니다.");
                Save();
            }
        }

        private void CleanGhostData()
        {
            EnsureUserData();
            if (dataService == null) return;

            int removedInventory = userData.inventory.RemoveAll(item =>
            {
                var fishData = dataService.GetFishData(item.fishId);
                return fishData == null;
            });

            int removedEncyclopedia = userData.encyclopedia.RemoveAll(record =>
            {
                var fishData = dataService.GetFishData(record.fishId);
                return fishData == null;
            });

            if (removedInventory > 0 || removedEncyclopedia > 0)
            {
                Debug.Log($"[UserStorageService] Cleaned {removedInventory} ghost inventory items and {removedEncyclopedia} ghost encyclopedia records.");
                Save();
            }
        }

        public void SaveRemoteMerge()
        {
            Save();
            OnDataChanged?.Invoke();
        }

        private void EnsureUserData()
        {
            if (userData == null)
                userData = new UserSaveData();

            userData.inventory ??= new List<InventoryItem>();
            userData.encyclopedia ??= new List<FishRecord>();
            userData.ownedRodIds ??= new List<string>();
            userData.ownedBaitIds ??= new List<string>();
            userData.equippedRodId ??= "";
            userData.equippedBaitId ??= "";
        }

        private void SyncSoldFish(InventoryItem item)
        {
            if (item == null) return;

            EnsureCaughtFishSyncService();
            caughtFishSyncService?.MarkCaughtFishSold(item);
        }

        private void SyncSoldFish(IEnumerable<InventoryItem> items)
        {
            if (items == null) return;

            EnsureCaughtFishSyncService();
            caughtFishSyncService?.MarkCaughtFishSold(items);
        }

        private void EnsureCaughtFishSyncService()
        {
            if (caughtFishSyncService != null) return;

            DIContainer.TryResolve(out caughtFishSyncService);
        }
    }
}
