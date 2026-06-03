using TMPro;
using UnityEngine;

namespace TestBoids.Gameplay.UI
{
    [DisallowMultipleComponent]
    public sealed class ScreenEdgeGuideIndicator : MonoBehaviour
    {
        public enum DirectionMode
        {
            CameraHorizontalPlane,
            CameraScreenPlane
        }

        public enum BackTargetDefaultSide
        {
            Left = -1,
            Right = 1
        }

        [Header("References")]
        [SerializeField] private Transform source;
        [SerializeField] private Transform target;
        [SerializeField] private Camera referenceCamera;
        [SerializeField] private RectTransform guideRoot;
        [SerializeField] private RectTransform guideImage;
        [SerializeField] private CanvasGroup guideCanvasGroup;
        [SerializeField] private TMP_Text distanceGuide;
        [SerializeField] private GameStateManager stateManager;

        [Header("Direction")]
        [SerializeField] private DirectionMode directionMode = DirectionMode.CameraHorizontalPlane;
        [SerializeField, Min(0f)] private float minimumDistanceToShow = 0.5f;

        [Header("Horizontal Navigation")]
        [SerializeField, Range(0f, 1f)] private float backSideDeadZone = 0.15f;
        [SerializeField] private BackTargetDefaultSide defaultBackSide = BackTargetDefaultSide.Right;
        [SerializeField, Min(0f)] private float verticalCueThreshold = 0.35f;
        [SerializeField, Min(0f)] private float verticalCueStrength = 1f;

        [Header("Visibility")]
        [SerializeField] private bool hideDuringIntro = true;
        [SerializeField] private bool hideWhenTargetOnScreen = true;
        [SerializeField, Range(0f, 0.5f)] private float screenPadding = 0.05f;

        [Header("Distance")]
        [SerializeField] private bool showDistance = true;

        [Header("Placement")]
        [SerializeField, Min(0f)] private float edgeInset;
        [SerializeField, Min(0f)] private float positionSmoothTime = 0.08f;

        [Header("Fade")]
        [SerializeField, Range(0f, 1f)] private float visibleAlpha = 0.75f;
        [SerializeField, Min(0f)] private float fadeSpeed = 8f;
        [SerializeField] private bool hideWhenTargetInFrontCenter;
        [SerializeField, Range(0f, 1f)] private float centerHideRadius = 0.2f;

        private Vector2 positionVelocity;

