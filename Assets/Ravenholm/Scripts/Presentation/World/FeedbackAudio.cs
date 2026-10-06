using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Placeholder sound and haptics, synthesized at startup (no audio assets yet; GDD 20 palette: soft
    /// knocks, metal taps, a calm town loop). Transfers are rate-limited and pitch-stepped so a burst reads
    /// as one grouped sound. Effects and music have separate volumes; haptics only mark completed
    /// deliveries, crafts, dispatches and discoveries, never every worker swing.
    /// </summary>
    public sealed class FeedbackAudio : MonoBehaviour
    {
        public enum Cue { Pickup, Deliver, Full, Craft, Dispatch, ReturnGood, ReturnBad, Discovery, Coin, Arrival, Tap }

        private static FeedbackAudio _instance;

        [SerializeField] private float minInterval = 0.06f;

        private AudioSource _source;
        private AudioSource _music;
        private AudioClip _pickup, _deliver, _full, _craft, _dispatch, _good, _bad, _discovery, _coin, _arrival, _tap;
        private float _lastPlay, _lastDeliverHaptic;
        private int _streak;

        /// <summary>Cues played this session (tests).</summary>
        public static int Played { get; private set; }
        public static Cue LastCue { get; private set; }

        public static void Play(Cue cue)
        {
            if (_instance != null) _instance.PlayInternal(cue);
        }

        private void Awake()
        {
            _instance = this;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _pickup = Tone("pickup", 0.05f, (1250f, 1f));
            _deliver = Knock("deliver", 520f, 0.08f);
            _full = Tone("full", 0.14f, (220f, 1f), (165f, 0.5f));
            _craft = Chime("craft", new[] { 880f, 1320f }, 0.25f);
            _dispatch = Chime("dispatch", new[] { 392f, 523f }, 0.35f);
            _good = Chime("return_good", new[] { 523f, 659f, 784f }, 0.5f);
            _bad = Chime("return_bad", new[] { 392f, 311f }, 0.5f);
            _discovery = Chime("discovery", new[] { 659f, 784f, 988f, 1319f }, 0.7f);
            _coin = Chime("coin", new[] { 1568f, 2093f }, 0.18f);
            _arrival = Chime("arrival", new[] { 587f, 740f }, 0.4f);
            _tap = Knock("tap", 900f, 0.03f);

            _music = gameObject.AddComponent<AudioSource>();
            _music.clip = TownLoop();
            _music.loop = true;
            _music.playOnAwake = false;
            _music.volume = Feel.MusicVolume * 0.5f;
            _music.Play();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            var target = Feel.MusicVolume * 0.5f;
            if (!Mathf.Approximately(_music.volume, target)) _music.volume = target;
        }

        private void PlayInternal(Cue cue)
        {
            Played++;
            LastCue = cue;
            var volume = Feel.EffectsVolume * 0.45f;
            var now = Time.unscaledTime;
            switch (cue)
            {
                case Cue.Pickup:
                case Cue.Deliver:
                case Cue.Full:
                    if (cue != Cue.Full && now - _lastPlay < minInterval) return;
                    _streak = now - _lastPlay < 0.4f ? Mathf.Min(_streak + 1, 8) : 0;
                    _lastPlay = now;
                    _source.pitch = cue == Cue.Full ? 1f : 1f + _streak * 0.04f;
                    _source.PlayOneShot(cue == Cue.Pickup ? _pickup : cue == Cue.Deliver ? _deliver : _full, volume);
                    if (cue == Cue.Deliver && now - _lastDeliverHaptic > 0.5f) { _lastDeliverHaptic = now; Feel.Haptic(HapticKind.Light); }
                    if (cue == Cue.Full) Feel.Haptic(HapticKind.Warning);
                    return;
            }
            _source.pitch = 1f;
            var clip = cue == Cue.Craft ? _craft : cue == Cue.Dispatch ? _dispatch : cue == Cue.ReturnGood ? _good : cue == Cue.ReturnBad ? _bad
                : cue == Cue.Discovery ? _discovery : cue == Cue.Coin ? _coin : cue == Cue.Arrival ? _arrival : _tap;
            _source.PlayOneShot(clip, cue == Cue.Tap ? volume * 0.5f : volume);
            switch (cue)
            {
                case Cue.Craft: Feel.Haptic(HapticKind.Medium); break;
                case Cue.Dispatch: Feel.Haptic(HapticKind.Medium); break;
                case Cue.Discovery: Feel.Haptic(HapticKind.Success); break;
                case Cue.ReturnGood: Feel.Haptic(HapticKind.Light); break;
                case Cue.ReturnBad: Feel.Haptic(HapticKind.Warning); break;
            }
        }

        // ------------------------------------------------------------------ synthesis (placeholder palette)

        private const int Rate = 44100;

        private static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Soft sine tone with a quick attack and exponential decay; optional partials.</summary>
        private static AudioClip Tone(string name, float seconds, params (float freq, float amp)[] partials)
        {
            var n = Mathf.CeilToInt(Rate * seconds);
            var d = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                var env = Mathf.Exp(-t / (seconds * 0.3f)) * Mathf.Min(1f, i / 60f);
                var v = 0f;
                foreach (var (f, a) in partials) v += Mathf.Sin(2f * Mathf.PI * f * t) * a;
                d[i] = v * env * 0.5f;
            }
            return Make(name, d);
        }

        /// <summary>Timber knock: a short pitched thump with a little filtered noise.</summary>
        private static AudioClip Knock(string name, float freq, float seconds)
        {
            var n = Mathf.CeilToInt(Rate * seconds);
            var d = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float lp = 0;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                var env = Mathf.Exp(-t / (seconds * 0.25f)) * Mathf.Min(1f, i / 30f);
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.15f;
                d[i] = (Mathf.Sin(2f * Mathf.PI * freq * t * (1f - t * 2f)) * 0.7f + lp * 0.5f) * env * 0.6f;
            }
            return Make(name, d);
        }

        /// <summary>A short arpeggio of bell-like notes (metal taps).</summary>
        private static AudioClip Chime(string name, float[] notes, float seconds)
        {
            var n = Mathf.CeilToInt(Rate * seconds);
            var d = new float[n];
            var step = seconds / (notes.Length + 1);
            for (var k = 0; k < notes.Length; k++)
            {
                var start = Mathf.FloorToInt(k * step * Rate);
                for (var i = start; i < n; i++)
                {
                    var t = (float)(i - start) / Rate;
                    var env = Mathf.Exp(-t / 0.12f) * Mathf.Min(1f, (i - start) / 40f);
                    d[i] += (Mathf.Sin(2f * Mathf.PI * notes[k] * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * notes[k] * 2.01f * t)) * env * 0.3f;
                }
            }
            return Make(name, d);
        }

        /// <summary>Calm 16-second town loop: a soft pad over a slow I-vi-IV-V progression.</summary>
        private static AudioClip TownLoop()
        {
            const float seconds = 16f;
            var n = Mathf.CeilToInt(Rate * seconds);
            var d = new float[n];
            var chords = new[] { new[] { 261.6f, 329.6f, 392f }, new[] { 220f, 261.6f, 329.6f }, new[] { 174.6f, 220f, 261.6f }, new[] { 196f, 246.9f, 293.7f } };
            var bar = seconds / chords.Length;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                var c = chords[Mathf.Min(chords.Length - 1, (int)(t / bar))];
                var local = t % bar;
                var env = Mathf.Min(1f, local / 0.8f) * Mathf.Min(1f, (bar - local) / 0.8f);
                var v = 0f;
                foreach (var f in c) v += Mathf.Sin(2f * Mathf.PI * f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * f * 0.5f * t);
                d[i] = v * env * 0.06f;
            }
            return Make("town_loop", d);
        }
    }
}
