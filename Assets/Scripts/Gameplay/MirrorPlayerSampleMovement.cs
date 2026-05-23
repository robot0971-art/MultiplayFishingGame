using UnityEngine;
using Mirror;
using MultiplayFishing.Core;

namespace MultiplayFishing.Gameplay
{
    [RequireComponent(typeof(CharacterController))]
    public class MirrorPlayerSampleMovement : NetworkBehaviour, IPlayerMovementController
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float runSpeed = 7.5f;
        [SerializeField] private float turnSnapSpeed = 12f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float groundedVelocity = -2f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string walkParameter = "WalkSpeed";
        [SerializeField] private float walkAnimDampTime = 0.15f;

        [Header("Footstep Sound")]
        [SerializeField] private AudioSource footstepAudioSource;
        [SerializeField] private AudioClip walkFootstepSound;
        [SerializeField] private AudioClip runFootstepSound;
        [SerializeField] private float walkFootstepInterval = 0.45f;
        [SerializeField] private float sprintFootstepInterval = 0.28f;
        [SerializeField] private float walkFootstepPitch = 1f;
        [SerializeField] private float sprintFootstepPitch = 1f;
        [SerializeField] private float footstepVolume = 0.75f;

        private CharacterController characterController;
        private IPlayerMovementInputSource inputSource;
        private Vector3 velocity;
        private Vector2 serverMoveInput;
        private Vector3 serverCameraForward = Vector3.forward;
        private Vector3 serverCameraRight = Vector3.right;
        private Vector3 targetMoveDirection;
        private Quaternion targetRotation;
        private int walkParameterHash;
        private bool hasWalkParameter;
        private bool isMovementBlocked;
        private bool localSprintHeld;
        private float nextFootstepTime;
        private bool wasFootstepMoving;
        private AudioClip currentFootstepClip;

        [SyncVar] private float syncedWalkSpeed;
        [SyncVar] private bool syncedSprint;

