using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using AO.Unity;
using AO.Unity.Prototype;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AO.Unity.World
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PrototypeWalkerController : MonoBehaviour
    {
        public enum MovementMode
        {
            Grounded = 0,
            Flight = 1
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
#endif

        [SerializeField] private float moveSpeed = 8f;
        [SerializeField] private float walkSpeed = 3f;
        [SerializeField] private float sprintMultiplier = 1.75f;
        [SerializeField] private float turnSpeedDegrees = 140f;
        [SerializeField] private float gravity = 20f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float lookSensitivity = 4f;
        [SerializeField] private float maxLookPitch = 80f;
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 1.6f, -4.5f);
        [SerializeField] private bool lockCursor = false;
        [SerializeField] private float minCameraDistance = 1.5f;
        [SerializeField] private float maxCameraDistance = 14f;
        [SerializeField] private float cameraZoomStep = 0.75f;
        [Header("Camera Collision")]
        [SerializeField] private bool enableCameraCollision = true;
        [SerializeField] private LayerMask cameraCollisionMask = ~0;
        [SerializeField] private float cameraCollisionRadius = 0.25f;
        [SerializeField] private float cameraCollisionPadding = 0.08f;
        [SerializeField] private float minCameraCollisionDistance = 0.2f;
        [SerializeField] private bool allowAoCoordHotkey = true;
        [SerializeField] private bool localMovementEnabled = true;
        [SerializeField] private float authoritativeCorrectionSpeed = 10f;
        [SerializeField] private float maxAuthoritativeErrorDistance = 1.25f;
        [SerializeField] private float authoritativeSnapDistance = 3f;
        [SerializeField] private MovementMode movementMode = MovementMode.Grounded;
        [SerializeField] private float flightVerticalSpeed = 8f;

        private CharacterController _controller;
        private Transform _cam;
        private float _cameraYaw;
        private float _cameraDistance;
        private float _pitch;
        private float _verticalVelocity;
        private bool _warnedInputBackend;
        private PrototypeWorldBootstrap _bootstrap;
        private CharacterAppearanceController _appearanceController;
        private bool _leftLookDragActive;
        private bool _rightLookDragActive;
        private bool _dragRestorePending;
        private Vector2 _dragStartPointerPos;
        private Vector3 _authoritativeTargetPosition;
        private bool _hasAuthoritativeTarget;
        private Vector3 _lastMovementIntent = Vector3.zero;
        private bool _movementBlockedByCollision;
        private Vector3 _blockedMovementDirection = Vector3.zero;
        private readonly RaycastHit[] _cameraCollisionHits = new RaycastHit[32];

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _controller.height = 1.8f;
            _controller.radius = 0.35f;
            _controller.center = new Vector3(0f, 0.9f, 0f);

            _cam = Camera.main != null ? Camera.main.transform : null;
            _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            _appearanceController = GetComponent<CharacterAppearanceController>();
            _cameraYaw = transform.eulerAngles.y;
            _cameraDistance = Mathf.Clamp(Mathf.Abs(cameraOffset.z), minCameraDistance, maxCameraDistance);
            if (cameraOffset.z > 0f)
                _cameraDistance *= -1f;

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            ApplyMovementModePresentation();
        }

        private void Start()
        {
            RecenterCameraBehindCharacter();
            SnapToGround();
        }

        private void Update()
        {
            HandleLook();
            HandleMove();
            HandleCameraZoom();
            HandleAoCoordHotkey();
        }

        private void LateUpdate()
        {
            if (_cam == null && Camera.main != null)
                _cam = Camera.main.transform;
            if (_cam == null)
                return;

            var pivot = transform.position + Vector3.up * cameraOffset.y;
            var orbit = Quaternion.Euler(_pitch, _cameraYaw, 0f);
            Vector3 desiredDirection = orbit * Vector3.back;
            float desiredDistance = Mathf.Abs(_cameraDistance);
            float resolvedDistance = ResolveCameraDistanceWithCollision(pivot, desiredDirection, desiredDistance);
            var desired = pivot + (desiredDirection * resolvedDistance);
            _cam.position = desired;
            _cam.LookAt(pivot);
        }

        private float ResolveCameraDistanceWithCollision(Vector3 pivot, Vector3 direction, float desiredDistance)
        {
            if (!enableCameraCollision || desiredDistance <= 0.0001f)
                return desiredDistance;

            float castRadius = Mathf.Max(0.01f, cameraCollisionRadius);
            float castPadding = Mathf.Max(0f, cameraCollisionPadding);
            int hitCount = Physics.SphereCastNonAlloc(
                pivot,
                castRadius,
                direction,
                _cameraCollisionHits,
                desiredDistance + castPadding,
                cameraCollisionMask,
                QueryTriggerInteraction.Ignore);

            if (hitCount <= 0)
                return desiredDistance;

            float nearest = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                var hit = _cameraCollisionHits[i];
                var col = hit.collider;
                if (col == null)
                    continue;

                // Ignore the controlled character so the camera can sit behind them naturally.
                if (col.transform == transform || col.transform.IsChildOf(transform))
                    continue;

                if (hit.distance < nearest)
                    nearest = hit.distance;
            }

            if (float.IsInfinity(nearest))
                return desiredDistance;

            float blockedDistance = nearest - castPadding;
            float minBlockedDistance = Mathf.Max(0.01f, minCameraCollisionDistance);
            return Mathf.Clamp(blockedDistance, minBlockedDistance, desiredDistance);
        }

        private void HandleLook()
        {
            var look = ReadLookInput() * lookSensitivity;
            float mx = look.x;
            float my = look.y;

            bool wasAnyDragActive = _leftLookDragActive || _rightLookDragActive;

            if (WasLeftMousePressedThisFrame() && !IsPointerOverBlockingUi() && IsPointerOverWorld())
            {
                if (!wasAnyDragActive)
                    _dragStartPointerPos = ReadPointerPosition();
                _leftLookDragActive = true;
                _dragRestorePending = true;
            }
            if (WasRightMousePressedThisFrame() && !IsPointerOverBlockingUi() && IsPointerOverWorld())
            {
                if (!wasAnyDragActive)
                    _dragStartPointerPos = ReadPointerPosition();
                _rightLookDragActive = true;
                _dragRestorePending = true;
            }
            if (WasLeftMouseReleasedThisFrame())
                _leftLookDragActive = false;
            if (WasRightMouseReleasedThisFrame())
                _rightLookDragActive = false;

            bool anyDragActive = _leftLookDragActive || _rightLookDragActive;
            if (!anyDragActive && wasAnyDragActive && _dragRestorePending)
            {
                WarpPointerToScreenPos(_dragStartPointerPos);
                _dragRestorePending = false;
            }

            if (_leftLookDragActive)
            {
                _cameraYaw += mx;
                _pitch = Mathf.Clamp(_pitch - my, -maxLookPitch, maxLookPitch);
            }

            if (_rightLookDragActive)
            {
                transform.Rotate(0f, mx, 0f, Space.World);
                _cameraYaw = transform.eulerAngles.y;
                _pitch = Mathf.Clamp(_pitch - my, -maxLookPitch, maxLookPitch);
            }

            UpdateDragCursorState();
        }

        private static bool IsPointerOverBlockingUi()
        {
            if (EventSystem.current == null)
                return false;

            Vector2 pointerPos = ReadPointerPosition();
            var eventData = new PointerEventData(EventSystem.current)
            {
                position = pointerPos
            };

            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            return results.Count > 0;
        }

        private void UpdateDragCursorState()
        {
            if (lockCursor)
                return;

            bool dragging = _leftLookDragActive || _rightLookDragActive;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = !dragging;
        }

        private static bool IsPointerOverWorld()
        {
            var cam = Camera.main;
            if (cam == null)
                return false;

            var ray = cam.ScreenPointToRay(ReadPointerPosition());
            return Physics.Raycast(ray, out _, 5000f, ~0, QueryTriggerInteraction.Ignore);
        }

        private static void WarpPointerToScreenPos(Vector2 pos)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                mouse.WarpCursorPosition(pos);
                return;
            }
