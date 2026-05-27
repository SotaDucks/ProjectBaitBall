using System;
using FishFlock.Utils;
using UnityEngine;

namespace TestBoids.Boids
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class BaitBallBehaviorModulator : MonoBehaviour
    {
        [SerializeField] private InstancedFishSchoolManager target;
        [SerializeField] private bool modulateOnPlay = true;
        [SerializeField] private bool restoreOriginalWeightsOnDisable = true;
        [SerializeField] private int seed = 9127;

        [Header("Safety Limits")]
        [SerializeField, MinMax(0f, 4f)] private Vector2 toroidalFlowLimits = new(0.8f, 2.8f);
        [SerializeField, MinMax(0f, 4f)] private Vector2 alignLimits = new(0.3f, 2.3f);
        [SerializeField, MinMax(0f, 4f)] private Vector2 cohesionLimits = new(0.4f, 2f);

        [Header("States")]
        [SerializeField] private BehaviorState[] states =
        {
            new("Calm", new Vector2(1.2f, 1.6f), new Vector2(1.1f, 1.7f), new Vector2(0.7f, 1.1f)),
            new("Swirl", new Vector2(2f, 2.7f), new Vector2(0.5f, 1.1f), new Vector2(0.8f, 1.4f)),
            new("Pulse", new Vector2(1.5f, 2.2f), new Vector2(1f, 1.8f), new Vector2(1.3f, 1.8f)),
            new("Loose", new Vector2(1f, 1.5f), new Vector2(0.4f, 0.9f), new Vector2(0.5f, 0.9f))
        };

        private System.Random random;
        private int currentStateIndex = -1;
        private float dwellRemaining;
        private float transitionDuration;
        private float transitionElapsed;
        private BehaviorWeights originalWeights;
        private BehaviorWeights startWeights;
        private BehaviorWeights targetWeights;
        private bool capturedOriginalWeights;

        public string CurrentStateName
        {
            get
            {
                if (currentStateIndex < 0 || states == null || currentStateIndex >= states.Length)
                {
                    return string.Empty;
                }

                return states[currentStateIndex].Name;
            }
        }

        private void Reset()
        {
            target = GetComponent<InstancedFishSchoolManager>();
        }

        private void OnEnable()
        {
            ResolveTarget();
            random = new System.Random(seed);
            capturedOriginalWeights = false;
            currentStateIndex = -1;

            if (!Application.isPlaying || !modulateOnPlay || !target)
            {
                return;
            }

            CaptureAndEnterInitialState();
        }

        private void OnDisable()
        {
            if (Application.isPlaying && restoreOriginalWeightsOnDisable && target && capturedOriginalWeights)
            {
                ApplyWeights(originalWeights);
            }
        }

        private void OnValidate()
        {
            toroidalFlowLimits = Ordered(toroidalFlowLimits);
            alignLimits = Ordered(alignLimits);
            cohesionLimits = Ordered(cohesionLimits);

            if (states == null)
            {
                return;
            }

            for (int i = 0; i < states.Length; i++)
            {
                states[i] ??= new BehaviorState();
                states[i].Normalize();
            }
        }

        private void Update()
        {
            if (!modulateOnPlay || !Application.isPlaying)
            {
                return;
            }

            ResolveTarget();
            if (!target || states == null || states.Length == 0)
            {
                return;
            }

            if (!capturedOriginalWeights)
            {
                CaptureAndEnterInitialState();
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            dwellRemaining -= dt;
            transitionElapsed += dt;

            float t = transitionDuration <= 0f ? 1f : Mathf.Clamp01(transitionElapsed / transitionDuration);
            t = t * t * (3f - 2f * t);
            ApplyWeights(BehaviorWeights.Lerp(startWeights, targetWeights, t));

            if (dwellRemaining <= 0f)
            {
                EnterNextState(false);
            }
        }

        private void CaptureAndEnterInitialState()
        {
            originalWeights = ReadTargetWeights();
            capturedOriginalWeights = true;
            startWeights = originalWeights;
            targetWeights = originalWeights;
            EnterNextState(true);
        }

        private void ResolveTarget()
        {
            if (!target)
            {
                target = GetComponent<InstancedFishSchoolManager>();
            }
        }

        private void EnterNextState(bool immediate)
        {
            if (!target || states == null || states.Length == 0)
            {
                return;
            }

            NormalizeStates();
            int nextStateIndex = PickStateIndex();
            BehaviorState nextState = states[nextStateIndex];
            currentStateIndex = nextStateIndex;

            startWeights = immediate ? ReadTargetWeights() : targetWeights;
            targetWeights = Clamp(nextState.Sample(random));
            dwellRemaining = Mathf.Max(0.1f, nextState.SampleDuration(random));
            transitionDuration = immediate ? 0f : Mathf.Min(dwellRemaining, Mathf.Max(0f, nextState.SampleTransition(random)));
            transitionElapsed = 0f;

            if (immediate)
            {
                ApplyWeights(targetWeights);
            }
        }

        private int PickStateIndex()
        {
            if (states.Length == 1)
            {
                return 0;
            }

            float totalWeight = 0f;
            for (int i = 0; i < states.Length; i++)
            {
                if (i != currentStateIndex)
                {
                    totalWeight += Mathf.Max(0f, states[i].SelectionWeight);
                }
            }

            if (totalWeight <= 0f)
            {
                return (currentStateIndex + 1) % states.Length;
            }

            float roll = Range(0f, totalWeight, random);
            for (int i = 0; i < states.Length; i++)
            {
                if (i == currentStateIndex)
                {
                    continue;
                }

                roll -= Mathf.Max(0f, states[i].SelectionWeight);
                if (roll <= 0f)
                {
                    return i;
                }
            }

            return (currentStateIndex + 1) % states.Length;
        }

        private BehaviorWeights ReadTargetWeights()
        {
            target.GetBehaviorWeights(out float flow, out float align, out float cohesion);
            return new BehaviorWeights(flow, align, cohesion);
        }

        private void ApplyWeights(BehaviorWeights weights)
        {
            weights = Clamp(weights);
            target.SetBehaviorWeights(weights.ToroidalFlow, weights.Align, weights.Cohesion);
        }

        private BehaviorWeights Clamp(BehaviorWeights weights)
        {
            return new BehaviorWeights(
                Mathf.Clamp(weights.ToroidalFlow, toroidalFlowLimits.x, toroidalFlowLimits.y),
                Mathf.Clamp(weights.Align, alignLimits.x, alignLimits.y),
                Mathf.Clamp(weights.Cohesion, cohesionLimits.x, cohesionLimits.y));
        }

        private static Vector2 Ordered(Vector2 range)
        {
            return range.x <= range.y ? range : new Vector2(range.y, range.x);
        }

        private static float Range(float min, float max, System.Random source)
        {
            return Mathf.Lerp(min, max, (float)source.NextDouble());
        }

        private void NormalizeStates()
        {
            for (int i = 0; i < states.Length; i++)
            {
                states[i] ??= new BehaviorState();
                states[i].Normalize();
            }
        }

        [Serializable]
        private sealed class BehaviorState
        {
            [SerializeField] private string name;
            [SerializeField, MinMax(0f, 4f)] private Vector2 toroidalFlowRange;
            [SerializeField, MinMax(0f, 4f)] private Vector2 alignRange;
            [SerializeField, MinMax(0f, 4f)] private Vector2 cohesionRange;
            [SerializeField, MinMax(0.25f, 30f)] private Vector2 durationRange = new(6f, 12f);
            [SerializeField, MinMax(0f, 12f)] private Vector2 transitionRange = new(2f, 5f);
            [SerializeField, Min(0f)] private float selectionWeight = 1f;

            public BehaviorState()
                : this("State", new Vector2(1.2f, 1.8f), new Vector2(0.8f, 1.4f), new Vector2(0.8f, 1.3f))
            {
            }

            public BehaviorState(string name, Vector2 toroidalFlowRange, Vector2 alignRange, Vector2 cohesionRange)
            {
                this.name = name;
                this.toroidalFlowRange = toroidalFlowRange;
                this.alignRange = alignRange;
                this.cohesionRange = cohesionRange;
            }

            public string Name => name;
            public float SelectionWeight => selectionWeight;

            public BehaviorWeights Sample(System.Random random)
            {
                return new BehaviorWeights(
                    Range(toroidalFlowRange.x, toroidalFlowRange.y, random),
                    Range(alignRange.x, alignRange.y, random),
                    Range(cohesionRange.x, cohesionRange.y, random));
            }

            public float SampleDuration(System.Random random)
            {
                return Range(durationRange.x, durationRange.y, random);
            }

            public float SampleTransition(System.Random random)
            {
                return Range(transitionRange.x, transitionRange.y, random);
            }

            public void Normalize()
            {
                toroidalFlowRange = Ordered(toroidalFlowRange);
                alignRange = Ordered(alignRange);
                cohesionRange = Ordered(cohesionRange);
                durationRange = Ordered(durationRange);
                transitionRange = Ordered(transitionRange);
            }
        }

        private readonly struct BehaviorWeights
        {
            public readonly float ToroidalFlow;
            public readonly float Align;
            public readonly float Cohesion;

            public BehaviorWeights(float toroidalFlow, float align, float cohesion)
            {
                ToroidalFlow = toroidalFlow;
                Align = align;
                Cohesion = cohesion;
            }

            public static BehaviorWeights Lerp(BehaviorWeights from, BehaviorWeights to, float t)
            {
                return new BehaviorWeights(
                    Mathf.Lerp(from.ToroidalFlow, to.ToroidalFlow, t),
                    Mathf.Lerp(from.Align, to.Align, t),
                    Mathf.Lerp(from.Cohesion, to.Cohesion, t));
            }
        }
    }
}
