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
    /// Touch input plumbing (Phase 0). Produces a normalized move vector from a drag anchored
    /// at the first touch that does not start on UI, and tap events for short touches.
    /// Input resets to zero on release, cancel, focus loss or while a modal is open.
    /// Mouse drag and WASD/arrow keys are editor/dev conveniences.
    ///
    /// Phase 1 builds the visible floating joystick, lower-screen movement area and
    /// collision-aware motor on top of this; it does not replace the contract.
    /// </summary>
    public sealed class TouchInputRouter : MonoBehaviour
    {
        private static int _modalDepth;

        private SandboxSettings _settings;
        private int _trackedTouchId = -1;
        private readonly HashSet<int> _ignoredTouchIds = new HashSet<int>();
        private bool _mouseTracking;
        private Vector2 _anchor;
        private Vector2 _current;
        private double _startTime;
        private bool _movedBeyondTap;
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private PointerEventData _pointerData;
        private EventSystem _pointerDataOwner;

        /// <summary>Normalized movement, magnitude 0..1, screen-up = +y.</summary>
        public Vector2 MoveVector { get; private set; }

        public bool IsDragging => (_trackedTouchId >= 0 || _mouseTracking) && _movedBeyondTap;
        public Vector2 DragAnchor => _anchor;
        public Vector2 DragCurrent => _current;

        /// <summary>Raised with the screen position of a short, still touch or click that did not start on UI.</summary>
        public event Action<Vector2> Tapped;

        public static bool IsBlocked => _modalDepth > 0;

        /// <summary>Call when a modal opens; movement input is zeroed until the matching <see cref="PopModal"/>.</summary>
        public static void PushModal() => _modalDepth++;

        public static void PopModal() => _modalDepth = Math.Max(0, _modalDepth - 1);

        public void Configure(SandboxSettings settings) => _settings = settings;

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
            MoveVector = Vector2.zero;
        }

        private void Update()
        {
            if (_settings == null) return;
            if (IsBlocked)
            {
                // Swallow everything that happens under a modal, including touches still held when it closes.
                if (_trackedTouchId >= 0) _ignoredTouchIds.Add(_trackedTouchId);
                ResetInput();
                foreach (var t in ETouch.activeTouches) _ignoredTouchIds.Add(t.touchId);
                return;
            }

            var drag = Vector2.zero;
            if (!UpdateTouches(ref drag)) UpdateMouse(ref drag);

            var keys = ReadKeyboard();
            var combined = drag + keys;
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

                if (_trackedTouchId < 0 && t.phase == ETouchPhase.Began)
                {
                    if (IsOverUi(t.screenPosition)) { _ignoredTouchIds.Add(t.touchId); continue; }
                    Begin(t.touchId, t.screenPosition, t.startTime);
                }

                if (t.touchId != _trackedTouchId) continue;
                trackedStillActive = true;
                _current = t.screenPosition;

                switch (t.phase)
                {
                    case ETouchPhase.Ended:
                        EndPress(t.time);
                        return true;
                    case ETouchPhase.Canceled:
                        ResetInput();
                        return true;
                    default:
                        drag = ComputeDrag();
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

            if (!_mouseTracking && mouse.leftButton.wasPressedThisFrame)
            {
                if (IsOverUi(pos)) return;
                Begin(-1, pos, Time.realtimeSinceStartupAsDouble);
                _mouseTracking = true;
            }
            if (!_mouseTracking) return;

            _current = pos;
            if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
            {
                EndPress(Time.realtimeSinceStartupAsDouble);
                return;
            }
            drag = ComputeDrag();
        }

        private void Begin(int touchId, Vector2 pos, double startTime)
        {
            _trackedTouchId = touchId;
            _anchor = pos;
            _current = pos;
            _startTime = startTime;
            _movedBeyondTap = false;
        }

        private void EndPress(double endTime)
        {
            var wasTap = !_movedBeyondTap && endTime - _startTime <= _settings.tapMaxSeconds;
            var pos = _current;
            ResetInput();
            if (wasTap) Tapped?.Invoke(pos);
        }

        private Vector2 ComputeDrag()
        {
            var deltaPoints = (_current - _anchor) / PixelsPerPoint();
            var dist = deltaPoints.magnitude;
            if (dist > _settings.tapMaxMovePoints) _movedBeyondTap = true;
            if (dist <= _settings.dragDeadZonePoints) return Vector2.zero;
            var range = Mathf.Max(1f, _settings.dragRadiusPoints - _settings.dragDeadZonePoints);
            var strength = Mathf.Clamp01((dist - _settings.dragDeadZonePoints) / range);
            return deltaPoints / dist * strength;
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
        private static float PixelsPerPoint() => Screen.dpi > 0 ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;

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
