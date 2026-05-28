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
    private Dictionary<Vector3Int, Renderer>    targetRenderers = new Dictionary<Vector3Int, Renderer>();
    private HashSet<Vector3Int> temporarilyHiddenGridCells = new HashSet<Vector3Int>();

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

    public void Initialize(GameObject mainShape, float cellSize, float spacing, Vector3 origin)
    {
        CellSize = cellSize;
        Spacing  = spacing;
        Origin   = origin;
        occupiedCells.Clear();
        ClearAllCellObjects();

        targetCells.Clear();
        targetRenderers.Clear();
        float step = cellSize + spacing;

        if (mainShape != null)
        {
            foreach (var r in mainShape.GetComponentsInChildren<Renderer>())
            {
                // Sadece sahnede aktif ve etkin olan görsel hücreleri baz al!
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

                string name = r.gameObject.name;
                if (name.StartsWith("Cube_"))
                {
                    string[] parts = name.Split('_');
                    if (parts.Length >= 4)
                    {
                        if (int.TryParse(parts[1], out int x) &&
                            int.TryParse(parts[2], out int y) &&
                            int.TryParse(parts[3], out int z))
                        {
                            var cell = new Vector3Int(x, y, z);
                            targetCells.Add(cell);
                            targetRenderers[cell] = r;
                        }
                    }
                }
            }
        }

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

        // Hedef saydam kılavuzu gizle ki üst üste binip rengi yutmasın!
        if (targetRenderers.TryGetValue(cell, out var r) && r != null)
        {
            r.enabled = false;
        }

        StartCoroutine(BumpAnimation(cube.transform));
    }

    public (int cleared, int bonusLines) CheckAndClearLines(System.Action onComplete = null)
    {
        if (!lineClearEnabled) { onComplete?.Invoke(); return (0, 0); }

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

        if (allLines.Count == 0) { onComplete?.Invoke(); return (0, 0); }

        int bonusLineCount = 0;
        var toClear = new HashSet<Vector3Int>();

        foreach (var line in allLines)
        {
            if (!IsLineMonochrome(line)) continue;
            bonusLineCount++;
            foreach (var cell in line) toClear.Add(cell);
        }

        if (toClear.Count == 0) { onComplete?.Invoke(); return (0, 0); }

        var sorted = new List<Vector3Int>(toClear);
        sorted.Sort((a, b) => (a.x + a.y + a.z).CompareTo(b.x + b.y + b.z));

        int pendingCount = sorted.Count;
        System.Action onOneDone = null;
        onOneDone = () =>
        {
            pendingCount--;
            if (pendingCount <= 0) onComplete?.Invoke();
        };

        for (int i = 0; i < sorted.Count; i++)
        {
            var cell = sorted[i];
            occupiedCells.Remove(cell);
            cellColors.Remove(cell);

            // Hücre boşaldığında kılavuz saydam görseli geri göster
            if (targetRenderers.TryGetValue(cell, out var r) && r != null)
            {
                r.enabled = true;
            }

            if (cellObjects.TryGetValue(cell, out var go))
            {
                cellObjects.Remove(cell);
                AnimateAndDestroy(go, i * 0.03f, true, onOneDone);
            }
            else
            {
                // Nesne yoksa yine de sayacı düşür
                onOneDone();
            }
        }

        return (sorted.Count, bonusLineCount);
    }

    private List<Vector3Int> BuildLine(int a, int b, bool xAxis, bool yAxis, bool zAxis)
    {
        var line = new List<Vector3Int>();
        int lo = xAxis ? gridMinX : (yAxis ? gridMinY : gridMinZ);
        int hi = xAxis ? gridMaxX : (yAxis ? gridMaxY : gridMaxZ);
        int targetCellCountInLine = 0;

        for (int v = lo; v <= hi; v++)
        {
            Vector3Int cell = xAxis ? new Vector3Int(v, a, b)
                            : yAxis  ? new Vector3Int(a, v, b)
                                     : new Vector3Int(a, b, v);

            if (targetCells.Contains(cell))
            {
                targetCellCountInLine++;
                if (!occupiedCells.Contains(cell)) return null;
                line.Add(cell);
            }
        }

        if (targetCellCountInLine < 2) return null;
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

    public static Color GetMaterialColor(Material mat)
    {
        if (mat == null) return Color.white;
        if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
        return Color.white;
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
        cellColors.Clear();
    }

    private IEnumerator BumpAnimation(Transform target)
    {
        if (target == null) yield break;
        Vector3 originalScale = Vector3.one * CellSize;
        // Hızlı scale-up, sonra küçük overshoot ile geri dön
        DOTween.Kill(target);
        target.localScale = originalScale;
        target.DOScale(originalScale * 1.35f, 0.08f).SetEase(Ease.OutQuad)
              .OnComplete(() =>
              {
                  if (target != null)
                      target.DOScale(originalScale, 0.14f).SetEase(Ease.OutElastic);
              });
        yield break;
    }

    private static void AnimateAndDestroy(GameObject go, float delay, bool isBonus, System.Action onDone = null)
    {
        if (go == null) { onDone?.Invoke(); return; }
        var t = go.transform;
        Vector3 origin = t.position;

        // --- Merge (bonus) efekti: flash → radyal patlama → merkeze çekim (implode) ---
        if (isBonus)
        {
            // Flash için tüm renderer'lara erişelim
            var renderers = go.GetComponentsInChildren<Renderer>();
            Color[] originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var mat = renderers[i].material;
                originalColors[i] = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                                  : mat.HasProperty("_Color")     ? mat.GetColor("_Color")
                                  : Color.white;
            }

            // Rastgele radyal patlama yönü
            Vector3 blastDir = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(0.2f, 1f),
                Random.Range(-1f, 1f)).normalized;
            float blastDist = Random.Range(0.55f, 1.0f);
            Vector3 blastTarget = origin + blastDir * blastDist;

            var seq = DOTween.Sequence().SetLink(go);
            if (delay > 0f) seq.AppendInterval(delay);

            // 1. Flash: hızlı scale-up + emit parlaması
            seq.Append(t.DOScale(t.localScale * 1.5f, 0.07f).SetEase(Ease.OutQuad));
            seq.Join(t.DOMove(blastTarget, 0.12f).SetEase(Ease.OutQuad));

            // 2. Kısa tutunma
            seq.AppendInterval(0.04f);

            // 3. Implode: merkeze geri çekilip sıfırla
            seq.Append(t.DOMove(origin, 0.18f).SetEase(Ease.InCubic));
            seq.Join(t.DOScale(Vector3.zero, 0.18f).SetEase(Ease.InBack));

            seq.OnComplete(() =>
            {
                if (go != null) Object.Destroy(go);
                onDone?.Invoke();
            });
        }
        else
        {
            // Normal (non-bonus) yerleştirme geri alma efekti — mevcut sade animasyon
            float drift = 0.35f;
            Vector3 d = new Vector3(
                Random.Range(-drift, drift),
                Random.Range(0.1f, 0.45f),
                Random.Range(-drift, drift));

            var seq = DOTween.Sequence().SetLink(go);
            if (delay > 0f) seq.AppendInterval(delay);
            seq.Append(t.DOScale(t.localScale * 1.3f, 0.07f).SetEase(Ease.OutBack));
            seq.Join(t.DOMove(t.position + d, 0.22f).SetEase(Ease.OutCubic));
            seq.Append(t.DOScale(Vector3.zero, 0.14f).SetEase(Ease.InBack));
            seq.OnComplete(() =>
            {
                if (go != null) Object.Destroy(go);
                onDone?.Invoke();
            });
        }
    }

    public Vector3 CellToWorld(Vector3Int cell)
    {
        Vector3 localPos = new Vector3(cell.x, cell.y, cell.z) * Step + Vector3.one * (CellSize * 0.5f);
        if (LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null)
        {
            return LevelManager.Instance.ActiveMainPiece.transform.TransformPoint(localPos);
        }
        return Origin + localPos;
    }

    public Vector3Int RootToOffset(Vector3 rootWorld)
    {
        if (LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null)
        {
            Vector3 local = LevelManager.Instance.ActiveMainPiece.transform.InverseTransformPoint(rootWorld) / Step;
            return new Vector3Int(
                Mathf.RoundToInt(local.x),
                Mathf.RoundToInt(local.y),
                Mathf.RoundToInt(local.z));
        }
        Vector3 globalLocal = (rootWorld - Origin) / Step;
        return new Vector3Int(
            Mathf.RoundToInt(globalLocal.x),
            Mathf.RoundToInt(globalLocal.y),
            Mathf.RoundToInt(globalLocal.z));
    }

    public Vector3 OffsetToRoot(Vector3Int offset)
    {
        Vector3 localPos = new Vector3(offset.x, offset.y, offset.z) * Step;
        if (LevelManager.Instance != null && LevelManager.Instance.ActiveMainPiece != null)
        {
            return LevelManager.Instance.ActiveMainPiece.transform.TransformPoint(localPos);
        }
        return Origin + localPos;
    }

    public bool TryFindSnapOffset(List<Vector3Int> cells, Ray ray, float maxDist, out Vector3Int result)
    {
        result = Vector3Int.zero;
        // Limit snap distance to a very generous 4.5 units to ensure smooth and easy snapping to all valid areas
        float minD = 4.5f; 
        bool found = false;

        var seen = new HashSet<Vector3Int>();
        foreach (var t in targetCells)
        {
            foreach (var c in cells)
            {
                var off = t - c;
                if (!seen.Add(off)) continue;
                if (!CanPlace(cells, off)) continue;

                // Parçanın yerleşeceği tüm hücrelerin görsel ağırlık merkezini (Visual Center) hesapla
                Vector3 snappedCenter = Vector3.zero;
                foreach (var cell in cells)
                {
                    snappedCenter += CellToWorld(cell + off);
                }
                snappedCenter /= cells.Count;

                // Sürüklenen parçanın merkezinden çıkan ışının, hedef merkezine olan dikey mesafesini ölç
                float d = Vector3.Cross(ray.direction, snappedCenter - ray.origin).magnitude;
                if (d < minD) 
                { 
                    minD = d; 
                    result = off; 
                    found = true; 
                }
            }
        }
        return found;
    }

    public bool CanPlace(List<Vector3Int> cells, Vector3Int offset)
    {
        foreach (var c in cells)
        {
            var g = c + offset;
            if (!targetCells.Contains(g)) return false;
            if (occupiedCells.Contains(g)) return false;
            if (cellObjects.ContainsKey(g) && cellObjects[g] != null) return false;
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
        foreach (var c in cells)
        {
            var cell = c + offset;
            occupiedCells.Remove(cell);
            if (targetRenderers.TryGetValue(cell, out var r) && r != null)
            {
                r.enabled = true;
            }
        }
    }

    public void UpdateSnappedPreviewCells(List<Vector3Int> snappedCells)
    {
        var newSnapped = new HashSet<Vector3Int>(snappedCells);
        
        // 1. Önce eski gizlenmiş olanlardan artık snaplenmeyenleri geri göster
        foreach (var cell in temporarilyHiddenGridCells)
        {
            if (!newSnapped.Contains(cell))
            {
                if (!occupiedCells.Contains(cell))
                {
                    if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                    {
                        r.enabled = true;
                    }
                }
            }
        }

        // 2. Şimdi yeni snaplenen kılavuz hücrelerini gizle
        foreach (var cell in newSnapped)
        {
            if (!occupiedCells.Contains(cell))
            {
                if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                {
                    r.enabled = false;
                }
            }
        }

        temporarilyHiddenGridCells = newSnapped;
    }

    public void ClearSnappedPreviewCells()
    {
        foreach (var cell in temporarilyHiddenGridCells)
        {
            if (!occupiedCells.Contains(cell))
            {
                if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                {
                    r.enabled = true;
                }
            }
        }
        temporarilyHiddenGridCells.Clear();
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
        int targetCellCountInLine = 0;

        for (int v = lo; v <= hi; v++)
        {
            Vector3Int cell = xAxis ? new Vector3Int(v, a, b)
                            : yAxis  ? new Vector3Int(a, v, b)
                                     : new Vector3Int(a, b, v);

            if (targetCells.Contains(cell))
            {
                targetCellCountInLine++;
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
        }

        if (targetCellCountInLine < 2) return null;
        if (!hasNew) return null;
        return foundCol ?? Color.white;
    }
}
