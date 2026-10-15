using System;
using System.Collections.Generic;
using Ravenholm.Content;
using Ravenholm.Domain.Shop;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The player's shop in the world (Shop Titans style, 2026-10-15). It draws the shop from its grid: floor tiles,
    /// full back walls and low walls on the edges, posts at the joins, and every placed piece of furniture, filled with
    /// stock. The walls and furniture block walking.
    /// In build mode the shop also shows the 2 x 2 plots that can be bought, and taps in the world select, move and place
    /// furniture or pick a plot. Every change is a command to the shop service; the view only redraws.
    /// </summary>
    public sealed class ShopView : MonoBehaviour
    {
        public static ShopView Current { get; private set; }

        [Tooltip("World position of the lot's front corner (tile 0,0).")]
        [SerializeField] private Vector2 origin;
        [SerializeField] private Sprite[] floorSprites = Array.Empty<Sprite>();
        [SerializeField] private Sprite plotSprite;
        [SerializeField] private Sprite plotSign;
        [SerializeField] private Sprite wallTallLeft, wallTallRight, wallLowLeft, wallLowRight, postTall, postLow;
        [SerializeField] private Sprite tileOk, tileBlocked;
        [Tooltip("Optional variants: a window on some back-left walls; the doorway's low-wall pieces (left and right shapes).")]
        [SerializeField] private Sprite wallWindowLeft, wallWindowRight, entranceLeft, entranceRight;
        [Tooltip("Optional: the stockroom door (one, on the back-right wall) and the rail wall that furniture hangs on.")]
        [SerializeField] private Sprite wallDoorLeft, wallDoorRight, wallRailLeft, wallRailRight;
        [SerializeField] private Material material;
        [SerializeField] private int blockerLayer;
        [Header("The wagon shop (the journey, step 3): per art level 1-3")]
        [SerializeField] private Sprite[] wagonSprites = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] carpetSprites = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] carpetEdgeA = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] carpetEdgeB = Array.Empty<Sprite>();

        /// <summary>The wagon's depth behind the carpet (tiles along B) and where its counter is.</summary>
        public const float WagonDepth = 1.4f;

        public Transform Wagon { get; private set; }

        /// <summary>Full walls sort this far behind their edge, so furniture hanging on them and anyone inside draw in front.</summary>
        private const float WallSortBack = 0.2f;

        private GameRoot _root;
        private Transform _shell, _plots, _marks, _furnitureRoot;
        private readonly Dictionary<string, FurnitureView> _views = new Dictionary<string, FurnitureView>();
        private string _stockKey = "";
        private float _blockedUntil;

        public bool BuildMode { get; private set; }
        /// <summary>The piece being moved or placed in build mode (placed or from storage), or null.</summary>
        public string Selected { get; private set; }
        /// <summary>The plot picked in build mode (block coordinates), or null.</summary>
        public Tile? SelectedPlot { get; private set; }
        public string LastMessage { get; private set; } = "";
        public Vector2 Origin => origin;

        /// <summary>Raised when the build-mode selection or message changes (the build panel listens).</summary>
        public event Action SelectionChanged;

        public IReadOnlyDictionary<string, FurnitureView> Views => _views;

#if UNITY_EDITOR
        public void EditorSetup(Vector2 lotOrigin, Sprite[] floors, Sprite plot, Sprite sign, Sprite tallL, Sprite tallR, Sprite lowL, Sprite lowR,
            Sprite pTall, Sprite pLow, Sprite ok, Sprite blocked, Material mat, int blocker)
        {
            origin = lotOrigin;
            floorSprites = floors;
            plotSprite = plot;
            plotSign = sign;
            wallTallLeft = tallL;
            wallTallRight = tallR;
            wallLowLeft = lowL;
            wallLowRight = lowR;
            postTall = pTall;
            postLow = pLow;
            tileOk = ok;
            tileBlocked = blocked;
            material = mat;
            blockerLayer = blocker;
        }

        public void EditorSetupWagon(Sprite[] wagons, Sprite[] carpets, Sprite[] edgesA, Sprite[] edgesB)
        {
            wagonSprites = wagons;
            carpetSprites = carpets;
            carpetEdgeA = edgesA;
            carpetEdgeB = edgesB;
        }

        public void EditorSetupVariants(Sprite windowL, Sprite windowR, Sprite doorL, Sprite doorR)
        {
            wallWindowLeft = windowL;
            wallWindowRight = windowR;
            entranceLeft = doorL;
            entranceRight = doorR;
        }

        public void EditorSetupWallKinds(Sprite stockDoorL, Sprite stockDoorR, Sprite railL, Sprite railR)
        {
            wallDoorLeft = stockDoorL;
            wallDoorRight = stockDoorR;
            wallRailLeft = railL;
            wallRailRight = railR;
        }
