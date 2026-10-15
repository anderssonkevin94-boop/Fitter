using NUnit.Framework;
using Ravenholm.Domain.Sites;
using Ravenholm.Services;

namespace Ravenholm.Tests
{
    /// <summary>Shared steps for tests that use the real content's Mine (the dig slab).</summary>
    public static class DigTestKit
    {
        /// <summary>The supplier at (0, 1) on the cliff face, feet on row 4, facing along it: a tunnel a routine can carry on.</summary>
        public static readonly DigFace Along = new DigFace { X = 0, U = 1, Feet = 4, DX = 0, DU = 1 };

        /// <summary>Every block of the quarry's plot, from a row down, row by row.</summary>
        public static System.Collections.Generic.IEnumerable<DigCell> QuarryBlocks(DigSlab slab, int fromRow)
        {
            for (var row = fromRow; row < slab.Depth; row++)
            for (var u = slab.MinU; u < slab.Length; u++)
            for (var x = 0; x < slab.Thickness; x++)
                yield return new DigCell(x, u, row);
        }

        /// <summary>Digs one block by hand until it breaks (true) or the bound runs out.</summary>
        public static bool Dig(GameServices g, int x, int u, int row, DigFace from = null)
        {
            for (var i = 0; i < 4000; i++)
                if (g.Sites.PlayerDig(x, u, row, 0.1f, from ?? Along)) return true;
            return false;
        }

        /// <summary>Digs straight down at the entry (standing on each block in turn): a shaft with a ladder.</summary>
        public static void DigShaft(GameServices g, int u, int depth)
        {
            for (var row = 0; row < depth; row++)
                Assert.IsTrue(Dig(g, 0, u, row, new DigFace { X = 0, U = u, Feet = row, DX = 0, DU = 1 }), $"dig down to row {row}");
        }

    }
}
