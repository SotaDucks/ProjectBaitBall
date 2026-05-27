using System.Collections.Generic;
using UnityEngine;

namespace TestBoids.Boids
{
    public sealed class FishSchoolManager : MonoBehaviour
    {
        private static readonly Vector3 DefaultFlowAxis = Vector3.up;
        private const float MinCachedClearDirectionDot = 0.5f;

        [Header("School")]
        [SerializeField] private FishAgent fishPrefab;
        [SerializeField, Min(0)] private int fishCount = 72;
        [SerializeField] private Transform fishParent;
        [SerializeField] private bool spawnOnStart = true;
        [SerializeField] private bool useExistingChildAgents;
        [SerializeField] private int seed = 42;

        [Header("Bait Ball")]
        [SerializeField] private float baitBallRadius = 3.3f;
        [SerializeField] private float baitBallCoreRatio = 0.34f;
        [SerializeField] private float centeringWeight = 1.4f;
        [SerializeField] private float toroidalFlowWeight = 1.9f;
        [SerializeField] private float toroidalRollWeight = 0.38f;
        [SerializeField] private float toroidalAxisSpeed = 0.42f;

        [Header("Boids")]
        [SerializeField] private float minSpeed = 2.8f;
        [SerializeField] private float maxSpeed = 7f;
        [SerializeField] private float maxTurnRate = 18f;
        [SerializeField] private float perceptionRadius = 4.2f;
        [SerializeField] private float separationRadius = 1.25f;
        [SerializeField] private float maxSteerForce = 4.2f;
        [SerializeField] private float alignWeight = 0.5f;
        [SerializeField] private float cohesionWeight = 0.9f;
        [SerializeField] private float separateWeight = 3f;

        [Header("Obstacle Avoidance")]
        [SerializeField] private float boundsRadius = 0.27f;
        [SerializeField] private float avoidCollisionWeight = 4f;
        [SerializeField] private float collisionAvoidDistance = 7f;
        [SerializeField] private float sphereSeparationMargin = 4f;
        [SerializeField] private float sphereSeparationWeight = 20f;
        [SerializeField, Min(8)] private int obstacleRayCount = 300;
        [SerializeField] private bool autoCollectObstacles = true;
        [SerializeField] private BoidObstacle[] obstacles = new BoidObstacle[0];

        [Header("Pose")]
        [SerializeField] private float maxBankAngleDegrees = 12f;
        [SerializeField] private float bankTurnScale = 0.18f;
        [SerializeField] private float bankResponse = 8f;

        private readonly List<FishAgent> agents = new();
        private readonly List<BoidObstacle> activeObstacles = new();
        private FishState[] fish = new FishState[0];
        private Vector3[] nextVelocities = new Vector3[0];
        private Vector3[] nextPositions = new Vector3[0];
        private Vector3[] rayDirections = new Vector3[0];
        private Vector3 flowAxis = DefaultFlowAxis;
        private float elapsedTime;

        public IReadOnlyList<FishAgent> Agents => agents;

        [ContextMenu("Apply Three.js Bait Ball Defaults")]
        private void ApplyBaitBallDefaults()
        {
            fishCount = 72;
            seed = 42;
            baitBallRadius = 3.3f;
            baitBallCoreRatio = 0.34f;
            centeringWeight = 1.4f;
            toroidalFlowWeight = 1.9f;
            toroidalRollWeight = 0.38f;
            toroidalAxisSpeed = 0.42f;
            minSpeed = 2.8f;
            maxSpeed = 7f;
            maxTurnRate = 18f;
            perceptionRadius = 4.2f;
            separationRadius = 1.25f;
            maxSteerForce = 4.2f;
            alignWeight = 0.5f;
            cohesionWeight = 0.9f;
            separateWeight = 3f;
            boundsRadius = 0.27f;
            avoidCollisionWeight = 4f;
            collisionAvoidDistance = 7f;
            sphereSeparationMargin = 4f;
            sphereSeparationWeight = 20f;
            obstacleRayCount = 300;
            maxBankAngleDegrees = 12f;
            bankTurnScale = 0.18f;
            bankResponse = 8f;
        }

