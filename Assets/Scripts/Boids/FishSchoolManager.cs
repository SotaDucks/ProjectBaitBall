using System;
using System.Collections.Generic;
using FishFlock.Utils;
using UnityEngine;

namespace TestBoids.Boids
{
    [DisallowMultipleComponent]
    public sealed class FishSchoolManager : MonoBehaviour
    {
        [Header("Fish Source")]
        [SerializeField] private GameObject fishPrefab;
        [SerializeField, Min(0)] private int fishCount = 60;
        [SerializeField] private Transform sceneFishRoot;
        [SerializeField] private FishAgent[] sceneFish = Array.Empty<FishAgent>();
        [SerializeField] private bool autoCollectChildFish = true;
        [SerializeField] private bool spawnOnStart = true;
        [SerializeField] private bool parentSpawnedFishToManager = true;
        [SerializeField] private bool destroySpawnedFishOnDisable = true;
        [SerializeField] private int seed = 42;

        [Header("Aquarium")]
        [SerializeField] private Vector3 aquariumHalfSize = new(11f, 6.6f, 8.5f);
        [SerializeField, Range(0.05f, 1f)] private float spawnBoundsScale = 0.62f;
        [SerializeField, Min(0f)] private float boundaryMargin = 2f;
        [SerializeField, Min(0f)] private float boundaryWeight = 9f;

        [Header("School Shape")]
        [SerializeField, Min(0.1f)] private float schoolRadius = 5.5f;
        [SerializeField, Range(0f, 0.9f)] private float schoolCoreRatio = 0.18f;
        [SerializeField, Min(0f)] private float centeringWeight = 1.1f;
        [SerializeField, Min(0f)] private float flowWeight = 0.65f;
        [SerializeField, Min(0f)] private float flowAxisSpeed = 0.25f;
        [SerializeField, Min(0.1f)] private float schoolWidthScale = 1.55f;
        [SerializeField, Min(0.1f)] private float schoolHeightScale = 0.82f;

        [Header("Boids")]
        [SerializeField, HideInInspector] private float minSpeed = 3f;
        [SerializeField, HideInInspector] private float maxSpeed = 7.5f;
        [SerializeField, Min(0f)] private float maxTurnRateDegrees = 540f;
        [SerializeField, Min(0f)] private float perceptionRadius = 4.2f;
        [SerializeField, Min(0f)] private float separationRadius = 1.25f;
        [SerializeField, Min(0f)] private float maxSteerForce = 4.2f;
        [SerializeField, Min(0f)] private float alignWeight = 0.75f;
        [SerializeField, Min(0f)] private float cohesionWeight = 1.15f;
        [SerializeField, Min(0f)] private float separateWeight = 2.4f;
        [SerializeField, Min(0), Tooltip("Maximum other fish sampled by each fish each frame. Set to 0 to scan every fish.")]
        private int neighborScanLimit;

        [Header("Individual Randomization")]
        [SerializeField, MinMax(0.01f, 1.5f)] private Vector2 scaleMultiplierRange = new(0.18f, 0.25f);
        [SerializeField, MinMax(0f, 12f)] private Vector2 speedRange = new(3f, 7.5f);
        [SerializeField, HideInInspector] private bool individualRandomRangesInitialized;

        [Header("Pose")]
        [SerializeField, Min(0f)] private float maxBankAngleDegrees = 12f;
        [SerializeField, Min(0f)] private float bankTurnScale = 0.18f;
        [SerializeField, Min(0f)] private float bankResponse = 8f;

        [Header("Baked Initial State")]
        [SerializeField] private bool useBakedInitialState = true;
        [SerializeField, Min(0)] private int bakeWarmupFrames = 360;
        [SerializeField, Min(0.001f)] private float bakeTimeStep = 0.03333334f;
        [SerializeField, HideInInspector] private BakedFishState[] bakedInitialStates = Array.Empty<BakedFishState>();

        private readonly List<GameObject> spawnedFishObjects = new();
        private FishAgent[] agents = Array.Empty<FishAgent>();
        private FishState[] fish = Array.Empty<FishState>();
        private FishState[] nextFish = Array.Empty<FishState>();
        private int simulationFrame;
        private float simulationTime;

