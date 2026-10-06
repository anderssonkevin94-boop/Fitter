using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Placeholder feedback sounds synthesized at startup (no audio assets yet). Rate-limited and
    /// pitch-stepped so a burst of transfers reads as one grouped sound, not one per unit (GDD 20).
    /// </summary>
    public sealed class FeedbackAudio : MonoBehaviour
    {
        public enum Cue { Pickup, Deliver, Full }

        private static FeedbackAudio _instance;

        [Range(0f, 1f)] [SerializeField] private float volume = 0.35f;
        [SerializeField] private float minInterval = 0.06f;

        private AudioSource _source;
        private AudioClip _pickup, _deliver, _full;
        private float _lastPlay;
        private int _streak;

        public static void Play(Cue cue)
        {
            if (_instance != null) _instance.PlayInternal(cue);
        }

        private void Awake()
        {
            _instance = this;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _pickup = Blip("pickup", 1250f, 0.045f, 0.5f);
            _deliver = Blip("deliver", 520f, 0.07f, 0.6f);
            _full = Blip("full", 220f, 0.12f, 0.5f);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void PlayInternal(Cue cue)
        {
            var now = Time.unscaledTime;
            if (cue != Cue.Full && now - _lastPlay < minInterval) return;
            _streak = now - _lastPlay < 0.4f ? Mathf.Min(_streak + 1, 8) : 0;
            _lastPlay = now;
            _source.pitch = cue == Cue.Full ? 1f : 1f + _streak * 0.04f;
            _source.PlayOneShot(cue == Cue.Pickup ? _pickup : cue == Cue.Deliver ? _deliver : _full, volume);
        }

        private static AudioClip Blip(string name, float freq, float seconds, float decay)
        {
            const int rate = 44100;
            var n = Mathf.CeilToInt(rate * seconds);
            var data = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / rate;
                var env = Mathf.Exp(-t / (seconds * decay * 0.5f)) * Mathf.Min(1f, i / 60f);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.6f;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
