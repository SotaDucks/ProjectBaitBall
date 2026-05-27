using FishFlock.Utils;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FishFlock
{
    public enum FlockRenderingMode
    {
        GAMEOBJECT, INSTANCING
    };

    public enum FlockComputationMode
    {
        CPU,
        CPU_AND_ONE_THREAD,
        GPU
    };

    public class FishFlockController : MonoBehaviour
    {
        public float swimmingAreaWidth = 25;
        public float swimmingAreaHeight = 20;
        public float swimmingAreaDepth = 25;

        public FishMovementAxis FishMovementAxis = FishMovementAxis.XYZ;
        public bool debugDraw = true;

        public FlockRenderingMode renderingMode = FlockRenderingMode.INSTANCING;
        public FlockComputationMode computationMode = FlockComputationMode.CPU;

        public int fishesCount = 30;

        [MinMax(0, 10)]
        public Vector2 speed = new Vector2(2.58f, 6.58f);
        [MinMax(0, 10)]
        public Vector2 rotation = new Vector2(1.88f, 6.14f);
        public float neighbourDistance = 4f;
        public float spawnRadius = 2f;

        [MinMax(0.0f, 10.0f)]
        public Vector2 scale = new Vector2(0.2f, 0.4f);

        public Vector3 cohesionScale;

        public bool followTarget = false;
        public Transform target;

        [MinMax(1, 5)]
        public Vector2Int targetPointsAmount = new Vector2Int(2, 5);

        public bool recalculatePoints = true;
        public float groupAreaSpeed = 0.8f;

        Vector3[] targetPositions;
        int currentTargetPosIndex = 0;

        public FlockGfxProfile gfxProfile;
        public ComputeShader computeShader;

        public int lookAheadSteps = 3;
        public float force = 20;
        public float colliderSizeOffset = 0f;
        public Collider[] colliders = null;
        public bool updateAtRuntime = true;

        CollisionArea[] collisionData;
        int collisionDataLength;

        [HideInInspector]
        public Vector3 groupAnchor;
        Transform myTransform;
        Bounds instancingBounds;

        // Instanced data
        ComputeBuffer fishBuffer;
        ComputeBuffer drawArgsBuffer;
        ComputeBuffer collisionBuffer;
        MaterialPropertyBlock props;
        int kernelHandle;
        const int GROUP_SIZE = 256;

        public FishData[] fishesData;
        Transform[] fishesTransforms;

        int currentFishesCount;
        int oldFishesCount;

        SimpleCounter refreshFishCounter = new SimpleCounter();
        SimpleCounter refreshCollisionCounter = new SimpleCounter();
        SimpleCounter refreshPredatorsCounter = new SimpleCounter();

        float _neighbourDistance;
        float intendedNeighbourDistance;
        float neighbourDistanceRateSmoothRate = 10.0f;

        Material _fishInstancedMaterial;

        bool initialized = false;
        Vector3[] oldPositions;

        float unityTime;
        float unityDeltaTime;

        ThreadProcess threadProcess;

        FlockPredator[] predators;
        ComputeBuffer predatorsBuffer;
        PredatorData[] predatorsData;
        int predatorsCount;

        private void Awake()
        {
            groupAnchor = transform.position;

            myTransform = transform;

            _neighbourDistance = neighbourDistance;
            intendedNeighbourDistance = neighbourDistance;
            _neighbourDistance = 0.00001f;

            refreshFishCounter.Start(0.6f);
            refreshCollisionCounter.Start(0.3f);
            refreshPredatorsCounter.Start(0.1f);



            if (followTarget)
                groupAnchor = target.position;
            else
            {
                GeneratePath();
                groupAnchor = targetPositions[0];
            }

            InitializeFishes();
            UpdateCollisionData();


            if (renderingMode == FlockRenderingMode.INSTANCING)
            {
                props = new MaterialPropertyBlock();
                props.SetFloat("_UniqueID", Random.value);

                if (_fishInstancedMaterial == null)
                    _fishInstancedMaterial = new Material(gfxProfile.material);
            }


            CreateFishData();

            initialized = true;

            predators = FindObjectsOfType<FlockPredator>();
            if (computationMode == FlockComputationMode.GPU)
            {
                if (predators != null && predators.Length > 0)
                {
                    predatorsData = new PredatorData[predators.Length];
                    for (int i = 0; i < predatorsData.Length; i++)
                    {
                        var predator = predators[i];
                        PredatorData data = new PredatorData();
                        data.position = predator.transform.position;
                        data.radius = predator.fleeRadius;

                        predatorsData[i] = data;
                    }

                    predatorsBuffer = new ComputeBuffer(predatorsData.Length, sizeof(float) * 4);
                    predatorsBuffer.SetData(predatorsData);
                    predatorsCount = predatorsData.Length;
                }
                else
                {
                    predatorsCount = 0;
                    predatorsBuffer = new ComputeBuffer(1, sizeof(float) * 4);
                    predatorsBuffer.SetData(new PredatorData[1] { new PredatorData() });
                }
            }
        }

        void InitializeFishes()
        {
            currentFishesCount = fishesCount;
            oldFishesCount = currentFishesCount;

            if (computationMode == FlockComputationMode.CPU_AND_ONE_THREAD)
                threadProcess = ThreadManager.DoTask(ComputeFlockBehaviours);

            if (renderingMode == FlockRenderingMode.INSTANCING)
            {
                instancingBounds = new Bounds(myTransform.position, Vector3.one * 1000);
                drawArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
                drawArgsBuffer.SetData(new uint[5] { gfxProfile.mesh.GetIndexCount(0), (uint)fishesCount, 0, 0, 0 });
            }
        }

        void UpdateCollisionData()
        {
            int collidersLength = colliders.Length;
            if (colliders != null && collidersLength > 0)
            {
                if (collisionData == null || (collisionData != null && collidersLength != collisionData.Length))
                    collisionData = new CollisionArea[collidersLength];

                for (int i = 0; i < collidersLength; i++)
                {
                    CollisionArea ca = new CollisionArea();
                    Collider collider = colliders[i];
                    if (collider == null)
                    {
                        Debug.LogError("One of the Colliders is null!");

                        colliders = new BoxCollider[0];
                        return;
                    }

                    if (collider.GetType() == typeof(BoxCollider))
                    {
                        BoxCollider bc = (BoxCollider)collider;
                        ca.type = 0;

                        Transform bcTransform = bc.transform;
                        Vector3 localScale = bcTransform.localScale;

                        Vector3 coll_pos = bc.transform.position;

                        Vector3 coll_size = bc.size;

                        coll_size.x *= localScale.x;
                        coll_size.y *= localScale.y;
                        coll_size.z *= localScale.z;

                        coll_size.x += colliderSizeOffset;
                        coll_size.y += colliderSizeOffset;
                        coll_size.z += colliderSizeOffset;

                        coll_pos.x -= coll_size.x / 2.0f;
                        coll_pos.y -= coll_size.y / 2.0f;
                        coll_pos.z -= coll_size.z / 2.0f;

                        ca.position = coll_pos;
                        ca.size = coll_size;
                    }
                    else if (collider.GetType() == typeof(SphereCollider))
                    {
                        SphereCollider sc = (SphereCollider)collider;
                        ca.type = 1;

                        Transform bcTransform = sc.transform;
                        Vector3 localScale = bcTransform.localScale;

                        Vector3 coll_pos = sc.transform.position;

                        float coll_size = sc.radius;
                        coll_size *= localScale.x;

                        ca.position = coll_pos;
                        ca.size.x = coll_size;
                    }

                    collisionData[i] = ca;
                }
            }

            if (computationMode == FlockComputationMode.GPU)
            {
                if (colliders.Length <= 0)
                    collisionData = new CollisionArea[1] { new CollisionArea() };

                if (collisionBuffer != null)
                    collisionBuffer.Release();

                collisionBuffer = new ComputeBuffer(collisionData.Length, sizeof(float) * 6 + sizeof(int));
                collisionBuffer.SetData(collisionData);
                collisionDataLength = colliders.Length <= 0 ? 0 : collisionData.Length;
            }
        }

        void CreateFishData()
        {
            if (initialized)
            {
                oldPositions = new Vector3[fishesData.Length];
                for (int i = 0; i < fishesData.Length; i++)
                {
                    oldPositions[i] = fishesData[i].position;
                }
            }

            fishesData = new FishData[currentFishesCount];
            if (renderingMode != FlockRenderingMode.INSTANCING) fishesTransforms = new Transform[currentFishesCount];

            for (int i = 0; i < currentFishesCount; i++)
            {
                fishesData[i] = CreateBehaviour();
                fishesData[i].speed_offset = Random.value * 10.0f;

                if (initialized)
                {
                    if (i < oldPositions.Length)
                        fishesData[i].position = oldPositions[i];
                }

                if (renderingMode != FlockRenderingMode.INSTANCING)
                {
                    fishesTransforms[i] = Instantiate(gfxProfile.prefab, fishesData[i].position, Quaternion.identity).transform;
                    fishesTransforms[i].localScale = fishesData[i].scale * Vector3.one;
                }
            }

            if (colliders.Length <= 0)
                collisionData = new CollisionArea[1] { new CollisionArea() };

            collisionDataLength = colliders.Length <= 0 ? 0 : collisionData.Length;

            if (renderingMode == FlockRenderingMode.INSTANCING || computationMode == FlockComputationMode.GPU)
            {
                fishBuffer = new ComputeBuffer(currentFishesCount, sizeof(float) * 10);
                fishBuffer.SetData(fishesData);

                if (_fishInstancedMaterial != null)
                    _fishInstancedMaterial.SetBuffer("fishBuffer", fishBuffer);
            }

            if (computationMode == FlockComputationMode.GPU)
            {
                kernelHandle = computeShader.FindKernel("CSMain");
                computeShader.SetBuffer(kernelHandle, "fishBuffer", fishBuffer);
            }

        }

        FishData CreateBehaviour()
        {
            FishData behaviour = new FishData();
            Vector3 pos = groupAnchor + Random.insideUnitSphere * spawnRadius;
            Quaternion rot = Quaternion.Slerp(transform.rotation, Random.rotation, 0.3f);

            switch (FishMovementAxis)
            {
                case FishMovementAxis.XY:
                    pos.z = rot.z = 0.0f;
                    break;
                case FishMovementAxis.XZ:
                    pos.y = rot.y = 0.0f;
                    break;
            }

            behaviour.position = pos;
            behaviour.velocity = rot.eulerAngles;

            behaviour.speed = Random.Range(speed.x, speed.y);
            behaviour.rot_speed = Random.Range(rotation.x, rotation.y);

            behaviour.scale = Random.Range(scale.x, scale.y);

            return behaviour;
        }

        Vector3 Evade(FishData data, Vector3 positionToEvade, Vector3 vel)
        {
            var dist = positionToEvade - data.position;
            var updatesAhead = dist.magnitude / data.speed;
            var futurePos = positionToEvade + vel * updatesAhead;

            return Flee(data, futurePos);
        }

        Vector3 Flee(FishData data, Vector3 positionToEvade)
        {
            Vector3 v = (data.position - positionToEvade).normalized * data.speed;
            return v - data.velocity;
        }

        void ComputeFlockBehaviours()
        {
            Vector3 temp;

            try
            {
                if (fishesData == null || fishesData.Length == 0) return;

                for (int i = 0; i < currentFishesCount; i++)
                {
                    FishData fish = fishesData[i];

                    if (FishMovementAxis == FishMovementAxis.XY) fish.position.z = 0.0f;
                    else if (FishMovementAxis == FishMovementAxis.XZ) fish.position.y = 0.0f;

                    var current_pos = fish.position;

                    var fish_velocity = fish.speed;

                    var separation = Vector3.zero;
                    var alignment = Vector3.zero;
                    var cohesion = groupAnchor;

                    //@Collisions!
                    Vector3 next_position = fish.position + (fish.velocity * lookAheadSteps) * (fish_velocity * unityDeltaTime);
                    Vector3 avoidance = new Vector3(0, 0, 0);
                    for (int c = 0; c < collisionDataLength; c++)
                    {
                        CollisionArea ca = collisionData[c];

                        Vector3 collider_pos = ca.position;
                        Vector3 collider_size = ca.size;

                        if (ca.type == 0)
                        {
                            if ((next_position.x >= collider_pos.x && next_position.x <= collider_pos.x + collider_size.x)
                                && (next_position.y >= collider_pos.y && next_position.y <= collider_pos.y + collider_size.y)
                                && (next_position.z >= collider_pos.z && next_position.z <= collider_pos.z + collider_size.z))
                            {

                                Vector3 coll_point = collider_pos;
                                coll_point.x += collider_size.x / 2.0f;
                                coll_point.y += collider_size.y / 2.0f;
                                coll_point.z += collider_size.z / 2.0f;

                                avoidance += next_position - coll_point;
                                avoidance = (avoidance.normalized * force);
                            }
                        }
                        else if (ca.type == 1)
                        {
                            if (VecMag(next_position - collider_pos) <= collider_size.x)
                            {
                                avoidance += next_position - collider_pos;
                                avoidance = (avoidance).normalized;
                                avoidance *= force;
                            }
                        }
                    }

                    var nearby_fishes_count = 1;

                    for (int j = 0; j < currentFishesCount; j++)
                    {
                        if (j == i) continue;

                        {
                            FishData other_fish = fishesData[j];
                            temp = current_pos - other_fish.position;

                            float len = VecMag(temp);
                            if (len < _neighbourDistance)
                            {
                                separation += GetSeparationVector(temp, len);
                                alignment += other_fish.velocity;
                                cohesion += other_fish.position;

                                nearby_fishes_count++;

                                if (nearby_fishes_count >= 5) break;
                            }
                        }
                    }

                    var avg = 1.0f / nearby_fishes_count;
                    alignment *= avg;
                    cohesion *= avg;

                    temp.x = cohesion.x - current_pos.x;
                    temp.y = cohesion.y - current_pos.y;
                    temp.z = cohesion.z - current_pos.z;

                    cohesion = (temp).normalized;

                    var velocity = separation + alignment + cohesion;
                    velocity += avoidance;

                    if (FishMovementAxis == FishMovementAxis.XY) velocity.z = 0.0f;
                    else if (FishMovementAxis == FishMovementAxis.XZ) velocity.y = 0.0f;


                    Vector3 fleeVec = Vector3.zero; ;
                    if (predators != null)
                    {
                        for (int j = 0; j < predators.Length; j++)
                        {
                            var predator = predators[j];
                            if ((fish.position - predator.transform.position).magnitude < predator.fleeRadius)
                            {
                                Vector3 flee = Flee(fish, predator.transform.position);
                                fleeVec += flee;
                            }
                        }
                    }

                    velocity += fleeVec;

                    var ip = Mathf.Exp(-fish.rot_speed * unityDeltaTime);

                    fish.velocity = Vector3.Lerp((velocity).normalized, (fish.velocity).normalized, ip);
                    fish.position += fish.velocity * (fish_velocity * unityDeltaTime);

                    fishesData[i] = fish;
                }
            }
            catch (System.Exception e)
            {
                // print(e.Message);
                // print(e.StackTrace);
            }
        }

        void Update()
        {
            unityTime = Time.time;
            unityDeltaTime = Time.deltaTime;

            if (oldFishesCount != fishesCount)
            {
                if (refreshFishCounter.Ended())
                {
                    // Dynamically change the fishes

                    ClearAll();
                    InitializeFishes();
                    CreateFishData();

                    //GC.Collect(); // Maybe?

                    refreshFishCounter.Reset();
                    return;
                }
            }

            if (refreshCollisionCounter.Ended() && updateAtRuntime)
            {
                UpdateCollisionData();
                refreshCollisionCounter.Reset();
            }

            UpdateGroupAnchor();


            switch (computationMode)
            {
                case FlockComputationMode.CPU:
                    {
                        ComputeFlockBehaviours();

                        //for (int i = 0; i < currentFishesCount; i++)
                        //{
                        //    FishData data = fishesData[i];
                        //    data.position += data.velocity * (data.speed * unityDeltaTime);
                        //    fishesData[i] = data;
                        //}

                        if (fishBuffer != null)
                            fishBuffer.SetData(fishesData);
                    }
                    break;
                case FlockComputationMode.CPU_AND_ONE_THREAD:
                    {
                        //for (int i = 0; i < currentFishesCount; i++)
                        //{
                        //    FishData data = fishesData[i];
                        //    data.position += data.velocity * (data.speed * unityDeltaTime);
                        //    fishesData[i] = data;
                        //}

                        if (fishBuffer != null)
                            fishBuffer.SetData(fishesData);
                    }
                    break;
                case FlockComputationMode.GPU:
                    {
                        switch (FishMovementAxis)
                        {
                            case FishMovementAxis.XY:
                                computeShader.SetInt("movementMode", 1);
                                break;
                            case FishMovementAxis.XZ:
                                computeShader.SetInt("movementMode", 2);
                                break;
                            default:
                                computeShader.SetInt("movementMode", 0);
                                break;
                        }

                        if (refreshPredatorsCounter.Ended())
                        {

                            if (predators != null && predators.Length > 0)
                            {
                                for (int i = 0; i < predatorsData.Length; i++)
                                {
                                    var predator = predators[i];
                                    PredatorData data = predatorsData[i];
                                    data.position = predator.transform.position;
                                    data.radius = predator.fleeRadius;

                                    predatorsData[i] = data;
                                }

                                predatorsBuffer.SetData(predatorsData);
                                predatorsCount = predatorsData.Length;
                            }
                            refreshPredatorsCounter.Reset();
                        }


                        computeShader.SetFloat("deltaTime", unityDeltaTime);

                        computeShader.SetVector("cohesionScale", cohesionScale);
                        computeShader.SetVector("target", groupAnchor);
                        computeShader.SetFloat("neighbourDistance", _neighbourDistance);
                        computeShader.SetInt("fishesCount", currentFishesCount);
                        computeShader.SetFloat("collisionForce", force);
                        computeShader.SetInt("collisionCount", collisionDataLength);

                        computeShader.SetInt("predatorsCount", predatorsCount);

                        computeShader.SetBuffer(kernelHandle, "fishBuffer", fishBuffer);
                        computeShader.SetBuffer(kernelHandle, "collisionBuffer", collisionBuffer);

                        if (predatorsBuffer != null)
                            computeShader.SetBuffer(kernelHandle, "predatorsBuffer", predatorsBuffer);

                        computeShader.Dispatch(kernelHandle, this.currentFishesCount / GROUP_SIZE + 1, 1, 1);
                    }
                    break;
            }

            switch (renderingMode)
            {
                case FlockRenderingMode.INSTANCING:
                    {
                        _fishInstancedMaterial.SetVector("offsetPosition", myTransform.position);
                        _fishInstancedMaterial.SetBuffer("fishBuffer", fishBuffer);
                    }
                    break;
                case FlockRenderingMode.GAMEOBJECT:
                    {
                        // Note: If has the fishBuffer and it is computing on GPU, you must collect the buffer data
                        if (fishBuffer != null && computationMode == FlockComputationMode.GPU)
                        {
                            fishBuffer.GetData(fishesData);

                            for (int i = 0; i < fishesTransforms.Length; i++)
                            {
                                Transform t = fishesTransforms[i];
                                Quaternion current_rot = t.rotation;
                                if (i < fishesData.Length)
                                {
                                    FishData data = fishesData[i];

                                    if (t)
                                    {
                                        t.position = data.position;
                                        var rotation = Quaternion.LookRotation((data.position + data.velocity) - data.position, Vector3.up);
                                        if (rotation != current_rot)
                                        {
                                            t.rotation = Quaternion.Slerp(rotation, current_rot, Time.deltaTime * data.rot_speed);
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.LogError("Fishes data smaller than transforms behaviours. This is using " +
                                            "a combination of Game Objects rendering + GPU computation, it shouldnt happen.");
                                }
                            }
                        }

                        for (int i = 0; i < fishesData.Length; i++)
                        {
                            FishData fish = fishesData[i];

                            Transform fish_transform = fishesTransforms[i];
                            fish_transform.position = fish.position;

                            Vector3 rotFwd = (fish.position + fish.velocity) - fish.position;
                            if (rotFwd != Vector3.zero)
                            {
                                fish_transform.rotation = Quaternion.LookRotation(rotFwd, Vector3.up);
                            }
                        }
                    }
                    break;
            }



            intendedNeighbourDistance = neighbourDistance;
            if (_neighbourDistance != intendedNeighbourDistance)
            {
                _neighbourDistance = Mathf.Lerp(_neighbourDistance, intendedNeighbourDistance, Time.smoothDeltaTime / neighbourDistanceRateSmoothRate);
                neighbourDistanceRateSmoothRate = Mathf.Lerp(neighbourDistanceRateSmoothRate, 1.0f, Time.smoothDeltaTime);
            }

            Rendering();
        }

        private void Rendering()
        {
            switch (renderingMode)
            {
                case FlockRenderingMode.INSTANCING:
                    {
                        instancingBounds.center = myTransform.position;

               //         Graphics.DrawMeshInstanced(
               //    gfxProfile.mesh, 0, _fishInstancedMaterial,
               //    null, fishesData.Length,
               //    props, gfxProfile.shadowCasting, gfxProfile.receiveShadows
               //);

                        Graphics.DrawMeshInstancedIndirect(
                            gfxProfile.mesh, 0, _fishInstancedMaterial,
                            instancingBounds,
                            drawArgsBuffer, 0, props, gfxProfile.shadowCasting, gfxProfile.receiveShadows, 0, null, UnityEngine.Rendering.LightProbeUsage.Off
                        );
                    }
                    break;
            }
        }

        void OnDestroy()
        {
            ClearAll();
        }

        void ClearAll()
        {
            if (threadProcess != null)
                ThreadManager.EndTask(threadProcess);

            fishesData = new FishData[0];

            switch (computationMode)
            {
                case FlockComputationMode.GPU:
                    {
                        computeShader.SetInt("fishesCount", 0);
                        fishBuffer.SetData(fishesData);

                        computeShader.SetBuffer(kernelHandle, "fishBuffer", fishBuffer);
                        computeShader.Dispatch(kernelHandle, 1, 1, 1);
                    }
                    break;
            }

            switch (renderingMode)
            {
                case FlockRenderingMode.INSTANCING:
                    {
                        if (_fishInstancedMaterial != null)
                            _fishInstancedMaterial.SetBuffer("fishBuffer", fishBuffer);
                    }
                    break;
                case FlockRenderingMode.GAMEOBJECT:
                    {
                        for (int i = 0; i < fishesTransforms.Length; i++)
                        {
                            Transform t = fishesTransforms[i];

                            if (t)
                            {
                                GameObject obj = t.gameObject;
                                obj.SetActive(false);

                                Destroy(obj, 0.3f);
                            }
                        }
                    }
                    break;
            }

            if (collisionBuffer != null) collisionBuffer.Release();
            if (fishBuffer != null) fishBuffer.Release();
            if (drawArgsBuffer != null) drawArgsBuffer.Release();
            if (predatorsBuffer != null) predatorsBuffer.Release();
        }

        void UpdateGroupAnchor()
        {
            float minX = myTransform.position.x - (swimmingAreaWidth / 2);
            float maxX = myTransform.position.x + (swimmingAreaWidth / 2);

            float minY = myTransform.position.y - (swimmingAreaHeight / 2);
            float maxY = myTransform.position.y + (swimmingAreaHeight / 2);

            float minZ = myTransform.position.z - (swimmingAreaDepth / 2);
            float maxZ = myTransform.position.z + (swimmingAreaDepth / 2);

            Vector3 futurePosition = myTransform.position;

            if (!followTarget && targetPositions.Length > 0)
            {
                if ((groupAnchor - targetPositions[currentTargetPosIndex]).magnitude < 1)
                {
                    currentTargetPosIndex++;

                    if (currentTargetPosIndex >= targetPositions.Length)
                    {
                        if (recalculatePoints)
                            GeneratePath();
                        else
                            currentTargetPosIndex = targetPositions.Length - 1;
                    }
                }

                Vector3 vel = (targetPositions[currentTargetPosIndex] - groupAnchor);
                futurePosition = groupAnchor + vel * Time.deltaTime * groupAreaSpeed;
            }
            else if (followTarget)
            {
                if (target != null)
                {
                    Vector3 vel = (target.position - groupAnchor);
                    futurePosition = groupAnchor + vel * Time.deltaTime * groupAreaSpeed;
                }
            }

            futurePosition.x = Mathf.Clamp(futurePosition.x, minX, maxX);
            futurePosition.y = Mathf.Clamp(futurePosition.y, minY, maxY);
            futurePosition.z = Mathf.Clamp(futurePosition.z, minZ, maxZ);

            groupAnchor = futurePosition;
        }

        void GeneratePath()
        {
            targetPositions = new Vector3[Random.Range(targetPointsAmount.x, targetPointsAmount.y)];
            for (int i = 0; i < targetPositions.Length; i++)
            {
                targetPositions[i] = RandomPos();
            }

            currentTargetPosIndex = 0;
        }

        public void SetGroupPosition(Vector3 at)
        {
            groupAnchor = at;

            fishBuffer.GetData(fishesData);

            for (int i = 0; i < fishesData.Length; i++)
            {
                var fish = fishesData[i];
                fish.position = groupAnchor + Random.insideUnitSphere * spawnRadius;

                fishesData[i] = fish;
            }

            fishBuffer.SetData(fishesData);
        }

        public Vector3 RandomPos()
        {
            var t = transform;
            float minX = t.position.x - (swimmingAreaWidth / 2);
            float maxX = t.position.x + (swimmingAreaWidth / 2);

            float minY = t.position.y - (swimmingAreaHeight / 2);
            float maxY = t.position.y + (swimmingAreaHeight / 2);

            float minZ = t.position.z - (swimmingAreaDepth / 2);
            float maxZ = t.position.z + (swimmingAreaDepth / 2);

            Vector3 tempPos;
            tempPos.x = Random.Range(minX, maxX);
            tempPos.y = Random.Range(minY, maxY);
            tempPos.z = Random.Range(minZ, maxZ);

            return tempPos;
        }

        Vector3 volumeSize;
        private void OnDrawGizmos()
        {
            if (!debugDraw) return;

            volumeSize.x = swimmingAreaWidth;
            volumeSize.y = swimmingAreaHeight;
            volumeSize.z = swimmingAreaDepth;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, volumeSize);

            if (Application.isPlaying && !followTarget)
            {
                Gizmos.color = Color.green;
                for (int i = 0; i < targetPositions.Length - 1; i++)
                {
                    Gizmos.DrawLine(targetPositions[i], targetPositions[i + 1]);

                    Gizmos.DrawWireSphere(targetPositions[i], 1f);

                    if ((i + 1) == targetPositions.Length - 1)
                        Gizmos.DrawWireSphere(targetPositions[i + 1], 1f);
                }
            }
        }

        Vector3 GetSeparationVector(Vector3 diff, float len)
        {
            var scaler = Mathf.Clamp01(1.0f - len / neighbourDistance);
            return diff * (scaler / len);
        }

        float VecMag(Vector3 v)
        {
            return Mathf.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
        }

        Vector3 VecNormalized(Vector3 v)
        {
            float len = VecMag(v);
            v.x = v.x / len;
            v.y = v.y / len;

            return v;
        }
    }
}