        private void Start()
        {
            if (spawnOnStart)
            {
                ResetSchool(fishCount, seed);
            }
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f || fish.Length == 0)
            {
                return;
            }

            UpdateSimulation(dt);
            ApplyAgents();
        }

        public void ResetSchool(int count)
        {
            ResetSchool(count, seed);
        }

        public void ResetSchool(int count, int randomSeed)
        {
            count = Mathf.Max(0, count);
            EnsureRayDirections();
            RefreshObstacles();
            ClearSpawnedAgents();

            if (useExistingChildAgents)
            {
                agents.AddRange(GetComponentsInChildren<FishAgent>());
                count = agents.Count;
            }
            else
            {
                SpawnAgents(count);
                count = agents.Count;
            }

            fish = new FishState[count];
            nextVelocities = new Vector3[count];
            nextPositions = new Vector3[count];
            elapsedTime = 0f;

            BoidRandom random = new((uint)randomSeed);
            for (int i = 0; i < count; i++)
            {
                Vector3 position = CreateInitialPosition(ref random);
                Vector3 direction = CreateInitialDirection(position, ref random);
                float speed = Mathf.Lerp(minSpeed, maxSpeed, random.Next01());
                fish[i] = new FishState(position, direction * speed);
                agents[i].Initialize(position, fish[i].Velocity, fish[i].Bank);
            }
        }

        public void SetCount(int count)
        {
            ResetSchool(count, seed);
        }

        private void SpawnAgents(int count)
        {
            if (!fishPrefab)
            {
                Debug.LogWarning($"{nameof(FishSchoolManager)} needs a fish prefab before it can spawn a school.", this);
                return;
            }

            Transform parent = fishParent ? fishParent : transform;
            for (int i = 0; i < count; i++)
            {
                FishAgent agent = Instantiate(fishPrefab, parent);
                agent.name = $"{fishPrefab.name} {i:000}";
                agents.Add(agent);
            }
        }

        private void ClearSpawnedAgents()
        {
            if (!useExistingChildAgents)
            {
                for (int i = agents.Count - 1; i >= 0; i--)
                {
                    if (agents[i])
                    {
                        Destroy(agents[i].gameObject);
                    }
                }
            }

            agents.Clear();
        }

        private void RefreshObstacles()
        {
            activeObstacles.Clear();
            if (obstacles != null)
            {
                foreach (BoidObstacle obstacle in obstacles)
                {
                    if (obstacle && obstacle.isActiveAndEnabled)
                    {
                        activeObstacles.Add(obstacle);
                    }
                }
            }

            if (!autoCollectObstacles)
            {
                return;
            }

            BoidObstacle[] found = FindObjectsByType<BoidObstacle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (BoidObstacle obstacle in found)
            {
                if (obstacle && obstacle.isActiveAndEnabled && !activeObstacles.Contains(obstacle))
                {
                    activeObstacles.Add(obstacle);
                }
            }
        }