        private bool HasFish => fish != null && fish.Length > 0;
        private bool HasBakedInitialState => useBakedInitialState && bakedInitialStates != null && bakedInitialStates.Length > 0;
        public IReadOnlyList<FishAgent> Agents => agents;
        public int BakedInitialStateCount => bakedInitialStates?.Length ?? 0;

        private void OnValidate()
        {
            InitializeIndividualRandomRangesFromLegacyValues();
            NormalizeIndividualRandomRanges();
            fishCount = Mathf.Max(0, fishCount);
            aquariumHalfSize = new Vector3(
                Mathf.Max(0f, aquariumHalfSize.x),
                Mathf.Max(0f, aquariumHalfSize.y),
                Mathf.Max(0f, aquariumHalfSize.z));

            schoolRadius = Mathf.Max(0.1f, schoolRadius);
            schoolCoreRatio = Mathf.Clamp01(schoolCoreRatio);
            schoolWidthScale = Mathf.Max(0.1f, schoolWidthScale);
            schoolHeightScale = Mathf.Max(0.1f, schoolHeightScale);
            perceptionRadius = Mathf.Max(0f, perceptionRadius);
            separationRadius = Mathf.Min(Mathf.Max(0f, separationRadius), perceptionRadius);
            neighborScanLimit = Mathf.Max(0, neighborScanLimit);
            bakeWarmupFrames = Mathf.Max(0, bakeWarmupFrames);
            bakeTimeStep = Mathf.Max(0.001f, bakeTimeStep);
        }

        private void InitializeIndividualRandomRangesFromLegacyValues()
        {
            if (individualRandomRangesInitialized)
            {
                return;
            }

            speedRange = OrderedRange(minSpeed, maxSpeed);
            individualRandomRangesInitialized = true;
        }

        private void NormalizeIndividualRandomRanges()
        {
            scaleMultiplierRange = OrderedRange(scaleMultiplierRange);
            speedRange = OrderedRange(speedRange);
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
            if (dt <= 0f || !HasFish)
            {
                return;
            }

            UpdateSimulation(dt);
            ApplyAgentPoses();
            simulationFrame++;
        }

        private void OnDisable()
        {
            if (destroySpawnedFishOnDisable)
            {
                DestroySpawnedFish();
                agents = Array.Empty<FishAgent>();
                fish = Array.Empty<FishState>();
                nextFish = Array.Empty<FishState>();
            }
        }

        public void ResetSchool()
        {
            ResetSchool(fishCount, seed);
        }

        public void ResetSchool(int count)
        {
            ResetSchool(count, seed);
        }

        public void ResetSchool(int count, int randomSeed)
        {
            fishCount = Mathf.Max(0, count);
            seed = randomSeed;
            simulationFrame = 0;
            simulationTime = 0f;

            DestroySpawnedFish();
            FishAgent[] sceneAgents = CollectSceneAgents();
            if (sceneAgents.Length > 0)
            {
                agents = sceneAgents;
                fishCount = agents.Length;
                AllocateFishState(agents.Length);
                if (!TryInitializeFishStateFromBaked())
                {
                    InitializeFishState(randomSeed);
                }
            }
            else if (HasBakedInitialState && fishPrefab)
            {
                SpawnPrefabAgentsFromBaked();
                fishCount = agents.Length;
                AllocateFishState(agents.Length);
                TryInitializeFishStateFromBaked();
            }
            else
            {
                SpawnPrefabAgents(fishCount, randomSeed);
                AllocateFishState(agents.Length);
                InitializeFishState(randomSeed);
            }

            ApplyAgentPoses();
        }

        public void SetCount(int count)
        {
            ResetSchool(count, seed);
        }

        public bool TryReleaseAgent(FishAgent agent, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (!agent || agents == null)
            {
                return false;
            }

            int index = Array.IndexOf(agents, agent);
            if (index < 0)
            {
                return false;
            }

            if (fish != null && index < fish.Length)
            {
                velocity = fish[index].Velocity;
            }
            else if (agent.Velocity.sqrMagnitude > 0.000001f)
            {
                velocity = agent.Velocity;
            }

            agents = RemoveAt(agents, index);
            fish = RemoveAt(fish, index);
            nextFish = RemoveAt(nextFish, index);
            fishCount = agents.Length;
            return true;
        }

