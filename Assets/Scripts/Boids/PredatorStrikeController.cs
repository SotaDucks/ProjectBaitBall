using System.Collections.Generic;
using UnityEngine;

namespace TestBoids.Boids
{
    [DisallowMultipleComponent]
    public sealed class PredatorStrikeController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FishSchoolManager schoolManager;
        [SerializeField] private Transform baitBallTarget;
        [SerializeField] private bool autoResolveMissingReferences = true;

        [Header("Eligibility")]
        [SerializeField, Min(0f)] private float minimumDistanceFromBaitBall = 5f;
        [SerializeField, Min(0f)] private float maximumDistanceFromBaitBall = 26f;
        [SerializeField, Min(0)] private int maximumConcurrentStrikes = 2;
        [SerializeField, Min(1)] private int strikesPerPulse = 1;

        [Header("Timing")]
        [SerializeField] private Vector2 initialDelayRange = new(0.5f, 2.5f);
        [SerializeField] private Vector2 pulseIntervalRange = new(2f, 5f);
        [SerializeField] private Vector2 predatorCooldownRange = new(7f, 14f);
        [SerializeField, Min(0f)] private float retryDelay = 0.5f;
        [SerializeField, Range(0f, 1f)] private float strikeChance = 1f;
        [SerializeField, Min(0.01f)] private float trackedStrikeDuration = 1.25f;

        [Header("Strike")]
        [SerializeField, Min(0f)] private float dashSpeed = 18f;
        [SerializeField, Min(0f)] private float baitBallRadius = 5f;
        [SerializeField, Min(0f)] private float exitOvershoot = 4f;
        [SerializeField, Min(0f)] private float lateralAimRadius = 1.25f;
        [SerializeField, Range(0f, 0.5f)] private float currentDirectionBlend = 0.08f;
        [SerializeField] private bool faceStrikeDirectionBeforeLaunch = true;

