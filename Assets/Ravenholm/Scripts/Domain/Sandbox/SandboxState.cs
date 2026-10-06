namespace Ravenholm.Domain.Sandbox
{
    /// <summary>
    /// PHASE 0 FIXTURE. Runtime state of the debug sandbox, used only to demonstrate that a
    /// value survives a restart. It is not a game system and will be removed or replaced
    /// once real runtime records (inventories, sites, contractors) exist.
    /// </summary>
    public sealed class SandboxState
    {
        public int BellRingCount;
        public int SessionCount;
        public float PlayerX;
        public float PlayerY;

        public SandboxState Clone() => (SandboxState)MemberwiseClone();
    }
}
