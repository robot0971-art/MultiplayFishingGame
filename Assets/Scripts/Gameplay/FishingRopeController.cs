using System.Collections;
using System.Reflection;
using UnityEngine;

namespace MultiplayFishing.Gameplay
{
    public sealed class FishingRopeController
    {
        private readonly Transform tipPoint;
        private readonly Transform hookPoint;
        private readonly GameObject fishingRopeObject;
        private readonly Component fishingRopeComponent;
        private readonly Transform originalHookParent;
        private readonly Vector3 originalHookLocalPosition;
        private readonly Quaternion originalHookLocalRotation;
        private readonly Vector3 originalHookLocalScale;
        private Vector3 lastExpectedHookPosition;
        private bool hasExpectedHookPosition;

        public FishingRopeController(
            Transform tipPoint,
            Transform hookPoint,
            GameObject fishingRopeObject,
            Component fishingRopeComponent)
        {
            this.tipPoint = tipPoint;
            this.hookPoint = hookPoint;
            this.fishingRopeObject = fishingRopeObject;
            this.fishingRopeComponent = fishingRopeComponent;

            if (hookPoint != null)
            {
                originalHookParent = hookPoint.parent;
                originalHookLocalPosition = hookPoint.localPosition;
                originalHookLocalRotation = hookPoint.localRotation;
                originalHookLocalScale = hookPoint.localScale;
            }
        }

        public bool IsConfigured => tipPoint != null && hookPoint != null;

        public Transform GetTipPoint() => tipPoint;

        public Transform GetHookPoint() => hookPoint;

        public Vector3 GetIdleHookPosition(Transform owner, Vector3 idleHookOffset)
        {
            if (tipPoint == null)
            {
                return owner.position;
            }

            return tipPoint.TransformPoint(idleHookOffset);
        }

        public float GetDesiredRopeLength(Vector3 targetPosition, float minimumLength, float slack)
        {
            if (tipPoint == null)
            {
                return minimumLength;
            }

            float directDistance = Vector3.Distance(tipPoint.position, targetPosition);
            return Mathf.Max(minimumLength, directDistance + Mathf.Max(0f, slack));
        }

        public void SetHookPosition(Vector3 position)
        {
            if (hookPoint != null)
            {
                hookPoint.position = position;
            }
        }

        public void DetachHookFromRod()
        {
            if (hookPoint == null || hookPoint.parent == null)
            {
                return;
            }

            hookPoint.SetParent(null, true);
        }

        public void RestoreHookToRod()
        {
            if (hookPoint == null || originalHookParent == null)
            {
                return;
            }

            hookPoint.SetParent(originalHookParent, false);
            hookPoint.localPosition = originalHookLocalPosition;
            hookPoint.localRotation = originalHookLocalRotation;
            hookPoint.localScale = originalHookLocalScale;
        }

        public void RestoreHookToRod(Vector3 worldPosition)
        {
            if (hookPoint == null || originalHookParent == null)
            {
                return;
            }

            // keepWorldPosition=true: 부모 변경 시 순간 스냅 방지.
            // 이후 hookPoint.position으로 원하는 world 위치를 덮어씀.
            hookPoint.SetParent(originalHookParent, true);
            hookPoint.localRotation = originalHookLocalRotation;
            hookPoint.localScale = originalHookLocalScale;
            hookPoint.position = worldPosition;
        }

        public void AttachHookToAnchor(Transform anchorPoint)
        {
            if (hookPoint == null || anchorPoint == null)
            {
                return;
            }

            hookPoint.SetParent(anchorPoint, false);
            hookPoint.localPosition = Vector3.zero;
            hookPoint.localRotation = Quaternion.identity;
            hookPoint.localScale = Vector3.one;
        }

        public void SetVisible(bool visible)
        {
            if (fishingRopeObject != null)
            {
                fishingRopeObject.SetActive(visible);
            }
        }