#endif
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var guiPos = new Vector2(pos.x, Screen.height - pos.y);
            var screenPos = GUIUtility.GUIToScreenPoint(guiPos);
            SetCursorPos(Mathf.RoundToInt(screenPos.x), Mathf.RoundToInt(screenPos.y));
#endif
        }

        private static Vector2 ReadPointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }

        private bool IsGameplayInputBlockedByUi()
        {
            return UiInputUtility.IsTextInputFocused();
        }

        private void HandleMove()
        {
            if (IsGameplayInputBlockedByUi())
            {
                _lastMovementIntent = Vector3.zero;
                _movementBlockedByCollision = false;
                _blockedMovementDirection = Vector3.zero;
                MoveVerticalOnly();
                return;
            }

            if (_appearanceController == null)
                _appearanceController = GetComponent<CharacterAppearanceController>();

            if (movementMode != MovementMode.Flight)
            {
                float turn = ReadTurnInput();
                if (!IsRightMouseHeld() && Mathf.Abs(turn) > 0.001f)
                {
                    transform.Rotate(0f, turn * turnSpeedDegrees * Time.deltaTime, 0f, Space.World);
                    _cameraYaw = transform.eulerAngles.y;
                }
            }

            if (_appearanceController != null && _appearanceController.IsSitting)
            {
                _lastMovementIntent = Vector3.zero;
                _movementBlockedByCollision = false;
                _blockedMovementDirection = Vector3.zero;
                MoveVerticalOnly();
                return;
            }

            float forward = ReadForwardInput();
            float strafe = ReadStrafeInput();
            _lastMovementIntent = BuildMovementIntent(forward, strafe);

            if (movementMode == MovementMode.Flight)
            {
                var flightHorizontal = ComputeFlightHorizontalMotion();
                _lastMovementIntent = flightHorizontal.sqrMagnitude > 0.0001f ? flightHorizontal.normalized : Vector3.zero;
                HandleFlight(flightHorizontal);
                return;
            }

            var horizontal = ComputeHorizontalMotion(forward, strafe);

            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
                if (WantsJump())
                    _verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            }
            else
            {
                _verticalVelocity -= gravity * Time.deltaTime;
            }

            if (localMovementEnabled)
            {
                var motion = horizontal;
                motion.y = _verticalVelocity;
                var collisionFlags = _controller.Move(motion * Time.deltaTime);
                UpdateBlockedMovementState(collisionFlags, _lastMovementIntent);
            }
            else
            {
                ApplyAuthoritativeHorizontalMotion();
                MoveVerticalOnly();
            }
        }

        public Vector3 GetMovementIntentWorld()
        {
            float forward = ReadForwardInput();
            float strafe = ReadStrafeInput();
            var intent = BuildMovementIntent(forward, strafe);
            if (_movementBlockedByCollision && intent.sqrMagnitude > 0.0001f)
                return Vector3.zero;

            return intent;
        }

        public void SetLocalMovementEnabled(bool enabled)
        {
            localMovementEnabled = enabled;
            if (enabled)
            {
                _hasAuthoritativeTarget = false;
                _authoritativeTargetPosition = transform.position;
            }
        }

        public float GetRunSpeed() => moveSpeed;

        public void SetRunSpeed(float value)
        {
            moveSpeed = Mathf.Max(0.1f, value);
        }

        public float GetJumpHeight() => jumpHeight;

        public void SetJumpHeight(float value)
        {
            jumpHeight = Mathf.Max(0f, value);
        }

        public MovementMode GetMovementMode() => movementMode;

        public void SetMovementMode(MovementMode value)
        {
            if (movementMode == value)
                return;

            MovementMode previousMode = movementMode;
            movementMode = value;
            _verticalVelocity = 0f;
            ApplyMovementModePresentation();

            if (previousMode == MovementMode.Flight && movementMode == MovementMode.Grounded)
                SnapToGround();
        }

        public void ApplyAuthoritativePosition(Vector3 position)
        {
            var current = transform.position;
            var target = new Vector3(position.x, current.y, position.z);
            var horizontalError = target - current;
            horizontalError.y = 0f;

            bool hasMoveIntent = _lastMovementIntent.sqrMagnitude > 0.0001f;
            if (!hasMoveIntent)
            {
                float snapDistance = Mathf.Max(0.5f, authoritativeSnapDistance);
                if (horizontalError.sqrMagnitude <= snapDistance * snapDistance)
                {
                    _authoritativeTargetPosition = current;
                    _hasAuthoritativeTarget = false;
                    return;
                }
            }

            float maxError = Mathf.Max(0.1f, maxAuthoritativeErrorDistance);
            if (horizontalError.sqrMagnitude > maxError * maxError)
                target = current + Vector3.ClampMagnitude(horizontalError, maxError);

            _authoritativeTargetPosition = target;
            _hasAuthoritativeTarget = true;
        }

        public void ApplyAuthoritativeTeleportPosition(Vector3 position)
        {
            if (_controller == null)
                _controller = GetComponent<CharacterController>();

            bool wasEnabled = _controller != null && _controller.enabled;
            if (wasEnabled)
                _controller.enabled = false;

            transform.position = position;

            if (wasEnabled)
                _controller.enabled = true;

            _verticalVelocity = 0f;
            _authoritativeTargetPosition = transform.position;
            _hasAuthoritativeTarget = false;
            _movementBlockedByCollision = false;
            _blockedMovementDirection = Vector3.zero;
            _lastMovementIntent = Vector3.zero;
            // Re-center camera behind character after server-authoritative reposition.
            RecenterCameraBehindCharacter();
        }

        public void RecenterCameraBehindCharacter()
        {
            _cameraYaw = transform.eulerAngles.y;
            _pitch = 0f;
        }

        private void ApplyAuthoritativeHorizontalMotion()
        {
            if (!_hasAuthoritativeTarget || _controller == null)
                return;

            var current = transform.position;
            var delta = _authoritativeTargetPosition - current;
            delta.y = 0f;

            if (delta.sqrMagnitude <= 0.000001f)
                return;

            bool hasMoveIntent = _lastMovementIntent.sqrMagnitude > 0.0001f;
            float snapDistance = Mathf.Max(0.5f, authoritativeSnapDistance);
            if (!hasMoveIntent)
            {
                _authoritativeTargetPosition = current;
                _hasAuthoritativeTarget = false;
                return;
            }

            float maxStep = Mathf.Max(0.01f, authoritativeCorrectionSpeed) * Time.deltaTime;
            var horizontalStep = Vector3.ClampMagnitude(delta, maxStep);
            var collisionFlags = _controller.Move(horizontalStep);

            // If terrain/buildings block the correction, discard the queued catch-up so we do not
            // keep sliding toward an unreachable server position.
            if ((collisionFlags & CollisionFlags.Sides) != 0)
            {
                _authoritativeTargetPosition = transform.position;
                _hasAuthoritativeTarget = false;
            }
        }

        private Vector3 ComputeHorizontalMotion(float forward, float strafe)
        {
            var wish = BuildMovementIntent(forward, strafe);
            bool walking = _appearanceController != null && _appearanceController.IsWalkModeEnabled;
            float baseSpeed = walking ? walkSpeed : moveSpeed;
            float speed = baseSpeed * ((!walking && IsSprinting()) ? sprintMultiplier : 1f);
            return wish * speed;
        }

        private void HandleFlight(Vector3 horizontal)
        {
            float vertical = ReadFlightVerticalInput();
            var motion = horizontal + Vector3.up * (vertical * flightVerticalSpeed);

            if (localMovementEnabled)
            {
                _controller.Move(motion * Time.deltaTime);
                _movementBlockedByCollision = false;
                _blockedMovementDirection = Vector3.zero;
            }
            else
            {
                ApplyAuthoritativeHorizontalMotion();
                if (Mathf.Abs(vertical) > 0.0001f)
                    _controller.Move(Vector3.up * (vertical * flightVerticalSpeed * Time.deltaTime));
            }
        }

        private Vector3 ComputeFlightHorizontalMotion()
        {
            Vector2 move = ReadMoveInput();
            Vector3 wish;
            if (_cam != null && IsRightMouseHeld())
            {
                Vector3 forward = _cam.forward;
                Vector3 right = _cam.right;
                wish = forward * move.y + right * move.x;
                if (wish.sqrMagnitude > 0.0001f)
                    wish.Normalize();
            }
            else
            {
                wish = BuildMovementIntent(move.y, move.x);
            }

            bool walking = _appearanceController != null && _appearanceController.IsWalkModeEnabled;
            float baseSpeed = walking ? walkSpeed : moveSpeed;
            float speed = baseSpeed * ((!walking && IsSprinting()) ? sprintMultiplier : 1f);
            return wish * speed;
        }

        private Vector3 BuildMovementIntent(float forward, float strafe)
        {
            var wish = transform.forward * forward + transform.right * strafe;
            if (wish.sqrMagnitude <= 0.0001f)
                return Vector3.zero;

            return wish.normalized;
        }

        private void MoveVerticalOnly()
        {
            var motion = new Vector3(0f, _verticalVelocity, 0f);
            _controller.Move(motion * Time.deltaTime);
        }

        private void UpdateBlockedMovementState(CollisionFlags collisionFlags, Vector3 movementIntent)
        {
            if (movementIntent.sqrMagnitude <= 0.0001f)
            {
                _movementBlockedByCollision = false;
                _blockedMovementDirection = Vector3.zero;
                return;
            }

            if ((collisionFlags & CollisionFlags.Sides) != 0)
            {
                _movementBlockedByCollision = true;
                _blockedMovementDirection = movementIntent.normalized;
                _authoritativeTargetPosition = transform.position;
                _hasAuthoritativeTarget = false;
                return;
            }

            if (_movementBlockedByCollision)
            {
                float alignment = Vector3.Dot(movementIntent.normalized, _blockedMovementDirection);
                if (alignment < 0.5f)
                {
                    _movementBlockedByCollision = false;
                    _blockedMovementDirection = Vector3.zero;
                    return;
                }
            }

            _movementBlockedByCollision = false;
            _blockedMovementDirection = Vector3.zero;
        }

        private void SnapToGround()
        {
            var start = transform.position + Vector3.up * 500f;
            if (Physics.Raycast(start, Vector3.down, out var hit, 5000f, ~0, QueryTriggerInteraction.Ignore))
            {
                float offset = (_controller != null ? _controller.height * 0.5f : 1f) + 0.05f;
                transform.position = hit.point + Vector3.up * offset;
            }
        }

        private void ApplyMovementModePresentation()
        {
            if (_appearanceController == null)
                _appearanceController = GetComponent<CharacterAppearanceController>();

            if (_appearanceController == null)
                return;

            if (movementMode == MovementMode.Flight)
            {
                _appearanceController.SetTemporaryLocomotionClipOverride(ResolveFlightHoverClipName());
                return;
            }

            _appearanceController.ClearTemporaryLocomotionClipOverride();
        }

        private string ResolveFlightHoverClipName()
        {
            var bridge = GetComponent<CharacterRuntimeBridge>();
            int breedId = bridge?.Character?.BreedId ?? 1;
            string breedToken = breedId switch
            {
                1 => "solitus",
                2 => "opifex",
                3 => "nanomage",
                4 => "athrox",
                7 => "humanmonster",
                _ => "solitus"
            };

            return $"{breedToken}_idle-hover_01_01";
        }

        private Vector2 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                float x = 0f;
                float y = 0f;
                if (kb.aKey.isPressed) x -= 1f;
                if (kb.dKey.isPressed) x += 1f;
                if (kb.sKey.isPressed) y -= 1f;
                if (kb.wKey.isPressed) y += 1f;
                return new Vector2(x, y);
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#else
            WarnNoInputBackend();
            return Vector2.zero;
