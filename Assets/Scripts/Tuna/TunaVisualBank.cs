using UnityEngine;

namespace TestBoids.Tuna
{
    public sealed class TunaVisualBank : MonoBehaviour
    {
        [SerializeField] private TunaMotor motor;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Vector3 localBankAxis = Vector3.forward;
        [SerializeField] private float maxBankAngle = 20f;
        [SerializeField] private float response = 8f;
        [SerializeField] private float direction = -1f;

        private Quaternion baseLocalRotation;
        private float currentBankAngle;

        private void Reset()
        {
            motor = GetComponentInParent<TunaMotor>();
            visualRoot = transform;
        }

        private void Awake()
        {
            if (!motor)
            {
                motor = GetComponentInParent<TunaMotor>();
            }

            if (!visualRoot)
            {
                visualRoot = transform;
            }

            baseLocalRotation = visualRoot.localRotation;
            if (localBankAxis.sqrMagnitude <= 0.000001f)
            {
                localBankAxis = Vector3.forward;
            }
        }

        private void LateUpdate()
        {
            if (!visualRoot || !motor)
            {
                return;
            }

            float targetBankAngle = motor.CurrentTurnAmount * maxBankAngle * direction;
            currentBankAngle = Mathf.Lerp(currentBankAngle, targetBankAngle, 1f - Mathf.Exp(-response * Time.deltaTime));
            visualRoot.localRotation = baseLocalRotation * Quaternion.AngleAxis(currentBankAngle, localBankAxis.normalized);
        }
    }
}