        public void SetRopeLength(float ropeLength)
        {
            if (fishingRopeComponent == null)
            {
                return;
            }

            PropertyInfo ropeLengthProperty = fishingRopeComponent.GetType().GetProperty("ropeLength");
            if (ropeLengthProperty != null && ropeLengthProperty.CanWrite)
            {
                ropeLengthProperty.SetValue(fishingRopeComponent, ropeLength);
                return;
            }

            FieldInfo ropeLengthField = fishingRopeComponent.GetType().GetField("ropeLength");
            if (ropeLengthField != null)
            {
                ropeLengthField.SetValue(fishingRopeComponent, ropeLength);
            }
        }

        public IEnumerator MoveHook(
            Vector3 targetPosition,
            float startDelay,
            float duration,
            float arcHeight,
            float ropeSlack,
            float minimumRopeLength,
            bool showRopeOnStart,
            bool hideRopeOnComplete,
            bool useArcPath,
            bool stopAtWaterSurface,
            float waterSurfaceY,
            System.Action onWaterHit = null,
            FishingLineVisual lineVisual = null,
            Transform completionAnchor = null)
        {
            System.Action<Vector3> onWaterHitAtPosition = onWaterHit != null
                ? hitPosition => onWaterHit()
                : null;

            return MoveHook(
                targetPosition,
                startDelay,
                duration,
                arcHeight,
                ropeSlack,
                minimumRopeLength,
                showRopeOnStart,
                hideRopeOnComplete,
                useArcPath,
                stopAtWaterSurface,
                waterSurfaceY,
                onWaterHitAtPosition,
                lineVisual,
                completionAnchor);
        }

        public IEnumerator MoveHook(
            Vector3 targetPosition,
            float startDelay,
            float duration,
            float arcHeight,
            float ropeSlack,
            float minimumRopeLength,
            bool showRopeOnStart,
            bool hideRopeOnComplete,
            bool useArcPath,
            bool stopAtWaterSurface,
            float waterSurfaceY,
            System.Action<Vector3> onWaterHit = null,
            FishingLineVisual lineVisual = null,
            Transform completionAnchor = null)
        {
            return MoveHookDynamic(
                targetPosition,
                () => startDelay,
                () => duration,
                () => arcHeight,
                () => ropeSlack,
                () => minimumRopeLength,
                showRopeOnStart,
                hideRopeOnComplete,
                useArcPath,
                stopAtWaterSurface,
                () => waterSurfaceY,
                onWaterHit,
                lineVisual,
                completionAnchor);
        }

        public IEnumerator MoveHookDynamic(
            Vector3 targetPosition,
            System.Func<float> startDelayProvider,
            System.Func<float> durationProvider,
            System.Func<float> arcHeightProvider,
            System.Func<float> ropeSlackProvider,
            System.Func<float> minimumRopeLengthProvider,
            bool showRopeOnStart,
            bool hideRopeOnComplete,
            bool useArcPath,
            bool stopAtWaterSurface,
            System.Func<float> waterSurfaceYProvider,
            System.Action onWaterHit = null,
            FishingLineVisual lineVisual = null,
            Transform completionAnchor = null)
        {
            System.Action<Vector3> onWaterHitAtPosition = onWaterHit != null
                ? hitPosition => onWaterHit()
                : null;

            return MoveHookDynamic(
                targetPosition,
                startDelayProvider,
                durationProvider,
                arcHeightProvider,
                ropeSlackProvider,
                minimumRopeLengthProvider,
                showRopeOnStart,
                hideRopeOnComplete,
                useArcPath,
                stopAtWaterSurface,
                waterSurfaceYProvider,
                onWaterHitAtPosition,
                lineVisual,
                completionAnchor);
        }