#endif
        }

        private Vector2 ReadLookInput()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.delta.ReadValue() * 0.02f;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#else
            WarnNoInputBackend();
            return Vector2.zero;
#endif
        }

        private float ReadForwardInput()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                float value = 0f;
                if (kb.wKey.isPressed) value += 1f;
                if (kb.sKey.isPressed) value -= 1f;
                return value;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            float value = 0f;
            if (Input.GetKey(KeyCode.W)) value += 1f;
            if (Input.GetKey(KeyCode.S)) value -= 1f;
            return value;
#else
            WarnNoInputBackend();
            return 0f;
#endif
        }

        private float ReadStrafeInput()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                float value = 0f;
                if (kb.zKey.isPressed) value -= 1f;
                if (kb.cKey.isPressed) value += 1f;
                return value;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            float value = 0f;
            if (Input.GetKey(KeyCode.Z)) value -= 1f;
            if (Input.GetKey(KeyCode.C)) value += 1f;
            return value;
#else
            WarnNoInputBackend();
            return 0f;
#endif
        }

        private float ReadTurnInput()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                float value = 0f;
                if (kb.aKey.isPressed) value -= 1f;
                if (kb.dKey.isPressed) value += 1f;
                return value;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            float value = 0f;
            if (Input.GetKey(KeyCode.A)) value -= 1f;
            if (Input.GetKey(KeyCode.D)) value += 1f;
            return value;