        private void UpdateSimulation(float dt)
        {
            float perceptionRadiusSq = perceptionRadius * perceptionRadius;
            float separationRadiusSq = separationRadius * separationRadius;
            Vector3 currentFlowAxis = ReadFlowAxis(elapsedTime);
            float flowPhase = elapsedTime * toroidalAxisSpeed * 3.7f;

            for (int i = 0; i < fish.Length; i++)
            {
                FishState current = fish[i];
                Vector3 acceleration = Vector3.zero;
                Vector3 headingSum = Vector3.zero;
                Vector3 centerSum = Vector3.zero;
                Vector3 avoidanceSum = Vector3.zero;
                int neighborCount = 0;

                for (int j = 0; j < fish.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    FishState other = fish[j];
                    Vector3 offset = other.Position - current.Position;
                    float distanceSq = offset.sqrMagnitude;

                    if (distanceSq < perceptionRadiusSq)
                    {
                        neighborCount++;
                        headingSum += SafeNormalize(other.Velocity);
                        centerSum += other.Position;

                        if (distanceSq < separationRadiusSq)
                        {
                            float distance = Mathf.Sqrt(Mathf.Max(distanceSq, 0.0001f));
                            avoidanceSum += offset * (-1f / distance);
                        }
                    }
                }

                if (neighborCount > 0)
                {
                    centerSum /= neighborCount;
                    acceleration += SteerTowards(headingSum, current.Velocity) * alignWeight;
                    acceleration += SteerTowards(centerSum - current.Position, current.Velocity) * cohesionWeight;
                    acceleration += SteerTowards(avoidanceSum, current.Velocity) * separateWeight;
                }

                acceleration += SphericalEnvelopeForce(current.Position, current.Velocity) * centeringWeight;
                acceleration += ToroidalFlowForce(current.Position, current.Velocity, currentFlowAxis, flowPhase) * toroidalFlowWeight;
                acceleration += SphereObstacleSeparationForce(current.Position, current.Velocity);

                Vector3 forward = SafeNormalize(current.Velocity);
                if (IsHeadingForCollision(current.Position, forward))
                {
                    Vector3 clearDirection = ObstacleRays(current.Position, forward, ref current);
                    acceleration += SteerTowards(clearDirection, current.Velocity) * avoidCollisionWeight;
                }
                else
                {
                    current.HasCollisionAvoidanceDirection = false;
                }

                Vector3 desiredVelocity = current.Velocity + acceleration * dt;
                float speed = Mathf.Clamp(desiredVelocity.magnitude, minSpeed, maxSpeed);
                desiredVelocity = SafeNormalize(desiredVelocity) * speed;
                Vector3 velocity = LimitTurn(current.Velocity, desiredVelocity, dt);

                fish[i] = current;
                nextVelocities[i] = velocity;
                nextPositions[i] = current.Position + velocity * dt;
            }

            for (int i = 0; i < fish.Length; i++)
            {
                UpdateMotionState(ref fish[i], nextVelocities[i], dt);
                fish[i].Velocity = nextVelocities[i];
                fish[i].Position = nextPositions[i];
            }

            elapsedTime += dt;
        }

        private void ApplyAgents()
        {
            int count = Mathf.Min(agents.Count, fish.Length);
            for (int i = 0; i < count; i++)
            {
                if (agents[i])
                {
                    agents[i].ApplyPose(fish[i].Position, fish[i].Velocity, fish[i].Bank);
                }
            }
        }

        private Vector3 SteerTowards(Vector3 vector, Vector3 velocity)
        {
            if (vector.sqrMagnitude < 0.000001f)
            {
                return Vector3.zero;
            }

            Vector3 desired = vector.normalized * maxSpeed;
            return Vector3.ClampMagnitude(desired - velocity, maxSteerForce);
        }

        private Vector3 LimitTurn(Vector3 currentVelocity, Vector3 desiredVelocity, float dt)
        {
            Vector3 currentDirection = SafeNormalize(currentVelocity);
            Vector3 desiredDirection = SafeNormalize(desiredVelocity);
            float angle = Vector3.Angle(currentDirection, desiredDirection) * Mathf.Deg2Rad;
            float maxAngle = maxTurnRate * dt;

            if (angle <= maxAngle || angle < 0.000001f)
            {
                return desiredVelocity;
            }

            float t = maxAngle / angle;
            Vector3 direction = Vector3.Slerp(currentDirection, desiredDirection, t).normalized;
            return direction * desiredVelocity.magnitude;
        }

        private bool IsHeadingForCollision(Vector3 position, Vector3 forward)
        {
            return activeObstacles.Count > 0 && RayHitsObstacle(position, forward, collisionAvoidDistance);
        }

        private Vector3 ObstacleRays(Vector3 position, Vector3 forward, ref FishState state)
        {
            if (state.HasCollisionAvoidanceDirection
                && Vector3.Dot(state.CollisionAvoidanceDirection, forward) > MinCachedClearDirectionDot
                && IsDirectionClear(position, state.CollisionAvoidanceDirection, collisionAvoidDistance))
            {
                return state.CollisionAvoidanceDirection;
            }

            Vector3 result = FindClearObstacleDirection(position, forward);
            state.CollisionAvoidanceDirection = result;
            state.HasCollisionAvoidanceDirection = true;
            return result;
        }