        public void BakeStableInitialStateForEditor()
        {
            if (Application.isPlaying)
            {
                return;
            }

            InitializeIndividualRandomRangesFromLegacyValues();
            NormalizeIndividualRandomRanges();

            simulationFrame = 0;
            simulationTime = 0f;
            DestroySpawnedFish();

            FishAgent[] sceneAgents = CollectSceneAgents();
            bool generatedFromPrefab = sceneAgents.Length == 0;
            if (sceneAgents.Length > 0)
            {
                agents = sceneAgents;
                fishCount = agents.Length;
            }
            else
            {
                SpawnPrefabAgents(fishCount, seed);
            }

            AllocateFishState(agents.Length);
            InitializeFishState(seed);
            ApplyAgentPoses();

            int frames = Mathf.Max(0, bakeWarmupFrames);
            float dt = Mathf.Max(0.001f, bakeTimeStep);
            for (int i = 0; i < frames; i++)
            {
                UpdateSimulation(dt);
                ApplyAgentPoses();
                simulationFrame++;
            }

            CaptureBakedInitialState();
            useBakedInitialState = true;

            if (!Application.isPlaying && generatedFromPrefab && parentSpawnedFishToManager)
            {
                spawnedFishObjects.Clear();
            }
            else if (!Application.isPlaying && generatedFromPrefab)
            {
                DestroySpawnedFish();
                agents = Array.Empty<FishAgent>();
                fish = Array.Empty<FishState>();
                nextFish = Array.Empty<FishState>();
            }
        }

        public void ClearBakedInitialStateForEditor()
        {
            if (Application.isPlaying)
            {
                return;
            }

            bakedInitialStates = Array.Empty<BakedFishState>();
        }

        private void CaptureBakedInitialState()
        {
            int count = Mathf.Min(agents.Length, fish.Length);
            bakedInitialStates = count > 0 ? new BakedFishState[count] : Array.Empty<BakedFishState>();
            for (int i = 0; i < count; i++)
            {
                FishAgent agent = agents[i];
                FishState state = fish[i];
                Vector3 direction = state.Velocity.sqrMagnitude > 0.000001f ? state.Velocity.normalized : transform.forward;
                Quaternion rotation = agent ? agent.transform.rotation : Quaternion.LookRotation(direction, Vector3.up);
                Vector3 localScale = agent ? agent.transform.localScale : Vector3.one;
                bakedInitialStates[i] = new BakedFishState
                {
                    Position = state.Position,
                    Rotation = rotation,
                    LocalScale = localScale,
                    Velocity = state.Velocity,
                    MinSpeed = state.MinSpeed,
                    MaxSpeed = state.MaxSpeed,
                    Bank = state.Bank
                };
            }
        }

        private FishAgent[] CollectSceneAgents()
        {
            List<FishAgent> collected = new();

            if (sceneFish != null)
            {
                for (int i = 0; i < sceneFish.Length; i++)
                {
                    AddUniqueAgent(collected, sceneFish[i]);
                }
            }

            if (sceneFishRoot)
            {
                FishAgent[] childAgents = sceneFishRoot.GetComponentsInChildren<FishAgent>();
                for (int i = 0; i < childAgents.Length; i++)
                {
                    AddUniqueAgent(collected, childAgents[i]);
                }
            }

            if (autoCollectChildFish)
            {
                FishAgent[] childAgents = GetComponentsInChildren<FishAgent>();
                for (int i = 0; i < childAgents.Length; i++)
                {
                    AddUniqueAgent(collected, childAgents[i]);
                }
            }

            return collected.ToArray();
        }

        private static void AddUniqueAgent(List<FishAgent> target, FishAgent agent)
        {
            if (agent && !target.Contains(agent))
            {
                target.Add(agent);
            }
        }