#else
            WarnNoInputBackend();
            return 0f;
#endif
        }

        private bool IsLeftMouseHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.leftButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }

        private bool WasLeftMousePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.leftButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        private bool WasRightMousePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.rightButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(1);
#else
            return false;
#endif
        }

        private bool WasLeftMouseReleasedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.leftButton.wasReleasedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(0);
#else
            return false;
#endif
        }

        private bool WasRightMouseReleasedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.rightButton.wasReleasedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(1);
#else
            return false;
#endif
        }

        private bool IsRightMouseHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.rightButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(1);
#else
            return false;
#endif
        }

        private bool WantsJump()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetButtonDown("Jump");
#else
            return false;
#endif
        }

        private float ReadFlightVerticalInput()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                float value = 0f;
                if (kb.cKey.isPressed) value += 1f;
                if (kb.zKey.isPressed) value -= 1f;
                return value;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            float value = 0f;
            if (Input.GetKey(KeyCode.C)) value += 1f;
            if (Input.GetKey(KeyCode.Z)) value -= 1f;
            return value;
#else
            return 0f;
#endif
        }

        private bool IsSprinting()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.leftShiftKey.isPressed)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftShift);
#else
            return false;
#endif
        }

        private void WarnNoInputBackend()
        {
            if (_warnedInputBackend)
                return;
            _warnedInputBackend = true;
            Debug.LogWarning("No supported input backend enabled for PrototypeWalkerController.");
        }

        private void HandleAoCoordHotkey()
        {
            if (!allowAoCoordHotkey || IsGameplayInputBlockedByUi())
                return;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (kb.f9Key.wasPressedThisFrame)
                {
                    if (shift)
                        LogCurrentAoCoordinates("Hotkey Shift+F9");
                    else
                        LogCurrentAoCoordinates("Hotkey F9");
                    return;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.F9) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                LogCurrentAoCoordinates("Hotkey Shift+F9");
            else if (Input.GetKeyDown(KeyCode.F9))
                LogCurrentAoCoordinates("Hotkey F9");
#endif
        }

        private void HandleCameraZoom()
        {
            if (IsGameplayInputBlockedByUi())
                return;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb[Key.NumpadPlus].wasPressedThisFrame)
                    _cameraDistance = Mathf.Max(minCameraDistance, _cameraDistance - cameraZoomStep);
                if (kb[Key.NumpadMinus].wasPressedThisFrame)
                    _cameraDistance = Mathf.Min(maxCameraDistance, _cameraDistance + cameraZoomStep);
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.KeypadPlus))
                _cameraDistance = Mathf.Max(minCameraDistance, _cameraDistance - cameraZoomStep);
            if (Input.GetKeyDown(KeyCode.KeypadMinus))
                _cameraDistance = Mathf.Min(maxCameraDistance, _cameraDistance + cameraZoomStep);
#endif
        }

        private void LogCurrentAoCoordinates(string source)
        {
            if (_bootstrap == null)
                _bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();

            if (_bootstrap == null)
            {
                Debug.LogWarning("AO coord log failed: PrototypeWorldBootstrap not found.");
                return;
            }

            Vector3 world = transform.position;
            Vector3 ao = _bootstrap.ConvertWorldToAo(world);
            int pf = _bootstrap.ActivePlayfieldId;
            string message =
                $"AO Coords [{source}] PF={pf} AO=({ao.x:F5}, {ao.y:F5}, {ao.z:F5}) " +
                $"World=({world.x:F3}, {world.y:F3}, {world.z:F3})";
            Debug.Log(message);
            PrototypeUiContext.Active?.PublishStatus(message);
        }
    }
}