#endif

        // ------------------------------------------------------------------ geometry

        /// <summary>World point of vertex (a, b): tile corners; tile (a, b)'s front corner is vertex (a, b).</summary>
        public Vector2 Vertex(float a, float b) => origin + FurnitureView.AxisA * a + FurnitureView.AxisB * b;

        public Vector2 TileCentre(int a, int b) => Vertex(a + 0.5f, b + 0.5f);

        /// <summary>Whether a world point is on the shop's lot (nothing else may be built there).</summary>
        public bool InLot(Vector2 world)
        {
            var def = _root != null ? _root.Services.Shop.Definition : null;
            if (def == null) return false;
            var t = TileAt(world);
            if (def.Wagon)
            {
                // The biggest carpet and the wagon parked behind it.
                var max = def.CarpetSize(def.WagonMaxLevel);
                return t.A >= -1 && t.B >= -1 && t.A <= max.x && t.B <= max.y + 2;
            }
            return t.A >= -1 && t.B >= -1 && t.A <= def.LotBlocks.x * ShopGrid.BlockSize && t.B <= def.LotBlocks.y * ShopGrid.BlockSize;
        }

        public Tile TileAt(Vector2 world)
        {
            var d = world - origin;   // d = AxisA * a + AxisB * b
            return new Tile(Mathf.FloorToInt(d.x + 2f * d.y), Mathf.FloorToInt(2f * d.y - d.x));
        }

        /// <summary>Where a placement stands: a floor piece's footprint centre, or the floor point under a wall piece's middle.</summary>
        public Vector2 GroundOf(Placement p, FurnitureShape shape)
        {
            if (shape.Wall)
                return p.Facing == Facing.A ? Vertex(p.A + shape.WallEdges * 0.5f, p.B + 1) : Vertex(p.A + 1, p.B + shape.WallEdges * 0.5f);
            var (fa, fb) = shape.Footprint(p.Level, p.Facing);
            return Vertex(p.A + fa * 0.5f, p.B + fb * 0.5f);
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake() => Current = this;

        private void Start()
        {
            _root = GameRoot.Instance;
            if (_root == null || !_root.Services.Shop.Enabled) { enabled = false; return; }
            _shell = Group("Shell");
            _plots = Group("Plots");
            _marks = Group("Marks");
            _furnitureRoot = Group("Furniture");
            _root.Services.Shop.Changed += Rebuild;
            // The carpet grows only onto cleared ground: shop tile (a, b) is map cell (ox + a, oy + b).
            var ox = Mathf.RoundToInt(origin.x + 2f * origin.y);
            var oy = Mathf.RoundToInt(2f * origin.y - origin.x);
            _root.Services.Shop.CarpetGround = (a, b) => _root.Services.Stations.LandProblem(ox + a, oy + b);
            Rebuild();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_root != null) _root.Services.Shop.Changed -= Rebuild;
            if (SandboxSceneController.TapOverride == HandleTap) SandboxSceneController.TapOverride = null;
        }

        private Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        private void Update()
        {
            Restock();
            if (_blockedUntil > 0f && Time.unscaledTime > _blockedUntil) { _blockedUntil = 0f; DrawSelection(); }
        }

        // ------------------------------------------------------------------ drawing

        public void Rebuild()
        {
            if (_shell == null) return;
            var shop = _root.Services.Shop;
            var grid = shop.Grid;
            Clear(_shell);
            if (shop.Wagon)
            {
                RebuildWagon();
                SyncFurniture();
                DrawPlots();
                DrawSelection();
                SandboxSceneController.Current?.RefreshInspectables();
                return;
            }
            // The stockroom door: the free back-right wall edge furthest along it (nothing hangs there).
            var stockDoor = new Tile(int.MinValue, 0);
            foreach (var e in grid.Edges)
                if (e.Kind == EdgeKind.Tall && e.Side == EdgeSide.BackRight && (stockDoor.A == int.MinValue || e.Tile.B > stockDoor.B))
                    stockDoor = e.Tile;
            // Wall edges with furniture hanging on them use the rail wall (no window behind a shelf).
            var hung = new HashSet<(Tile, EdgeSide)>();
            foreach (var p in shop.State.Furniture)
            {
                var shape = p.Placed ? shop.ShapeOf(p.FurnitureId) : null;
                if (shape != null && shape.Wall) foreach (var e in grid.WallEdges(shape, p.A, p.B, p.Facing)) hung.Add(e);
            }

            foreach (var t in grid.Floor)
            {
                var sr = Sprite(_shell, "Floor", floorSprites.Length > 0 ? floorSprites[Mathf.Abs(t.A * 31 + t.B * 17) % floorSprites.Length] : null,
                    TileCentre(t.A, t.B));
                sr.sortingLayerName = "Ground";
                sr.sortingOrder = 1;
            }

            foreach (var e in grid.Edges)
            {
                var ((a0, b0), (a1, b1)) = ShopGrid.Ends(e.Tile, e.Side);
                var p0 = Vertex(a0, b0);
                var p1 = Vertex(a1, b1);
                var mid = (p0 + p1) * 0.5f;
                var alongA = e.Side == EdgeSide.BackLeft || e.Side == EdgeSide.FrontRight;
                if (e.Kind == EdgeKind.Entrance)
                {
                    var door = alongA ? entranceLeft : entranceRight;
                    if (door != null) Piece(_shell, "Doorway", door, mid, 0f, false);
                    continue;
                }
                EdgeBlocker(p0, p1, e.Side);
                var tall = e.Kind == EdgeKind.Tall;
                var sprite = tall ? (alongA ? wallTallLeft : wallTallRight) : (alongA ? wallLowLeft : wallLowRight);
                if (tall)
                {
                    // A wall that furniture hangs on is the rail wall; one stockroom door on the back-right wall; every third
                    // other full wall a window (where the art exists), counted along the wall.
                    var along = alongA ? e.Tile.A : e.Tile.B;
                    var rail = alongA ? wallRailLeft : wallRailRight;
                    var window = alongA ? wallWindowLeft : wallWindowRight;
                    if (hung.Contains((e.Tile, e.Side))) { if (rail != null) sprite = rail; }
                    else if (!alongA && wallDoorRight != null && e.Tile.Equals(stockDoor)) sprite = wallDoorRight;
                    else if (window != null && along % 3 == 1) sprite = window;
                }
                Piece(_shell, tall ? "Wall" : "LowWall", sprite, mid, tall ? WallSortBack : 0f, tall);
            }

            foreach (var post in grid.Posts())
                Piece(_shell, "Post", post.Tall ? postTall : postLow, Vertex(post.A, post.B), post.Tall ? WallSortBack + 0.01f : 0f, post.Tall);

            SyncFurniture();
            DrawPlots();
            DrawSelection();
            SandboxSceneController.Current?.RefreshInspectables();
        }

        /// <summary>Which of the three art levels a wagon level uses (1, 2, 2, 3).</summary>
        private static int ArtIndex(int wagonLevel) => wagonLevel <= 1 ? 0 : wagonLevel >= 4 ? 2 : 1;

        private static Sprite Pick(Sprite[] sprites, int i) => sprites.Length == 0 ? null : sprites[Mathf.Clamp(i, 0, sprites.Length - 1)];

        /// <summary>
        /// The wagon shop: the carpet laid on the ground with its fringe, the wagon parked behind it (its long side, folded
        /// down into the counter, facing the carpet), the Store's marker at the counter. Nothing hides anything: no walls.
        /// </summary>
        private void RebuildWagon()
        {
            var shop = _root.Services.Shop;
            var grid = shop.Grid;
            var art = ArtIndex(shop.WagonLevel);
            foreach (var t in grid.Floor)
            {
                var sr = Sprite(_shell, "Carpet", Pick(carpetSprites, art), TileCentre(t.A, t.B));
                sr.sortingLayerName = "Ground";
                sr.sortingOrder = 1;
            }
            foreach (var e in grid.Edges)
            {
                var ((a0, b0), (a1, b1)) = ShopGrid.Ends(e.Tile, e.Side);
                var alongA = e.Side == EdgeSide.BackLeft || e.Side == EdgeSide.FrontRight;
                var sr = Sprite(_shell, "Fringe", Pick(alongA ? carpetEdgeA : carpetEdgeB, art), (Vertex(a0, b0) + Vertex(a1, b1)) * 0.5f);
                sr.sortingLayerName = "Ground";
                sr.sortingOrder = 2;
            }

            var size = shop.CarpetSize;
            var ground = Vertex(size.x * 0.5f, size.y + WagonDepth * 0.5f);
            var wagon = new GameObject("Wagon");
            wagon.transform.SetParent(_shell, false);
            wagon.transform.position = ground;
            Wagon = wagon.transform;
            var body = wagon.AddComponent<SpriteRenderer>();
            body.sprite = Pick(wagonSprites, art);
            body.spriteSortPoint = SpriteSortPoint.Pivot;
            if (material != null) body.sharedMaterial = material;
            wagon.AddComponent<SpriteOutline>();
            // It stands in the way: a footprint 3 tiles along the carpet, the wagon's depth back.
            var blocker = new GameObject("WagonBlocker") { layer = blockerLayer };
            blocker.transform.SetParent(wagon.transform, false);
            var a0w = size.x * 0.5f - 1.5f;
            var a1w = size.x * 0.5f + 1.5f;
            var b0w = size.y + 0.15f;
            var b1w = size.y + WagonDepth;
            blocker.AddComponent<PolygonCollider2D>().points = new[]
            {
                Vertex(a0w, b0w) - ground, Vertex(a1w, b0w) - ground, Vertex(a1w, b1w) - ground, Vertex(a0w, b1w) - ground,
            };
            // The counter does the desk's job: tap it for the Store; ring its bell.
            var col = wagon.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(2.6f, 1.6f);
            col.offset = new Vector2(0f, 0.7f);
            var counter = wagon.AddComponent<StationInspectable>();
            counter.EditorSetup("Wagon counter", "Where customers do business. Open the Store's inventory here; in build mode, upgrade the wagon.", "");
            counter.EditorSetupStation("", true, Domain.Logistics.ReceiverKind.Store);
            var bell = new GameObject("Store Bell");
            bell.transform.SetParent(wagon.transform, false);
            bell.transform.position = ground - new Vector2(0f, 0.01f);   // just in front of the wagon, so its tap wins
            var bc = bell.AddComponent<BoxCollider2D>();
            bc.isTrigger = true;
            bc.size = new Vector2(0.45f, 0.45f);
            bc.offset = new Vector2(0.96f, 0.5f);   // over the bell painted on the counter's end
            var bi = bell.AddComponent<BellInspectable>();
            bi.EditorSetup("Store Bell", "", "");
            bi.EditorSetupVisual(bell.transform);
            // Customers stand at the counter: the Store's marker is on the carpet's back row, in front of it.
            if (ReceiverView.TryGet("receiver.store", out var store))
            {
                store.transform.position = Vertex(size.x * 0.5f, size.y - 0.5f);
                store.Reregister(true);
            }
            Physics2D.SyncTransforms();
        }

        private void SyncFurniture()
        {
            var shop = _root.Services.Shop;
            var keep = new HashSet<string>();
            foreach (var p in shop.State.Furniture)
            {
                if (!p.Placed) continue;
                var def = shop.FurnitureOf(p);
                var shape = shop.ShapeOf(p.FurnitureId);
                if (def == null || shape == null) continue;
                keep.Add(p.Id);
                if (!_views.TryGetValue(p.Id, out var view))
                {
                    view = FurnitureView.Create(_furnitureRoot, def, material, blockerLayer);
                    _views[p.Id] = view;
                    AddTapTarget(view, def);
                }
                view.Show(p, GroundOf(p, shape), shape.Footprint(p.Level, p.Facing));
                FitTapTarget(view);
            }
            foreach (var id in new List<string>(_views.Keys))
            {
                if (keep.Contains(id)) continue;
                Destroy(_views[id].gameObject);
                _views.Remove(id);
            }
            _stockKey = "";
        }

        /// <summary>Furniture is tapped like any building: the desk opens the Store and carries its bell; the rest show info.</summary>
        private static void AddTapTarget(FurnitureView view, ShopFurnitureDefinition def)
        {
            var col = view.gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            if (def.Display == FurnitureDisplay.None)
            {
                var desk = view.gameObject.AddComponent<StationInspectable>();
                desk.EditorSetup(def.DisplayName, "Where customers do business. Open the Store's inventory here; in build mode, move or upgrade it.", "");
                desk.EditorSetupStation("", true, Domain.Logistics.ReceiverKind.Store);
                var bell = new GameObject("Store Bell");
                bell.transform.SetParent(view.transform, false);
                bell.transform.localPosition = new Vector3(0f, -0.01f, 0f);   // just in front of the desk, so its tap wins
                var bc = bell.AddComponent<BoxCollider2D>();
                bc.isTrigger = true;
                bc.size = new Vector2(0.4f, 0.35f);
                bc.offset = new Vector2(0.04f, 0.52f);   // over the bell painted on the desk
                var bi = bell.AddComponent<BellInspectable>();
                bi.EditorSetup("Store Bell", "", "");
                bi.EditorSetupVisual(bell.transform);
                return;
            }
            view.gameObject.AddComponent<Inspectable>().EditorSetup(def.DisplayName,
                "Shows some of your stock. Open the Store for the full inventory; use Build to move, turn or upgrade it.", "");
        }

        private static void FitTapTarget(FurnitureView view)
        {
            var col = view.GetComponent<BoxCollider2D>();
            var sprite = view.Visual.sprite;
            if (col == null || sprite == null) return;
            var b = sprite.bounds;
            col.size = new Vector2(b.size.x * 0.8f, b.size.y * 0.7f);
            col.offset = new Vector2(view.Mirrored ? -b.center.x : b.center.x, b.center.y);
        }

        private void DrawPlots()
        {
            Clear(_plots);
            if (!BuildMode) return;
            foreach (var block in _root.Services.Shop.Grid.BuyableBlocks())
            {
                for (var i = 0; i < ShopGrid.BlockSize; i++)
                    for (var j = 0; j < ShopGrid.BlockSize; j++)
                    {
                        var t = new Tile(block.A * ShopGrid.BlockSize + i, block.B * ShopGrid.BlockSize + j);
                        var sr = Sprite(_plots, "Plot", plotSprite, TileCentre(t.A, t.B));
                        sr.sortingLayerName = "Ground";
                        sr.sortingOrder = 1;
                        if (SelectedPlot.HasValue && SelectedPlot.Value.Equals(block)) sr.color = new Color(1f, 0.92f, 0.7f);
                    }
                Sprite(_plots, "PlotSign", plotSign, Vertex(block.A * ShopGrid.BlockSize + 1, block.B * ShopGrid.BlockSize + 1));
            }
        }

        private void DrawSelection()
        {
            Clear(_marks);
            if (!BuildMode || Selected == null) return;
            var shop = _root.Services.Shop;
            var p = shop.Find(Selected);
            var shape = p != null ? shop.ShapeOf(p.FurnitureId) : null;
            if (p == null || !p.Placed || shape == null) return;
            if (shape.Wall)
            {
                foreach (var (t, _) in shop.Grid.WallEdges(shape, p.A, p.B, p.Facing)) Mark(t, tileOk);
                return;
            }
            foreach (var t in shop.Grid.FootprintTiles(shape, p.Level, p.A, p.B, p.Facing)) Mark(t, tileOk);
        }

        private void Mark(Tile t, Sprite sprite)
        {
            var sr = Sprite(_marks, "Mark", sprite, TileCentre(t.A, t.B));
            sr.sortingLayerName = "Ground";
            sr.sortingOrder = 3;
        }

        /// <summary>Hands each display piece its share of the stock: pieces of the same kind take consecutive slices.</summary>
        private void Restock()
        {
            if (_root == null) return;
            var st = _root.Services.Logistics.System.State;
            var key = st.StoreItems.Count + ":" + (st.StoreItems.Count > 0 ? st.StoreItems[st.StoreItems.Count - 1] : "") + ":" +
                      string.Join(",", st.PinnedDefinitions) + ":" + _views.Count;
            if (key == _stockKey) return;
            _stockKey = key;
            var lists = new Dictionary<FurnitureDisplay, List<(ItemDefinition def, int count, string newest)>>();
            var used = new Dictionary<FurnitureDisplay, int>();
            var ordered = new List<FurnitureView>(_views.Values);
            ordered.Sort((x, y) => string.CompareOrdinal(x.PlacementId, y.PlacementId));
            foreach (var v in ordered)
            {
                var kind = v.Definition.Display;
                if (!lists.TryGetValue(kind, out var list)) lists[kind] = list = Representative(_root, v.Definition.Shows);
                used.TryGetValue(kind, out var from);
                var slice = new List<(ItemDefinition, int)>();
                for (var i = from; i < Mathf.Min(list.Count, from + v.SlotCount); i++) slice.Add((list[i].def, list[i].count));
                used[kind] = from + v.SlotCount;
                v.SetStock(slice);
            }
        }

        /// <summary>
        /// Stocked items a display shows, one entry per item type with its count: pinned first, then strongest, then newest.
        /// </summary>
        public static List<(ItemDefinition def, int count, string newest)> Representative(GameRoot root, Func<ItemDefinition, bool> shows)
        {
            var st = root.Services.Logistics.System.State;
            var cat = root.Services.Catalogue;
            var groups = new Dictionary<string, (ItemDefinition def, int count, string newest)>();
            foreach (var inst in st.StoreItems)
            {
                if (!st.ItemDefinitions.TryGetValue(inst, out var defId) || !cat.TryGet<ItemDefinition>(defId, out var def) || !shows(def)) continue;
                groups.TryGetValue(defId, out var g);
                groups[defId] = (def, g.count + 1, string.CompareOrdinal(inst, g.newest ?? "") > 0 ? inst : g.newest);
            }
            var list = new List<(ItemDefinition def, int count, string newest)>(groups.Values);
            list.Sort((a, b) =>
            {
                var pa = st.PinnedDefinitions.Contains(a.def.Id);
                var pb = st.PinnedDefinitions.Contains(b.def.Id);
                if (pa != pb) return pa ? -1 : 1;
                var sa = a.def.Power + a.def.Protection + a.def.PotionStrength + a.def.CounterStrength;
                var sb = b.def.Power + b.def.Protection + b.def.PotionStrength + b.def.CounterStrength;
                return sa != sb ? sb.CompareTo(sa) : string.CompareOrdinal(b.newest, a.newest);
            });
            return list;
        }

        // ------------------------------------------------------------------ pieces

        private SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, Vector2 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.spriteSortPoint = SpriteSortPoint.Pivot;
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        /// <summary>A shell piece standing on <paramref name="anchor"/>, sorting <paramref name="sortBack"/> behind it.</summary>
        private void Piece(Transform parent, string name, Sprite sprite, Vector2 anchor, float sortBack, bool fades)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = anchor + new Vector2(0f, sortBack);
            root.AddComponent<SortingGroup>();
            var sr = Sprite(root.transform, "Visual", sprite, anchor);
            sr.transform.position = anchor;
            if (fades) sr.gameObject.AddComponent<ObstructionFader>();   // full walls fade while the player is behind them
        }

        /// <summary>A thin wall along the edge that stops walking (thickness toward the outside).</summary>
        private void EdgeBlocker(Vector2 p0, Vector2 p1, EdgeSide side)
        {
            var go = new GameObject("WallBlocker") { layer = blockerLayer };
            go.transform.SetParent(_shell, false);
            var outward = side switch
            {
                EdgeSide.BackLeft => FurnitureView.AxisB,
                EdgeSide.BackRight => FurnitureView.AxisA,
                EdgeSide.FrontLeft => -FurnitureView.AxisA,
                _ => -FurnitureView.AxisB,
            } * 0.12f;
            go.AddComponent<PolygonCollider2D>().points = new[] { p0 - outward * 0.3f, p1 - outward * 0.3f, p1 + outward, p0 + outward };
        }

        private static void Clear(Transform t)
        {
            for (var i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }

        // ------------------------------------------------------------------ build mode

        public void SetBuildMode(bool on)
        {
            if (BuildMode == on) return;
            BuildMode = on;
            Selected = null;
            SelectedPlot = null;
            LastMessage = "";
            SandboxSceneController.TapOverride = on ? HandleTap : null;
            StationBuild.Deselect();
            DrawPlots();
            DrawSelection();
            SelectionChanged?.Invoke();
        }

        /// <summary>Selects a piece (placed, or from storage to be placed by the next tap).</summary>
        public void Select(string placementId)
        {
            if (placementId != null) StationBuild.Deselect();
            Selected = placementId;
            SelectedPlot = null;
            LastMessage = "";
            DrawPlots();
            DrawSelection();
            SelectionChanged?.Invoke();
        }

        public void Deselect() => Select(null);

        public void Report(string message)
        {
            LastMessage = message ?? "";
            SelectionChanged?.Invoke();
        }

        /// <summary>Build-mode taps in the world. Returns true when the tap was used.</summary>
        public bool HandleTap(Vector2 world)
        {
            if (!BuildMode) return false;
            var shop = _root.Services.Shop;
            // The camp's stations come first (they stand outside the shop): pick one, or set the picked one up elsewhere.
            if (Selected == null && StationBuild.HandleTap(world)) return true;

            // A piece under the finger: pick it. While moving one, only what stands on the tapped tile counts, so a tall
            // piece's picture never steals a tap meant for the floor behind it.
            var tile = TileAt(world);
            var hit = Selected == null ? PieceAt(world) : PieceOnTile(tile) ?? (shop.Grid.IsFloor(tile) ? null : PieceAt(world));
            if (hit != null && hit.PlacementId != Selected) { Select(hit.PlacementId); return true; }

            if (Selected != null)
            {
                var p = shop.Find(Selected);
                if (p != null) MoveTo(p, world);
                return true;
            }

            var t = TileAt(world);
            var block = new Tile(Mathf.FloorToInt(t.A / (float)ShopGrid.BlockSize), Mathf.FloorToInt(t.B / (float)ShopGrid.BlockSize));
            if (shop.Grid.CanBuy(block) == null)
            {
                SelectedPlot = block;
                LastMessage = "";
                DrawPlots();
                SelectionChanged?.Invoke();
                return true;
            }
            if (SelectedPlot.HasValue) { SelectedPlot = null; DrawPlots(); SelectionChanged?.Invoke(); }
            return true;
        }

        private FurnitureView PieceOnTile(Tile t)
        {
            var shop = _root.Services.Shop;
            foreach (var p in shop.State.Furniture)
            {
                var shape = p.Placed ? shop.ShapeOf(p.FurnitureId) : null;
                if (shape == null || shape.Wall) continue;
                foreach (var f in shop.Grid.FootprintTiles(shape, p.Level, p.A, p.B, p.Facing))
                    if (f.Equals(t) && _views.TryGetValue(p.Id, out var v)) return v;
            }
            return null;
        }

        private FurnitureView PieceAt(Vector2 world)
        {
            FurnitureView best = null;
            foreach (var v in _views.Values)
            {
                var col = v.GetComponent<BoxCollider2D>();
                if (col == null || !col.OverlapPoint(world)) continue;
                if (best == null || v.transform.position.y < best.transform.position.y) best = v;
            }
            return best;
        }

        /// <summary>Moves the selected piece to where the finger is: a floor piece centred on the tile, a wall piece onto the nearest free wall.</summary>
        private void MoveTo(Placement p, Vector2 world)
        {
            var shop = _root.Services.Shop;
            var shape = shop.ShapeOf(p.FurnitureId);
            if (shape == null) return;
            string why;
            if (shape.Wall)
            {
                var best = (a: 0, b: 0, f: Facing.A, d: float.MaxValue);
                foreach (var t in shop.Grid.Floor)
                    foreach (var f in new[] { Facing.A, Facing.B })
                    {
                        var probe = new Placement { FurnitureId = p.FurnitureId, Level = p.Level, A = t.A, B = t.B, Facing = f };
                        if (shop.CanPlace(p, t.A, t.B, f) != null) continue;
                        var d = Vector2.Distance(GroundOf(probe, shape) + new Vector2(0f, 0.6f), world);
                        if (d < best.d) best = (t.A, t.B, f, d);
                    }
                why = best.d < 1.5f ? shop.Place(p.Id, best.a, best.b, best.f) : "Tap a free full wall";
            }
            else
            {
                var t = TileAt(world);
                var (fa, fb) = shape.Footprint(p.Level, p.Facing);
                var a = t.A - (fa - 1) / 2;
                var b = t.B - (fb - 1) / 2;
                why = shop.Place(p.Id, a, b, p.Facing);
                if (why != null)
                {
                    Clear(_marks);
                    foreach (var tile in shop.Grid.FootprintTiles(shape, p.Level, a, b, p.Facing)) Mark(tile, tileBlocked);
                    _blockedUntil = Time.unscaledTime + 1f;
                }
            }
            Report(why ?? "");
            if (why == null) DrawSelection();
        }
    }
}
