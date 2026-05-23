using System;
using UnityEngine;
using Mirror;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.InputSystem;
using MultiplayFishing.Core;
using MultiplayFishing.Data.Models;

namespace MultiplayFishing.Gameplay
{
    [DefaultExecutionOrder(200)]
    public class FishingPlayer : NetworkBehaviour
    {
        public event Action<string> OnPlayerNameChangedEvent;
        public event Action<Color> OnPlayerColorChangedEvent;
        public static event Action<string> OnSystemMessage;

        [Header("Player Identification")]
        [SyncVar(hook = nameof(OnPlayerNameChanged))] public string playerName = "";
        [SyncVar(hook = nameof(OnPlayerColorChanged))] public Color playerColor = Color.white;

        [Header("Equipment (Shop)")]
        [SyncVar(hook = nameof(OnEquippedRodChanged))] public string equippedRodId = "";
        [SyncVar(hook = nameof(OnEquippedBaitChanged))] public string equippedBaitId = "";
        [SyncVar(hook = nameof(OnRodDrawnChanged))] private bool isRodDrawn;

        [Header("Setup References")]
        [SerializeField] private Animator animator;
        [SerializeField] private Renderer characterRenderer;
        [SerializeField] private float groundSnapSearchHeight = 5f;
        [SerializeField] private float groundSnapSearchDistance = 20f;
        [SerializeField] private ParticleSystem fishingSplashParticle;
        [SerializeField] private AudioSource fishingAudioSource;
        [SerializeField] private AudioClip castSwingSound;
        [SerializeField] private float castSwingSoundDelay = 0.5f;
        [SerializeField] private AudioClip waterSplashSound;
        [SerializeField] private AudioClip reelingSound;
        [SerializeField] private float reelingSoundStartOffset;
        [SerializeField] private float reelingSoundLeadTime = 0.1f;

        [Header("Fishing Cast Settings")]
        [SerializeField] private float fishingMinCastDistance = 2f;
        [SerializeField] private float fishingMaxCastDistance = 35f;
        [SerializeField] private float fishingChargeSpeed = 20f;
        [SerializeField] private float fishingFallbackCastDistance = 6f;
        [SerializeField] private float fishingCastDuration = 0.9f;
        [SerializeField] private float fishingCastArcHeight = 2.3f;
        [SerializeField] private float fishingCastArcDistanceRatio = 0.25f;
        [SerializeField] private float fishingCastRopeLength = 3f;
        [SerializeField] private float fishingCastRopeSlack = 1f;

        [Header("Sprint UI")]

        private CharacterController characterController;
        private IPlayerMovementController movementController;
        private FishingRodVisibility rodVisibility;
        private MonoBehaviour playerController;
        private FieldInfo maxMoveSpeedField;
        private FieldInfo maxTurnSpeedField;
        private FieldInfo moveKeysField;
        private object originalMoveKeys;
        private bool hasOriginalMoveKeys;
        private float originalMaxTurnSpeed;
        private bool hasOriginalMaxTurnSpeed;
        private int walkParamHash;
        private int walkSpeedParamHash;
        private int rodEquippedParamHash;
        private int rodTakeOutTriggerHash;
        private int rodPutAwayTriggerHash;
        private bool hasRodEquippedParam;
        private bool hasRodTakeOutTrigger;
        private bool hasRodPutAwayTrigger;
        private bool isSprinting;
        private Vector3 lastPosition;
        [SerializeField]         private TMPro.TMP_Text sprintStatusText;
        private bool sprintUISearched;

        // 서비스 참조 (DI)
        private IDataService dataService;
        private IUserService userService;

        public bool IsFishing
        {
            get
            {
                return fishingController != null && fishingController.CurrentState != FishingState.Idle;
            }
        }

        public bool IsRodDrawn => isRodDrawn;

