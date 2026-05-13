using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayFishing.Data.Models
{
    [Serializable]
    public class InventoryItem
    {
        public string instanceId;
        public string remoteId;
        public string fishId;
        public float length;
        public long caughtTime;

        public InventoryItem(string id, float len)
        {
            instanceId = Guid.NewGuid().ToString();
            fishId = id;
            length = len;
            caughtTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    /// <summary>
    /// 도감에 기록되는 물고기별 정보
    /// </summary>
    [Serializable]
    public class FishRecord
    {
        public string fishId;
        public float maxRecord; // 역대 최대 크기

        public FishRecord(string id, float length)
        {
            fishId = id;
            maxRecord = length;
        }
    }

    [Serializable]
    public class UserSaveData
    {
        public List<InventoryItem> inventory = new List<InventoryItem>();
        
        // 발견한 물고기 기록 리스트 (도감)
        public List<FishRecord> encyclopedia = new List<FishRecord>();
        
        public int gold = 0;
        public int currentTier = 1;
        public int currentExp = 0;

        public List<string> ownedRodIds = new List<string>();
        public List<string> ownedBaitIds = new List<string>();
        public string equippedRodId = "";
        public string equippedBaitId = "";

        public void AddToInventory(string fishId, float length)
        {
            inventory.Add(new InventoryItem(fishId, length));
            UpdateEncyclopedia(fishId, length);
        }

        /// <summary>
        /// 도감 정보를 갱신합니다. (최초 등록 또는 최대 기록 경신)
        /// </summary>
        public void UpdateEncyclopedia(string fishId, float length)
        {
            var record = encyclopedia.Find(x => x.fishId == fishId);
            if (record == null)
            {
                // 생애 첫 발견!
                encyclopedia.Add(new FishRecord(fishId, length));
                Debug.Log($"<color=cyan>[도감]</color> 새로운 물고기 등록: {fishId}");
            }
            else
            {
                // 이미 발견된 물고기라면 기록 경신 여부 확인
                if (length > record.maxRecord)
                {
                    record.maxRecord = length;
                    Debug.Log($"<color=yellow>[도감]</color> {fishId} 최대 기록 경신: {length:F1}cm");
                }
            }
        }

        public bool IsDiscovered(string fishId)
        {
            return encyclopedia.Exists(x => x.fishId == fishId);
        }

        public FishRecord GetRecord(string fishId)
        {
            return encyclopedia.Find(x => x.fishId == fishId);
        }

        public bool AddExp(int amount)
        {
            currentExp += amount;
            bool didLevelUp = false;

            // 한 번의 획득으로 여러 번 레벨업이 가능하도록 while 루프 사용
            while (currentExp >= GetExpForNextTier())
            {
                currentTier++;
                didLevelUp = true;
            }
            
            return didLevelUp;
        }

        public int GetExpForNextTier()
        {
            return currentTier switch {
                1 => 100, 2 => 300, 3 => 800, 4 => 2000, 5 => 5000,
                _ => 5000 + (currentTier * 2000)
            };
        }
    }
}
