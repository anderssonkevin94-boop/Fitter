using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using ETouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Input plumbing (GDD 3). The first touch that does not start on UI is tracked:
    /// if it starts in the lower movement area it drives the <see cref="FloatingJoystick"/>;
    /// a short, still touch anywhere is a tap. Input resets to zero on release, cancel,
    /// focus loss and while a modal is open, and a touch held across a modal is ignored
    /// until it lifts. Mouse drag and WASD/arrows are editor conveniences.
    /// </summary>
    public sealed class TouchInputRouter : MonoBehaviour
    {
        private static int _modalDepth;

        private SandboxSettings _settings;
        private readonly FloatingJoystick _joystick = new FloatingJoystick();
        private int _trackedTouchId = -1;
        private readonly HashSet<int> _ignoredTouchIds = new HashSet<int>();
        private bool _mouseTracking;
        private bool _mouseIgnored;
        private bool _joystickAllowed;
        private Vector2 _pressStart;
        private Vector2 _current;
        private double _startTime;
        private bool _movedBeyondTap;
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private PointerEventData _pointerData;
        private EventSystem _pointerDataOwner;

        /// <summary>Normalized movement, magnitude 0..1, screen-up = +y.</summary>
        public Vector2 MoveVector { get; private set; }

        /// <summary>Joystick state in screen pixels for the HUD visual.</summary>
        public bool JoystickVisible => _joystick.Active && _movedBeyondTap;
        public Vector2 JoystickAnchorPixels => _joystick.Anchor * PixelsPerPoint();
        public Vector2 JoystickKnobPixels => _joystick.Knob * PixelsPerPoint();
        public float JoystickRadiusPixels => _joystick.Radius * PixelsPerPoint();

        /// <summary>Raised with the screen position of a short, still touch or click that did not start on UI.</summary>
        public event Action<Vector2> Tapped;

        public static bool IsBlocked => _modalDepth > 0;

        /// <summary>Call when a modal opens; movement input is zeroed until the matching <see cref="PopModal"/>.</summary>
        public static void PushModal() => _modalDepth++;

        public static void PopModal() => _modalDepth = Math.Max(0, _modalDepth - 1);

        public void Configure(SandboxSettings settings)
        {
            _settings = settings;
            _joystick.DeadZone = settings.dragDeadZonePoints;
            _joystick.Radius = settings.dragRadiusPoints;
        }

        private void OnEnable() => EnhancedTouchSupport.Enable();

        private void OnDisable()
        {
            ResetInput();
            EnhancedTouchSupport.Disable();
        }

        private void OnDestroy() => _modalDepth = 0;

        public void ResetInput()
        {
            _trackedTouchId = -1;
            _mouseTracking = false;
            _movedBeyondTap = false;
            _joystick.Reset();
            MoveVector = Vector2.zero;
        }

        private void Update()
        {
            if (_settings == null) return;
            if (IsBlocked)
            {
                // Swallow everything under a modal, including touches still held when it closes.
                if (_trackedTouchId >= 0) _ignoredTouchIds.Add(_trackedTouchId);
                if (_mouseTracking) _mouseIgnored = true;
                ResetInput();
                foreach (var t in ETouch.activeTouches) _ignoredTouchIds.Add(t.touchId);
                return;
            }

            var drag = Vector2.zero;
            if (!UpdateTouches(ref drag)) UpdateMouse(ref drag);

            var combined = drag + ReadKeyboard();
            MoveVector = combined.sqrMagnitude > 1f ? combined.normalized : combined;
        }

        private bool UpdateTouches(ref Vector2 drag)
        {
            var touches = ETouch.activeTouches;
            if (touches.Count == 0)
            {
                _ignoredTouchIds.Clear();
                if (_trackedTouchId >= 0) ResetInput();
                return false;
            }

            var trackedStillActive = false;
            foreach (var t in touches)
            {
                if (_ignoredTouchIds.Contains(t.touchId)) continue;

                // Adopt the first untracked touch even if its one-frame "Began" was missed (frame hitch),
                // judging it by where it started.
                if (_trackedTouchId < 0 && t.phase != ETouchPhase.Ended && t.phase != ETouchPhase.Canceled)
                {
                    if (IsOverUi(t.startScreenPosition)) { _ignoredTouchIds.Add(t.touchId); continue; }
                    Begin(t.startScreenPosition, t.startTime);
                    _trackedTouchId = t.touchId;
                }

                if (t.touchId != _trackedTouchId) continue;
                trackedStillActive = true;

                switch (t.phase)
                {
                    case ETouchPhase.Ended:
                        End(t.screenPosition, t.time);
                        return true;
                    case ETouchPhase.Canceled:
                        ResetInput();
                        return true;
                    default:
                        drag = Drag(t.screenPosition);
                        break;
                }
            }

            if (_trackedTouchId >= 0 && !trackedStillActive) ResetInput();
            return true;
        }

        private void UpdateMouse(ref Vector2 drag)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            var pos = mouse.position.ReadValue();

            if (_mouseIgnored)
            {
                if (!mouse.leftButton.isPressed) _mouseIgnored = false;
                return;
            }

            if (!_mouseTracking && mouse.leftButton.wasPressedThisFrame)
            {
                if (IsOverUi(pos)) return;
                Begin(pos, Time.realtimeSinceStartupAsDouble);
                _mouseTracking = true;
            }
            if (!_mouseTracking) return;

            if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
            {
                End(pos, Time.realtimeSinceStartupAsDouble);
                return;
            }
            drag = Drag(pos);
        }

        private void Begin(Vector2 pixels, double startTime)
        {
            _pressStart = pixels;
            _current = pixels;
            _startTime = startTime;
            _movedBeyondTap = false;
            _joystickAllowed = pixels.y <= Screen.height * _settings.movementAreaHeight;
            if (_joystickAllowed) _joystick.Begin(pixels / PixelsPerPoint());
        }

        private Vector2 Drag(Vector2 pixels)
        {
            _current = pixels;
            if ((pixels - _pressStart).magnitude / PixelsPerPoint() > _settings.tapMaxMovePoints) _movedBeyondTap = true;
            return _joystickAllowed ? _joystick.Move(pixels / PixelsPerPoint()) : Vector2.zero;
        }

        private void End(Vector2 pixels, double endTime)
        {
            _current = pixels;
            var wasTap = !_movedBeyondTap && endTime - _startTime <= _settings.tapMaxSeconds;
            ResetInput();
            if (wasTap) Tapped?.Invoke(pixels);
        }

        private static Vector2 ReadKeyboard()
        {
            var k = Keyboard.current;
            if (k == null) return Vector2.zero;
            var v = Vector2.zero;
            if (k.wKey.isPressed || k.upArrowKey.isPressed) v.y += 1;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) v.y -= 1;
            if (k.dKey.isPressed || k.rightArrowKey.isPressed) v.x += 1;
            if (k.aKey.isPressed || k.leftArrowKey.isPressed) v.x -= 1;
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        /// <summary>Approximate UIKit points: iPhones report ~460 dpi at 3x.</summary>
        public static float PixelsPerPoint() => Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;

        private bool IsOverUi(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            if (_pointerData == null || _pointerDataOwner != es)
            {
                _pointerData = new PointerEventData(es);
                _pointerDataOwner = es;
            }
            _pointerData.position = screenPos;
            _uiHits.Clear();
            es.RaycastAll(_pointerData, _uiHits);
            return _uiHits.Count > 0;
        }
    }
}