        private void SpawnPrefabAgentsFromBaked()
        {
            DestroySpawnedFish();
            if (!fishPrefab || !HasBakedInitialState)
            {
                agents = Array.Empty<FishAgent>();
                return;
            }

            int count = bakedInitialStates.Length;
            agents = new FishAgent[count];
            for (int i = 0; i < count; i++)
            {
                BakedFishState baked = bakedInitialStates[i];
                Transform parent = parentSpawnedFishToManager ? transform : null;
                GameObject instance = Instantiate(fishPrefab, baked.Position, baked.Rotation, parent);
                instance.transform.localScale = baked.LocalScale;

                FishAgent agent = instance.GetComponent<FishAgent>();
                if (!agent)
                {
                    agent = instance.GetComponentInChildren<FishAgent>();
                }

                if (!agent)
                {
                    agent = instance.AddComponent<FishAgent>();
                }

                spawnedFishObjects.Add(instance);
                agents[i] = agent;
            }
        }

        private void SpawnPrefabAgents(int count, int randomSeed)
        {
            DestroySpawnedFish();
            if (!fishPrefab || count <= 0)
            {
                agents = Array.Empty<FishAgent>();
                return;
            }

            BoidRandom random = new((uint)randomSeed);
            agents = new FishAgent[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 position = CreateInitialPosition(ref random);
                Vector3 direction = CreateRandomDirection(ref random);
                Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
                Transform parent = parentSpawnedFishToManager ? transform : null;
                GameObject instance = Instantiate(fishPrefab, position, rotation, parent);
                FishAgent agent = instance.GetComponent<FishAgent>();
                if (!agent)
                {
                    agent = instance.GetComponentInChildren<FishAgent>();
                }

                if (!agent)
                {
                    agent = instance.AddComponent<FishAgent>();
                }

                float scaleMultiplier = Mathf.Max(0.001f, SampleRange(scaleMultiplierRange, ref random));
                instance.transform.localScale *= scaleMultiplier;
                spawnedFishObjects.Add(instance);
                agents[i] = agent;
            }
        }

        private void AllocateFishState(int count)
        {
            fish = count > 0 ? new FishState[count] : Array.Empty<FishState>();
            nextFish = count > 0 ? new FishState[count] : Array.Empty<FishState>();
        }

        private void InitializeFishState(int randomSeed)
        {
            BoidRandom random = new((uint)randomSeed);
            for (int i = 0; i < fish.Length; i++)
            {
                FishAgent agent = agents[i];
                Vector3 position = agent ? agent.transform.position : CreateInitialPosition(ref random);
                Vector3 direction = ReadInitialDirection(agent, position, ref random);
                float speed = Mathf.Max(0.001f, SampleRange(speedRange, ref random));
                FishState state = new()
                {
                    Position = position,
                    Velocity = direction * speed,
                    MinSpeed = Mathf.Max(0.001f, speed * 0.88f),
                    MaxSpeed = Mathf.Max(0.001f, speed * 1.12f),
                    Bank = agent ? agent.Bank : 0f
                };

                fish[i] = state;
                nextFish[i] = state;
            }
        }

        private bool TryInitializeFishStateFromBaked()
        {
            if (!HasBakedInitialState || bakedInitialStates.Length != agents.Length)
            {
                return false;
            }

            for (int i = 0; i < bakedInitialStates.Length; i++)
            {
                BakedFishState baked = bakedInitialStates[i];
                FishAgent agent = agents[i];
                if (agent)
                {
                    agent.transform.position = baked.Position;
                    agent.transform.rotation = baked.Rotation;
                    agent.transform.localScale = baked.LocalScale;
                }

                float min = Mathf.Max(0.001f, baked.MinSpeed);
                float max = Mathf.Max(min, baked.MaxSpeed);
                Vector3 velocity = baked.Velocity;
                if (velocity.sqrMagnitude <= 0.000001f)
                {
                    Vector3 direction = agent && agent.transform.forward.sqrMagnitude > 0.000001f
                        ? agent.transform.forward.normalized
                        : transform.forward.normalized;
                    velocity = direction * min;
                }

                FishState state = new()
                {
                    Position = baked.Position,
                    Velocity = velocity,
                    MinSpeed = min,
                    MaxSpeed = max,
                    Bank = baked.Bank
                };
                fish[i] = state;
                nextFish[i] = state;
            }

            return true;
        }