        public IEnumerator MoveHookDynamic(
            Vector3 targetPosition,
            System.Func<float> startDelayProvider,
            System.Func<float> durationProvider,
            System.Func<float> arcHeightProvider,
            System.Func<float> ropeSlackProvider,
            System.Func<float> minimumRopeLengthProvider,
            bool showRopeOnStart,
            bool hideRopeOnComplete,
            bool useArcPath,
            bool stopAtWaterSurface,
            System.Func<float> waterSurfaceYProvider,
            System.Action<Vector3> onWaterHit = null,
            FishingLineVisual lineVisual = null,
            Transform completionAnchor = null)
        {
            if (!IsConfigured)
            {
                yield break;
            }

            // Let FishingLineVisual know the rope is controlling hook movement.
            lineVisual?.SetHookControlledByRope(true);

            try
            {
                float delayElapsed = 0f;
                while (true)
                {
                    float currentStartDelay = Mathf.Max(0f, startDelayProvider != null ? startDelayProvider() : 0f);
                    if (delayElapsed >= currentStartDelay)
                    {
                        break;
                    }

                    delayElapsed += Time.deltaTime;
                    yield return null;
                }

                Vector3 launchStartPosition = tipPoint != null ? tipPoint.position : hookPoint.position;

                if (!hideRopeOnComplete)
                {
                    DetachHookFromRod();
                    hookPoint.position = launchStartPosition;
                }

                if (showRopeOnStart)
                {
                    SetVisible(true);
                }

                // Apply the cast rope target only when the throw actually begins,
                // so the lure does not appear to snap toward the water on click.
                float initialMinimumRopeLength = minimumRopeLengthProvider != null ? minimumRopeLengthProvider() : 0f;
                float initialRopeSlack = ropeSlackProvider != null ? ropeSlackProvider() : 0f;
                SetRopeLength(GetDesiredRopeLength(targetPosition, initialMinimumRopeLength, initialRopeSlack));

                Vector3 startPosition = !hideRopeOnComplete ? launchStartPosition : hookPoint.position;
                float elapsed = 0f;
                bool hasHitWater = false;
                float lastT = 0f;
                bool loggedTrajectory = false;

                while (lastT < 1f)
                {
                    float currentDuration = durationProvider != null ? durationProvider() : 0f;
                    float currentArcHeight = arcHeightProvider != null ? arcHeightProvider() : 0f;
                    float currentRopeSlack = ropeSlackProvider != null ? ropeSlackProvider() : 0f;
                    float currentMinimumRopeLength = minimumRopeLengthProvider != null ? minimumRopeLengthProvider() : 0f;
                    float currentWaterSurfaceY = waterSurfaceYProvider != null ? waterSurfaceYProvider() : 0f;
                    Vector3 controlPoint = GetArcControlPoint(startPosition, targetPosition, currentArcHeight);
                    if (!loggedTrajectory)
                    {
                        loggedTrajectory = true;
                        Debug.Log(
                            $"[HookTrace] Trajectory start={startPosition:F3}, control={controlPoint:F3}, target={targetPosition:F3}, " +
                            $"arcHeight={currentArcHeight:F3}, duration={currentDuration:F3}, useArc={useArcPath}, " +
                            $"stopAtWater={stopAtWaterSurface}, waterY={currentWaterSurfaceY:F3}, " +
                            $"parent={(hookPoint.parent != null ? hookPoint.parent.name : "null")}");
                    }

                    if (currentDuration <= 0f)
                    {
                        lastT = 1f;
                    }
                    else
                    {
                        elapsed += Time.deltaTime;
                        lastT = Mathf.Clamp01(elapsed / currentDuration);
                    }

                    float t = Mathf.SmoothStep(0f, 1f, lastT);

                    Vector3 nextPosition = useArcPath
                        ? EvaluateQuadraticBezier(startPosition, controlPoint, targetPosition, t)
                        : Vector3.Lerp(startPosition, targetPosition, t);

                    // Clamp to the water surface so the hook does not keep sinking.
                    if (stopAtWaterSurface && nextPosition.y < currentWaterSurfaceY)
                    {
                        nextPosition.y = currentWaterSurfaceY;

                        // Trigger the splash only on the first water contact.
                        if (!hasHitWater)
                        {
                            hasHitWater = true;
                            onWaterHit?.Invoke(nextPosition);
                        }
                    }

                    hookPoint.position = nextPosition;
                    TrackExpectedHookPosition(nextPosition);
                    SetRopeLength(GetDesiredRopeLength(nextPosition, currentMinimumRopeLength, currentRopeSlack));
                    yield return null;
                    WarnIfHookPositionWasOverwritten("MoveHook frame");
                }

                // Apply the final evaluated position after the motion completes.
                float finalArcHeight = arcHeightProvider != null ? arcHeightProvider() : 0f;
                float finalMinimumRopeLength = minimumRopeLengthProvider != null ? minimumRopeLengthProvider() : 0f;
                float finalRopeSlack = ropeSlackProvider != null ? ropeSlackProvider() : 0f;
                float finalWaterSurfaceY = waterSurfaceYProvider != null ? waterSurfaceYProvider() : 0f;
                Vector3 finalControlPoint = GetArcControlPoint(startPosition, targetPosition, finalArcHeight);
                Vector3 finalPosition = useArcPath
                    ? EvaluateQuadraticBezier(startPosition, finalControlPoint, targetPosition, 1f)
                    : targetPosition;

                // Keep the final position from ending below the water surface clamp.
                if (stopAtWaterSurface && finalPosition.y < finalWaterSurfaceY)
                {
                    finalPosition.y = finalWaterSurfaceY;
                }

                if (stopAtWaterSurface && !hasHitWater && finalPosition.y <= finalWaterSurfaceY + 0.001f)
                {
                    hasHitWater = true;
                    onWaterHit?.Invoke(finalPosition);
                }

                hookPoint.position = finalPosition;
                TrackExpectedHookPosition(finalPosition);
                SetRopeLength(GetDesiredRopeLength(finalPosition, finalMinimumRopeLength, finalRopeSlack));
                yield return null;
                WarnIfHookPositionWasOverwritten("MoveHook final");

                if (hideRopeOnComplete)
                {
                    if (completionAnchor != null)
                    {
                        AttachHookToAnchor(completionAnchor);
                    }
                    else
                    {
                        RestoreHookToRod();
                    }

                    SetVisible(false);
                }
            }
            finally
            {
                hasExpectedHookPosition = false;
                // Hand hook control back to FishingLineVisual when rope motion ends.
                lineVisual?.SetHookControlledByRope(false);
            }
        }

