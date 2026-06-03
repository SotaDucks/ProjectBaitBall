using UnityEngine;

namespace TestBoids.Tuna
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TunaFreezeController : MonoBehaviour
    {
        [SerializeField] private TunaMotor tunaMotor;
        [SerializeField] private Rigidbody body;
        [SerializeField] private bool disableTunaMotor = true;
        [SerializeField] private bool restoreVelocityOnUnfreeze = true;

        private bool frozen;
        private bool tunaMotorWasEnabled;
        private RigidbodyConstraints originalConstraints;
        private Vector3 storedLinearVelocity;
        private Vector3 storedAngularVelocity;

        public bool IsFrozen => frozen;

        private void Reset()
        {
            tunaMotor = GetComponent<TunaMotor>();
            body = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            Unfreeze();
        }

        public void Freeze()
        {
            if (frozen)
            {
                return;
            }

            ResolveReferences();
            frozen = true;

            if (body)
            {
                originalConstraints = body.constraints;
                storedLinearVelocity = body.linearVelocity;
                storedAngularVelocity = body.angularVelocity;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.constraints = RigidbodyConstraints.FreezeAll;
            }

            if (disableTunaMotor && tunaMotor)
            {
                tunaMotorWasEnabled = tunaMotor.enabled;
                tunaMotor.enabled = false;
            }
        }

        public void Unfreeze()
        {
            if (!frozen)
            {
                return;
            }

            if (body)
            {
                body.constraints = originalConstraints;

                if (restoreVelocityOnUnfreeze)
                {
                    body.linearVelocity = storedLinearVelocity;
                    body.angularVelocity = storedAngularVelocity;
                }
            }

            if (disableTunaMotor && tunaMotor)
            {
                tunaMotor.enabled = tunaMotorWasEnabled;
            }

            frozen = false;
        }

        private void ResolveReferences()
        {
            if (!tunaMotor)
            {
                tunaMotor = GetComponent<TunaMotor>();
            }

            if (!body)
            {
                body = GetComponent<Rigidbody>();
            }
        }
    }
}