        private Vector3 ReadInitialDirection(FishAgent agent, Vector3 position, ref BoidRandom random)
        {
            if (agent && agent.Velocity.sqrMagnitude > 0.000001f)
            {
                return agent.Velocity.normalized;
            }

            if (agent && agent.transform.forward.sqrMagnitude > 0.000001f)
            {
                return agent.transform.forward.normalized;
            }

            return CreateInitialDirection(position, ref random);
        }

        private void UpdateSimulation(float dt)
        {
            float perceptionRadiusSq = perceptionRadius * perceptionRadius;
            float separationRadiusSq = separationRadius * separationRadius;
            Vector3 flowAxis = ReadFlowAxis(simulationTime);
            float flowPhase = simulationTime * flowAxisSpeed * 3.7f;

            for (int i = 0; i < fish.Length; i++)
            {
                FishState current = fish[i];
                Vector3 acceleration = Vector3.zero;
                Vector3 headingSum = Vector3.zero;
                Vector3 centerSum = Vector3.zero;
                Vector3 separationSum = Vector3.zero;
                int neighborCount = 0;
                int sampleCount = ReadNeighborSampleCount(fish.Length);

                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    int otherIndex = ReadNeighborIndex(i, sampleIndex, fish.Length);
                    if (otherIndex == i)
                    {
                        continue;
                    }

                    AccumulateNeighbor(
                        current,
                        fish[otherIndex],
                        perceptionRadiusSq,
                        separationRadiusSq,
                        ref headingSum,
                        ref centerSum,
                        ref separationSum,
                        ref neighborCount);
                }

                if (neighborCount > 0)
                {
                    Vector3 center = centerSum / neighborCount;
                    acceleration += SteerTowards(headingSum, current.Velocity, current.MaxSpeed) * alignWeight;
                    acceleration += SteerTowards(center - current.Position, current.Velocity, current.MaxSpeed) * cohesionWeight;
                    acceleration += SteerTowards(separationSum, current.Velocity, current.MaxSpeed) * separateWeight;
                }

                acceleration += SchoolEnvelopeForce(current.Position, current.Velocity, current.MaxSpeed) * centeringWeight;
                acceleration += SchoolFlowForce(current.Position, current.Velocity, flowAxis, flowPhase, current.MaxSpeed) * flowWeight;

                Vector3 boundary = AquariumBoundarySteer(current.Position);
                if (boundary.sqrMagnitude > 0.000001f)
                {
                    acceleration += SteerTowards(boundary, current.Velocity, current.MaxSpeed) * boundaryWeight;
                }

                Vector3 desiredVelocity = current.Velocity + acceleration * dt;
                float speed = Mathf.Clamp(desiredVelocity.magnitude, current.MinSpeed, current.MaxSpeed);
                if (desiredVelocity.sqrMagnitude <= 0.000001f)
                {
                    desiredVelocity = current.Velocity.sqrMagnitude > 0.000001f
                        ? current.Velocity.normalized * speed
                        : transform.forward * speed;
                }
                else
                {
                    desiredVelocity = desiredVelocity.normalized * speed;
                }

                Vector3 velocity = LimitTurn(current.Velocity, desiredVelocity, dt);
                current.Bank = UpdateBank(current.Bank, current.Velocity, velocity, dt);
                current.Velocity = velocity;
                current.Position += velocity * dt;
                nextFish[i] = current;
            }

            (fish, nextFish) = (nextFish, fish);
            simulationTime += dt;
        }

        private int ReadNeighborSampleCount(int count)
        {
            if (count <= 1)
            {
                return 0;
            }

            int maximumNeighbors = count - 1;
            return neighborScanLimit <= 0 ? maximumNeighbors : Mathf.Min(neighborScanLimit, maximumNeighbors);
        }

        private int ReadNeighborIndex(int fishIndex, int sampleIndex, int count)
        {
            if (neighborScanLimit <= 0 || neighborScanLimit >= count - 1)
            {
                return sampleIndex < fishIndex ? sampleIndex : sampleIndex + 1;
            }

            return SampleNeighborIndex(fishIndex, sampleIndex, count, simulationFrame);
        }