        private void Awake()
        {
            if (characterRenderer == null) characterRenderer = GetComponentInChildren<Renderer>();
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            characterController = GetComponent<CharacterController>();
            rodVisibility = GetComponentInChildren<FishingRodVisibility>(true);
            CacheAnimatorParameters();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            playerColor = Color.HSVToRGB(UnityEngine.Random.value, 0.8f, 1.0f);
            dataService = DIContainer.Resolve<IDataService>();
            userService = DIContainer.Resolve<IUserService>();

            if (userService != null)
            {
                equippedRodId = userService.UserData.equippedRodId;
                equippedBaitId = userService.UserData.equippedBaitId;
            }

            isRodDrawn = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            UpdateCharacterColor(playerColor);
            if (!string.IsNullOrEmpty(playerName))
            {
                OnPlayerNameChangedEvent?.Invoke(playerName);
            }
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            userService = DIContainer.Resolve<IUserService>();
            dataService = DIContainer.Resolve<IDataService>();

            if (userService != null)
            {
                string savedRodId = userService.UserData.equippedRodId;
                string savedBaitId = userService.UserData.equippedBaitId;

                if (!string.IsNullOrEmpty(savedRodId))
                    CmdEquipRod(savedRodId);
                if (!string.IsNullOrEmpty(savedBaitId))
                    CmdEquipBait(savedBaitId);
            }
            
            InitializeMovementController();
            SetupFishingController();

            // Sprint UI 초기값 비움
            UpdateSprintUI();
            
            StartCoroutine(SmartEscapeRoutine());
            SyncProfileName();
        }

        private void SyncProfileName()
        {
            string savedName = PlayerPrefs.GetString("PlayerName", $"낚시꾼 {UnityEngine.Random.Range(100, 999)}");
            if (DIContainer.TryResolve(out ICaughtFishSyncService caughtFishSyncService) && caughtFishSyncService.IsConfigured)
            {
                caughtFishSyncService.SyncProfileName(savedName, ApplyResolvedProfileName);
                return;
            }

            ApplyResolvedProfileName(savedName);
        }

        private void ApplyResolvedProfileName(string resolvedName)
        {
            if (!isLocalPlayer) return;

            string nextName = string.IsNullOrWhiteSpace(resolvedName)
                ? $"낚시꾼 {UnityEngine.Random.Range(100, 999)}"
                : resolvedName.Trim();

            PlayerPrefs.SetString("PlayerName", nextName);
            PlayerPrefs.Save();

            OnPlayerNameChangedEvent?.Invoke(nextName);
            CmdUpdatePlayerName(nextName);

            if (DIContainer.TryResolve(out ICaughtFishSyncService caughtFishSyncService))
            {
                caughtFishSyncService.SaveProfileName(nextName);
            }
        }

        private void InitializeMovementController()
        {
            movementController = GetComponent<IPlayerMovementController>();
            if (movementController == null)
            {
                movementController = gameObject.AddComponent<MirrorPlayerSampleMovement>();
            }

            Component[] components = GetComponents<Component>();
            foreach (Component component in components)
            {
                string typeName = component.GetType().Name;
                if (typeName != "PlayerControllerReliable" &&
                    typeName != "PlayerControllerBase" &&
                    typeName != "SampleSceneLocalPlayerController")
                {
                    continue;
                }

                if (component is MonoBehaviour monoBehaviour)
                {
                    monoBehaviour.enabled = false;
                }
            }

            Debug.Log("[FishingPlayer] Mirror PlayerSample movement initialized.");
        }
        void OnPlayerNameChanged(string oldValue, string newValue) => OnPlayerNameChangedEvent?.Invoke(newValue);
        
        void OnPlayerColorChanged(Color oldColor, Color newColor) 
        { 
            UpdateCharacterColor(newColor); 
            OnPlayerColorChangedEvent?.Invoke(newColor); 
        }

        void OnEquippedRodChanged(string oldValue, string newValue)
        {
            Debug.Log($"[FishingPlayer] Equipped rod changed: {oldValue} -> {newValue}");
            ApplyRodAnimationState(IsRodDrawn, false);
        }

        void OnEquippedBaitChanged(string oldValue, string newValue)
        {
            Debug.Log($"[FishingPlayer] Equipped bait changed: {oldValue} -> {newValue}");
        }

