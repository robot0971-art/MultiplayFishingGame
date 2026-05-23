using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Mirror;

namespace MultiplayFishing.Gameplay
{
    public enum FishingState
    {
        Idle,
        Charging,
        Casting,
        Waiting,
        Nibble,
        Catching,
        Success,
        Failure
    }

    public class FishingController : MonoBehaviour
    {
        [Header("State")]
        public FishingState CurrentState = FishingState.Idle;

        [Header("Casting Settings")]
        [SerializeField] private float minCastDistance = 2f;
        [SerializeField] private float maxCastDistance = 35f;
        [SerializeField] private float chargeSpeed = 20f;
        [SerializeField] private float fallbackCastDistance = 6f;
        [SerializeField] private Vector3 castTargetOffset = Vector3.zero;
        [SerializeField] private float castStartDelay = 0.18f;
        [SerializeField] private float castDuration = 0.9f;
        [SerializeField] private float castArcHeight = 2.3f;
        [SerializeField] private float castArcDistanceRatio = 0.25f;
        [SerializeField] private float castRopeLength = 3f;
        [SerializeField] private float castRopeSlack = 1f;
        [SerializeField] private float hookWaterSubmergeDepth = 0.08f;
        [Header("Water Raycast Settings")]
        [SerializeField] private Transform waterSurfaceTransform;
        [SerializeField] private LayerMask waterLayerMask;
        [SerializeField] private bool useCameraWaterRaycast = true;
        [Min(0f)]
        [SerializeField] private float waterRayStartHeight = 1.5f;
        [Min(0f)]
        [SerializeField] private float downwardCastBias = 0.2f;
        [SerializeField] private bool showTipWaterRaycastGizmo = true;
        [SerializeField] private bool alwaysShowTipWaterRaycastGizmo;
        [SerializeField] private float waterSurfaceYOffset;
        [SerializeField] private Vector3 splashWorldOffset = new Vector3(0f, 0.01f, 0f);
        [SerializeField] private bool clampSplashToWaterSurface = true;
        [SerializeField] private float minimumSplashHeightOffset = 0.02f;
        [SerializeField] private ParticleSystem fishingSplashParticle;
        private float currentChargeDistance;

        [Header("Timing Settings")]
        [SerializeField] private float nibbleReactionWindow = 3f;
        [SerializeField] private float catchingDuration = 10f;
        [SerializeField] private float reelDuration = 0.8f;
        [SerializeField] private float reelDurationWithFish = 2f;
        [SerializeField] private float reelArcHeight = 0.2f;
        [SerializeField] private float idleRopeLength = 1.8f;
        [SerializeField] private float idleRopeSlack = 0.1f;
        [SerializeField] private Vector3 idleHookOffset = new Vector3(0f, 0f, 0.1f);
        public float CatchingDuration => catchingDuration;

        [Header("Input Safety")]
        [SerializeField] private bool blockCastWhileMoving;
        [SerializeField] private bool useCastReleaseAnimationEvent = true;
        [SerializeField] private float castInputLockDuration = 0.5f;
        [SerializeField] private float castReleaseFallbackDelay = 2.1f;
        [SerializeField] private float autoDrawCastDelay = 0.25f;

        [Header("References")]
        private FishingPlayer fishingPlayer;
        private Animator animator;
        private FishingLineVisual fishingLineVisual;
        private FishingRopeController ropeController;
        private FishingSplashController splashController;
        private FishingWaterSurfaceResolver waterResolver;
        private FishingCatchPresenter catchPresenter;
        private FishingBiteSystem biteSystem;
        private WaterDetector waterDetector;