        private static void AccumulateNeighbor(
            FishState current,
            FishState other,
            float perceptionRadiusSq,
            float separationRadiusSq,
            ref Vector3 headingSum,
            ref Vector3 centerSum,
            ref Vector3 separationSum,
            ref int neighborCount)
        {
            Vector3 offset = other.Position - current.Position;
            float distanceSq = offset.sqrMagnitude;
            if (distanceSq >= perceptionRadiusSq)
            {
                return;
            }

            neighborCount++;
            if (other.Velocity.sqrMagnitude > 0.000001f)
            {
                headingSum += other.Velocity.normalized;
            }

            centerSum += other.Position;

            if (distanceSq < separationRadiusSq)
            {
                float distance = Mathf.Sqrt(Mathf.Max(distanceSq, 0.0001f));
                separationSum += offset * (-1f / distance);
            }
        }

        private Vector3 AquariumBoundarySteer(Vector3 position)
        {
            if (boundaryMargin <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 localPosition = transform.InverseTransformPoint(position);
            Vector3 localSteer = Vector3.zero;
            localSteer.x = AxisBoundarySteer(localPosition.x, aquariumHalfSize.x, boundaryMargin);
            localSteer.y = AxisBoundarySteer(localPosition.y, aquariumHalfSize.y, boundaryMargin);
            localSteer.z = AxisBoundarySteer(localPosition.z, aquariumHalfSize.z, boundaryMargin);
            return transform.TransformDirection(localSteer);
        }

        private Vector3 SchoolEnvelopeForce(Vector3 position, Vector3 velocity, float targetMaxSpeed)
        {
            Vector3 offset = position - transform.position;
            if (offset.sqrMagnitude < 0.000001f)
            {
                return Vector3.zero;
            }

            Vector3 localOffset = transform.InverseTransformDirection(offset);
            Vector3 normalized = new(
                localOffset.x / schoolWidthScale,
                localOffset.y / schoolHeightScale,
                localOffset.z);
            float normalizedDistance = normalized.magnitude;
            if (normalizedDistance < 0.000001f)
            {
                return Vector3.zero;
            }

            Vector3 localRadialDirection = normalized / normalizedDistance;
            Vector3 worldRadialDirection = transform.TransformDirection(new Vector3(
                localRadialDirection.x * schoolWidthScale,
                localRadialDirection.y * schoolHeightScale,
                localRadialDirection.z)).normalized;
            float targetRadius = Mathf.Max(0.001f, schoolRadius);
            float coreRadius = targetRadius * schoolCoreRatio;
            Vector3 force;
            float pressure;

            if (normalizedDistance > targetRadius)
            {
                float overshoot = (normalizedDistance - targetRadius) / targetRadius;
                force = -worldRadialDirection * (1f + overshoot * 2.4f);
                pressure = 1f + overshoot * 2f;
            }
            else if (normalizedDistance < coreRadius)
            {
                float corePressure = 1f - normalizedDistance / Mathf.Max(coreRadius, 0.0001f);
                force = worldRadialDirection * corePressure;
                pressure = 0.55f + corePressure;
            }
            else
            {
                float edgeBias = normalizedDistance / targetRadius;
                force = -worldRadialDirection * (edgeBias * 0.18f);
                pressure = 0.45f + edgeBias * 0.35f;
            }

            return SteerTowards(force, velocity, targetMaxSpeed) * pressure;
        }

        private Vector3 SchoolFlowForce(Vector3 position, Vector3 velocity, Vector3 axis, float phase, float targetMaxSpeed)
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

            Vector3 lateralFlow = Vector3.Cross(axis, ringRadial).normalized;
            Vector3 forwardBias = transform.forward.sqrMagnitude > 0.000001f ? transform.forward.normalized : Vector3.forward;
            float roll = Mathf.Sin(phase + axialOffset * 0.72f) * 0.22f;
            Vector3 desiredDirection = lateralFlow + forwardBias * 0.35f + axis * roll;

            return SteerTowards(desiredDirection, velocity, targetMaxSpeed);
        }

