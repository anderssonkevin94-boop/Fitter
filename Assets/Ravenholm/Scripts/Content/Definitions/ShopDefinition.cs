using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// The player's shop (Shop Titans style, Kevin 2026-10-15): it starts as the start blocks (4 x 6 tiles) and grows by
    /// buying 2 x 2 blocks that join it, inside the lot. The entrance is a gap in a front low wall and never moves.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Shop", fileName = "shop")]
    public sealed class ShopDefinition : ContentDefinition
    {
        public enum EntranceEdge { FrontLeft, FrontRight }

        [Serializable]
        public sealed class Cost
        {
            [Min(0)] public int gold;
            public List<ResourceCost> materials = new List<ResourceCost>();
        }

        [Serializable]
        public sealed class StartingPiece
        {
            public ShopFurnitureDefinition furniture;
            [Min(1)] public int level = 1;
            public Vector2Int tile;
            public bool facingB;
        }

        [Tooltip("The lot the shop can grow over, in 2 x 2 blocks.")]
        [SerializeField] private Vector2Int lotBlocks = new Vector2Int(5, 5);
        [Tooltip("Blocks owned at the start (block coordinates).")]
        [SerializeField] private List<Vector2Int> startBlocks = new List<Vector2Int>();
        [Tooltip("The floor tile just inside the entrance.")]
        [SerializeField] private Vector2Int entranceTile;
        [SerializeField] private EntranceEdge entranceEdge = EntranceEdge.FrontRight;
        [Tooltip("How many tiles wide the entrance is, from the entrance tile along its edge.")]
        [Min(1)] [SerializeField] private int entranceWidth = 2;
        [Tooltip("Price of each 2 x 2 block, in order of purchase; the last price repeats.")]
        [SerializeField] private List<Cost> expansionCosts = new List<Cost>();
        [SerializeField] private List<StartingPiece> startingFurniture = new List<StartingPiece>();
        [Header("The wagon shop (the journey, step 3)")]
        [Tooltip("The carpet's size in tiles (along A x along B) at each wagon level. Empty: the walled shop.")]
        [SerializeField] private List<Vector2Int> carpetSizes = new List<Vector2Int>();
        [Tooltip("The price of each wagon upgrade: to level 2, 3, ...")]
        [SerializeField] private List<Cost> wagonCosts = new List<Cost>();
        [Tooltip("What the wagon carries between camps at each level: Store goods plus materials, in units.")]
        [SerializeField] private List<int> wagonCargo = new List<int>();
        [Tooltip("The wagon shop's starting furniture (on the level-1 carpet).")]
        [SerializeField] private List<StartingPiece> wagonFurniture = new List<StartingPiece>();

        public override string IdKind => "shop";
        public Vector2Int LotBlocks => lotBlocks;
        public IReadOnlyList<Vector2Int> StartBlocks => startBlocks;
        public Vector2Int EntranceTile => entranceTile;
        public EntranceEdge Entrance => entranceEdge;
        public int EntranceWidth => entranceWidth;

        /// <summary>The tiles just inside the entrance: from the entrance tile along its edge.</summary>
        public List<Vector2Int> EntranceTiles()
        {
            var tiles = new List<Vector2Int>();
            var step = entranceEdge == EntranceEdge.FrontRight ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
            for (var i = 0; i < Mathf.Max(1, entranceWidth); i++) tiles.Add(entranceTile + step * i);
            return tiles;
        }
        public IReadOnlyList<StartingPiece> StartingFurniture => Wagon ? wagonFurniture : startingFurniture;

        /// <summary>The shop is the wagon's fold-out carpet (no walls), grown by upgrading the wagon.</summary>
        public bool Wagon => carpetSizes.Count > 0;
        public int WagonMaxLevel => carpetSizes.Count;
        public int WagonCargo(int level) => wagonCargo.Count == 0 ? int.MaxValue : wagonCargo[Mathf.Clamp(level - 1, 0, wagonCargo.Count - 1)];
        public Vector2Int CarpetSize(int level) => carpetSizes.Count == 0 ? Vector2Int.zero : carpetSizes[Mathf.Clamp(level - 1, 0, carpetSizes.Count - 1)];
        /// <summary>The price of upgrading the wagon to <paramref name="level"/> (2 and up).</summary>
        public Cost WagonCost(int level) => wagonCosts.Count == 0 ? new Cost() : wagonCosts[Mathf.Clamp(level - 2, 0, wagonCosts.Count - 1)];

        public Cost ExpansionCost(int purchaseIndex) =>
            expansionCosts.Count == 0 ? new Cost() : expansionCosts[Mathf.Clamp(purchaseIndex, 0, expansionCosts.Count - 1)];

        public override void Validate(List<string> errors)
        {
            base.Validate(errors);
            if (startBlocks.Count == 0) errors.Add($"{Describe()}: no start blocks");
            foreach (var p in startingFurniture)
                if (p.furniture == null) errors.Add($"{Describe()}: a starting piece has no furniture");
            foreach (var c in carpetSizes)
                if (c.x < 2 || c.y < 2 || c.x % 2 != 0 || c.y % 2 != 0) errors.Add($"{Describe()}: carpet sizes are whole 2 x 2 blocks");
        }

#if UNITY_EDITOR
        public void EditorSetWagon(List<Vector2Int> sizes, List<Cost> costs, List<StartingPiece> furniture)
        {
            carpetSizes = sizes;
            wagonCosts = costs;
            wagonFurniture = furniture;
        }

        public bool EditorHasWagon => carpetSizes.Count > 0;
        public bool EditorHasCargo => wagonCargo.Count > 0;
        public void EditorSetCargo(List<int> cargo) => wagonCargo = cargo;

        public void EditorSetLayout(List<Vector2Int> start, Vector2Int entrance, int width, List<StartingPiece> furniture)
        {
            startBlocks = start;
            entranceTile = entrance;
            entranceWidth = width;
            startingFurniture = furniture;
        }

        public void EditorSetup(Vector2Int lot, List<Vector2Int> start, Vector2Int entrance, EntranceEdge edge, List<Cost> costs, List<StartingPiece> furniture)
        {
            lotBlocks = lot;
            startBlocks = start;
            entranceTile = entrance;
            entranceEdge = edge;
            expansionCosts = costs;
            startingFurniture = furniture;
        }
#endif
    }
}
