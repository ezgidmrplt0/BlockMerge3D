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
    public int  mergeSize = 2;

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

        // Merge boyutu otomatik ayarlanir (0.6 oraninda, min 2)
        int width  = (gridMaxX - gridMinX) + 1;
        int height = (gridMaxY - gridMinY) + 1;
        int depth  = (gridMaxZ - gridMinZ) + 1;
        int maxDim = Mathf.Max(width, Mathf.Max(height, depth));

        mergeSize = Mathf.Max(2, Mathf.FloorToInt(maxDim * 0.6f));
        
        Debug.Log($"Grid Init: {width}x{height}x{depth}, Merge Size: {mergeSize}");
    }

    public void RegisterCell(Vector3Int cell, GameObject cube, Color color)
    {
        occupiedCells.Add(cell);
        cellObjects[cell] = cube;
        cellColors[cell] = color;
        
        // Yerlestirme animasyonu (Juicy Bump)
        StartCoroutine(BumpAnimation(cube.transform));
    }

    public (int cleared, int bonusLines) CheckAndClearLines()
    {
        if (!lineClearEnabled) return (0, 0);

        var toClear = new HashSet<Vector3Int>();
        int bonusCount = 0;
        int size = mergeSize;

        // Her üç düzlemde (XY, YZ, XZ) kare taraması yap
        // XY Düzlemleri
        for (int z = gridMinZ; z <= gridMaxZ; z++)
            for (int x = gridMinX; x <= gridMaxX - size + 1; x++)
                for (int y = gridMinY; y <= gridMaxY - size + 1; y++)
                    bonusCount += CheckAndAddSquare(x, y, z, size, true, true, false, toClear);

        // YZ Düzlemleri
        for (int x = gridMinX; x <= gridMaxX; x++)
            for (int y = gridMinY; y <= gridMaxY - size + 1; y++)
                for (int z = gridMinZ; z <= gridMaxZ - size + 1; z++)
                    bonusCount += CheckAndAddSquare(y, z, x, size, false, true, true, toClear);

        // XZ Düzlemleri
        for (int y = gridMinY; y <= gridMaxY; y++)
            for (int x = gridMinX; x <= gridMaxX - size + 1; x++)
                for (int z = gridMinZ; z <= gridMaxZ - size + 1; z++)
                    bonusCount += CheckAndAddSquare(x, z, y, size, true, false, true, toClear);

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
                StartCoroutine(AnimateRoutine(go, i * 0.01f, true));
            }
        }

        return (sorted.Count, bonusCount);
    }

    private int CheckAndAddVolume(int x, int y, int z, int size, HashSet<Vector3Int> toClear)
    {
        var cube = new List<Vector3Int>();
        Color? firstCol = null;
        for (int dx = 0; dx < size; dx++)
            for (int dy = 0; dy < size; dy++)
                for (int dz = 0; dz < size; dz++)
                {
                    var cell = new Vector3Int(x + dx, y + dy, z + dz);
                    if (!occupiedCells.Contains(cell) || !cellColors.TryGetValue(cell, out Color c)) return 0;
                    if (!firstCol.HasValue) firstCol = c;
                    else if (!ColorsApproxEqual(c, firstCol.Value)) return 0;
                    cube.Add(cell);
                }
        foreach (var c in cube) toClear.Add(c);
        return 1;
    }

    private int CheckAndAddSquare(int v1, int v2, int constV, int size, bool useX, bool useY, bool useZ, HashSet<Vector3Int> toClear)
    {
        var square = new List<Vector3Int>();
        Color? firstCol = null;
        for (int di = 0; di < size; di++)
            for (int dj = 0; dj < size; dj++)
            {
                Vector3Int cell = Vector3Int.zero;
                if (useX && useY) cell = new Vector3Int(v1 + di, v2 + dj, constV);
                else if (useY && useZ) cell = new Vector3Int(constV, v1 + di, v2 + dj);
                else if (useX && useZ) cell = new Vector3Int(v1 + di, constV, v2 + dj);

                if (!occupiedCells.Contains(cell) || !cellColors.TryGetValue(cell, out Color c)) return 0;
                if (!firstCol.HasValue) firstCol = c;
                else if (!ColorsApproxEqual(c, firstCol.Value)) return 0;
                square.Add(cell);
            }
        foreach (var c in square) toClear.Add(c);
        return 1;
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

    private IEnumerator AnimateRoutine(GameObject go, float delay, bool cleared)
    {
        yield return new WaitForSeconds(delay);
        if (go == null) yield break;

        float duration = 0.35f;
        float elapsed = 0f;
        Vector3 startScale = go.transform.localScale;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float scaleMultiplier = (t < 0.3f) ? Mathf.Lerp(1f, 1.3f, t / 0.3f) : Mathf.Lerp(1.3f, 0f, (t - 0.3f) / 0.7f);
            if (go != null) go.transform.localScale = startScale * scaleMultiplier;
            yield return null;
        }
        Destroy(go);
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

        int size = mergeSize;
        
        // Parcanin her hucresi icin etrafinda bir size x size kare olusuyor mu bak (3 duzlemde)
        foreach (var pPos in tempPlaced)
        {
            // XY
            var colXY = CheckSquareAt(tempPlaced, pPos, size, true, true, false);
            if (colXY.HasValue) return colXY;
            // YZ
            var colYZ = CheckSquareAt(tempPlaced, pPos, size, false, true, true);
            if (colYZ.HasValue) return colYZ;
            // XZ
            var colXZ = CheckSquareAt(tempPlaced, pPos, size, true, false, true);
            if (colXZ.HasValue) return colXZ;
        }

        return null;
    }

    private Color? CheckSquareAt(HashSet<Vector3Int> newCells, Vector3Int pPos, int size, bool useX, bool useY, bool useZ)
    {
        for (int o1 = -size + 1; o1 <= 0; o1++)
            for (int o2 = -size + 1; o2 <= 0; o2++)
            {
                Color? foundCol = null;
                bool valid = true;
                bool hasNew = false;
                for (int d1 = 0; d1 < size; d1++)
                {
                    for (int d2 = 0; d2 < size; d2++)
                    {
                        Vector3Int cell = Vector3Int.zero;
                        if (useX && useY) cell = pPos + new Vector3Int(o1 + d1, o2 + d2, 0);
                        else if (useY && useZ) cell = pPos + new Vector3Int(0, o1 + d1, o2 + d2);
                        else if (useX && useZ) cell = pPos + new Vector3Int(o1 + d1, 0, o2 + d2);

                        if (newCells.Contains(cell)) hasNew = true;
                        else if (occupiedCells.Contains(cell))
                        {
                            if (cellColors.TryGetValue(cell, out Color c))
                            {
                                if (!foundCol.HasValue) foundCol = c;
                                else if (!ColorsApproxEqual(c, foundCol.Value)) { valid = false; break; }
                            }
                        }
                        else { valid = false; break; }
                    }
                    if (!valid) break;
                }
                if (valid && hasNew) return foundCol ?? Color.white;
            }
        return null;
    }
}
