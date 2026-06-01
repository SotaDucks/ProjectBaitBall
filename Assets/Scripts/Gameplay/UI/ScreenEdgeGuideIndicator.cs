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

        [Header("References")]
        [SerializeField] private Transform source;
        [SerializeField] private Transform target;
        [SerializeField] private Camera referenceCamera;
        [SerializeField] private RectTransform guideRoot;
        [SerializeField] private RectTransform guideImage;
        [SerializeField] private CanvasGroup guideCanvasGroup;

        [Header("Direction")]
        [SerializeField] private DirectionMode directionMode = DirectionMode.CameraHorizontalPlane;
        [SerializeField, Min(0f)] private float minimumDistanceToShow = 0.5f;

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
            referenceCamera = Camera.main;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void LateUpdate()
        {
            ResolveReferences();

            if (!source || !target || !guideRoot || !guideImage)
            {
                SetAlpha(0f);
                return;
            }

            Vector3 offset = target.position - source.position;
            if (offset.sqrMagnitude <= minimumDistanceToShow * minimumDistanceToShow)
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
                Vector3 flatOffset = Vector3.ProjectOnPlane(worldOffset, Vector3.up);
                Vector3 flatForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                Vector3 flatRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up);

                if (flatOffset.sqrMagnitude <= 0.000001f || flatForward.sqrMagnitude <= 0.000001f)
                {
                    return false;
                }

                direction = new Vector2(
                    Vector3.Dot(flatRight.normalized, flatOffset.normalized),
                    Vector3.Dot(flatForward.normalized, flatOffset.normalized));
            }

            if (direction.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            direction.Normalize();
            return true;
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
