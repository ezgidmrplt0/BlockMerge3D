using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    private HashSet<Vector3Int> targetCells   = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();

    public float  CellSize { get; private set; }
    public float  Spacing  { get; private set; }
    public float  Step     => CellSize + Spacing;
    public Vector3 Origin  { get; private set; }

    public int TotalCells   => targetCells.Count;
    public int PlacedCells  => occupiedCells.Count;

    public int MaxCellY
    {
        get { int m = 0; foreach (var c in targetCells) if (c.y > m) m = c.y; return m; }
    }

    private void Awake() { Instance = this; }

    public void Initialize(List<Vector3Int> cells, float cellSize, float spacing, Vector3 origin)
    {
        targetCells   = new HashSet<Vector3Int>(cells);
        occupiedCells.Clear();
        CellSize = cellSize;
        Spacing  = spacing;
        Origin   = origin;
    }

    // Dünya konumundan grid hücresinin merkezi
    public Vector3 CellToWorld(Vector3Int cell)
        => Origin + new Vector3(cell.x, cell.y, cell.z) * Step + Vector3.one * (CellSize * 0.5f);

    // Parça root'unun dünya konumundan grid offset'i
    public Vector3Int RootToOffset(Vector3 rootWorld)
    {
        Vector3 local = (rootWorld - Origin) / Step;
        return new Vector3Int(
            Mathf.RoundToInt(local.x),
            Mathf.RoundToInt(local.y),
            Mathf.RoundToInt(local.z));
    }

    // Grid offset'inden parça root'unun dünya konumu
    public Vector3 OffsetToRoot(Vector3Int offset)
        => Origin + new Vector3(offset.x, offset.y, offset.z) * Step;

    // Kamera ışınına en yakın geçerli yerleşim offset'ini bulur (ekran-uzay snap)
    public bool TryFindSnapOffset(List<Vector3Int> cells, Ray ray, float maxDist, out Vector3Int result)
    {
        result = Vector3Int.zero;
        float best = maxDist;
        bool found = false;

        var seen = new HashSet<Vector3Int>();
        foreach (var t in targetCells)
        {
            foreach (var c in cells)
            {
                var off = t - c;
                if (!seen.Add(off)) continue;
                if (!CanPlace(cells, off)) continue;

                float d = Vector3.Cross(ray.direction, OffsetToRoot(off) - ray.origin).magnitude;
                if (d < best) { best = d; result = off; found = true; }
            }
        }
        return found;
    }

    public bool CanPlace(List<Vector3Int> cells, Vector3Int offset)
    {
        foreach (var c in cells)
        {
            var g = c + offset;
            if (!targetCells.Contains(g) || occupiedCells.Contains(g)) return false;
        }
        return true;
    }

    public bool TryPlace(List<Vector3Int> cells, Vector3Int offset)
    {
        if (!CanPlace(cells, offset)) return false;
        foreach (var c in cells) occupiedCells.Add(c + offset);
        return true;
    }

    public void Remove(List<Vector3Int> cells, Vector3Int offset)
    {
        foreach (var c in cells) occupiedCells.Remove(c + offset);
    }

    public bool IsComplete()
        => targetCells.Count > 0 && occupiedCells.SetEquals(targetCells);
}
