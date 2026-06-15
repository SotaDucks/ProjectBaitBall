using TestBoids.Gameplay.Lure;
using UnityEngine;

namespace TestBoids.Tuna
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TunaLureHookConnector : MonoBehaviour
    {
        private const string DefaultMouthMountName = "LureInMouthPosition";

        [Header("References")]
        [SerializeField] private Transform mouthMount;
        [SerializeField] private Rigidbody tunaBody;
        [SerializeField] private bool autoResolveReferences = true;

        [Header("Joint")]
        [SerializeField] private bool autoConfigureConnectedAnchor = true;
        [SerializeField] private float breakForce = Mathf.Infinity;
        [SerializeField] private float breakTorque = Mathf.Infinity;

        private FixedJoint activeJoint;
        private Rigidbody connectedLureBody;

        public bool HasConnectedLure => activeJoint && connectedLureBody;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        public bool TryConnectLure(AutomaticLureMotor lure)
        {
            if (!lure)
            {
                return false;
            }

            ResolveReferences();
            Rigidbody lureBody = lure.GetComponent<Rigidbody>();
            if (!mouthMount || !tunaBody || !lureBody)
            {
                Debug.LogError(
                    $"{nameof(TunaLureHookConnector)} on {name} could not connect {lure.name}. "
                    + $"Mouth mount, Tuna Rigidbody, and lure Rigidbody are required.",
                    this);
                return false;
            }

            if (HasConnectedLure)
            {
                return connectedLureBody == lureBody;
            }

            lure.enabled = false;
            lureBody.linearVelocity = Vector3.zero;
            lureBody.angularVelocity = Vector3.zero;
            lureBody.position = mouthMount.position;
            lureBody.rotation = mouthMount.rotation;

            activeJoint = tunaBody.gameObject.AddComponent<FixedJoint>();
            activeJoint.connectedBody = lureBody;
            activeJoint.autoConfigureConnectedAnchor = autoConfigureConnectedAnchor;
            activeJoint.breakForce = breakForce;
            activeJoint.breakTorque = breakTorque;
            connectedLureBody = lureBody;
            return true;
        }

        private void ResolveReferences()
        {
            if (!autoResolveReferences)
            {
                return;
            }

            if (!mouthMount)
            {
                mouthMount = FindChildByName(transform, DefaultMouthMountName);
            }

            if (!tunaBody)
            {
                tunaBody = GetComponent<Rigidbody>();
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
    }
}