        private Vector3 ReadFlowAxis(float time)
        {
            float speed = flowAxisSpeed;
            Vector3 localAxis = new(
                Mathf.Sin(time * speed * 0.83f) * 0.42f,
                1f + Mathf.Sin(time * speed * 0.47f) * 0.14f,
                Mathf.Cos(time * speed) * 0.42f);
            return transform.TransformDirection(localAxis.normalized);
        }

        private static float AxisBoundarySteer(float value, float halfSize, float margin)
        {
            if (halfSize <= 0f)
            {
                return 0f;
            }

            float innerLimit = Mathf.Max(0f, halfSize - margin);
            if (value > innerLimit)
            {
                return -(value - innerLimit) / Mathf.Max(0.0001f, margin);
            }

            if (value < -innerLimit)
            {
                return (-innerLimit - value) / Mathf.Max(0.0001f, margin);
            }

            return 0f;
        }

        private Vector3 SteerTowards(Vector3 vector, Vector3 velocity, float targetMaxSpeed)
        {
            if (vector.sqrMagnitude < 0.000001f)
            {
                return Vector3.zero;
            }

            Vector3 desired = vector.normalized * Mathf.Max(0.001f, targetMaxSpeed);
            return Vector3.ClampMagnitude(desired - velocity, maxSteerForce);
        }

        private Vector3 LimitTurn(Vector3 currentVelocity, Vector3 desiredVelocity, float dt)
        {
            if (currentVelocity.sqrMagnitude <= 0.000001f || desiredVelocity.sqrMagnitude <= 0.000001f)
            {
                return desiredVelocity;
            }

            Vector3 currentDirection = currentVelocity.normalized;
            Vector3 desiredDirection = desiredVelocity.normalized;
            float angle = Vector3.Angle(currentDirection, desiredDirection) * Mathf.Deg2Rad;
            float maxAngle = maxTurnRateDegrees * Mathf.Deg2Rad * dt;
            if (angle <= maxAngle || angle < 0.000001f)
            {
                return desiredVelocity;
            }

            float t = maxAngle / angle;
            Vector3 direction = Vector3.Slerp(currentDirection, desiredDirection, t).normalized;
            return direction * desiredVelocity.magnitude;
        }

        private float UpdateBank(float currentBank, Vector3 previousVelocity, Vector3 nextVelocity, float dt)
        {
            if (dt <= 0f || previousVelocity.sqrMagnitude <= 0.000001f || nextVelocity.sqrMagnitude <= 0.000001f)
            {
                return DampAngle(currentBank, 0f, bankResponse, dt);
            }

            Vector3 previousDirection = previousVelocity.normalized;
            Vector3 nextDirection = nextVelocity.normalized;
            float turnAngle = Vector3.Angle(previousDirection, nextDirection) * Mathf.Deg2Rad;
            Vector3 turnAxis = Vector3.Cross(previousDirection, nextDirection);
            float turnSign = Vector3.Dot(turnAxis, Vector3.up);
            float turnRate = turnAngle / dt;
            float maxBankAngle = maxBankAngleDegrees * Mathf.Deg2Rad;
            float targetBank = Mathf.Clamp(-turnSign * turnRate * bankTurnScale, -maxBankAngle, maxBankAngle);

            return DampAngle(currentBank, targetBank, bankResponse, dt);
        }

