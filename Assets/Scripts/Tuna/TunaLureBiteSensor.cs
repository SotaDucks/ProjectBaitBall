using TestBoids.Gameplay;
using TestBoids.Gameplay.Lure;
using UnityEngine;

namespace TestBoids.Tuna
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TunaLureHookConnector))]
    public sealed class TunaLureBiteSensor : MonoBehaviour
    {
        private const string DefaultMouthName = "DangerSphere";

        [Header("References")]
        [SerializeField] private Transform mouth;
        [SerializeField] private TunaLureHookConnector hookConnector;
        [SerializeField] private GameplayEventBus eventBus;
        [SerializeField] private bool autoResolveReferences = true;

        [Header("Bite Area")]
        [SerializeField, Min(0f)] private float biteRadius = 0.75f;
        [SerializeField, Range(0f, 360f)] private float biteAngleDegrees = 90f;

        private bool hasBittenLure;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (hasBittenLure)
            {
                return;
            }

            ResolveReferences();
            if (!eventBus || biteRadius <= 0f)
            {
                return;
            }

            AutomaticLureMotor[] lures = FindObjectsByType<AutomaticLureMotor>(FindObjectsSortMode.None);
            for (int i = 0; i < lures.Length; i++)
            {
                AutomaticLureMotor lure = lures[i];
                if (!lure || !IsInsideBiteArea(lure.transform.position))
                {
                    continue;
                }

                Vector3 lureForward = lure.transform.forward;
                if (!hookConnector || !hookConnector.TryConnectLure(lure))
                {
                    continue;
                }

                hasBittenLure = true;
                eventBus.RaiseLureBitten(new LureBittenEvent(transform, lure.transform, lureForward));
                return;
            }
        }

        private bool IsInsideBiteArea(Vector3 worldPosition)
        {
            Transform mouthTransform = mouth ? mouth : transform;
            Vector3 offset = worldPosition - mouthTransform.position;
            float distanceSq = offset.sqrMagnitude;
            if (distanceSq > biteRadius * biteRadius)
            {
                return false;
            }

            float halfAngle = Mathf.Clamp(biteAngleDegrees, 0f, 360f) * 0.5f;
            if (halfAngle >= 180f || distanceSq <= 0.000001f)
            {
                return true;
            }

            float forwardDot = Vector3.Dot(transform.forward, offset.normalized);
            return forwardDot >= Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        }

        private void ResolveReferences()
        {
            if (!autoResolveReferences)
            {
                return;
            }

            if (!mouth)
            {
                mouth = FindChildByName(transform, DefaultMouthName);
            }

            if (!hookConnector)
            {
                hookConnector = GetComponent<TunaLureHookConnector>();
            }

            if (!eventBus)
            {
                eventBus = GameplayEventBus.Instance;
            }

            if (!eventBus)
            {
                eventBus = FindFirstObjectByType<GameplayEventBus>(FindObjectsInactive.Include);
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
            Gizmos.color = new Color(0.95f, 0.2f, 0.15f, 0.35f);
            Gizmos.DrawWireSphere(origin, biteRadius);

            Vector3 forward = transform.forward;
            float halfAngle = Mathf.Clamp(biteAngleDegrees, 0f, 360f) * 0.5f;
            Quaternion left = Quaternion.AngleAxis(-halfAngle, transform.up);
            Quaternion right = Quaternion.AngleAxis(halfAngle, transform.up);
            Gizmos.DrawLine(origin, origin + left * forward * biteRadius);
            Gizmos.DrawLine(origin, origin + right * forward * biteRadius);
        }
    }
}