        [Header("Sound Effects")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip castSwingSound;
        [SerializeField] private float castSwingSoundDelay = 0.5f;
        [SerializeField] private AudioClip waterSplashSound;
        [SerializeField] private AudioClip reelingSound;
        [SerializeField] private float reelingSoundStartOffset;
        [SerializeField] private float reelingSoundLeadTime = 0.1f;
        [SerializeField] private AudioClip biteSound;
        [SerializeField] private AudioClip successSound;
        [SerializeField] private AudioClip failureSound;

        public event Action<FishingState> OnStateChanged;
        public event Action<float> OnChargeProgressChanged; // 0 ~ 1
        public event Action<float, float> OnCatchProgressChanged; // current, target
        public ParticleSystem FishingSplashParticle => fishingSplashParticle;

        public void ConfigureCastSettings(
            float configuredMinCastDistance,
            float configuredMaxCastDistance,
            float configuredChargeSpeed,
            float configuredFallbackCastDistance,
            float configuredCastDuration,
            float configuredCastArcHeight,
            float configuredCastArcDistanceRatio,
            float configuredCastRopeLength,
            float configuredCastRopeSlack)
        {
            minCastDistance = Mathf.Max(0f, configuredMinCastDistance);
            maxCastDistance = Mathf.Max(minCastDistance, configuredMaxCastDistance);
            chargeSpeed = Mathf.Max(0f, configuredChargeSpeed);
            fallbackCastDistance = Mathf.Max(0f, configuredFallbackCastDistance);
            castDuration = Mathf.Max(0.01f, configuredCastDuration);
            castArcHeight = Mathf.Max(0f, configuredCastArcHeight);
            castArcDistanceRatio = Mathf.Max(0f, configuredCastArcDistanceRatio);
            castRopeLength = Mathf.Max(0f, configuredCastRopeLength);
            castRopeSlack = Mathf.Max(0f, configuredCastRopeSlack);
        }

        public void ConfigureWaterSplashSound(AudioClip clip)
        {
            waterSplashSound = clip;
        }

        public void ConfigureCastSwingSound(AudioClip clip, float delay)
        {
            castSwingSound = clip;
            castSwingSoundDelay = Mathf.Max(0f, delay);
        }

        public void ConfigureReelingSound(AudioClip clip)
        {
            reelingSound = clip;
        }

        public void ConfigureReelingSoundTiming(float startOffset, float leadTime)
        {
            reelingSoundStartOffset = Mathf.Max(0f, startOffset);
            reelingSoundLeadTime = Mathf.Max(0f, leadTime);
        }

        public void ConfigureAudioSource(AudioSource source)
        {
            audioSource = source;
        }

        private Coroutine stateRoutine;
        private Vector3 targetPosition;
        private Vector3 castHitPoint;
        private bool castHasHit;
        private float stateTimer;
        private int spamCount;
        private int targetSpamCount;
        private int fishingCastTriggerHash;
        private int fishingBoolHash;
        private int hasFishBoolHash;
        private bool hasFishingCastTrigger;
        private bool hasFishingBool;
        private bool hasHasFishBool;
        private float inputLockedUntil;
        private bool wasLockedThisFrame;
        private Coroutine castReleaseFallbackRoutine;
        private Coroutine autoDrawCastRoutine;
        private bool waitingForCastRelease;
        private bool castReleaseReceived;
        private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();

        public void Initialize(
            FishingPlayer player,
            Animator anim,
            FishingLineVisual lineVisual,
            FishingRopeController rope,
            FishingSplashController splash,
            FishingWaterSurfaceResolver resolver,
            FishingCatchPresenter presenter = null,
            FishingBiteSystem bite = null)
        {
            fishingPlayer = player;
            animator = anim;
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            fishingLineVisual = lineVisual;
            ropeController = rope;
            splashController = splash;
            waterResolver = resolver != null ? resolver : CreateWaterSurfaceResolver();
            catchPresenter = presenter;
            biteSystem = bite;

            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (waterDetector == null) waterDetector = GetComponent<WaterDetector>();
            if (waterDetector == null) waterDetector = gameObject.AddComponent<WaterDetector>();

            CacheAnimatorParameters();
            EnsureAnimationEventRelay();

            if (catchPresenter != null && animator != null)
            {
                catchPresenter.Initialize(animator, "HasFish");
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        private void PlaySound(AudioClip clip, float delay)
        {
            if (audioSource == null || clip == null)
            {
                return;
            }

            if (delay <= 0f)
            {
                audioSource.PlayOneShot(clip);
                return;
            }

            StartCoroutine(PlaySoundDelayedRoutine(clip, delay));
        }

        private IEnumerator PlaySoundDelayedRoutine(AudioClip clip, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        private void StartReelingSound()
        {
            if (audioSource == null || reelingSound == null)
            {
                return;
            }

            audioSource.clip = reelingSound;
            audioSource.loop = true;
            if (reelingSoundStartOffset > 0f && reelingSound.length > 0f)
            {
                audioSource.time = Mathf.Min(reelingSoundStartOffset, reelingSound.length - 0.01f);
            }
            audioSource.Play();
        }

        private void StopReelingSound()
        {
            if (audioSource == null || audioSource.clip != reelingSound)
            {
                return;
            }

            audioSource.Stop();
            audioSource.loop = false;
            audioSource.clip = null;
        }

        private void Update()
        {
            if (fishingPlayer == null) return;
            if (!fishingPlayer.isLocalPlayer) return;

            HandleInput();
        }

        private void HandleInput()
        {
            if (Mouse.current == null) return;
            if (IsPointerOverUI()) return;

            wasLockedThisFrame = Time.time < inputLockedUntil;
            bool leftPressed = Mouse.current.leftButton.wasPressedThisFrame;
            bool leftHeld = Mouse.current.leftButton.isPressed;
            bool leftReleased = Mouse.current.leftButton.wasReleasedThisFrame;
            bool spacePressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

            if (wasLockedThisFrame) return;

            switch (CurrentState)
            {
                case FishingState.Idle:
                    if (leftPressed) StartCharging();
                    break;

                case FishingState.Charging:
                    if (leftHeld) UpdateCharging();
                    if (leftReleased || !leftHeld) Cast();
                    break;

                case FishingState.Nibble:
                    if (leftPressed) TryHooking();
                    break;

                case FishingState.Catching:
                    if (leftPressed || spacePressed) RecordSpam();
                    break;
            }
        }

        private bool IsPointerOverUI()
        {
            if (EventSystem.current == null || Mouse.current == null) return false;

            PointerEventData eventData = new PointerEventData(EventSystem.current)
            {
                position = Mouse.current.position.ReadValue()
            };

            uiRaycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, uiRaycastResults);

            foreach (RaycastResult result in uiRaycastResults)
            {
                if (result.gameObject == null) continue;
                if (result.gameObject.transform.IsChildOf(transform)) continue;

                if (result.gameObject.GetComponentInParent<Selectable>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsMovementInputPressed()
        {
            if (Keyboard.current == null) return false;

            return Keyboard.current.wKey.isPressed
                || Keyboard.current.aKey.isPressed
                || Keyboard.current.sKey.isPressed
                || Keyboard.current.dKey.isPressed
                || Keyboard.current.upArrowKey.isPressed
                || Keyboard.current.leftArrowKey.isPressed
                || Keyboard.current.downArrowKey.isPressed
                || Keyboard.current.rightArrowKey.isPressed;
        }

        private void ChangeState(FishingState newState)
        {
            CurrentState = newState;
            OnStateChanged?.Invoke(newState);

            SetFishingBool(IsFishingLoopState(newState));
        }

        private static bool IsFishingLoopState(FishingState state)
        {
            return state == FishingState.Casting
                || state == FishingState.Waiting
                || state == FishingState.Nibble
                || state == FishingState.Catching;
        }

        private void StartCharging()
        {
            if (!fishingPlayer.IsRodDrawn)
            {
                Debug.LogWarning("[FishingController] Cast ignored because the fishing rod is not drawn.");
                return;
            }

            if (blockCastWhileMoving && IsMovementInputPressed())
            {
                Debug.LogWarning("[FishingController] Cast ignored while moving.");
                return;
            }

            if (animator != null && hasFishingCastTrigger)
            {
                animator.ResetTrigger(fishingCastTriggerHash);
            }

            currentChargeDistance = minCastDistance;
            ChangeState(FishingState.Charging);
        }

        private void StartAutoDrawCast()
        {
            if (autoDrawCastRoutine != null)
            {
                StopCoroutine(autoDrawCastRoutine);
            }

            autoDrawCastRoutine = StartCoroutine(AutoDrawCastRoutine());
        }

        private IEnumerator AutoDrawCastRoutine()
        {
            yield return new WaitForSeconds(autoDrawCastDelay);
            autoDrawCastRoutine = null;

            if (CurrentState != FishingState.Idle || !fishingPlayer.IsRodDrawn)
            {
                yield break;
            }

            StartCharging();
        }

        private void UpdateCharging()
        {
            currentChargeDistance += chargeSpeed * Time.deltaTime;
            currentChargeDistance = Mathf.Min(currentChargeDistance, maxCastDistance);
            OnChargeProgressChanged?.Invoke((currentChargeDistance - minCastDistance) / (maxCastDistance - minCastDistance));
        }

        private void Cast()
        {
            targetPosition = GetCastTargetPosition(out castHasHit, out Vector3 hitPoint);

            if (!castHasHit)
            {
                Debug.Log("[FishingController] Cannot cast because no water surface was found.");
                ChangeState(FishingState.Idle);
                return;
            }

            castHitPoint = hitPoint;
            inputLockedUntil = Time.time + castInputLockDuration;
            ChangeState(FishingState.Casting);
            PlayCastAnimation();
            waitingForCastRelease = true;
            castReleaseReceived = false;

            float fallbackDelay = useCastReleaseAnimationEvent && animator != null && hasFishingBool
                ? castReleaseFallbackDelay
                : castStartDelay;
            StartCastReleaseFallback(fallbackDelay);
        }

        public void OnCastRelease()
        {
            if (!waitingForCastRelease) return;

            if (CurrentState != FishingState.Casting)
            {
                Debug.LogWarning($"[FishingController] OnCastRelease ignored: current state is {CurrentState}, expected Casting.");
                return;
            }

            waitingForCastRelease = false;
            castReleaseReceived = true;
            StopCastReleaseFallback();
            fishingPlayer.CmdStartFishing(targetPosition);

            if (stateRoutine != null) StopCoroutine(stateRoutine);
            stateRoutine = StartCoroutine(CastingRoutine(targetPosition, castHitPoint, castHasHit));
        }

        private IEnumerator CastingRoutine(Vector3 target, Vector3 hitPoint, bool hasHit)
        {
            if (ropeController == null || !ropeController.IsConfigured)
            {
                Debug.LogWarning("[FishingController] Fishing rope is not configured. Starting fishing wait state without hook animation.");
                ChangeState(FishingState.Waiting);
                yield break;
            }

            bool splashPlayed = false;
            Action<Vector3> playSplashAtPosition = splashPosition =>
            {
                splashPlayed = true;
                splashController?.UpdatePendingPosition(
                    hasHit,
                    splashPosition,
                    target,
                    splashWorldOffset,
                    clampSplashToWaterSurface,
                    minimumSplashHeightOffset);
                splashController?.Play();
                PlaySound(waterSplashSound);
            };

            yield return ropeController.MoveHookDynamic(
                target,
                () => useCastReleaseAnimationEvent && castReleaseReceived ? 0f : castStartDelay,
                () => castDuration,
                () => GetCastArcHeight(target),
                () => castRopeSlack,
                () => castRopeLength,
                false,
                false,
                true,
                true,
                GetCastWaterSurfaceY,
                playSplashAtPosition,
                fishingLineVisual);

            if (hasHit && !splashPlayed)
            {
                playSplashAtPosition(hitPoint);
            }

            ChangeState(FishingState.Waiting);
            fishingLineVisual?.SetFishingActiveVisualOnly(true);
        }

        private void TryHooking()
        {
            if (stateTimer <= nibbleReactionWindow)
            {
                fishingPlayer.CmdTryHook();
            }
            else
            {
                Miss();
            }
        }

        public void OnServerNibble(int requiredSpam)
        {
            if (CurrentState != FishingState.Waiting) return;

            targetSpamCount = requiredSpam;
            spamCount = 0;
            stateTimer = 0f;
            ChangeState(FishingState.Nibble);

            if (biteSystem != null)
            {
                biteSystem.ShowBiteSignal();
            }
            PlaySound(biteSound);

            if (stateRoutine != null) StopCoroutine(stateRoutine);
            stateRoutine = StartCoroutine(NibbleTimeoutRoutine());
        }

        private IEnumerator NibbleTimeoutRoutine()
        {
            while (stateTimer < nibbleReactionWindow)
            {
                stateTimer += Time.deltaTime;
                yield return null;
            }

            if (CurrentState == FishingState.Nibble)
            {
                Miss();
            }
        }

        public void OnServerEnterCatching()
        {
            ChangeState(FishingState.Catching);
            stateTimer = 0f;
            OnCatchProgressChanged?.Invoke(spamCount, targetSpamCount);

            if (biteSystem != null)
            {
                biteSystem.StopBiteLogic();
            }

            if (stateRoutine != null) StopCoroutine(stateRoutine);
            stateRoutine = StartCoroutine(CatchingRoutine());
        }

        private IEnumerator CatchingRoutine()
        {
            while (stateTimer < catchingDuration)
            {
                stateTimer += Time.deltaTime;
                yield return null;
            }

            if (CurrentState == FishingState.Catching)
            {
                fishingPlayer.CmdCompleteCatching(spamCount);
            }
        }

        private void RecordSpam()
        {
            spamCount++;
            OnCatchProgressChanged?.Invoke(spamCount, targetSpamCount);

            if (spamCount >= targetSpamCount)
            {
                fishingPlayer.CmdCompleteCatching(spamCount);
            }
        }

        public void OnFishingResult(bool success)
        {
            if (stateRoutine != null) StopCoroutine(stateRoutine);
            waitingForCastRelease = false;
            StopCastReleaseFallback();

            if (biteSystem != null)
            {
                biteSystem.StopBiteLogic();
            }

            if (success)
            {
                ChangeState(FishingState.Success);
                SetHasFishBool(true);
                PlaySound(successSound);
                StartCoroutine(SuccessRoutine());
            }
            else
            {
                ChangeState(FishingState.Failure);
                PlaySound(failureSound);
                StartCoroutine(FailureRoutine());
            }
        }

        public void CancelFishingFromRodPutAway()
        {
            StopReelingSound();

            if (CurrentState == FishingState.Idle)
            {
                HideRodLineVisuals();
                return;
            }

            waitingForCastRelease = false;
            StopCastReleaseFallback();
            StopAutoDrawCast();

            if (stateRoutine != null)
            {
                StopCoroutine(stateRoutine);
                stateRoutine = null;
            }

            if (biteSystem != null)
            {
                biteSystem.StopBiteLogic();
            }

            HideRodLineVisuals();
            ChangeState(FishingState.Idle);

            if (fishingPlayer != null && fishingPlayer.isLocalPlayer)
            {
                fishingPlayer.CmdFishingMissed();
            }
        }

        public void HideRodLineVisuals()
        {
            ropeController?.RestoreHookToRod();
            ropeController?.SetVisible(false);
            fishingLineVisual?.SetFishingActive(false);
            fishingLineVisual?.SetVisible(false);
        }

        public void ShowRodLineVisuals()
        {
            fishingLineVisual?.SetVisible(true);
            fishingLineVisual?.SetFishingActive(false);
            ropeController?.RestoreHookToRod();
        }

        private IEnumerator SuccessRoutine()
        {
            if (catchPresenter != null && ropeController != null)
            {
                // HookPoint is where the hook is currently
                Transform hookPoint = ropeController.GetHookPoint();
                catchPresenter.PerformCatch(hookPoint, 1.2f);
                
                // Wait for catch animation (Lifting etc)
                yield return new WaitUntil(() => !catchPresenter.IsAnimating);
            }
            else
            {
                yield return new WaitForSeconds(2f);
            }

            EndFishing();
        }

        private IEnumerator FailureRoutine()
        {
            yield return new WaitForSeconds(1.5f);
            EndFishing();
        }

        private void Miss()
        {
            StopReelingSound();
            waitingForCastRelease = false;
            StopCastReleaseFallback();
            fishingPlayer.CmdFishingMissed();
            ChangeState(FishingState.Failure);
            StartCoroutine(FailureRoutine());
        }

        private void EndFishing()
        {
            waitingForCastRelease = false;
            StopCastReleaseFallback();

            if (stateRoutine != null)
            {
                StopCoroutine(stateRoutine);
            }

            stateRoutine = StartCoroutine(EndFishingRoutine());
        }

        private IEnumerator EndFishingRoutine()
        {
            if (ropeController != null && ropeController.IsConfigured)
            {
                fishingLineVisual?.SetFishingActive(false);
                yield return ReelToIdleRoutine();
            }
            else
            {
                StopReelingSound();
                ropeController?.SetVisible(false);
            }

            SetHasFishBool(false);
            ChangeState(FishingState.Idle);
            stateRoutine = null;
        }

        private IEnumerator ReelToIdleRoutine()
        {
            StartReelingSound();
            if (reelingSoundLeadTime > 0f)
            {
                yield return new WaitForSeconds(reelingSoundLeadTime);
            }

            yield return ropeController.MoveHook(
                    GetIdleHookPosition(),
                    0f,
                    reelDuration,
                    reelArcHeight,
                    idleRopeSlack,
                    idleRopeLength,
                    true,
                    true,
                    true,
                    false,
                    0f,
                    (Action<Vector3>)null,
                    fishingLineVisual);

            StopReelingSound();
        }

        private void StartCastReleaseFallback(float fallbackDelay)
        {
            StopCastReleaseFallback();
            castReleaseFallbackRoutine = StartCoroutine(CastReleaseFallbackRoutine(fallbackDelay));
        }

        private void StopCastReleaseFallback()
        {
            if (castReleaseFallbackRoutine == null) return;

            StopCoroutine(castReleaseFallbackRoutine);
            castReleaseFallbackRoutine = null;
        }

        private void StopAutoDrawCast()
        {
            if (autoDrawCastRoutine == null) return;

            StopCoroutine(autoDrawCastRoutine);
            autoDrawCastRoutine = null;
        }

        private IEnumerator CastReleaseFallbackRoutine(float fallbackDelay)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, fallbackDelay));
            castReleaseFallbackRoutine = null;

            if (waitingForCastRelease && CurrentState == FishingState.Casting)
            {
                OnCastRelease();
            }
        }

        private Vector3 GetCastTargetPosition(out bool hasSurfaceHit, out Vector3 surfaceHitPoint)
        {
            waterResolver = CreateWaterSurfaceResolver();

            Vector3 offset = castTargetOffset;
            offset.z = Mathf.Approximately(offset.z, 0f) ? currentChargeDistance : offset.z;

            Vector3 resolvedTarget = waterResolver.ResolveCastTarget(
                transform,
                offset,
                Mathf.Max(fallbackCastDistance, currentChargeDistance),
                out hasSurfaceHit,
                out surfaceHitPoint);

            if (!hasSurfaceHit)
            {
                return GetPlanarCastTarget(offset);
            }

            resolvedTarget.y += waterSurfaceYOffset - hookWaterSubmergeDepth;
            surfaceHitPoint = resolvedTarget;
            return resolvedTarget;
        }

        private Vector3 GetPlanarCastTarget(Vector3 offset)
        {
            Vector3 start = ropeController != null && ropeController.GetTipPoint() != null
                ? ropeController.GetTipPoint().position
                : transform.position;
            Vector3 forward = GetPlanarCastForward();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            return start
                + right * offset.x
                + Vector3.up * offset.y
                + forward * Mathf.Max(minCastDistance, offset.z);
        }

        private Vector3 GetPlanarCastForward()
        {
            Camera playerCamera = fishingPlayer != null && fishingPlayer.isLocalPlayer ? Camera.main : null;
            Vector3 forward = playerCamera != null ? playerCamera.transform.forward : transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = transform.forward;
                forward.y = 0f;
            }

            return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        }

        private float GetCastArcHeight(Vector3 target)
        {
            Transform tipPoint = ropeController != null ? ropeController.GetTipPoint() : null;
            Vector3 start = tipPoint != null ? tipPoint.position : transform.position;
            float distance = Vector3.Distance(start, target);
            float verticalDrop = Mathf.Max(0f, start.y - target.y);
            return Mathf.Max(castArcHeight, distance * castArcDistanceRatio, verticalDrop * 0.6f);
        }

        private float GetCastWaterSurfaceY()
        {
            waterResolver = CreateWaterSurfaceResolver();

            return waterResolver.TryGetSurfaceHeight(out float waterSurfaceY)
                ? waterSurfaceY + waterSurfaceYOffset - hookWaterSubmergeDepth
                : targetPosition.y;
        }

        private Vector3 GetIdleHookPosition()
        {
            return ropeController != null
                ? ropeController.GetIdleHookPosition(transform, idleHookOffset)
                : transform.position;
        }

        private FishingWaterSurfaceResolver CreateWaterSurfaceResolver()
        {
            Camera playerCamera = fishingPlayer != null && fishingPlayer.isLocalPlayer ? Camera.main : null;
            Transform tipPoint = ropeController != null ? ropeController.GetTipPoint() : null;
            LayerMask resolvedWaterLayerMask = ResolveWaterLayerMask();

            return new FishingWaterSurfaceResolver(
                playerCamera,
                tipPoint,
                CollectTipRayOrigins(tipPoint),
                waterSurfaceTransform,
                resolvedWaterLayerMask,
                useCameraWaterRaycast,
                waterRayStartHeight,
                downwardCastBias,
                maxCastDistance);
        }

        private void OnDrawGizmos()
        {
            if (!alwaysShowTipWaterRaycastGizmo)
            {
                return;
            }

            DrawTipWaterRaycastGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (!showTipWaterRaycastGizmo)
            {
                return;
            }

            DrawTipWaterRaycastGizmo();
        }

        private void DrawTipWaterRaycastGizmo()
        {
            Transform tipPoint = ropeController != null ? ropeController.GetTipPoint() : null;
            if (tipPoint == null)
            {
                tipPoint = FindChildByName(transform, "TipPoint");
            }

            Transform[] tipRayOrigins = CollectTipRayOrigins(tipPoint);
            if (tipRayOrigins.Length == 0)
            {
                return;
            }

            float rayLength = Mathf.Max(0.1f, maxCastDistance);

            Gizmos.color = Color.cyan;
            for (int i = 0; i < tipRayOrigins.Length; i++)
            {
                Transform tipRayOrigin = tipRayOrigins[i];
                if (tipRayOrigin == null) continue;

                DrawRayGizmo(tipRayOrigin.position, Vector3.down, rayLength);
            }

            Vector3 ownerOrigin = transform.position + Vector3.up * waterRayStartHeight;
            Vector3 ownerDirection = (transform.forward + Vector3.down * downwardCastBias).normalized;
            Gizmos.color = Color.yellow;
            DrawRayGizmo(ownerOrigin, ownerDirection, rayLength);

            Vector3 tipDirection = (transform.forward + Vector3.down * downwardCastBias).normalized;
            Gizmos.color = Color.magenta;
            DrawRayGizmo(tipPoint.position, tipDirection, rayLength);

            if (useCameraWaterRaycast && Camera.main != null)
            {
                Ray cameraRay = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                Gizmos.color = Color.green;
                DrawRayGizmo(cameraRay.origin, cameraRay.direction, rayLength);
            }
        }

        private Transform[] CollectTipRayOrigins(Transform primaryTipPoint)
        {
            List<Transform> tipRayOrigins = new List<Transform>();

            if (primaryTipPoint != null)
            {
                tipRayOrigins.Add(primaryTipPoint);
            }

            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != "TipPoint") continue;
                if (tipRayOrigins.Contains(child)) continue;

                tipRayOrigins.Add(child);
            }

            tipRayOrigins.Sort(CompareTipRayOriginForward);
            return tipRayOrigins.ToArray();
        }