        private Vector3 FindClearObstacleDirection(Vector3 position, Vector3 forward)
        {
            Vector3 forwardDirection = SafeNormalize(forward);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, forwardDirection);
            for (int i = 0; i < rayDirections.Length; i++)
            {
                Vector3 direction = (rotation * rayDirections[i]).normalized;

                if (IsDirectionClear(position, direction, collisionAvoidDistance))
                {
                    return direction;
                }
            }

            return forwardDirection;
        }

        private bool IsDirectionClear(Vector3 origin, Vector3 direction, float maxDistance)
        {
            return activeObstacles.Count == 0 || !RayHitsObstacle(origin, direction, maxDistance);
        }

        private bool RayHitsObstacle(Vector3 origin, Vector3 direction, float maxDistance)
        {
            return float.IsFinite(RayObstacleHitDistance(origin, direction, maxDistance));
        }

        private float RayObstacleHitDistance(Vector3 origin, Vector3 direction, float maxDistance)
        {
            for (int i = 0; i < activeObstacles.Count; i++)
            {
                BoidObstacle obstacle = activeObstacles[i];
                if (!obstacle)
                {
                    continue;
                }

                if (obstacle.Shape == BoidObstacleShape.Box || obstacle.Shape == BoidObstacleShape.Plate)
                {
                    float distance = RayBoxObstacleHitDistance(origin, direction, maxDistance, obstacle);
                    if (distance <= maxDistance) return distance;
                }
                else
                {
                    float distance = RaySphereObstacleHitDistance(origin, direction, maxDistance, obstacle);
                    if (distance <= maxDistance) return distance;
                }
            }

            return float.PositiveInfinity;
        }

        private float RaySphereObstacleHitDistance(Vector3 origin, Vector3 direction, float maxDistance, BoidObstacle obstacle)
        {
            float radius = obstacle.Radius + boundsRadius;
            Vector3 offset = origin - obstacle.Position;
            float b = Vector3.Dot(offset, direction);
            float c = offset.sqrMagnitude - radius * radius;
            float discriminant = b * b - c;

            if (discriminant < 0f)
            {
                return float.PositiveInfinity;
            }

            float root = Mathf.Sqrt(discriminant);
            float near = -b - root;
            float far = -b + root;
            if (near >= 0f && near <= maxDistance)
            {
                return near;
            }

            return far >= 0f && far <= maxDistance ? far : float.PositiveInfinity;
        }

        private float RayBoxObstacleHitDistance(Vector3 origin, Vector3 direction, float maxDistance, BoidObstacle obstacle)
        {
            Quaternion inverseRotation = Quaternion.Inverse(obstacle.Rotation);
            Vector3 localOrigin = inverseRotation * (origin - obstacle.Position);
            Vector3 localDirection = inverseRotation * direction;
            Vector3 halfSize = obstacle.Size * 0.5f + Vector3.one * boundsRadius;

            return RayExpandedBoxHitDistance(localOrigin, localDirection, halfSize, maxDistance);
        }

        private static float RayExpandedBoxHitDistance(Vector3 origin, Vector3 direction, Vector3 halfSize, float maxDistance)
        {
            float near = 0f;
            float far = maxDistance;

            if (!ClipSlab(origin.x, direction.x, halfSize.x, ref near, ref far)) return float.PositiveInfinity;
            if (!ClipSlab(origin.y, direction.y, halfSize.y, ref near, ref far)) return float.PositiveInfinity;
            if (!ClipSlab(origin.z, direction.z, halfSize.z, ref near, ref far)) return float.PositiveInfinity;

            return far >= 0f && near <= maxDistance ? near : float.PositiveInfinity;
        }

        private static bool ClipSlab(float origin, float direction, float halfSize, ref float near, ref float far)
        {
            if (Mathf.Abs(direction) < 0.000001f)
            {
                return origin >= -halfSize && origin <= halfSize;
            }

            float inverseDirection = 1f / direction;
            float axisNear = (-halfSize - origin) * inverseDirection;
            float axisFar = (halfSize - origin) * inverseDirection;
            if (axisNear > axisFar)
            {
                (axisNear, axisFar) = (axisFar, axisNear);
            }

            near = Mathf.Max(near, axisNear);
            far = Mathf.Min(far, axisFar);
            return near <= far;
        }

        private Vector3 SphereObstacleSeparationForce(Vector3 position, Vector3 velocity)
        {
            float margin = Mathf.Max(0f, sphereSeparationMargin);
            float weight = Mathf.Max(0f, sphereSeparationWeight);
            if (margin <= 0f || weight <= 0f || activeObstacles.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 away = Vector3.zero;
            float maxPressure = 0f;

            for (int i = 0; i < activeObstacles.Count; i++)
            {
                BoidObstacle obstacle = activeObstacles[i];
                if (!obstacle || obstacle.Shape != BoidObstacleShape.Sphere)
                {
                    continue;
                }

                float radius = obstacle.Radius;
                float influenceRadius = radius + margin;
                Vector3 offset = position - obstacle.Position;
                float distanceSq = offset.sqrMagnitude;

                if (distanceSq >= influenceRadius * influenceRadius)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSq);
                if (distance < 0.000001f)
                {
                    offset = -velocity;
                    if (offset.sqrMagnitude < 0.000001f)
                    {
                        offset = Vector3.right;
                    }

                    distance = offset.magnitude;
                }

                float surfaceDistance = distance - radius;
                float pressure = surfaceDistance >= 0f
                    ? 1f - surfaceDistance / margin
                    : 1f + Mathf.Min(1f, -surfaceDistance / Mathf.Max(radius, 0.000001f));

                away += offset * (pressure / distance);
                maxPressure = Mathf.Max(maxPressure, pressure);
            }

            if (away.sqrMagnitude < 0.000001f)
            {
                return Vector3.zero;
            }

            return SteerTowards(away, velocity) * (weight * maxPressure);
        }

        private Vector3 SphericalEnvelopeForce(Vector3 position, Vector3 velocity)
        {
            float targetRadius = Mathf.Max(0.001f, baitBallRadius);
            float coreRadius = targetRadius * Mathf.Max(0f, baitBallCoreRatio);
            Vector3 radial = position - transform.position;
            float distance = radial.magnitude;

            if (distance < 0.000001f)
            {
                return Vector3.zero;
            }

            Vector3 radialDirection = radial / distance;
            Vector3 force = Vector3.zero;
            float pressure = 1f;

            if (distance > targetRadius)
            {
                float overshoot = Mathf.Max(0f, (distance - targetRadius) / targetRadius);
                pressure = 1f + overshoot * 3f;
                force += radialDirection * (-1f - overshoot);
            }
            else if (distance < coreRadius)
            {
                float corePressure = 1f - distance / Mathf.Max(coreRadius, 0.000001f);
                pressure = 0.5f + corePressure * 1.5f;
                force += radialDirection * corePressure;
            }
            else
            {
                float inwardBias = 0.28f * (distance / targetRadius);
                pressure = 0.55f + inwardBias;
                force += radialDirection * -inwardBias;
            }

            return SteerTowards(force, velocity) * pressure;
        }

        private Vector3 ToroidalFlowForce(Vector3 position, Vector3 velocity, Vector3 axis, float phase)
        {
            Vector3 radial = position - transform.position;
            if (radial.sqrMagnitude < 0.000001f)
            {
                return Vector3.zero;
            }

            float axialOffset = Vector3.Dot(radial, axis);
            Vector3 ringRadial = radial - axis * axialOffset;
            if (ringRadial.sqrMagnitude < 0.000001f)
            {
                ringRadial = Vector3.Cross(axis, velocity);
                if (ringRadial.sqrMagnitude < 0.000001f)
                {
                    ringRadial = Vector3.Cross(axis, Vector3.right);
                }
            }

            Vector3 toroidal = Vector3.Cross(axis, ringRadial).normalized;
            Vector3 radialDirection = radial.normalized;
            Vector3 poloidal = Vector3.Cross(toroidal, radialDirection).normalized;
            float roll = Mathf.Sin(phase + axialOffset * 0.72f);
            Vector3 desiredDirection = toroidal + poloidal * (roll * toroidalRollWeight);

            return SteerTowards(desiredDirection, velocity);
        }

        private Vector3 ReadFlowAxis(float time)
        {
            float speed = toroidalAxisSpeed;
            flowAxis.Set(
                Mathf.Sin(time * speed * 0.83f) * 0.62f,
                1f + Mathf.Sin(time * speed * 0.47f) * 0.22f,
                Mathf.Cos(time * speed) * 0.62f);
            return transform.TransformDirection(flowAxis.normalized);
        }

        private Vector3 CreateInitialPosition(ref BoidRandom random)
        {
            float radius = Mathf.Max(0.001f, baitBallRadius);
            Vector3 direction = CreateRandomUnitVector(ref random);
            float shellBias = 0.32f + 0.68f * Mathf.Pow(random.Next01(), 1f / 3f);
            return transform.position + direction * (radius * shellBias);
        }

        private Vector3 CreateInitialDirection(Vector3 position, ref BoidRandom random)
        {
            Vector3 localPosition = position - transform.position;
            Vector3 tangent = Vector3.Cross(DefaultFlowAxis, localPosition);
            if (tangent.sqrMagnitude < 0.000001f)
            {
                tangent = Vector3.Cross(Vector3.right, localPosition);
            }

            tangent.Normalize();
            Vector3 radial = SafeNormalize(localPosition);
            tangent += radial * ((random.Next01() * 2f - 1f) * 0.18f);
            return SafeNormalize(tangent);
        }

        private static Vector3 CreateRandomUnitVector(ref BoidRandom random)
        {
            float z = random.Next01() * 2f - 1f;
            float angle = random.Next01() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));

            return new Vector3(Mathf.Cos(angle) * radius, z, Mathf.Sin(angle) * radius);
        }

        private void UpdateMotionState(ref FishState current, Vector3 nextVelocity, float dt)
        {
            Vector3 previousDirection = SafeNormalize(current.Velocity);
            Vector3 nextDirection = SafeNormalize(nextVelocity);

            if (previousDirection.sqrMagnitude <= 0.000001f || nextDirection.sqrMagnitude <= 0.000001f)
            {
                current.Bank = DampAngle(current.Bank, 0f, bankResponse, dt);
                return;
            }

            float turnAngle = Vector3.Angle(previousDirection, nextDirection) * Mathf.Deg2Rad;
            Vector3 turnAxis = Vector3.Cross(previousDirection, nextDirection);
            float turnSign = Vector3.Dot(turnAxis, Vector3.up);
            float turnRate = turnAngle / dt;
            float maxBankAngle = maxBankAngleDegrees * Mathf.Deg2Rad;
            float targetBank = Mathf.Clamp(-turnSign * turnRate * bankTurnScale, -maxBankAngle, maxBankAngle);

            current.Bank = DampAngle(current.Bank, targetBank, bankResponse, dt);
        }

        private static float DampAngle(float current, float target, float response, float dt)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0f, response) * dt));
        }

        private void EnsureRayDirections()
        {
            if (rayDirections.Length == obstacleRayCount)
            {
                return;
            }

            rayDirections = new Vector3[obstacleRayCount];
            float goldenRatio = (1f + Mathf.Sqrt(5f)) * 0.5f;
            float angleIncrement = Mathf.PI * 2f * goldenRatio;

            for (int i = 0; i < obstacleRayCount; i++)
            {
                float t = (float)i / obstacleRayCount;
                float inclination = Mathf.Acos(1f - 2f * t);
                float azimuth = angleIncrement * i;
                rayDirections[i] = new Vector3(
                    Mathf.Sin(inclination) * Mathf.Cos(azimuth),
                    Mathf.Sin(inclination) * Mathf.Sin(azimuth),
                    Mathf.Cos(inclination));
            }
        }

        private static Vector3 SafeNormalize(Vector3 vector)
        {
            return vector.sqrMagnitude > 0.000001f ? vector.normalized : Vector3.zero;
        }

        private struct FishState
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 CollisionAvoidanceDirection;
            public float Bank;
            public bool HasCollisionAvoidanceDirection;

            public FishState(Vector3 position, Vector3 velocity)
            {
                Position = position;
                Velocity = velocity;
                CollisionAvoidanceDirection = Vector3.zero;
                Bank = 0f;
                HasCollisionAvoidanceDirection = false;
            }
        }
    }
}
