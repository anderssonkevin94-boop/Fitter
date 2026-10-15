using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The drive (the journey, step 4): after making camp at the next clearing, the ox pulls the wagon up the old road
    /// from the camp left behind to the new one, the camera following, the supplier riding along; then the shop stands
    /// folded out at the new camp and the supplier steps down in front of it. Placeholder art until Astra's ox lands.
    /// </summary>
    public sealed class DriveView : MonoBehaviour
    {
        [SerializeField] private Sprite oxSprite;
        [SerializeField] private float speed = 7f;
        [SerializeField] private Material material;

        public static DriveView Current { get; private set; }
        public bool Driving { get; private set; }

        private GameRoot _root;

        public void EditorSetup(Sprite ox, Material mat)
        {
            oxSprite = ox;
            material = mat;
        }

        private void Awake() => Current = this;

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _root.Services.Camp.Moved += OnMoved;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_root != null && _root.Services != null) _root.Services.Camp.Moved -= OnMoved;
        }

        private void OnMoved(int from, int to)
        {
            if (this == null || !isActiveAndEnabled) return;
            StopAllCoroutines();
            StartCoroutine(Drive(from, to));
        }

        private SpriteRenderer Make(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = "Overlay";   // above the trees it passes
            sr.sortingOrder = order;
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        private IEnumerator Drive(int from, int to)
        {
            Driving = true;
            var shop = ShopView.Current;
            var camp = _root.Services.Camp;
            var road = _root.Services.Catalogue.CurrentCamp;
            yield return null;   // the shop has redrawn at the new camp
            var end = shop != null && shop.Wagon != null ? (Vector2)shop.Wagon.position : camp.CentreOf(to);
            var start = end - (camp.OffsetOf(to) - camp.OffsetOf(from));
            // Along the old road, from the camp left behind to the new one.
            var path = new List<Vector2> { start };
            var ridge = road.Ridge;
            for (var s = road.CampAhead(from) + 14; s < road.CampAhead(to) - 8; s += 3)
            {
                var (x, y) = ridge.PointAt(ridge.CampS + s);
                path.Add(new Vector2(((float)x - (float)y) * 0.5f, ((float)x + (float)y) * 0.25f + 0.25f));
            }
            path.Add(end);

            var wagonSprite = shop != null && shop.Wagon != null ? shop.Wagon.GetComponent<SpriteRenderer>() : null;
            if (wagonSprite != null) wagonSprite.enabled = false;
            var wagon = Make("DrivingWagon", wagonSprite != null ? wagonSprite.sprite : null, 10);
            wagon.transform.localScale = Vector3.one * 0.8f;
            var ox = Make("Ox", oxSprite, 11);
            var player = SandboxSceneController.Current != null ? SandboxSceneController.Current.Player : null;
            var visual = player != null ? player.Find("Visual") : null;
            if (visual != null) visual.gameObject.SetActive(false);   // riding on the wagon
            var cam = SandboxSceneController.Current != null ? SandboxSceneController.Current.WorldCamera.GetComponent<FollowCamera>() : null;
            if (cam != null) cam.Override = wagon.transform;

            for (var i = 0; i + 1 < path.Count; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                var dir = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : Vector2.right;
                for (var t = 0f; t < 1f; t += Time.deltaTime * speed / Mathf.Max(0.01f, Vector2.Distance(a, b)))
                {
                    var p = Vector2.Lerp(a, b, t);
                    wagon.transform.position = p;
                    wagon.flipX = dir.x < 0f;
                    ox.transform.position = p + dir * 1.25f;
                    ox.flipX = dir.x < 0f;
                    if (player != null) player.position = p;
                    yield return null;
                }
            }

            Destroy(wagon.gameObject);
            Destroy(ox.gameObject);
            if (wagonSprite != null) wagonSprite.enabled = true;
            if (visual != null) visual.gameObject.SetActive(true);
            if (cam != null) cam.Override = null;
            // Step down in front of the folded-out shop.
            var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
            if (motor != null && shop != null) motor.Teleport(shop.TileCentre(1, -1));
            _root.Hud.ShowToast($"Camp made at {camp.Name}. Set your stations up from their kits (Build).");
            Driving = false;
        }
    }
}
