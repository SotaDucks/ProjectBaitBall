using UnityEngine;
using UnityEngine.InputSystem;

namespace TestBoids.Tuna
{
    public sealed class TunaInputReader : MonoBehaviour
    {
        [Header("Input Actions")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private bool enableActionsOnEnable = true;

        [Header("Cursor")]
        [SerializeField] private bool lockCursorOnEnable = true;
        [SerializeField] private bool unlockCursorOnDisable;

        private bool enabledMoveAction;
        private bool enabledLookAction;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }

        private void OnEnable()
        {
            if (enableActionsOnEnable)
            {
                enabledMoveAction = EnableAction(moveAction);
                enabledLookAction = EnableAction(lookAction);
            }

            if (lockCursorOnEnable)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            Move = Vector2.zero;
            Look = Vector2.zero;

            if (enabledMoveAction)
            {
                moveAction.action.Disable();
                enabledMoveAction = false;
            }

            if (enabledLookAction)
            {
                lookAction.action.Disable();
                enabledLookAction = false;
            }

            if (unlockCursorOnDisable)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            Move = ReadVector2(moveAction);
            Look = ReadVector2(lookAction);
        }

        private static bool EnableAction(InputActionReference actionReference)
        {
            InputAction action = actionReference ? actionReference.action : null;
            if (action == null || action.enabled)
            {
                return false;
            }

            action.Enable();
            return true;
        }

        private static Vector2 ReadVector2(InputActionReference actionReference)
        {
            InputAction action = actionReference ? actionReference.action : null;
            return action != null ? action.ReadValue<Vector2>() : Vector2.zero;
        }
    }
}