        private readonly Dictionary<FishAgent, float> predatorCooldownEnds = new();
        private readonly List<ActiveStrike> activeStrikes = new();
        private readonly List<FishAgent> strikeCandidates = new();
        private float nextStrikeTime;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
            ScheduleNextStrike(initialDelayRange);
        }

        private void OnEnable()
        {
            if (nextStrikeTime <= 0f)
            {
                ScheduleNextStrike(initialDelayRange);
            }
        }

        private void OnDisable()
        {
            activeStrikes.Clear();
            predatorCooldownEnds.Clear();
            strikeCandidates.Clear();
        }

        private void OnValidate()
        {
            initialDelayRange = OrderedRange(initialDelayRange);
            pulseIntervalRange = OrderedRange(pulseIntervalRange);
            predatorCooldownRange = OrderedRange(predatorCooldownRange);
            minimumDistanceFromBaitBall = Mathf.Max(0f, minimumDistanceFromBaitBall);
            maximumDistanceFromBaitBall = Mathf.Max(0f, maximumDistanceFromBaitBall);
            maximumConcurrentStrikes = Mathf.Max(0, maximumConcurrentStrikes);
            strikesPerPulse = Mathf.Max(1, strikesPerPulse);
            retryDelay = Mathf.Max(0f, retryDelay);
            trackedStrikeDuration = Mathf.Max(0.01f, trackedStrikeDuration);
            dashSpeed = Mathf.Max(0f, dashSpeed);
            baitBallRadius = Mathf.Max(0f, baitBallRadius);
            exitOvershoot = Mathf.Max(0f, exitOvershoot);
            lateralAimRadius = Mathf.Max(0f, lateralAimRadius);
        }

        private void Update()
        {
            PruneActiveStrikes();

            if (Time.time < nextStrikeTime)
            {
                return;
            }

            ResolveReferences();
            int startedCount = TryStartStrikePulse();
            if (startedCount > 0)
            {
                ScheduleNextStrike(pulseIntervalRange);
                return;
            }

            nextStrikeTime = Time.time + retryDelay;
        }

        private int TryStartStrikePulse()
        {
            if (!schoolManager || !baitBallTarget || dashSpeed <= 0f)
            {
                return 0;
            }

            if (Random.value > strikeChance)
            {
                return 0;
            }

            int availableSlots = maximumConcurrentStrikes > 0
                ? maximumConcurrentStrikes - activeStrikes.Count
                : strikesPerPulse;
            if (availableSlots <= 0)
            {
                return 0;
            }

            int targetCount = Mathf.Min(strikesPerPulse, availableSlots);
            int startedCount = 0;
            for (int i = 0; i < targetCount; i++)
            {
                if (!TryPickCandidate(out FishAgent candidate))
                {
                    break;
                }

                if (TryStartStrike(candidate))
                {
                    startedCount++;
                }
                else
                {
                    predatorCooldownEnds[candidate] = Time.time + retryDelay;
                }
            }

            return startedCount;
        }

        private bool TryPickCandidate(out FishAgent candidate)
        {
            candidate = null;
            strikeCandidates.Clear();

            IReadOnlyList<FishAgent> agents = schoolManager.Agents;
            for (int i = 0; i < agents.Count; i++)
            {
                FishAgent agent = agents[i];
                if (CanAgentStartStrike(agent))
                {
                    strikeCandidates.Add(agent);
                }
            }

            if (strikeCandidates.Count <= 0)
            {
                return false;
            }

            int index = Random.Range(0, strikeCandidates.Count);
            candidate = strikeCandidates[index];
            return candidate;
        }

        private bool CanAgentStartStrike(FishAgent candidate)
        {
            if (!candidate || activeStrikes.Exists(strike => strike.Agent == candidate))
            {
                return false;
            }

            if (predatorCooldownEnds.TryGetValue(candidate, out float cooldownEndTime) && Time.time < cooldownEndTime)
            {
                return false;
            }

            float distance = Vector3.Distance(candidate.transform.position, baitBallTarget.position);
            if (distance < minimumDistanceFromBaitBall)
            {
                return false;
            }

            return maximumDistanceFromBaitBall <= 0f || distance <= maximumDistanceFromBaitBall;
        }

        private bool TryStartStrike(FishAgent candidate)
        {
            Vector3 position = candidate.transform.position;
            Vector3 targetCenter = baitBallTarget.position;
            Vector3 toCenter = targetCenter - position;
            if (toCenter.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            Vector3 strikeVelocity = BuildStrikeVelocity(candidate, position, targetCenter, toCenter.normalized);
            if (strikeVelocity.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            if (faceStrikeDirectionBeforeLaunch)
            {
                candidate.ApplyPose(position, strikeVelocity, 0f);
            }

            if (!schoolManager.TryBeginImpactPhysics(candidate, strikeVelocity, position))
            {
                return false;
            }

            BeginStrikeTracking(candidate);
            return true;
        }

        private Vector3 BuildStrikeVelocity(FishAgent candidate, Vector3 position, Vector3 targetCenter, Vector3 strikeAxis)
        {
            Vector3 lateralOffset = CreateLateralOffset(strikeAxis);
            float exitDistance = Mathf.Max(0.1f, baitBallRadius + exitOvershoot);
            Vector3 exitPoint = targetCenter + lateralOffset + strikeAxis * exitDistance;
            Vector3 pathDirection = exitPoint - position;
            if (pathDirection.sqrMagnitude <= 0.000001f)
            {
                return Vector3.zero;
            }

            Vector3 direction = pathDirection.normalized;
            Vector3 currentDirection = candidate.Velocity.sqrMagnitude > 0.000001f
                ? candidate.Velocity.normalized
                : candidate.transform.forward;

            if (currentDirectionBlend > 0f && currentDirection.sqrMagnitude > 0.000001f)
            {
                direction = Vector3.Slerp(direction, currentDirection.normalized, currentDirectionBlend).normalized;
            }

            return direction * dashSpeed;
        }

        private Vector3 CreateLateralOffset(Vector3 strikeAxis)
        {
            if (lateralAimRadius <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 basisA = Vector3.Cross(strikeAxis, Vector3.up);
            if (basisA.sqrMagnitude <= 0.000001f)
            {
                basisA = Vector3.Cross(strikeAxis, Vector3.right);
            }

            basisA.Normalize();
            Vector3 basisB = Vector3.Cross(strikeAxis, basisA).normalized;
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Random.value) * lateralAimRadius;
            return (basisA * Mathf.Cos(angle) + basisB * Mathf.Sin(angle)) * radius;
        }

        private void BeginStrikeTracking(FishAgent candidate)
        {
            activeStrikes.Add(new ActiveStrike
            {
                Agent = candidate,
                EndTime = Time.time + trackedStrikeDuration
            });
            predatorCooldownEnds[candidate] = Time.time + Random.Range(predatorCooldownRange.x, predatorCooldownRange.y);
        }

        private void PruneActiveStrikes()
        {
            for (int i = activeStrikes.Count - 1; i >= 0; i--)
            {
                ActiveStrike strike = activeStrikes[i];
                if (!strike.Agent || Time.time >= strike.EndTime)
                {
                    activeStrikes.RemoveAt(i);
                }
            }
        }

        private void ScheduleNextStrike(Vector2 range)
        {
            nextStrikeTime = Time.time + Random.Range(range.x, range.y);
        }

        private void ResolveReferences()
        {
            if (!autoResolveMissingReferences)
            {
                return;
            }

            if (!schoolManager)
            {
                schoolManager = GetComponent<FishSchoolManager>();
            }

            if (!schoolManager)
            {
                schoolManager = GetComponentInParent<FishSchoolManager>();
            }

            if (!baitBallTarget)
            {
                BaitBallFormationController baitBall = FindFirstObjectByType<BaitBallFormationController>(FindObjectsInactive.Exclude);
                if (baitBall)
                {
                    baitBallTarget = baitBall.transform;
                }
            }
        }

        private static Vector2 OrderedRange(Vector2 range)
        {
            return range.x <= range.y ? range : new Vector2(range.y, range.x);
        }

        private struct ActiveStrike
        {
            public FishAgent Agent;
            public float EndTime;
        }
    }
}
