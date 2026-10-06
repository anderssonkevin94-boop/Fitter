using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Floating joystick maths (GDD 3), free of input devices so it can be unit-tested.
    /// The anchor is placed where the touch begins. Output is a normalized vector (magnitude 0..1)
    /// with a dead zone and a clamped radius; if the finger travels past the radius the anchor
    /// trails behind it, so reversing direction responds immediately.
    /// All distances are in screen points.
    /// </summary>
    public sealed class FloatingJoystick
    {
        public float DeadZone = 8f;
        public float Radius = 60f;

        public bool Active { get; private set; }
        public Vector2 Anchor { get; private set; }
        public Vector2 Knob { get; private set; }
        public Vector2 Output { get; private set; }

        public void Begin(Vector2 point)
        {
            Active = true;
            Anchor = point;
            Knob = point;
            Output = Vector2.zero;
        }

        public Vector2 Move(Vector2 point)
        {
            if (!Active) return Vector2.zero;
            var delta = point - Anchor;
            var dist = delta.magnitude;
            if (dist > Radius)
            {
                Anchor = point - delta / dist * Radius;   // trail the anchor
                delta = point - Anchor;
                dist = Radius;
            }
            Knob = Anchor + delta;
            if (dist <= DeadZone)
            {
                Output = Vector2.zero;
                return Output;
            }
            var strength = Mathf.Clamp01((dist - DeadZone) / Mathf.Max(0.001f, Radius - DeadZone));
            Output = delta / dist * strength;
            return Output;
        }

        /// <summary>Release, cancel, focus loss or modal: always returns to zero.</summary>
        public void Reset()
        {
            Active = false;
            Output = Vector2.zero;
        }
    }
}
