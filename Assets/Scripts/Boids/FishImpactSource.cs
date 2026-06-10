using System.Collections.Generic;
using UnityEngine;

namespace TestBoids.Boids
{
    [DisallowMultipleComponent]
    public sealed class FishImpactSource : MonoBehaviour
    {
        [SerializeField] private FishSchoolManager schoolManager;
        [SerializeField] private bool autoFindSchoolManager = true;
        [SerializeField] private Rigidbody sourceRigidbody;
        [SerializeField] private LayerMask fishLayerMask = ~0;
        [SerializeField] private bool reactToCollisions = true;
        [SerializeField] private bool reactToTriggers = true;

        [Header("Impulse")]
        [SerializeField, Min(0f)] private float fallbackImpactSpeed = 6f;
        [SerializeField, Min(0f)] private float sourceVelocityScale = 1f;
        [SerializeField, Min(0f)] private float minimumImpactSpeed = 2f;
        [SerializeField, Min(0f)] private float maximumImpactSpeed = 16f;
        [SerializeField, Min(0f)] private float repeatHitCooldown = 0.12f;

        private readonly Dictionary<FishAgent, float> lastImpactTimes = new();

        private void Reset()
        {
            sourceRigidbody = GetComponentInParent<Rigidbody>();
        }

        private void Awake()
        {
            if (!sourceRigidbody)
            {
                sourceRigidbody = GetComponentInParent<Rigidbody>();
            }
        }

        private void OnValidate()
        {
            fallbackImpactSpeed = Mathf.Max(0f, fallbackImpactSpeed);
            sourceVelocityScale = Mathf.Max(0f, sourceVelocityScale);
            minimumImpactSpeed = Mathf.Max(0f, minimumImpactSpeed);
            maximumImpactSpeed = Mathf.Max(0f, maximumImpactSpeed);
            repeatHitCooldown = Mathf.Max(0f, repeatHitCooldown);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!reactToCollisions || collision == null)
            {
                return;
            }

            Vector3 contactPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : collision.collider.ClosestPoint(transform.position);
            TryImpact(collision.collider, contactPoint);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!reactToTriggers || !other)
            {
                return;
            }

            TryImpact(other, other.ClosestPoint(transform.position));
        }

        private void TryImpact(Collider other, Vector3 contactPoint)
        {
            if (!other || !IsLayerAllowed(other.gameObject.layer))
            {
                return;
            }

            FishAgent agent = other.GetComponentInParent<FishAgent>();
            if (!agent || IsOnCooldown(agent))
            {
                return;
            }

            FishSchoolManager manager = ResolveSchoolManager(agent);
            if (!manager)
            {
                return;
            }

            Vector3 impactVelocity = BuildImpactVelocity(agent, contactPoint);
            if (manager.TryBeginImpactPhysics(agent, impactVelocity, contactPoint))
            {
                lastImpactTimes[agent] = Time.time;
            }
        }

        private bool IsLayerAllowed(int layer)
        {
            return (fishLayerMask.value & (1 << layer)) != 0;
        }

        private bool IsOnCooldown(FishAgent agent)
        {
            if (repeatHitCooldown <= 0f || !lastImpactTimes.TryGetValue(agent, out float lastImpactTime))
            {
                return false;
            }

            return Time.time - lastImpactTime < repeatHitCooldown;
        }

        private FishSchoolManager ResolveSchoolManager(FishAgent agent)
        {
            if (schoolManager)
            {
                return schoolManager;
            }

            FishSchoolManager manager = agent.GetComponentInParent<FishSchoolManager>();
            if (!manager && autoFindSchoolManager)
            {
                manager = FindFirstObjectByType<FishSchoolManager>(FindObjectsInactive.Exclude);
            }

            return manager;
        }

        private Vector3 BuildImpactVelocity(FishAgent agent, Vector3 contactPoint)
        {
            Vector3 sourceVelocity = sourceRigidbody ? sourceRigidbody.linearVelocity : Vector3.zero;
            Vector3 direction = sourceVelocity.sqrMagnitude > 0.000001f
                ? sourceVelocity.normalized
                : agent.transform.position - transform.position;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                direction = agent.transform.position - contactPoint;
            }

            if (direction.sqrMagnitude <= 0.000001f)
            {
                direction = transform.forward.sqrMagnitude > 0.000001f ? transform.forward : Vector3.forward;
            }

            float speed = sourceVelocity.sqrMagnitude > 0.000001f
                ? sourceVelocity.magnitude * sourceVelocityScale
                : fallbackImpactSpeed;
            speed = Mathf.Max(speed, minimumImpactSpeed);
            if (maximumImpactSpeed > 0f)
            {
                speed = Mathf.Min(speed, maximumImpactSpeed);
            }

            return direction.normalized * speed;
        }
    }
}
