using UnityEngine;

namespace TestBoids.Tuna
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class TunaSway : MonoBehaviour
    {
        public enum Axis
        {
            X,
            Y,
            Z
        }

        public enum SpaceMode
        {
            Local,
            World
        }

        [System.Serializable]
        public struct SwaySettings
        {
            [Min(0f)] public float amplitude;
            [Min(0f)] public float frequency;
        }

        [Header("Target Bone")]
        [SerializeField] private Transform targetBone;
        [SerializeField] private Axis rotateAxis = Axis.Y;
        [SerializeField] private SpaceMode spaceMode = SpaceMode.Local;

        [Header("Speed Source")]
        [SerializeField] private Rigidbody targetRigidbody;
        [SerializeField] private bool autoFindRigidbody = true;

        [Header("Speed Bands")]
        [SerializeField, Min(0f)] private float lowSpeedMax = 2f;
        [SerializeField, Min(0f)] private float mediumSpeedMax = 6f;
        [SerializeField] private SwaySettings lowSpeed = new() { amplitude = 4f, frequency = 1.2f };
        [SerializeField] private SwaySettings mediumSpeed = new() { amplitude = 9f, frequency = 2.2f };
        [SerializeField] private SwaySettings highSpeed = new() { amplitude = 16f, frequency = 3.6f };

        [Header("Blending")]
        [SerializeField, Min(0f)] private float response = 8f;
        [SerializeField, Min(0f)] private float globalIntensity = 1f;
        [SerializeField] private bool pauseWhenDisabled = true;

        private float currentAmplitude;
        private float currentFrequency;
        private float phase;
        private Quaternion lastOffset = Quaternion.identity;

        private void Reset()
        {
            targetBone = transform;
            targetRigidbody = GetComponentInParent<Rigidbody>();
        }

        private void Awake()
        {
            if (!targetBone)
            {
                targetBone = transform;
            }

            if (autoFindRigidbody && !targetRigidbody)
            {
                targetRigidbody = GetComponentInParent<Rigidbody>();
            }

            NormalizeSpeedBands();
            SelectSettings(GetSpeed(), out currentAmplitude, out currentFrequency);
        }

        private void OnDisable()
        {
            if (!pauseWhenDisabled || !targetBone)
            {
                return;
            }

            RemoveLastOffset();
            lastOffset = Quaternion.identity;
        }

        private void LateUpdate()
        {
            if (!targetBone)
            {
                return;
            }

            SelectSettings(GetSpeed(), out float targetAmplitude, out float targetFrequency);
            float blend = 1f - Mathf.Exp(-response * Time.deltaTime);
            currentAmplitude = Mathf.Lerp(currentAmplitude, targetAmplitude, blend);
            currentFrequency = Mathf.Lerp(currentFrequency, targetFrequency, blend);

            phase += Time.deltaTime * Mathf.Max(0f, currentFrequency);
            if (phase > 1000f)
            {
                phase -= 1000f;
            }

            float angle = Mathf.Sin(phase * Mathf.PI * 2f)
                * Mathf.Max(0f, currentAmplitude)
                * Mathf.Max(0f, globalIntensity);
            Quaternion currentOffset = Quaternion.AngleAxis(angle, AxisToVector(rotateAxis));

            ApplyOffset(currentOffset);
            lastOffset = currentOffset;
        }

        private float GetSpeed()
        {
            return targetRigidbody ? targetRigidbody.linearVelocity.magnitude : 0f;
        }

        private void SelectSettings(float speed, out float amplitude, out float frequency)
        {
            SwaySettings settings;
            if (speed <= lowSpeedMax)
            {
                settings = lowSpeed;
            }
            else if (speed <= mediumSpeedMax)
            {
                settings = mediumSpeed;
            }
            else
            {
                settings = highSpeed;
            }

            amplitude = Mathf.Max(0f, settings.amplitude);
            frequency = Mathf.Max(0f, settings.frequency);
        }

        private void ApplyOffset(Quaternion currentOffset)
        {
            if (spaceMode == SpaceMode.Local)
            {
                Quaternion baseLocal = targetBone.localRotation;
                Quaternion withoutLastOffset = baseLocal * Quaternion.Inverse(lastOffset);
                targetBone.localRotation = withoutLastOffset * currentOffset;
                return;
            }

            Quaternion baseWorld = targetBone.rotation;
            Quaternion withoutLastWorldOffset = Quaternion.Inverse(lastOffset) * baseWorld;
            targetBone.rotation = currentOffset * withoutLastWorldOffset;
        }

        private void RemoveLastOffset()
        {
            if (spaceMode == SpaceMode.Local)
            {
                targetBone.localRotation *= Quaternion.Inverse(lastOffset);
                return;
            }

            targetBone.rotation = Quaternion.Inverse(lastOffset) * targetBone.rotation;
        }

        private void OnValidate()
        {
            NormalizeSpeedBands();
            globalIntensity = Mathf.Max(0f, globalIntensity);
            response = Mathf.Max(0f, response);
            lowSpeed = NormalizeSettings(lowSpeed);
            mediumSpeed = NormalizeSettings(mediumSpeed);
            highSpeed = NormalizeSettings(highSpeed);
        }

        private void NormalizeSpeedBands()
        {
            lowSpeedMax = Mathf.Max(0f, lowSpeedMax);
            mediumSpeedMax = Mathf.Max(lowSpeedMax, mediumSpeedMax);
        }

        private static SwaySettings NormalizeSettings(SwaySettings settings)
        {
            settings.amplitude = Mathf.Max(0f, settings.amplitude);
            settings.frequency = Mathf.Max(0f, settings.frequency);
            return settings;
        }

        private static Vector3 AxisToVector(Axis axis)
        {
            return axis switch
            {
                Axis.X => Vector3.right,
                Axis.Y => Vector3.up,
                Axis.Z => Vector3.forward,
                _ => Vector3.up
            };
        }
    }
}
