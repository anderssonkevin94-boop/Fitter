using System.Collections;
using Ravenholm.Content;
using Ravenholm.Domain.Production;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Small in-town signals (GDD 13/16/20): a flash where a crafted item appears, the Trader's next-arrival
    /// timer above the Trader Pad, and a "report waiting" marker over the Store while results are unread.
    /// Presentation only.
    /// </summary>
    public sealed class TownSignals : MonoBehaviour
    {
        private GameRoot _root;
        private TextMeshPro _traderTimer;
        private GameObject _reportMarker;
        private float _nextRefresh;

        public string TraderTimerText => _traderTimer != null && _traderTimer.gameObject.activeSelf ? _traderTimer.text : "";
        public bool ReportMarkerShown => _reportMarker != null && _reportMarker.activeSelf;
        public int Flashes { get; private set; }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) { enabled = false; return; }
            _root.Services.Production.System.ItemCrafted += OnCrafted;

            var pad = FindAnyObjectByType<TraderInspectable>();
            if (pad != null) _traderTimer = WorldText(pad.transform, new Vector3(-0.9f, -0.55f, 0f), 2.3f);   // left of centre: the pad sits near the screen edge

            foreach (var s in FindObjectsByType<StationInspectable>(FindObjectsInactive.Exclude))
                if (s.IsStore) _reportMarker = Marker(s.transform, new Vector3(1.75f, 1.1f, 0f));   // beside the counter, clear of the HUD tip card
        }

        private void OnDestroy()
        {
            if (_root != null) _root.Services.Production.System.ItemCrafted -= OnCrafted;
        }

        private TextMeshPro WorldText(Transform parent, Vector3 local, float size)
        {
            var go = new GameObject("TownSignalText");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var t = go.AddComponent<TextMeshPro>();
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color32(250, 242, 226, 255);
            t.outlineWidth = 0.25f;
            t.outlineColor = new Color32(59, 42, 32, 255);
            t.rectTransform.sizeDelta = new Vector2(4f, 0.6f);
            t.sortingLayerID = SortingLayer.NameToID("Overlay");
            return t;
        }

        /// <summary>A bubble with the Job Board icon: "results are waiting" (a distinct Store marker, GDD 13).</summary>
        private GameObject Marker(Transform parent, Vector3 local)
        {
            var theme = _root.Config.UiTheme;
            var go = new GameObject("ReportMarker");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var back = go.AddComponent<SpriteRenderer>();
            back.sprite = theme.roundButton;
            back.sortingLayerName = "Overlay";
            back.sortingOrder = 5;
            go.transform.localScale = Vector3.one * 0.55f;
            var icon = new GameObject("Icon").AddComponent<SpriteRenderer>();
            icon.transform.SetParent(go.transform, false);
            icon.sprite = theme.board;
            icon.sortingLayerName = "Overlay";
            icon.sortingOrder = 6;
            icon.transform.localScale = Vector3.one * 0.6f;
            var label = WorldText(go.transform, new Vector3(0f, -0.95f, 0f), 4.5f);
            label.text = "Report";
            go.SetActive(false);
            return go;
        }

        private void Update()
        {
            if (_reportMarker != null)
            {
                var unread = _root.Services.Jobs.Board != null && _root.Services.Jobs.Board.UnacknowledgedResults > 0;
                if (_reportMarker.activeSelf != unread) _reportMarker.SetActive(unread);
                if (unread && !Feel.ReducedMotion)
                    _reportMarker.transform.localPosition = new Vector3(1.75f, 1.1f + Mathf.Sin(Time.time * 3f) * 0.06f, 0f);
            }
            if (Time.unscaledTime < _nextRefresh || _traderTimer == null) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            var eco = _root.Services.Economy;
            var t = eco.Trader;
            if (t == null || !t.Scheduled) { _traderTimer.gameObject.SetActive(false); return; }
            _traderTimer.gameObject.SetActive(true);
            _traderTimer.text = t.IsPresent(eco.Now)
                ? $"Trader here · {Services.EconomyService.Minutes(t.UntilDeparture(eco.Now))}"
                : $"Trader in {Services.EconomyService.Minutes(t.UntilNextArrival(eco.Now))}";
        }

        private void OnCrafted(CraftRecipe recipe, string instanceId)
        {
            Flashes++;
            if (!WorldContainerView.TryGet(recipe.PadContainerId, out var pad)) return;
            StartCoroutine(Flash(pad.transform.position + Vector3.up * 0.35f));
        }

        /// <summary>One concise flash where the item appears (GDD 20). Reduced motion: a still glint, no burst.</summary>
        private IEnumerator Flash(Vector3 at)
        {
            var go = new GameObject("CraftFlash");
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _root.Config.UiTheme.reputation;   // star
            sr.sortingLayerName = "Overlay";
            sr.sortingOrder = 8;
            const float seconds = 0.45f;
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var k = t / seconds;
                var scale = Feel.ReducedMotion ? 0.5f : Mathf.Lerp(0.2f, 0.9f, k);
                go.transform.localScale = Vector3.one * scale;
                sr.color = new Color(1f, 0.95f, 0.6f, 1f - k);
                yield return null;
            }
            Destroy(go);
        }
    }
}