        private void Reset()
        {
            guideRoot = transform as RectTransform;
            guideCanvasGroup = GetComponentInChildren<CanvasGroup>();
            distanceGuide = GetComponentInChildren<TMP_Text>(true);
            referenceCamera = Camera.main;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void LateUpdate()
        {
            ResolveReferences();

            if (ShouldHideForCurrentState())
            {
                SetAlpha(0f);
                return;
            }

            if (!source || !target || !guideRoot || !guideImage)
            {
                SetAlpha(0f);
                return;
            }

            Vector3 offset = target.position - source.position;
            UpdateDistanceGuide(offset.magnitude);

            if (offset.sqrMagnitude <= minimumDistanceToShow * minimumDistanceToShow)
            {
                SetAlpha(0f);
                return;
            }

            if (hideWhenTargetOnScreen && IsTargetOnScreen())
            {
                SetAlpha(0f);
                return;
            }

            if (!TryGetGuideDirection(offset, out Vector2 direction))
            {
                SetAlpha(0f);
                return;
            }

            Vector2 edgePosition = GetEdgePosition(direction);
            MoveGuideImage(edgePosition);

            float targetAlpha = ShouldHideAtCenter(direction) ? 0f : visibleAlpha;
            SetAlpha(targetAlpha);
        }

        public void SetSource(Transform newSource)
        {
            source = newSource;
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        public void HideImmediate()
        {
            SetAlphaImmediate(0f);
        }

        private void ResolveReferences()
        {
            if (!referenceCamera)
            {
                referenceCamera = Camera.main;
            }

            if (!guideCanvasGroup && guideImage)
            {
                guideCanvasGroup = guideImage.GetComponent<CanvasGroup>();
            }

            if (!distanceGuide)
            {
                distanceGuide = FindDistanceGuide();
            }

            if (!stateManager)
            {
                stateManager = GameStateManager.Instance;
            }

            if (!stateManager)
            {
                stateManager = FindFirstObjectByType<GameStateManager>();
            }
        }

        private bool TryGetGuideDirection(Vector3 worldOffset, out Vector2 direction)
        {
            direction = Vector2.zero;

            if (!referenceCamera)
            {
                return false;
            }

            Transform cameraTransform = referenceCamera.transform;

            if (directionMode == DirectionMode.CameraScreenPlane)
            {
                direction = new Vector2(
                    Vector3.Dot(cameraTransform.right, worldOffset),
                    Vector3.Dot(cameraTransform.up, worldOffset));
            }
            else
            {
                return TryGetHorizontalNavigationDirection(worldOffset, cameraTransform, out direction);
            }

            if (direction.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            direction.Normalize();
            return true;
        }

        private bool TryGetHorizontalNavigationDirection(
            Vector3 worldOffset,
            Transform cameraTransform,
            out Vector2 direction)
        {
            direction = Vector2.zero;

            Vector3 flatOffset = Vector3.ProjectOnPlane(worldOffset, Vector3.up);
            Vector3 flatForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            Vector3 flatRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up);

            float horizontalDistance = flatOffset.magnitude;
            float verticalCue = GetVerticalCue(worldOffset.y, horizontalDistance);

            if (horizontalDistance <= 0.000001f)
            {
                if (Mathf.Abs(verticalCue) <= 0.000001f)
                {
                    return false;
                }

                direction = new Vector2(0f, verticalCue);
                direction.Normalize();
                return true;
            }

            if (flatForward.sqrMagnitude <= 0.000001f || flatRight.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            Vector3 flatDirection = flatOffset / horizontalDistance;
            float rightAmount = Vector3.Dot(flatRight.normalized, flatDirection);
            float forwardAmount = Vector3.Dot(flatForward.normalized, flatDirection);

            if (forwardAmount < 0f)
            {
                float sideAmount = Mathf.Abs(rightAmount) < backSideDeadZone
                    ? (float)defaultBackSide
                    : rightAmount;

                direction = new Vector2(sideAmount, verticalCue);
            }
            else
            {
                float upwardCue = Mathf.Max(forwardAmount, verticalCue);
                direction = new Vector2(rightAmount, verticalCue < 0f ? verticalCue : upwardCue);
            }

            if (direction.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            direction.Normalize();
            return true;
        }

        private float GetVerticalCue(float verticalOffset, float horizontalDistance)
        {
            float distance = Mathf.Max(horizontalDistance, 0.000001f);
            float verticalRatio = verticalOffset / distance;
            float verticalAmount = Mathf.Abs(verticalRatio);

            if (verticalAmount <= verticalCueThreshold)
            {
                return 0f;
            }

            return Mathf.Sign(verticalRatio)
                * Mathf.Clamp01((verticalAmount - verticalCueThreshold) * verticalCueStrength);
        }

        private bool IsTargetOnScreen()
        {
            if (!referenceCamera || !target)
            {
                return false;
            }

            Vector3 viewportPoint = referenceCamera.WorldToViewportPoint(target.position);
            float padding = Mathf.Clamp01(screenPadding);

            return viewportPoint.z > 0f
                && viewportPoint.x >= padding
                && viewportPoint.x <= 1f - padding
                && viewportPoint.y >= padding
                && viewportPoint.y <= 1f - padding;
        }

        private bool ShouldHideForCurrentState()
        {
            return hideDuringIntro
                && stateManager
                && stateManager.CurrentState == GameState.Intro;
        }

        private TMP_Text FindDistanceGuide()
        {
            TMP_Text[] textComponents = GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text textComponent in textComponents)
            {
                if (textComponent.name == "DistanceGuide")
                {
                    return textComponent;
                }
            }

            return textComponents.Length > 0 ? textComponents[0] : null;
        }

        private void UpdateDistanceGuide(float distance)
        {
            if (!distanceGuide)
            {
                return;
            }

            distanceGuide.enabled = showDistance;
            if (showDistance)
            {
                distanceGuide.text = $"{Mathf.RoundToInt(distance)}M";
            }
        }

        private Vector2 GetEdgePosition(Vector2 direction)
        {
            Rect rect = guideRoot.rect;
            float halfWidth = Mathf.Max(0f, rect.width * 0.5f - edgeInset);
            float halfHeight = Mathf.Max(0f, rect.height * 0.5f - edgeInset);

            float xLimit = Mathf.Abs(direction.x) <= 0.000001f
                ? float.PositiveInfinity
                : halfWidth / Mathf.Abs(direction.x);
            float yLimit = Mathf.Abs(direction.y) <= 0.000001f
                ? float.PositiveInfinity
                : halfHeight / Mathf.Abs(direction.y);

            return direction * Mathf.Min(xLimit, yLimit);
        }

        private void MoveGuideImage(Vector2 targetPosition)
        {
            if (positionSmoothTime <= 0f)
            {
                guideImage.anchoredPosition = targetPosition;
                positionVelocity = Vector2.zero;
                return;
            }

            guideImage.anchoredPosition = Vector2.SmoothDamp(
                guideImage.anchoredPosition,
                targetPosition,
                ref positionVelocity,
                positionSmoothTime,
                Mathf.Infinity,
                Time.deltaTime);
        }

        private bool ShouldHideAtCenter(Vector2 direction)
        {
            return hideWhenTargetInFrontCenter
                && direction.y > 0f
                && Mathf.Abs(direction.x) <= centerHideRadius;
        }

        private void SetAlpha(float targetAlpha)
        {
            if (!guideCanvasGroup)
            {
                return;
            }

            guideCanvasGroup.alpha = Mathf.Lerp(
                guideCanvasGroup.alpha,
                targetAlpha,
                Time.deltaTime * fadeSpeed);
        }

        private void SetAlphaImmediate(float alpha)
        {
            if (guideCanvasGroup)
            {
                guideCanvasGroup.alpha = alpha;
            }
        }
    }
}
