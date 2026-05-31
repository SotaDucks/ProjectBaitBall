using UnityEngine;
using UnityEngine.UI;

namespace TestBoids.Gameplay.UI
{
    [DisallowMultipleComponent]
    public sealed class FishStaminaRingView : MonoBehaviour
    {
        [SerializeField] private Image fillImage;

        private void Reset()
        {
            fillImage = GetComponentInChildren<Image>();
        }

        private void Awake()
        {
            if (!fillImage)
            {
                fillImage = GetComponentInChildren<Image>();
            }
        }

        public void SetProgress(float percent)
        {
            if (fillImage)
            {
                fillImage.fillAmount = Mathf.Clamp01(percent);
            }
        }
    }
}
