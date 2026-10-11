using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The town's earthen edge: short dirt faces under every ground tile whose front edge has open background below
    /// it, so the town reads as a diorama (and the Mine cutaway sits flush with it). Built once from the tilemap.
    /// </summary>
    [RequireComponent(typeof(Tilemap))]
    public sealed class PlotSkirtView : MonoBehaviour
    {
        [SerializeField] private Material material;
        [SerializeField] private float depth = 0.7f;
        [SerializeField] private Color leftFace = new Color32(150, 112, 80, 255);
        [SerializeField] private Color rightFace = new Color32(118, 86, 60, 255);

        public int Faces { get; private set; }

        public void EditorSetup(Material mat) => material = mat;

        private void Start()
        {
            var tm = GetComponent<Tilemap>();
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
            {
                var i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                if (QualitySettings.activeColorSpace == ColorSpace.Linear) col = col.linear;   // sRGB-authored, linear project
                for (var k = 0; k < 4; k++) cols.Add(col);
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            var down = new Vector3(0f, -depth, 0f);
            var ink = new Color32(59, 42, 32, 255);
            foreach (var cell in tm.cellBounds.allPositionsWithin)
            {
                if (!tm.HasTile(cell)) continue;
                // Only where nothing stands below this tile: the front edge of the town.
                if (tm.HasTile(cell + new Vector3Int(-1, -1, 0))) continue;
                var c = transform.InverseTransformPoint(tm.GetCellCenterWorld(cell));
                var bottom = c + new Vector3(0f, -0.25f, 0f);
                var left = c + new Vector3(-0.5f, 0f, 0f);
                var right = c + new Vector3(0.5f, 0f, 0f);
                if (!tm.HasTile(cell + new Vector3Int(-1, 0, 0))) { Quad(left, bottom, bottom + down, left + down, leftFace); Faces++; }
                if (!tm.HasTile(cell + new Vector3Int(0, -1, 0))) { Quad(bottom, right, right + down, bottom + down, rightFace); Faces++; }
                Quad(left + down, bottom + down, bottom + down + new Vector3(0, -0.04f, 0), left + down + new Vector3(0, -0.04f, 0), ink);
                Quad(bottom + down, right + down, right + down + new Vector3(0, -0.04f, 0), bottom + down + new Vector3(0, -0.04f, 0), ink);
            }
            var go = new GameObject("Skirt");
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "PlotSkirt" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.sortingLayerName = "Ground";
            mr.sortingOrder = 1;
        }
    }
}
