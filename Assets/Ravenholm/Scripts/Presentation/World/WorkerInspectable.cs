using System.Text;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>Tapping a worker shows their live status, equipment and pack.</summary>
    public sealed class WorkerInspectable : Inspectable
    {
        [SerializeField] private WorkerView view;

        public void EditorSetupWorker(WorkerView workerView) => view = workerView;

        public override string BuildDetail()
        {
            var root = GameRoot.Instance;
            var sim = root != null ? root.Services.Sites.Get(view.SiteId) : null;
            var w = sim?.Record.Workers.Find(x => x.Id == view.WorkerId);
            if (w == null) return base.BuildDetail();
            var sb = new StringBuilder();
            sb.Append("<b>").Append(w.Name).Append("</b> · ").Append(sim.Config.Kind == Domain.Sites.SiteKind.Mine ? "miner" : "woodcutter").Append('\n');
            sb.Append(WorkerView.Describe(sim, w)).Append("\n\n");
            sb.Append("Tool: ").Append(UnitVisuals.DisplayName(w.ToolId)).Append("  ·  Pack: ").Append(UnitVisuals.DisplayName(w.PackId)).Append('\n');
            sb.Append("Carrying: ").Append(w.Pack.Count == 0 ? "nothing" : string.Join(", ", w.Pack.ConvertAll(UnitVisuals.DisplayName))).Append("\n\n");
            sb.Append("Workers keep their names and improve only through equipment (no XP, wages or needs).");
            return sb.ToString();
        }
    }
}