        private int CompareTipRayOriginForward(Transform left, Transform right)
        {
            if (left == null && right == null) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            Vector3 origin = transform.position;
            float leftForward = Vector3.Dot(transform.forward, left.position - origin);
            float rightForward = Vector3.Dot(transform.forward, right.position - origin);
            return rightForward.CompareTo(leftForward);
        }

        private static void DrawRayGizmo(Vector3 origin, Vector3 direction, float length)
        {
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            Vector3 normalizedDirection = direction.normalized;
            Vector3 end = origin + normalizedDirection * length;
            Gizmos.DrawLine(origin, end);
            Gizmos.DrawSphere(origin, 0.08f);
            Gizmos.DrawWireSphere(end, 0.12f);
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private LayerMask ResolveWaterLayerMask()
        {
            if (waterLayerMask.value != 0) return waterLayerMask;

            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer >= 0) return 1 << waterLayer;

            int oceanLayer = LayerMask.NameToLayer("Ocean");
            if (oceanLayer >= 0) return 1 << oceanLayer;

            return Physics.DefaultRaycastLayers;
        }

        private void CacheAnimatorParameters()
        {
            hasFishingCastTrigger = false;
            hasFishingBool = false;
            hasHasFishBool = false;
            if (animator == null) return;

            fishingCastTriggerHash = Animator.StringToHash("FishingCast");
            fishingBoolHash = Animator.StringToHash("fishing");
            hasFishBoolHash = Animator.StringToHash("HasFish");
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger &&
                    parameter.nameHash == fishingCastTriggerHash)
                {
                    hasFishingCastTrigger = true;
                }
                else if (parameter.type == AnimatorControllerParameterType.Bool &&
                         parameter.nameHash == fishingBoolHash)
                {
                    hasFishingBool = true;
                }
                else if (parameter.type == AnimatorControllerParameterType.Bool &&
                         parameter.nameHash == hasFishBoolHash)
                {
                    hasHasFishBool = true;
                }
            }
        }

        private void EnsureAnimationEventRelay()
        {
            if (animator == null)
            {
                return;
            }

            FishingAnimationEventRelay relay = animator.GetComponent<FishingAnimationEventRelay>();
            if (relay == null)
            {
                relay = animator.gameObject.AddComponent<FishingAnimationEventRelay>();
            }

            relay.Initialize(this);
        }

        private void SetFishingBool(bool value)
        {
            if (animator == null || !hasFishingBool) return;

            animator.SetBool(fishingBoolHash, value);
        }

        private void SetHasFishBool(bool value)
        {
            if (animator == null || !hasHasFishBool) return;

            animator.SetBool(hasFishBoolHash, value);
        }

        private void PlayCastAnimation()
        {
            if (animator == null) return;

            PlaySound(castSwingSound, castSwingSoundDelay);
            SetFishingBool(true);
        }
    }
}