        void OnRodDrawnChanged(bool oldValue, bool newValue)
        {
            Debug.Log($"[FishingPlayer] Rod drawn changed: {oldValue} -> {newValue}");
            ApplyRodAnimationState(IsRodDrawn);
        }

        private void UpdateCharacterColor(Color color)
        {
            if (characterRenderer != null) 
            {
                characterRenderer.material.color = color;
            }
        }

        [Command]
        public void CmdUpdatePlayerName(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;

            bool isFirstName = string.IsNullOrEmpty(playerName);
            playerName = newName;

            if (isFirstName)
            {
                RpcBroadcastSystemMessage($"{newName}님이 입장하셨습니다.");
            }
        }

        [ClientRpc]
        private void RpcBroadcastSystemMessage(string message)
        {
            OnSystemMessage?.Invoke(message);
        }

        private IEnumerator SmartEscapeRoutine()
        {
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                SnapToGround(cc);
                yield return new WaitForFixedUpdate();
                cc.enabled = true;
            }
        }

        private void SnapToGround(CharacterController cc)
        {
            if (cc == null) return;

            Vector3 rayOrigin = transform.position + Vector3.up * groundSnapSearchHeight;
            int groundMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Water");
            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                groundSnapSearchDistance,
                groundMask,
                QueryTriggerInteraction.Ignore);

            if (hits == null || hits.Length == 0)
            {
                return;
            }

