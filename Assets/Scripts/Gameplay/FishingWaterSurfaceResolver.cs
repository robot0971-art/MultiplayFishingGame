using System;
using UnityEngine;

namespace MultiplayFishing.Gameplay
{
    public sealed class FishingWaterSurfaceResolver
    {
        private readonly Camera playerCamera;
        private readonly Transform tipPoint;
        private readonly Transform[] tipRayOrigins;
        private readonly LayerMask waterLayerMask;
        private readonly LayerMask waterBlockerLayerMask;
        private readonly bool useCameraWaterRaycast;
        private readonly float waterRayStartHeight;
        private readonly float downwardCastBias;
        private readonly float maxCastDistance;

        public Transform WaterSurfaceTransform { get; private set; }

        public FishingWaterSurfaceResolver(
            Camera playerCamera,
            Transform tipPoint,
            Transform[] tipRayOrigins,
            Transform waterSurfaceTransform,
            LayerMask waterLayerMask,
            bool useCameraWaterRaycast,
            float waterRayStartHeight,
            float downwardCastBias,
            float maxCastDistance)
        {
            this.playerCamera = playerCamera;
            this.tipPoint = tipPoint;
            this.tipRayOrigins = tipRayOrigins ?? Array.Empty<Transform>();
            WaterSurfaceTransform = waterSurfaceTransform;
            this.waterLayerMask = waterLayerMask;
            waterBlockerLayerMask = ResolveWaterBlockerLayerMask(waterLayerMask);
            this.useCameraWaterRaycast = useCameraWaterRaycast;
            this.waterRayStartHeight = waterRayStartHeight;
            this.downwardCastBias = downwardCastBias;
            this.maxCastDistance = maxCastDistance;
        }

        public FishingWaterSurfaceResolver(
            Camera playerCamera,
            Transform tipPoint,
            Transform waterSurfaceTransform,
            LayerMask waterLayerMask,
            bool useCameraWaterRaycast,
            float waterRayStartHeight,
            float downwardCastBias,
            float maxCastDistance)
            : this(
                playerCamera,
                tipPoint,
                tipPoint != null ? new[] { tipPoint } : Array.Empty<Transform>(),
                waterSurfaceTransform,
                waterLayerMask,
                useCameraWaterRaycast,
                waterRayStartHeight,
                downwardCastBias,
                maxCastDistance)
        {
        }

        public Vector3 ResolveCastTarget(
            Transform owner,
            Vector3 castTargetOffset,
            float fallbackCastDistance,
            out bool hasSurfaceHit,
            out Vector3 surfaceHitPoint)
        {
            Vector3 fallbackTarget = GetFallbackCastTarget(owner, castTargetOffset, fallbackCastDistance);
            if (TryGetSurfaceHit(owner, out RaycastHit hit))
            {
                hasSurfaceHit = true;
                fallbackTarget.y = hit.point.y + castTargetOffset.y;
                surfaceHitPoint = fallbackTarget;

                return fallbackTarget;
            }

            hasSurfaceHit = false;
            surfaceHitPoint = Vector3.zero;

            if (TryGetSurfaceHeight(out float fallbackWaterSurfaceY))
            {
                fallbackTarget.y = fallbackWaterSurfaceY + castTargetOffset.y;
            }

            return fallbackTarget;
        }

        public bool TryGetSurfaceHeight(out float waterSurfaceY)
        {
            EnsureWaterSurfaceTransform();

            if (WaterSurfaceTransform == null)
            {
                waterSurfaceY = 0f;
                return false;
            }

            waterSurfaceY = WaterSurfaceTransform.position.y;
            return true;
        }

        private bool TryGetSurfaceHit(Transform owner, out RaycastHit hit)
        {
            for (int i = 0; i < tipRayOrigins.Length; i++)
            {
                Transform tipRayOrigin = tipRayOrigins[i];
                if (tipRayOrigin == null) continue;

                if (TryGetUnblockedWaterHit(tipRayOrigin.position, Vector3.down, out hit))
                {
                    return true;
                }
            }

            if (tipRayOrigins.Length > 0)
            {
                hit = default;
                return false;
            }

            if (useCameraWaterRaycast && playerCamera != null)
            {
                Ray screenCenterRay = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (TryGetUnblockedWaterHit(screenCenterRay.origin, screenCenterRay.direction, out hit))
                {
                    return true;
                }
            }

            Vector3 rayOrigin = owner.position + Vector3.up * waterRayStartHeight;
            Vector3 forwardDirection = owner.forward;
            Vector3 biasedDirection = (forwardDirection + Vector3.down * downwardCastBias).normalized;

            if (TryGetUnblockedWaterHit(rayOrigin, biasedDirection, out hit))
            {
                return true;
            }

            hit = default;
            return false;
        }

        private bool TryGetUnblockedWaterHit(Vector3 origin, Vector3 direction, out RaycastHit hit)
        {
            if (Physics.Raycast(origin, direction, out hit, maxCastDistance, waterBlockerLayerMask, QueryTriggerInteraction.Ignore))
            {
                return IsInLayerMask(hit.collider.gameObject.layer, waterLayerMask);
            }

            hit = default;
            return false;
        }

        private static LayerMask ResolveWaterBlockerLayerMask(LayerMask waterMask)
        {
            int blockerMask = waterMask.value;
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
            {
                blockerMask |= 1 << groundLayer;
            }

            return blockerMask;
        }

        private static bool IsInLayerMask(int layer, LayerMask layerMask)
        {
            return (layerMask.value & (1 << layer)) != 0;
        }

        private Vector3 GetFallbackCastTarget(Transform owner, Vector3 castTargetOffset, float fallbackCastDistance)
        {
            float forwardDistance = Mathf.Approximately(castTargetOffset.z, 0f)
                ? fallbackCastDistance
                : castTargetOffset.z;

            if (tipPoint != null)
            {
                return tipPoint.position
                    + owner.right * castTargetOffset.x
                    + owner.up * castTargetOffset.y
                    + owner.forward * forwardDistance;
            }

            return owner.position
                + owner.right * castTargetOffset.x
                + owner.up * castTargetOffset.y
                + owner.forward * forwardDistance;
        }

        private void EnsureWaterSurfaceTransform()
        {
            if (WaterSurfaceTransform != null)
            {
                return;
            }

            Transform waterTransform = FindWaterSurfaceTransformByName();
            if (waterTransform != null)
            {
                WaterSurfaceTransform = waterTransform;
            }
        }

        private static Transform FindWaterSurfaceTransformByName()
        {
            Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null) continue;

                if (candidate.name == "WaterBlock_50m"
                    || candidate.name == "WaterBlock_50m (1)"
                    || candidate.name == "Water")
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
