using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    private HashSet<Vector3Int> targetCells   = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();

    public float     CellSize   { get; private set; }
    public float     Spacing    { get; private set; }
    public float     Step       => CellSize + Spacing;
    public Vector3   Origin     { get; private set; }   // local space relative to LocalSpace
    public Transform LocalSpace { get; private set; }   // mainCubeLocation

    public int TotalCells  => targetCells.Count;
    public int PlacedCells => occupiedCells.Count;

    // World-space origin (for drag plane, etc.)
    public Vector3 WorldOrigin => LocalSpace != null ? LocalSpace.TransformPoint(Origin) : Origin;

    private void Awake() { Instance = this; }

    public void Initialize(List<Vector3Int> cells, float cellSize, float spacing, Vector3 localOrigin, Transform localSpace)
    {
        targetCells   = new HashSet<Vector3Int>(cells);
        occupiedCells.Clear();
        CellSize   = cellSize;
        Spacing    = spacing;
        Origin     = localOrigin;
        LocalSpace = localSpace;
    }

    public Vector3 CellToWorld(Vector3Int cell)
    {
        Vector3 local = Origin + new Vector3(cell.x, cell.y, cell.z) * Step + Vector3.one * (CellSize * 0.5f);
        return LocalSpace != null ? LocalSpace.TransformPoint(local) : local;
    }

    public Vector3Int RootToOffset(Vector3 rootWorld)
    {
        Vector3 local = LocalSpace != null ? LocalSpace.InverseTransformPoint(rootWorld) : rootWorld;
        Vector3 offset = (local - Origin) / Step;
        return new Vector3Int(
            Mathf.RoundToInt(offset.x),
            Mathf.RoundToInt(offset.y),
            Mathf.RoundToInt(offset.z));
    }

    public Vector3 OffsetToRoot(Vector3Int offset)
    {
        Vector3 local = Origin + new Vector3(offset.x, offset.y, offset.z) * Step;
        return LocalSpace != null ? LocalSpace.TransformPoint(local) : local;
    }

    public bool TryFindSnapOffset(List<Vector3Int> cells, Ray ray, float maxDist, out Vector3Int result)
    {
        result = Vector3Int.zero;
        float minD = 5.0f;
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
                if (d < minD) { minD = d; result = off; found = true; }
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
