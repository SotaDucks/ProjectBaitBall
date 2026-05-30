using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace TestBoids.Gameplay.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class IntroUIView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup rootCanvasGroup;
        [SerializeField] private Graphic pressAnyKeyGraphic;

        [Header("Fade Out")]
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.6f;
        [SerializeField] private Ease fadeOutEase = Ease.OutCubic;
        [SerializeField] private bool deactivateAfterFadeOut;

        [Header("Blink")]
        [SerializeField] private bool blinkOnEnable = true;
        [SerializeField, Range(0f, 1f)] private float blinkMinAlpha = 0.25f;
        [SerializeField, Range(0f, 1f)] private float blinkMaxAlpha = 1f;
        [SerializeField, Min(0.01f)] private float blinkHalfCycleDuration = 0.55f;
        [SerializeField] private Ease blinkEase = Ease.InOutSine;

        private Tween fadeTween;
        private Tween blinkTween;
        private Color pressAnyKeyBaseColor = Color.white;
        private bool hasPressAnyKeyBaseColor;

        private void Reset()
        {
            rootCanvasGroup = GetComponent<CanvasGroup>();
            pressAnyKeyGraphic = GetComponentInChildren<Image>();
        }

        private void Awake()
        {
            ResolveReferences();
            CapturePressAnyKeyBaseColor();
        }

        private void OnEnable()
        {
            if (blinkOnEnable)
            {
                StartPressAnyKeyBlink();
            }
        }

        private void OnDisable()
        {
            KillFadeTween();
            StopPressAnyKeyBlink(false);
        }

        public void ShowImmediate(bool startBlinking = true)
        {
            gameObject.SetActive(true);
            ResolveReferences();
            KillFadeTween();

            if (rootCanvasGroup)
            {
                rootCanvasGroup.alpha = 1f;
                rootCanvasGroup.interactable = true;
                rootCanvasGroup.blocksRaycasts = true;
            }

            if (startBlinking)
            {
                StartPressAnyKeyBlink();
            }
            else
            {
                StopPressAnyKeyBlink(true);
            }
        }

        public void HideImmediate()
        {
            ResolveReferences();
            KillFadeTween();
            StopPressAnyKeyBlink(false);

            if (rootCanvasGroup)
            {
                rootCanvasGroup.alpha = 0f;
                rootCanvasGroup.interactable = false;
                rootCanvasGroup.blocksRaycasts = false;
            }
        }

        public Tween FadeOut()
        {
            gameObject.SetActive(true);
            ResolveReferences();
            KillFadeTween();
            StopPressAnyKeyBlink(false);

            if (!rootCanvasGroup)
            {
                return null;
            }

            rootCanvasGroup.interactable = false;
            rootCanvasGroup.blocksRaycasts = false;

            fadeTween = rootCanvasGroup
                .DOFade(0f, fadeOutDuration)
                .SetEase(fadeOutEase)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .OnComplete(() =>
                {
                    fadeTween = null;

                    if (deactivateAfterFadeOut)
                    {
                        gameObject.SetActive(false);
                    }
                });

            return fadeTween;
        }

        public void StartPressAnyKeyBlink()
        {
            ResolveReferences();
            CapturePressAnyKeyBaseColor();
            StopPressAnyKeyBlink(false);

            if (!pressAnyKeyGraphic)
            {
                return;
            }

            SetPressAnyKeyAlpha(blinkMaxAlpha);
            blinkTween = pressAnyKeyGraphic
                .DOFade(blinkMinAlpha, blinkHalfCycleDuration)
                .SetEase(blinkEase)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        public void StopPressAnyKeyBlink(bool restoreMaxAlpha)
        {
            if (blinkTween != null)
            {
                blinkTween.Kill();
                blinkTween = null;
            }

            if (restoreMaxAlpha)
            {
                SetPressAnyKeyAlpha(blinkMaxAlpha);
            }
        }

        private void ResolveReferences()
        {
            if (!rootCanvasGroup)
            {
                rootCanvasGroup = GetComponent<CanvasGroup>();
            }
        }

        private void CapturePressAnyKeyBaseColor()
        {
            if (hasPressAnyKeyBaseColor || !pressAnyKeyGraphic)
            {
                return;
            }

            pressAnyKeyBaseColor = pressAnyKeyGraphic.color;
            hasPressAnyKeyBaseColor = true;
        }

        private void SetPressAnyKeyAlpha(float alpha)
        {
            if (!pressAnyKeyGraphic)
            {
                return;
            }

            Color color = hasPressAnyKeyBaseColor ? pressAnyKeyBaseColor : pressAnyKeyGraphic.color;
            color.a = alpha;
            pressAnyKeyGraphic.color = color;
        }

        private void KillFadeTween()
        {
            if (fadeTween == null)
            {
                return;
            }

            fadeTween.Kill();
            fadeTween = null;
        }
    }
}
