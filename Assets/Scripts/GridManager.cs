using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    private HashSet<Vector3Int> targetCells   = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();
    private Dictionary<Vector3Int, GameObject> cellObjects = new Dictionary<Vector3Int, GameObject>();

    public float  CellSize { get; private set; }
    public float  Spacing  { get; private set; }
    public float  Step     => CellSize + Spacing;
    public Vector3 Origin  { get; private set; }

    public int TotalCells   => targetCells.Count;
    public int PlacedCells  => occupiedCells.Count;

    public bool lineClearEnabled;

    private int gridMinX, gridMaxX, gridMinY, gridMaxY, gridMinZ, gridMaxZ;

    private void Awake() { Instance = this; }

    public void Initialize(List<Vector3Int> cells, float cellSize, float spacing, Vector3 origin)
    {
        targetCells   = new HashSet<Vector3Int>(cells);
        occupiedCells.Clear();
        ClearAllCellObjects();
        CellSize = cellSize;
        Spacing  = spacing;
        Origin   = origin;

        gridMinX = gridMinY = gridMinZ = int.MaxValue;
        gridMaxX = gridMaxY = gridMaxZ = int.MinValue;
        foreach (var c in targetCells)
        {
            if (c.x < gridMinX) gridMinX = c.x; if (c.x > gridMaxX) gridMaxX = c.x;
            if (c.y < gridMinY) gridMinY = c.y; if (c.y > gridMaxY) gridMaxY = c.y;
            if (c.z < gridMinZ) gridMinZ = c.z; if (c.z > gridMaxZ) gridMaxZ = c.z;
        }
    }

    public void RegisterCell(Vector3Int cell, GameObject cube) => cellObjects[cell] = cube;

    public int CheckAndClearLines()
    {
        if (!lineClearEnabled) return 0;

        var toClear = new HashSet<Vector3Int>();

        for (int y = gridMinY; y <= gridMaxY; y++)
            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                bool full = true;
                for (int x = gridMinX; x <= gridMaxX; x++)
                {
                    var cell = new Vector3Int(x, y, z);
                    if (!targetCells.Contains(cell) || !occupiedCells.Contains(cell)) { full = false; break; }
                }
                if (full)
                    for (int x = gridMinX; x <= gridMaxX; x++) toClear.Add(new Vector3Int(x, y, z));
            }

        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                bool full = true;
                for (int y = gridMinY; y <= gridMaxY; y++)
                {
                    var cell = new Vector3Int(x, y, z);
                    if (!targetCells.Contains(cell) || !occupiedCells.Contains(cell)) { full = false; break; }
                }
                if (full)
                    for (int y = gridMinY; y <= gridMaxY; y++) toClear.Add(new Vector3Int(x, y, z));
            }

        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int y = gridMinY; y <= gridMaxY; y++)
            {
                bool full = true;
                for (int z = gridMinZ; z <= gridMaxZ; z++)
                {
                    var cell = new Vector3Int(x, y, z);
                    if (!targetCells.Contains(cell) || !occupiedCells.Contains(cell)) { full = false; break; }
                }
                if (full)
                    for (int z = gridMinZ; z <= gridMaxZ; z++) toClear.Add(new Vector3Int(x, y, z));
            }

        var sortedClear = new List<Vector3Int>(toClear);
        sortedClear.Sort((a, b) => (a.x + a.y + a.z).CompareTo(b.x + b.y + b.z));

        for (int i = 0; i < sortedClear.Count; i++)
        {
            var cell = sortedClear[i];
            occupiedCells.Remove(cell);
            if (cellObjects.TryGetValue(cell, out var go))
            {
                cellObjects.Remove(cell);
                AnimateAndDestroy(go, i * 0.025f);
            }
        }

        return toClear.Count;
    }

    public void ClearAllCellObjects()
    {
        foreach (var go in cellObjects.Values)
        {
            if (go == null) continue;
            DOTween.Kill(go.transform);
            Object.Destroy(go);
        }
        cellObjects.Clear();
    }

    private static void AnimateAndDestroy(GameObject go, float delay)
    {
        var t = go.transform;
        Vector3 drift = new Vector3(
            Random.Range(-0.35f, 0.35f),
            Random.Range(0.15f, 0.55f),
            Random.Range(-0.35f, 0.35f));

        var seq = DOTween.Sequence().SetLink(go);
        if (delay > 0f) seq.AppendInterval(delay);
        seq.Append(t.DOScale(t.localScale * 1.3f, 0.07f).SetEase(Ease.OutQuad));
        seq.Join(t.DOMove(t.position + drift, 0.28f).SetEase(Ease.OutCubic));
        seq.Append(t.DOScale(Vector3.zero, 0.16f).SetEase(Ease.InBack));
        seq.OnComplete(() => { if (go != null) Object.Destroy(go); });
    }

    public Vector3 CellToWorld(Vector3Int cell)
        => Origin + new Vector3(cell.x, cell.y, cell.z) * Step + Vector3.one * (CellSize * 0.5f);

    public Vector3Int RootToOffset(Vector3 rootWorld)
    {
        Vector3 local = (rootWorld - Origin) / Step;
        return new Vector3Int(
            Mathf.RoundToInt(local.x),
            Mathf.RoundToInt(local.y),
            Mathf.RoundToInt(local.z));
    }

    public Vector3 OffsetToRoot(Vector3Int offset)
        => Origin + new Vector3(offset.x, offset.y, offset.z) * Step;

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
