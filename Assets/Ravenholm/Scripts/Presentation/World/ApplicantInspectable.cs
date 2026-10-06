using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>An applicant waiting at the Store. "Meet" opens their card and the loadout (GDD 11).</summary>
    public sealed class ApplicantInspectable : Inspectable
    {
        public string ContractorId { get; private set; }

        public override string ContextVerb => "Meet";
        public override bool OpensOwnPanel => true;

        public void Bind(string contractorId, string name)
        {
            ContractorId = contractorId;
            EditorSetup(name, "Applicant", "");
        }

        public override void OnInspected()
        {
            var root = GameRoot.Instance;
            if (root != null) root.Hud.ShowLoadout(ContractorId);
        }
    }
}