        private void TrackExpectedHookPosition(Vector3 expectedPosition)
        {
            lastExpectedHookPosition = expectedPosition;
            hasExpectedHookPosition = true;
        }

        private void WarnIfHookPositionWasOverwritten(string phase)
        {
            if (!hasExpectedHookPosition || hookPoint == null)
            {
                return;
            }

            float delta = Vector3.Distance(hookPoint.position, lastExpectedHookPosition);
            if (delta <= 0.02f)
            {
                return;
            }

            Debug.LogWarning(
                $"[HookTrace] HookPoint overwritten during {phase}. " +
                $"expected={lastExpectedHookPosition:F3}, actual={hookPoint.position:F3}, delta={delta:F3}, " +
                $"parent={(hookPoint.parent != null ? hookPoint.parent.name : "null")}");
        }

        private Vector3 GetArcControlPoint(Vector3 startPosition, Vector3 targetPosition, float baseArcHeight)
        {
            Vector3 controlPoint = Vector3.Lerp(startPosition, targetPosition, 0.5f);
            controlPoint.y = Mathf.Max(startPosition.y, targetPosition.y) + baseArcHeight;
            return controlPoint;
        }

        private static Vector3 EvaluateQuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * start
                + 2f * oneMinusT * t * control
                + t * t * end;
        }
    }
}