        private static float DampAngle(float current, float target, float response, float dt)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0f, response) * dt));
        }

        private void ApplyAgentPoses()
        {
            int count = Mathf.Min(agents.Length, fish.Length);
            for (int i = 0; i < count; i++)
            {
                FishAgent agent = agents[i];
                if (agent)
                {
                    FishState state = fish[i];
                    agent.ApplyPose(state.Position, state.Velocity, state.Bank);
                }
            }
        }

        private Vector3 CreateInitialPosition(ref BoidRandom random)
        {
            Vector3 localDirection = CreateRandomDirection(ref random);
            float radius = schoolRadius * Mathf.Pow(random.Next01(), 1f / 3f);
            Vector3 localPosition = new(
                localDirection.x * radius * schoolWidthScale,
                localDirection.y * radius * schoolHeightScale,
                localDirection.z * radius);
            Vector3 clampedHalfSize = aquariumHalfSize * spawnBoundsScale;
            localPosition.x = Mathf.Clamp(localPosition.x, -clampedHalfSize.x, clampedHalfSize.x);
            localPosition.y = Mathf.Clamp(localPosition.y, -clampedHalfSize.y, clampedHalfSize.y);
            localPosition.z = Mathf.Clamp(localPosition.z, -clampedHalfSize.z, clampedHalfSize.z);
            return transform.TransformPoint(localPosition);
        }

        private Vector3 CreateInitialDirection(Vector3 position, ref BoidRandom random)
        {
            Vector3 radial = position - transform.position;
            Vector3 axis = transform.up.sqrMagnitude > 0.000001f ? transform.up.normalized : Vector3.up;
            Vector3 tangent = Vector3.Cross(axis, radial);
            if (tangent.sqrMagnitude < 0.000001f)
            {
                tangent = Vector3.Cross(transform.right, radial);
            }

            if (tangent.sqrMagnitude < 0.000001f)
            {
                return CreateRandomDirection(ref random);
            }

            Vector3 inward = radial.sqrMagnitude > 0.000001f ? -radial.normalized : Vector3.zero;
            Vector3 jitter = CreateRandomDirection(ref random) * 0.18f;
            return (tangent.normalized + inward * 0.22f + jitter).normalized;
        }

        private Vector3 CreateRandomDirection(ref BoidRandom random)
        {
            for (int i = 0; i < 24; i++)
            {
                Vector3 direction = new(
                    RandomRange(-1f, 1f, ref random),
                    RandomRange(-1f, 1f, ref random),
                    RandomRange(-1f, 1f, ref random));
                if (direction.sqrMagnitude > 0.000001f)
                {
                    return direction.normalized;
                }
            }

            return transform.forward.sqrMagnitude > 0.000001f ? transform.forward.normalized : Vector3.forward;
        }

        private static float RandomRange(float min, float max, ref BoidRandom random)
        {
            return Mathf.Lerp(min, max, random.Next01());
        }

        private static float SampleRange(Vector2 range, ref BoidRandom random)
        {
            return Mathf.Lerp(range.x, range.y, random.Next01());
        }

        private static Vector2 OrderedRange(float x, float y)
        {
            return x <= y ? new Vector2(x, y) : new Vector2(y, x);
        }

        private static Vector2 OrderedRange(Vector2 range)
        {
            return OrderedRange(range.x, range.y);
        }

        private void DestroySpawnedFish()
        {
            for (int i = 0; i < spawnedFishObjects.Count; i++)
            {
                GameObject instance = spawnedFishObjects[i];
                if (!instance)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(instance);
                }
                else
                {
                    DestroyImmediate(instance);
                }
            }

            spawnedFishObjects.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.35f);
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, aquariumHalfSize * 2f);
            Gizmos.matrix = previous;
        }

        private static int SampleNeighborIndex(int fishIndex, int sampleIndex, int fishCount, int sampleFrame)
        {
            uint value = (uint)fishIndex * 747796405u
                + (uint)sampleIndex * 2891336453u
                + (uint)sampleFrame * 277803737u;
            value ^= value >> 16;
            value *= 2246822519u;
            value ^= value >> 13;
            return (int)(value % (uint)fishCount);
        }

        private static T[] RemoveAt<T>(T[] source, int index)
        {
            if (source == null || index < 0 || index >= source.Length)
            {
                return source ?? Array.Empty<T>();
            }

            if (source.Length == 1)
            {
                return Array.Empty<T>();
            }

            T[] result = new T[source.Length - 1];
            if (index > 0)
            {
                Array.Copy(source, 0, result, 0, index);
            }

            int tailCount = source.Length - index - 1;
            if (tailCount > 0)
            {
                Array.Copy(source, index + 1, result, index, tailCount);
            }

            return result;
        }

        private struct FishState
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float MinSpeed;
            public float MaxSpeed;
            public float Bank;
        }

        [Serializable]
        private struct BakedFishState
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 LocalScale;
            public Vector3 Velocity;
            public float MinSpeed;
            public float MaxSpeed;
            public float Bank;
        }
    }
}