        public bool IsMovementBlocked => isMovementBlocked;
        public bool IsSprinting => isLocalPlayer ? localSprintHeld : syncedSprint;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            targetRotation = transform.rotation;
            targetMoveDirection = transform.forward;
            walkParameterHash = Animator.StringToHash(walkParameter);
            ResolveInputSource();
            CacheAnimatorParameter();
            EnsureFootstepAudioSource();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            SetCharacterControllerActive(true);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // 클라이언트는 NetworkTransform 보간 결과만 표시합니다.
            // CharacterController가 같이 켜져 있으면 서버 위치 보정과 충돌해 화면 떨림이 생길 수 있습니다.
            if (!isServer)
            {
                SetCharacterControllerActive(false);
            }
        }

        private void Update()
        {
            if (isLocalPlayer)
            {
                SendLocalInput();
            }

            if (isServer)
            {
                SimulateServerMovement(Time.deltaTime);
            }

            UpdateWalkAnimation();
            UpdateFootstepSound();
        }

        private void OnDisable()
        {
            StopFootstepSound();
        }

        public void SetMovementBlocked(bool blocked)
        {
            isMovementBlocked = blocked;
            if (blocked)
            {
                StopFootstepSound();
            }

            if (isLocalPlayer)
            {
                CmdSetMovementBlocked(blocked);
            }
        }

        private void ResolveInputSource()
        {
            if (!DIContainer.TryResolve<IPlayerMovementInputSource>(out inputSource))
            {
                inputSource = new KeyboardPlayerMovementInputSource();
            }
        }

        private bool EnsureInputSource()
        {
            if (inputSource == null)
            {
                ResolveInputSource();
            }

            return inputSource != null;
        }

        private void SendLocalInput()
        {
            if (!EnsureInputSource()) return;

            Vector2 move = isMovementBlocked ? Vector2.zero : inputSource.ReadMove();
            bool sprint = !isMovementBlocked && inputSource.ReadSprint() && move.sqrMagnitude > 0.01f;
            localSprintHeld = sprint;

            Vector3 cameraForward = transform.forward;
            Vector3 cameraRight = transform.right;
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                cameraForward = mainCamera.transform.forward;
                cameraRight = mainCamera.transform.right;
            }

            cameraForward.y = 0f;
            cameraRight.y = 0f;
            if (cameraForward.sqrMagnitude < 0.001f) cameraForward = transform.forward;
            if (cameraRight.sqrMagnitude < 0.001f) cameraRight = transform.right;
            cameraForward.Normalize();
            cameraRight.Normalize();

            if (isServer)
            {
                ApplyServerInput(move, cameraForward, cameraRight, sprint);
            }
            else
            {
                CmdSetMovementInput(move, cameraForward, cameraRight, sprint);
            }
        }

        [Command]
        private void CmdSetMovementInput(Vector2 move, Vector3 cameraForward, Vector3 cameraRight, bool sprint)
        {
            ApplyServerInput(move, cameraForward, cameraRight, sprint);
        }

        [Command]
        private void CmdSetMovementBlocked(bool blocked)
        {
            isMovementBlocked = blocked;
            if (blocked)
            {
                ApplyServerInput(Vector2.zero, serverCameraForward, serverCameraRight, false);
            }
        }

        [Server]
        private void ApplyServerInput(Vector2 move, Vector3 cameraForward, Vector3 cameraRight, bool sprint)
        {
            serverMoveInput = isMovementBlocked ? Vector2.zero : Vector2.ClampMagnitude(move, 1f);
            serverCameraForward = FlattenOrFallback(cameraForward, transform.forward);
            serverCameraRight = FlattenOrFallback(cameraRight, transform.right);
            syncedSprint = sprint && serverMoveInput.sqrMagnitude > 0.01f;
            syncedWalkSpeed = serverMoveInput.sqrMagnitude > 0.01f ? (syncedSprint ? 2f : 1f) : 0f;
        }

        [Server]
        private void SimulateServerMovement(float deltaTime)
        {
            if (characterController == null) return;
            if (!characterController.enabled) return;
            if (!gameObject.activeInHierarchy) return;

            Vector2 moveInput = isMovementBlocked ? Vector2.zero : serverMoveInput;
            if (moveInput.sqrMagnitude > 0.01f)
            {
                targetMoveDirection = ((serverCameraForward * moveInput.y) + (serverCameraRight * moveInput.x)).normalized;
                targetRotation = Quaternion.LookRotation(targetMoveDirection, Vector3.up);
            }

            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSnapSpeed * deltaTime);

            if (characterController.isGrounded && velocity.y < 0f)
            {
                velocity.y = groundedVelocity;
            }

            velocity.y += gravity * deltaTime;

            Vector3 horizontalMove = moveInput.sqrMagnitude > 0.01f ? targetMoveDirection : Vector3.zero;
            float speed = syncedSprint ? runSpeed : moveSpeed;
            Vector3 motion = (horizontalMove * speed) + (Vector3.up * velocity.y);
            characterController.Move(motion * deltaTime);
        }

        private void UpdateWalkAnimation()
        {
            if (!hasWalkParameter && animator != null)
            {
                CacheAnimatorParameter();
            }

            if (animator == null || !hasWalkParameter) return;

            float targetSpeed = isLocalPlayer
                ? GetLocalWalkSpeedTarget()
                : syncedWalkSpeed;

            animator.SetFloat(walkParameterHash, targetSpeed, walkAnimDampTime, Time.deltaTime);
        }

        private void UpdateFootstepSound()
        {
            if (!NetworkClient.active) return;

            EnsureFootstepAudioSource();
            if (footstepAudioSource == null) return;

            float targetSpeed = isLocalPlayer ? GetLocalWalkSpeedTarget() : syncedWalkSpeed;
            if (targetSpeed <= 0.01f)
            {
                StopFootstepSound();
                return;
            }

            bool sprinting = isLocalPlayer ? localSprintHeld : syncedSprint;
            AudioClip footstepClip = sprinting && runFootstepSound != null ? runFootstepSound : walkFootstepSound;
            if (footstepClip == null) return;

            if (wasFootstepMoving && currentFootstepClip != footstepClip)
            {
                StopFootstepSound();
            }

            wasFootstepMoving = true;
            currentFootstepClip = footstepClip;

            float interval = sprinting ? sprintFootstepInterval : walkFootstepInterval;
            if (Time.time < nextFootstepTime) return;

            footstepAudioSource.pitch = sprinting ? sprintFootstepPitch : walkFootstepPitch;
            footstepAudioSource.PlayOneShot(footstepClip, footstepVolume);
            nextFootstepTime = Time.time + Mathf.Max(0.05f, interval);
        }

        private void StopFootstepSound()
        {
            nextFootstepTime = 0f;
            wasFootstepMoving = false;
            currentFootstepClip = null;

            if (footstepAudioSource != null)
            {
                footstepAudioSource.Stop();
            }
        }

        private float GetLocalWalkSpeedTarget()
        {
            if (isMovementBlocked) return 0f;
            if (!EnsureInputSource()) return 0f;

            Vector2 move = inputSource.ReadMove();
            if (move.sqrMagnitude <= 0.01f) return 0f;

            return inputSource.ReadSprint() ? 2f : 1f;
        }

        private void CacheAnimatorParameter()
        {
            hasWalkParameter = false;
            if (animator == null) return;

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == walkParameterHash &&
                    parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasWalkParameter = true;
                    break;
                }
            }
        }

        private void SetCharacterControllerActive(bool active)
        {
            if (characterController == null)
            {
                return;
            }

            characterController.enabled = active;
        }

        private void EnsureFootstepAudioSource()
        {
            if (footstepAudioSource != null) return;

            footstepAudioSource = gameObject.AddComponent<AudioSource>();
            footstepAudioSource.playOnAwake = false;
            footstepAudioSource.loop = false;
            footstepAudioSource.spatialBlend = 1f;
        }

        private static Vector3 FlattenOrFallback(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude < 0.001f)
            {
                value = fallback;
                value.y = 0f;
            }

            return value.sqrMagnitude > 0.001f ? value.normalized : Vector3.forward;
        }
    }
}
