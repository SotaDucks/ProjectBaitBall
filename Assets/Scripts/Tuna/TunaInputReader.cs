using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TestBoids.Tuna
{
    public sealed class TunaInputReader : MonoBehaviour
    {
        [Header("Input Actions")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference lookAction;
        [Tooltip("Optional action treated as a sprint click. Assign Player/Attack to use the left mouse button binding.")]
        [SerializeField] private InputActionReference sprintClickAction;
        [SerializeField] private bool enableActionsOnEnable = true;

        [Header("Cursor")]
        [SerializeField] private bool lockCursorOnEnable = true;
        [SerializeField] private bool unlockCursorOnDisable;

        private bool enabledMoveAction;
        private bool enabledLookAction;
        private bool enabledSprintClickAction;
        private float lookSuppressedUntil;
        private readonly Queue<float> sprintClickTimes = new Queue<float>();
        private const int MaxBufferedSprintClicks = 12;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }

        public void ClearLook()
        {
            Look = Vector2.zero;
        }

        public void ClearSprintClicks()
        {
            sprintClickTimes.Clear();
        }

        public bool TryConsumeSprintClick(out float clickTime)
        {
            if (sprintClickTimes.Count <= 0)
            {
                clickTime = 0f;
                return false;
            }

            clickTime = sprintClickTimes.Dequeue();
            return true;
        }

        public void SuppressLookForSeconds(float duration)
        {
            ClearLook();

            if (duration <= 0f)
            {
                return;
            }

            lookSuppressedUntil = Mathf.Max(lookSuppressedUntil, Time.unscaledTime + duration);
        }

        private void OnEnable()
        {
            if (enableActionsOnEnable)
            {
                enabledMoveAction = EnableAction(moveAction);
                enabledLookAction = EnableAction(lookAction);
                enabledSprintClickAction = EnableAction(sprintClickAction);
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
            sprintClickTimes.Clear();

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

            if (enabledSprintClickAction)
            {
                sprintClickAction.action.Disable();
                enabledSprintClickAction = false;
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
            Look = IsLookSuppressed() ? Vector2.zero : ReadVector2(lookAction);

            if (ReadSprintClickPressedThisFrame())
            {
                sprintClickTimes.Enqueue(Time.time);
                while (sprintClickTimes.Count > MaxBufferedSprintClicks)
                {
                    sprintClickTimes.Dequeue();
                }
            }
        }

        private bool IsLookSuppressed()
        {
            return Time.unscaledTime < lookSuppressedUntil;
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

        private bool ReadSprintClickPressedThisFrame()
        {
            InputAction action = sprintClickAction ? sprintClickAction.action : null;
            if (action != null && action.enabled)
            {
                return action.WasPressedThisFrame();
            }

            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        }
    }
}