            RaycastHit bestHit = default;
            bool hasHit = false;
            float bestDistance = float.MaxValue;

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform == null || hit.transform.IsChildOf(transform)) continue;
                if (hit.collider != null && hit.collider.gameObject.layer == LayerMask.NameToLayer("Water")) continue;
                if (hit.distance >= bestDistance) continue;

                bestHit = hit;
                bestDistance = hit.distance;
                hasHit = true;
            }

            if (!hasHit)
            {
                return;
            }

            float controllerBottomOffset = cc.center.y - (cc.height * 0.5f);
            Vector3 position = transform.position;
            position.y = bestHit.point.y - controllerBottomOffset;
            transform.position = position;
        }

        // ==================== 달리기 시스템 ====================

        private void Update()
        {
            if (!isLocalPlayer) return;

            HandleRodToggleInput();

            if (IsFishing) return;

            HandleSprintInput();
            UpdateSprintUI();
        }

        private void HandleRodToggleInput()
        {
            if (Keyboard.current == null || !Keyboard.current.gKey.wasPressedThisFrame) return;

            SetRodDrawnLocal(!IsRodDrawn);
            CmdSetRodDrawn(isRodDrawn);
        }

        public void DrawRodForFishing()
        {
            if (IsRodDrawn) return;

            SetRodDrawnLocal(true);
            CmdSetRodDrawn(true);
        }

        private void HandleSprintInput()
        {
            if (movementController == null) return;

            bool nextSprinting = movementController.IsSprinting;
            if (nextSprinting != isSprinting)
            {
                isSprinting = nextSprinting;
                UpdateSprintUI();
            }
        }

        private void UpdateSprintUI()
        {
            if (sprintStatusText == null && !sprintUISearched)
            {
                TMPro.TMP_Text[] texts = FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None);
                foreach (TMPro.TMP_Text text in texts)
                {
                    if (text.name != "SprintStatusText") continue;

                    sprintStatusText = text;
                    break;
                }

                sprintUISearched = true;
            }

            if (sprintStatusText == null) return;
            sprintStatusText.text = isSprinting ? "SPRINT ON" : "";
        }

        public bool IsSprinting => isSprinting;
        // ==================== 낚시 시스템 (네트워크) ====================

        private FishingController fishingController;
        private FishDataSO pendingFish;
        private float pendingFishLength;
        private Coroutine serverFishingRoutine;
        private ICaughtFishSyncService caughtFishSyncService;

        private void SetupFishingController()
        {
            if (fishingController != null) return;
            
            fishingController = GetComponent<FishingController>();
            if (fishingController == null) fishingController = gameObject.AddComponent<FishingController>();
            fishingController.ConfigureAudioSource(EnsureFishingAudioSource());
            fishingController.ConfigureCastSettings(
                fishingMinCastDistance,
                fishingMaxCastDistance,
                fishingChargeSpeed,
                fishingFallbackCastDistance,
                fishingCastDuration,
                fishingCastArcHeight,
                fishingCastArcDistanceRatio,
                fishingCastRopeLength,
                fishingCastRopeSlack);
            fishingController.ConfigureWaterSplashSound(waterSplashSound);
            fishingController.ConfigureCastSwingSound(castSwingSound, castSwingSoundDelay);
            fishingController.ConfigureReelingSound(reelingSound);
            fishingController.ConfigureReelingSoundTiming(reelingSoundStartOffset, reelingSoundLeadTime);

            // AudioSource 보장
            EnsureFishingAudioSource();

            // 컴포넌트 및 오브젝트 검색 (방어적 코딩)
            var lineVisual = GetComponentInChildren<FishingLineVisual>();
            
            GameObject ropeObject = null;
            Transform ropeTransform = transform.Find("FishingRope");
            if (ropeTransform == null) ropeTransform = transform.GetComponentInChildren<FishingRopeController>()?.GetType().GetField("fishingRopeObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(null) as Transform; // 대체 검색 시도
            
            if (ropeTransform != null) ropeObject = ropeTransform.gameObject;
            else Debug.LogWarning($"[FishingPlayer] 'FishingRope' 오브젝트를 {playerName}에게서 찾을 수 없습니다.");

            var ropeComponent = ropeObject?.GetComponent("Rope");
            
            // 바늘(Hook)과 끝점(Tip) 검색 시도
            Transform tip = FindChildRecursive(transform, "Tip") ?? transform.Find("Skeleton/Hand_R/Rod/Tip");
            Transform hook = FindChildRecursive(transform, "Hook") ?? (ropeTransform != null ? ropeTransform.Find("Hook") : null);

            ParticleSystem splashParticle = fishingController.FishingSplashParticle;
            if (splashParticle == null)
            {
                splashParticle = fishingSplashParticle;
            }

            if (splashParticle == null)
            {
                splashParticle = GetComponentInChildren<ParticleSystem>();
            }
            
            // 카메라 검색 (로컬 플레이어인 경우만 필요)
            Camera pCam = isLocalPlayer ? Camera.main : null;

            var ropeController = new FishingRopeController(tip, hook, ropeObject, ropeComponent);
            var splashController = new FishingSplashController(splashParticle);

            // 추가 컴포넌트 검색 (CatchPresenter, BiteSystem)
            var catchPresenter = GetComponentInChildren<FishingCatchPresenter>();
            var biteSystem = GetComponentInChildren<FishingBiteSystem>();

            fishingController.Initialize(this, animator, lineVisual, ropeController, splashController, null, catchPresenter, biteSystem);
            
            // 낚시 상태 변경 시 PlayerController 토글 (낚시 중 이동 차단)
            if (isLocalPlayer)
            {
                fishingController.OnStateChanged += OnFishingStateChanged;
            }

            if (tip == null || hook == null) 
            {
                Debug.LogWarning($"[FishingPlayer] {playerName}의 낚시 포인트(Tip:{tip != null}, Hook:{hook != null}) 일부가 누락되었습니다. 연출이 제한될 수 있습니다.");
            }
        }

        private bool EnsureFishingController()
        {
            if (fishingController == null)
                SetupFishingController();

            if (fishingController != null)
                return true;

            Debug.LogWarning("[FishingPlayer] FishingController is not ready.");
            return false;
        }

        private AudioSource EnsureFishingAudioSource()
        {
            if (fishingAudioSource != null)
            {
                return fishingAudioSource;
            }

            fishingAudioSource = gameObject.AddComponent<AudioSource>();
            fishingAudioSource.playOnAwake = false;
            fishingAudioSource.loop = false;
            fishingAudioSource.spatialBlend = 0f;
            fishingAudioSource.priority = 64;
            return fishingAudioSource;
        }

        private void OnFishingStateChanged(FishingState newState)
        {

            // 낚시 시작 시 Sprint 해제
            if (newState != FishingState.Idle && isSprinting)
            {
                isSprinting = false;
                UpdateSprintUI();
            }

            SetFishingMovementLocked(newState != FishingState.Idle);
        }

        private void SetFishingMovementLocked(bool locked)
        {
            movementController?.SetMovementBlocked(locked);
        }

        private Transform FindChildRecursive(Transform parent, string nameContains)
        {
            foreach (Transform child in parent)
            {
                if (child.name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                    return child;
                
                Transform result = FindChildRecursive(child, nameContains);
                if (result != null) return result;
            }
            return null;
        }
        [Command]
        public void CmdStartFishing(Vector3 targetPos)
        {
            if (serverFishingRoutine != null)
            {
                Debug.LogWarning("[FishingPlayer] CmdStartFishing ignored: server fishing routine already running.");
                return;
            }
            serverFishingRoutine = StartCoroutine(ServerFishingTimer());
        }

        private IEnumerator ServerFishingTimer()
        {
            // 3~30초 대기
            float waitTime = UnityEngine.Random.Range(3f, 10f);
            yield return new WaitForSeconds(waitTime);

            // 물고기 결정
            if (dataService == null) dataService = DIContainer.Resolve<IDataService>();
            pendingFish = CalculateCatch();

            if (pendingFish != null)
            {
                pendingFishLength = UnityEngine.Random.Range(pendingFish.minSize, pendingFish.maxSize);
                
                // 무게 기반 연타 횟수 계산
                int requiredSpam = GetRequiredSpam(pendingFish);
                
                // 클라이언트에 입질 알림
                TargetOnNibble(connectionToClient, requiredSpam);
            }
            else
            {
                // 물고기 없음 (실패)
                TargetOnFishingResult(connectionToClient, false, "", 0, 0);
            }

            serverFishingRoutine = null;
        }

        private int GetRequiredSpam(FishDataSO fish)
        {
            if (fish == null) return 5;

            // 1. 엑셀/SO에 저장된 값이 있다면 우선 사용
            if (fish.requiredSpam > 0)
            {
                return fish.requiredSpam;
            }

            // 2. 폴백 계산 (1~10 범위)
            float weightBonus = Mathf.Log10(fish.weight + 1) * 1.5f;
            int rankBonus = fish.rank.Length; 

            return Mathf.Clamp(Mathf.RoundToInt(1 + weightBonus + rankBonus), 1, 10);
        }

        [TargetRpc]
        private void TargetOnNibble(NetworkConnection target, int requiredSpam)
        {
            if (!EnsureFishingController()) return;
            fishingController.OnServerNibble(requiredSpam);
        }

        [Command]
        public void CmdTryHook()
        {
            // 서버에서도 0.5초 체크 가능하지만, 일단 클라이언트 신뢰 후 상태 전환
            TargetOnEnterCatching(connectionToClient);
        }

        [TargetRpc]
        private void TargetOnEnterCatching(NetworkConnection target)
        {
            if (!EnsureFishingController()) return;
            fishingController.OnServerEnterCatching();
        }

        [Command]
        public void CmdCompleteCatching(int spamCount)
        {
            if (pendingFish == null)
            {
                serverFishingRoutine = null;
                return;
            }

            int required = GetRequiredSpam(pendingFish);
            bool success = spamCount >= required;

            if (success)
            {
                // 보상 지급
                TargetOnFishingResult(connectionToClient, true, pendingFish.id, pendingFishLength, pendingFish.expReward);
                
                // 알림
                if (pendingFish.rank == "S")
                {
                    RpcBroadcastSystemMessage($"{playerName}님이 [{pendingFish.fishName}] ({pendingFishLength:F1}cm)을(를) 낚았습니다! 🎉");
                }
            }
            else
            {
                TargetOnFishingResult(connectionToClient, false, "", 0, 0);
            }

            pendingFish = null;
            serverFishingRoutine = null;
        }

        [Command]
        public void CmdFishingMissed()
        {
            if (serverFishingRoutine != null)
            {
                StopCoroutine(serverFishingRoutine);
                serverFishingRoutine = null;
            }
            pendingFish = null;
        }

        // ==================== 상점 시스템 (네트워크) ====================

        [Command]
        public void CmdBuyItem(int itemType, string itemId)
        {
            if (userService == null) userService = DIContainer.Resolve<IUserService>();
            if (userService == null) return;

            bool success = userService.BuyItem((ShopItemType)itemType, itemId);
            TargetRpcBuyResult(connectionToClient, success);
        }

        [Command]
        public void CmdEquipRod(string rodId)
        {
            if (userService == null) userService = DIContainer.Resolve<IUserService>();
            if (userService == null) return;

            if (userService.EquipRod(rodId))
            {
                equippedRodId = rodId;
            }
        }

        [Command]
        public void CmdEquipBait(string baitId)
        {
            if (userService == null) userService = DIContainer.Resolve<IUserService>();
            if (userService == null) return;

            if (userService.EquipBait(baitId))
            {
                equippedBaitId = baitId;
            }
        }

        [Command]
        public void CmdUnequipRod()
        {
            if (userService == null) userService = DIContainer.Resolve<IUserService>();
            if (userService == null) return;

            isRodDrawn = false;
            userService.UnequipRod();
            equippedRodId = "";
        }

        [Command]
        public void CmdSetRodDrawn(bool drawn)
        {
            if (isRodDrawn == drawn) return;

            SetRodDrawnLocal(drawn);
            RpcApplyRodDrawn(drawn);
        }

        [ClientRpc(includeOwner = false)]
        private void RpcApplyRodDrawn(bool drawn)
        {
            SetRodDrawnLocal(drawn);
        }

        private void SetRodDrawnLocal(bool drawn)
        {
            isRodDrawn = drawn;
            ApplyRodAnimationState(drawn);
        }

        [Command]
        public void CmdUnequipBait()
        {
            if (userService == null) userService = DIContainer.Resolve<IUserService>();
            if (userService == null) return;

            userService.UnequipBait();
            equippedBaitId = "";
        }

        [TargetRpc]
        private void TargetRpcBuyResult(NetworkConnection target, bool success)
        {
            if (success)
            {
                Debug.Log("[Shop] Purchase successful.");
                OnSystemMessage?.Invoke("[상점] 구매가 완료되었습니다!");
            }
            else
            {
                Debug.Log("[Shop] Purchase failed.");
                OnSystemMessage?.Invoke("[상점] 구매에 실패했습니다. 골드를 확인해주세요.");
            }
        }

        [TargetRpc]
        private void TargetOnFishingResult(NetworkConnection target, bool success, string fishId, float length, int exp)
        {
            if (success)
            {
                if (userService == null) userService = DIContainer.Resolve<IUserService>();
                if (userService == null)
                {
                    Debug.LogWarning("[FishingPlayer] UserService is not ready. Fishing reward was skipped.");
                    return;
                }
                
                // IUserService.AddFish는 내부적으로 경험치 추가, 도감 갱신, 저장을 모두 수행합니다.
                userService.AddFish(fishId, length);
                if (caughtFishSyncService == null)
                {
                    DIContainer.TryResolve(out caughtFishSyncService);
                }

                if (dataService == null)
                {
                    dataService = DIContainer.Resolve<IDataService>();
                }

                FishDataSO fishData = dataService != null ? dataService.GetFishData(fishId) : null;
                caughtFishSyncService?.SaveCaughtFish(playerName, fishId, length, fishData);
                
                Debug.Log($"<color=green>[낚시 성공]</color> {fishId} 획득!");
                OnSystemMessage?.Invoke($"[낚시 성공] {fishId} 획득!");
            }

            if (!EnsureFishingController()) return;
            fishingController.OnFishingResult(success);
        }

        FishDataSO CalculateCatch()
        {
            if (dataService == null) dataService = DIContainer.Resolve<IDataService>();
            if (dataService == null) return null;

            List<FishDataSO> allFish = dataService.GetAllFishData();
            if (allFish == null || allFish.Count == 0) return null;

            float catchBonus = 0f;
            if (!string.IsNullOrEmpty(equippedRodId))
            {
                var rod = dataService.GetRodData(equippedRodId);
                if (rod != null) catchBonus += rod.catchChanceBonus;
            }
            if (!string.IsNullOrEmpty(equippedBaitId))
            {
                var bait = dataService.GetBaitData(equippedBaitId);
                if (bait != null) catchBonus += bait.catchChanceBonus;
            }

            float totalChance = 0f;
            foreach (var fish in allFish)
            {
                totalChance += fish.catchChance;
            }

            float adjustedTotal = totalChance + catchBonus;
            float randomValue = UnityEngine.Random.Range(0f, adjustedTotal);
            float currentChance = 0f;

            foreach (var fish in allFish)
            {
                float fishChance = fish.catchChance;
                currentChance += fishChance;
                if (randomValue <= currentChance)
                {
                    return fish;
                }
            }

            return allFish[allFish.Count - 1];
        }

        public float GetCastDistanceBonus()
        {
            if (string.IsNullOrEmpty(equippedRodId)) return 0f;
            var rod = dataService.GetRodData(equippedRodId);
            return rod != null ? rod.castDistanceBonus : 0f;
        }

        // ==================== 애니메이션 및 캐릭터 제어 ====================

        private void CacheAnimatorParameters()
        {
            hasRodEquippedParam = false;
            hasRodTakeOutTrigger = false;
            hasRodPutAwayTrigger = false;

            if (animator == null) return;

            rodEquippedParamHash = Animator.StringToHash("RodEquipped");
            rodTakeOutTriggerHash = Animator.StringToHash("RodTakeOut");
            rodPutAwayTriggerHash = Animator.StringToHash("RodPutAway");

            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Bool && param.nameHash == rodEquippedParamHash)
                {
                    hasRodEquippedParam = true;
                }
                else if (param.type == AnimatorControllerParameterType.Trigger && param.nameHash == rodTakeOutTriggerHash)
                {
                    hasRodTakeOutTrigger = true;
                }
                else if (param.type == AnimatorControllerParameterType.Trigger && param.nameHash == rodPutAwayTriggerHash)
                {
                    hasRodPutAwayTrigger = true;
                }
            }
        }

        private void Start()
        {
            CacheAnimatorParameters();
            lastPosition = transform.position;
            ApplyRodAnimationState(IsRodDrawn, false);
        }

        private void ApplyRodAnimationState(bool equipped, bool playTrigger = true)
        {
            if (animator == null) return;

            if (playTrigger && equipped && hasRodTakeOutTrigger)
            {
                if (hasRodPutAwayTrigger) animator.ResetTrigger(rodPutAwayTriggerHash);
                if (hasRodEquippedParam) animator.SetBool(rodEquippedParamHash, true);
                fishingController?.ShowRodLineVisuals();
                animator.SetTrigger(rodTakeOutTriggerHash);
            }
            else if (playTrigger && !equipped && hasRodPutAwayTrigger)
            {
                if (hasRodTakeOutTrigger) animator.ResetTrigger(rodTakeOutTriggerHash);
                if (hasRodEquippedParam) animator.SetBool(rodEquippedParamHash, false);
                if (fishingController != null)
                {
                    fishingController.CancelFishingFromRodPutAway();
                    fishingController.HideRodLineVisuals();
                }
                animator.SetTrigger(rodPutAwayTriggerHash);
            }
            else if (hasRodEquippedParam)
            {
                animator.SetBool(rodEquippedParamHash, equipped);
            }

            if (!playTrigger)
            {
                rodVisibility?.ApplyImmediate(equipped);
                if (!equipped)
                {
                    fishingController?.HideRodLineVisuals();
                }
                else
                {
                    fishingController?.ShowRodLineVisuals();
                }
            }
        }
    }
}
