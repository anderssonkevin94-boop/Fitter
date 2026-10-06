using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>Fades a building while the player stands behind it (GDD 3), so the player is never lost.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ObstructionFader : MonoBehaviour
    {
        [Range(0.1f, 1f)] [SerializeField] private float fadedAlpha = 0.45f;
        [SerializeField] private float fadeSpeed = 5f;

        private SpriteRenderer _sr;

        private void Awake() => _sr = GetComponent<SpriteRenderer>();

        private void LateUpdate()
        {
            var c = SandboxSceneController.Current;
            if (c == null || c.Player == null) return;
            var p = c.Player.position;
            var body = p + Vector3.up * 0.5f;   // test the character's middle, not the feet
            var behind = p.y > transform.position.y && _sr.bounds.Contains(new Vector3(body.x, body.y, _sr.bounds.center.z));
            var col = _sr.color;
            col.a = Mathf.MoveTowards(col.a, behind ? fadedAlpha : 1f, Time.deltaTime * fadeSpeed);
            _sr.color = col;
        }
    }
}
