using Ravenholm.Domain.Sites;
using TMPro;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Bubble over a site entrance (GDD 5): "Choose next section" when the site waits for a depth
    /// decision, or a warning while a worker is paused on full staging.
    /// </summary>
    public sealed class SiteMarkerView : MonoBehaviour
    {
        [SerializeField] private string siteId = "";
        [SerializeField] private GameObject bubbleRoot;
        [SerializeField] private SpriteRenderer icon;
        [SerializeField] private TextMeshPro label;
        [SerializeField] private Sprite decisionIcon;
        [SerializeField] private Sprite blockedIcon;

        private GameRoot _root;

        public bool Showing => bubbleRoot != null && bubbleRoot.activeSelf;
        public string Text => label != null ? label.text : "";

        public void EditorSetup(string site, GameObject root, SpriteRenderer iconRenderer, TextMeshPro text, Sprite decision, Sprite blocked)
        {
            siteId = site;
            bubbleRoot = root;
            icon = iconRenderer;
            label = text;
            decisionIcon = decision;
            blockedIcon = blocked;
        }

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null) enabled = false;
        }

        private void Update()
        {
            var sim = _root.Services.Sites.Get(siteId);
            if (sim == null) { bubbleRoot.SetActive(false); return; }
            string text = null;
            Sprite sprite = null;
            if (sim.Record.Status == SiteStatus.AwaitDecision)
            {
                text = sim.Config.Kind == SiteKind.Mine ? "Choose next section" : "Choose next area";
                sprite = decisionIcon;
            }
            else if (sim.Record.Workers.Exists(w => w.State == WorkerState.StagingFull))
            {
                text = "Staging full";
                sprite = blockedIcon;
            }
            bubbleRoot.SetActive(text != null);
            if (text == null) return;
            label.text = text;
            icon.sprite = sprite;
        }
    }
}
