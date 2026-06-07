using TestBoids.Boids;
using UnityEngine;

namespace TestBoids.Tuna
{
    [DisallowMultipleComponent]
    public sealed class TunaPredationSensor : MonoBehaviour
    {
        private const string DefaultMouthName = "DangerSphere";

        [Header("References")]
        [SerializeField] private TunaMotor tunaMotor;
        [SerializeField] private Rigidbody tunaBody;
        [SerializeField] private Transform mouth;
        [SerializeField] private InstancedFishSchoolManager baitBallManager;
        [SerializeField] private bool autoResolveReferences = true;

        [Header("Eating")]
        [SerializeField, Min(0f)] private float eatRadius = 0.75f;
        [SerializeField, Range(0f, 360f)] private float eatAngleDegrees = 90f;
        [SerializeField, Min(0f)] private float minEatSpeed = 2.5f;
        [SerializeField, Min(0f)] private float eatCooldown = 0.12f;
        [SerializeField, Min(0f)] private float hungerPerFish = 8f;
        [SerializeField] private bool stopEatingWhenFull = true;

        [Header("Feedback")]
        [SerializeField] private ParticleSystem eatEffectPrefab;
        [SerializeField] private AudioSource eatAudioSource;

        private float nextEatTime;

        private void Reset()
        {
            ResolveLocalReferences();
            ResolveBaitBallManager();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void FixedUpdate()
        {
            if (Time.time < nextEatTime)
            {
                return;
            }

            ResolveReferences();
            if (!CanTryEat())
            {
                return;
            }

            Vector3 mouthPosition = mouth ? mouth.position : transform.position;
            Vector3 mouthForward = transform.forward;
            if (!baitBallManager.TryConsumeFish(
                    mouthPosition,
                    mouthForward,
                    eatRadius,
                    eatAngleDegrees,
                    out Vector3 eatenPosition))
            {
                return;
            }

            tunaMotor.AddHunger(hungerPerFish);
            PlayEatFeedback(eatenPosition);
            nextEatTime = Time.time + eatCooldown;
        }

        private bool CanTryEat()
        {
            if (!tunaMotor || !baitBallManager)
            {
                return false;
            }

            if (stopEatingWhenFull && tunaMotor.CurrentHunger >= tunaMotor.MaxHunger)
            {
                return false;
            }

            if (minEatSpeed <= 0f || !tunaBody)
            {
                return true;
            }

            return tunaBody.linearVelocity.sqrMagnitude >= minEatSpeed * minEatSpeed;
        }

        private void PlayEatFeedback(Vector3 eatenPosition)
        {
            if (eatEffectPrefab)
            {
                ParticleSystem effect = Instantiate(eatEffectPrefab, eatenPosition, Quaternion.identity);
                ParticleSystem.MainModule main = effect.main;
                Destroy(effect.gameObject, main.duration + main.startLifetime.constantMax);
            }

            if (eatAudioSource)
            {
                eatAudioSource.Play();
            }
        }

        private void ResolveReferences()
        {
            if (!autoResolveReferences)
            {
                return;
            }

            ResolveLocalReferences();
            ResolveBaitBallManager();
        }

        private void ResolveLocalReferences()
        {
            if (!tunaMotor)
            {
                tunaMotor = GetComponent<TunaMotor>();
            }

            if (!tunaBody)
            {
                tunaBody = GetComponent<Rigidbody>();
            }

            if (!mouth)
            {
                mouth = FindChildByName(transform, DefaultMouthName);
            }
        }

        private void ResolveBaitBallManager()
        {
            if (!baitBallManager)
            {
                baitBallManager = FindFirstObjectByType<InstancedFishSchoolManager>(FindObjectsInactive.Include);
            }
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (!root)
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }

                Transform nested = FindChildByName(child, childName);
                if (nested)
                {
                    return nested;
                }
            }

            return null;
        }

        private void OnDrawGizmosSelected()
        {
            Transform mouthTransform = mouth ? mouth : FindChildByName(transform, DefaultMouthName);
            Vector3 origin = mouthTransform ? mouthTransform.position : transform.position;
            Gizmos.color = new Color(1f, 0.72f, 0.18f, 0.35f);
            Gizmos.DrawWireSphere(origin, eatRadius);

            Vector3 forward = transform.forward;
            float halfAngle = Mathf.Clamp(eatAngleDegrees, 0f, 360f) * 0.5f;
            Quaternion left = Quaternion.AngleAxis(-halfAngle, transform.up);
            Quaternion right = Quaternion.AngleAxis(halfAngle, transform.up);
            Gizmos.DrawLine(origin, origin + left * forward * eatRadius);
            Gizmos.DrawLine(origin, origin + right * forward * eatRadius);
        }
    }
}
