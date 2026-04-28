using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    private HashSet<Vector3Int> targetCells   = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> occupiedCells = new HashSet<Vector3Int>();
    private Dictionary<Vector3Int, GameObject> cellObjects = new Dictionary<Vector3Int, GameObject>();
    private Dictionary<Vector3Int, Color>      cellColors  = new Dictionary<Vector3Int, Color>();

    public float  CellSize { get; private set; }
    public float  Spacing  { get; private set; }
    public float  Step     => CellSize + Spacing;
    public Vector3 Origin  { get; private set; }

    public bool GetCellColor(Vector3Int cell, out Color color)
    {
        return cellColors.TryGetValue(cell, out color);
    }

    public int TotalCells   => targetCells.Count;
    public int PlacedCells  => occupiedCells.Count;

    public bool lineClearEnabled = true;

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

    public void RegisterCell(Vector3Int cell, GameObject cube, Color color)
    {
        occupiedCells.Add(cell);
        cellObjects[cell] = cube;
        cellColors[cell] = color;

        StartCoroutine(BumpAnimation(cube.transform));
    }

    public (int cleared, int bonusLines) CheckAndClearLines()
    {
        if (!lineClearEnabled) return (0, 0);

        var allLines = new List<List<Vector3Int>>();

        for (int y = gridMinY; y <= gridMaxY; y++)
            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                var line = BuildLine(y, z, true, false, false);
                if (line != null) allLines.Add(line);
            }
        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                var line = BuildLine(x, z, false, true, false);
                if (line != null) allLines.Add(line);
            }
        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int y = gridMinY; y <= gridMaxY; y++)
            {
                var line = BuildLine(x, y, false, false, true);
                if (line != null) allLines.Add(line);
            }

        if (allLines.Count == 0) return (0, 0);

        int bonusLineCount = 0;
        var toClear = new HashSet<Vector3Int>();

        foreach (var line in allLines)
        {
            if (!IsLineMonochrome(line)) continue;
            bonusLineCount++;
            foreach (var cell in line) toClear.Add(cell);
        }

        if (toClear.Count == 0) return (0, 0);

        var sorted = new List<Vector3Int>(toClear);
        sorted.Sort((a, b) => (a.x + a.y + a.z).CompareTo(b.x + b.y + b.z));

        for (int i = 0; i < sorted.Count; i++)
        {
            var cell = sorted[i];
            occupiedCells.Remove(cell);
            cellColors.Remove(cell);
            if (cellObjects.TryGetValue(cell, out var go))
            {
                cellObjects.Remove(cell);
                AnimateAndDestroy(go, i * 0.025f, true);
            }
        }

        return (sorted.Count, bonusLineCount);
    }

    private List<Vector3Int> BuildLine(int a, int b, bool xAxis, bool yAxis, bool zAxis)
    {
        var line = new List<Vector3Int>();
        int lo = xAxis ? gridMinX : (yAxis ? gridMinY : gridMinZ);
        int hi = xAxis ? gridMaxX : (yAxis ? gridMaxY : gridMaxZ);
        for (int v = lo; v <= hi; v++)
        {
            Vector3Int cell = xAxis ? new Vector3Int(v, a, b)
                            : yAxis  ? new Vector3Int(a, v, b)
                                     : new Vector3Int(a, b, v);
            if (!targetCells.Contains(cell) || !occupiedCells.Contains(cell)) return null;
            line.Add(cell);
        }
        return line.Count > 0 ? line : null;
    }

    private bool IsLineMonochrome(List<Vector3Int> line)
    {
        if (!cellColors.TryGetValue(line[0], out Color first)) return false;
        for (int i = 1; i < line.Count; i++)
        {
            if (!cellColors.TryGetValue(line[i], out Color c)) return false;
            if (!ColorsApproxEqual(c, first)) return false;
        }
        return true;
    }

    public static bool ColorsApproxEqual(Color a, Color b)
        => Mathf.Abs(a.r - b.r) < 0.05f
        && Mathf.Abs(a.g - b.g) < 0.05f
        && Mathf.Abs(a.b - b.b) < 0.05f;

    public void ClearAllCellObjects()
    {
        foreach (var go in cellObjects.Values)
        {
            if (go == null) continue;
            DOTween.Kill(go.transform);
            Object.Destroy(go);
        }
        cellObjects.Clear();
        cellColors.Clear();
    }

    private IEnumerator BumpAnimation(Transform target)
    {
        Vector3 originalScale = Vector3.one * CellSize;
        float duration = 0.2f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.2f;
            target.localScale = originalScale * scale;
            yield return null;
        }
        target.localScale = originalScale;
    }

    private static void AnimateAndDestroy(GameObject go, float delay, bool isBonus)
    {
        var t = go.transform;
        float drift   = isBonus ? 0.65f : 0.35f;
        float upMin   = isBonus ? 0.35f : 0.15f;
        float upMax   = isBonus ? 0.90f : 0.55f;
        float scaleUp = isBonus ? 1.6f  : 1.3f;

        Vector3 d = new Vector3(
            Random.Range(-drift, drift),
            Random.Range(upMin, upMax),
            Random.Range(-drift, drift));

        var seq = DOTween.Sequence().SetLink(go);
        if (delay > 0f) seq.AppendInterval(delay);
        seq.Append(t.DOScale(t.localScale * scaleUp, isBonus ? 0.10f : 0.07f).SetEase(Ease.OutElastic));
        seq.Join(t.DOMove(t.position + d, isBonus ? 0.38f : 0.28f).SetEase(Ease.OutCubic));
        if (isBonus) seq.AppendInterval(0.04f);
        seq.Append(t.DOScale(Vector3.zero, isBonus ? 0.20f : 0.16f).SetEase(Ease.InBack));
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

    // --- Smart Spawn Helpers ---

    public static List<Vector3Int> RotateCells(List<Vector3Int> cells, Quaternion q)
    {
        var result = new List<Vector3Int>(cells.Count);
        foreach (var c in cells)
        {
            Vector3 v = q * new Vector3(c.x, c.y, c.z);
            result.Add(new Vector3Int(
                Mathf.RoundToInt(v.x),
                Mathf.RoundToInt(v.y),
                Mathf.RoundToInt(v.z)));
        }

        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        foreach (var c in result)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.z < minZ) minZ = c.z;
        }
        for (int i = 0; i < result.Count; i++)
            result[i] -= new Vector3Int(minX, minY, minZ);

        return result;
    }

    public List<Vector3Int> GetPossibleOffsets(List<Vector3Int> cells)
    {
        var valid = new List<Vector3Int>();
        var seen  = new HashSet<Vector3Int>();
        foreach (var t in targetCells)
        {
            if (occupiedCells.Contains(t)) continue;
            foreach (var c in cells)
            {
                var off = t - c;
                if (!seen.Add(off)) continue;
                if (CanPlace(cells, off)) valid.Add(off);
            }
        }
        return valid;
    }

    public Color? GetMergeColor(List<Vector3Int> cells, Vector3Int offset)
    {
        var tempPlaced = new HashSet<Vector3Int>();
        foreach (var c in cells) tempPlaced.Add(c + offset);

        for (int y = gridMinY; y <= gridMaxY; y++)
            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                var col = CheckLineForMerge(tempPlaced, y, z, true, false, false);
                if (col.HasValue) return col;
            }
        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int z = gridMinZ; z <= gridMaxZ; z++)
            {
                var col = CheckLineForMerge(tempPlaced, x, z, false, true, false);
                if (col.HasValue) return col;
            }
        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int y = gridMinY; y <= gridMaxY; y++)
            {
                var col = CheckLineForMerge(tempPlaced, x, y, false, false, true);
                if (col.HasValue) return col;
            }

        return null;
    }

    private Color? CheckLineForMerge(HashSet<Vector3Int> newCells, int a, int b, bool xAxis, bool yAxis, bool zAxis)
    {
        int lo = xAxis ? gridMinX : (yAxis ? gridMinY : gridMinZ);
        int hi = xAxis ? gridMaxX : (yAxis ? gridMaxY : gridMaxZ);

        bool hasNew = false;
        Color? foundCol = null;

        for (int v = lo; v <= hi; v++)
        {
            Vector3Int cell = xAxis ? new Vector3Int(v, a, b)
                            : yAxis  ? new Vector3Int(a, v, b)
                                     : new Vector3Int(a, b, v);

            if (!targetCells.Contains(cell)) return null;

            if (newCells.Contains(cell))
            {
                hasNew = true;
            }
            else if (occupiedCells.Contains(cell))
            {
                if (cellColors.TryGetValue(cell, out Color c))
                {
                    if (!foundCol.HasValue) foundCol = c;
                    else if (!ColorsApproxEqual(c, foundCol.Value)) return null;
                }
            }
            else return null;
        }

        if (!hasNew) return null;
        return foundCol ?? Color.white;
    }
}
