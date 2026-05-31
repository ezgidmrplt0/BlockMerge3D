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
    private HashSet<Vector3Int> occludedGridCells           = new HashSet<Vector3Int>();

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
    public IEnumerable<Vector3Int> TargetCells => targetCells;

    public bool lineClearEnabled = true;

    private int gridMinX, gridMaxX, gridMinY, gridMaxY, gridMinZ, gridMaxZ;

    private void Awake() { Instance = this; }

    public int ActiveLayerY { get; private set; }

    public void SetActiveLayer(int y)
    {
        ActiveLayerY = Mathf.Clamp(y, gridMinY, gridMaxY);
        lineClearEnabled = false;
        RefreshLayerVisibility();
    }

    public int GridMinY => gridMinY;
    public int GridMaxY => gridMaxY;

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

        ActiveLayerY = gridMinY;
        lineClearEnabled = false; // Layer-by-layer mode
        RefreshLayerVisibility();
    }

    public void RefreshLayerVisibility()
    {
        bool isPanelMode = false;
        if (CameraOrbit.Instance != null && CameraOrbit.Instance.IsInPanelMode)
        {
            isPanelMode = true;
        }

        foreach (var kvp in targetRenderers)
        {
            Vector3Int cell = kvp.Key;
            Renderer r = kvp.Value;
            if (r != null)
            {
                if (occupiedCells.Contains(cell)) 
                    r.enabled = false;
                else if (isPanelMode)
                    r.enabled = (cell.y == ActiveLayerY);
                else
                    r.enabled = true; // 3D modunda hepsi görünür
            }
        }

        foreach (var kvp in cellObjects)
        {
            Vector3Int cell = kvp.Key;
            GameObject cube = kvp.Value;
            if (cube != null)
            {
                if (isPanelMode)
                    cube.SetActive(cell.y == ActiveLayerY);
                else
                    cube.SetActive(true);
            }
        }
    }

    public void CheckLayerCompletion(System.Action onLayerComplete, System.Action onLevelComplete)
    {
        int cellsInLayer = 0;
        int occupiedInLayer = 0;
        foreach (var c in targetCells)
        {
            if (c.y == ActiveLayerY)
            {
                cellsInLayer++;
                if (occupiedCells.Contains(c)) occupiedInLayer++;
            }
        }

        if (cellsInLayer > 0 && occupiedInLayer == cellsInLayer)
        {
            ActiveLayerY++;
            RefreshLayerVisibility();

            if (ActiveLayerY > gridMaxY)
            {
                onLevelComplete?.Invoke();
            }
            else
            {
                onLayerComplete?.Invoke();
            }
        }
    }

    public void RegisterCell(Vector3Int cell, GameObject cube, Color color)
    {
        occupiedCells.Add(cell);
        cellObjects[cell] = cube;
        cellColors[cell] = color;

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
        StopVisualFocus(null);
        ClearOccludingCells();
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
        
        // 1. Raycast-based precision target alignment (hitCell + hitNormal)
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            Ray mouseRay = mainCam.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(mouseRay, 100f);
            
            // Sort hits by distance
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            
            foreach (var hit in hits)
            {
                // Ignore hits on the dragged piece itself or its children
                if (hit.collider == null) continue;
                if (DraggablePiece.activeDrag != null && 
                    (hit.transform.IsChildOf(DraggablePiece.activeDrag.transform) || hit.transform == DraggablePiece.activeDrag.transform))
                {
                    continue;
                }
                
                // Get the grid coordinate of the hit block
                Vector3Int hitCell = RootToOffset(hit.collider.transform.position);
                
                // Verify the hit cell is a valid grid cell (guide cell or occupied cell)
                if (targetCells.Contains(hitCell) || occupiedCells.Contains(hitCell))
                {
                    // Convert hit normal to Vector3Int cardinal direction
                    Vector3Int normalInt = new Vector3Int(
                        Mathf.RoundToInt(hit.normal.x),
                        Mathf.RoundToInt(hit.normal.y),
                        Mathf.RoundToInt(hit.normal.z)
                    );
                    
                    Vector3Int targetAnchorCell = occupiedCells.Contains(hitCell) ? (hitCell + normalInt) : hitCell;
                    
                    // Find which block of the dragged piece is closest in world space to the hit point
                    int closestIndex = 0;
                    float minWorldDist = float.MaxValue;
                    for (int i = 0; i < cells.Count; i++)
                    {
                        Vector3 cellWorldPos = CellToWorld(cells[i]);
                        float dist = Vector3.Distance(cellWorldPos, hit.point);
                        if (dist < minWorldDist)
                        {
                            minWorldDist = dist;
                            closestIndex = i;
                        }
                    }
                    
                    Vector3Int snapOff = targetAnchorCell - cells[closestIndex];
                    
                    // Check if this snap offset keeps the entire piece within target boundaries
                    bool outOfBounds = false;
                    foreach (var cell in cells)
                    {
                        Vector3Int g = cell + snapOff;
                        if (!targetCells.Contains(g) || g.y != ActiveLayerY)
                        {
                            outOfBounds = true;
                            break;
                        }
                    }
                    
                    if (!outOfBounds)
                    {
                        result = snapOff;
                        return true;
                    }
                }
            }
        }

        // 2. Proximity-based Snapping Fallback (when dragging in empty space near the grid)
        float minD = 4.5f; 
        bool found = false;
        var seen = new HashSet<Vector3Int>();
        
        float bestValidD = 4.5f;
        Vector3Int bestValidOff = Vector3Int.zero;
        bool foundValid = false;

        float bestInvalidD = 3.0f; 
        Vector3Int bestInvalidOff = Vector3Int.zero;
        bool foundInvalid = false;

        foreach (var t in targetCells)
        {
            foreach (var c in cells)
            {
                var off = t - c;
                if (!seen.Add(off)) continue;

                // Check if all snapped cells are within the target grid shape boundaries
                bool outOfBounds = false;
                foreach (var cell in cells)
                {
                    Vector3Int g = cell + off;
                    if (!targetCells.Contains(g) || g.y != ActiveLayerY)
                    {
                        outOfBounds = true;
                        break;
                    }
                }
                if (outOfBounds) continue;

                // Visual Center of the snapped piece cells
                Vector3 snappedCenter = Vector3.zero;
                foreach (var cell in cells)
                {
                    snappedCenter += CellToWorld(cell + off);
                }
                snappedCenter /= cells.Count;

                // Distance from center to the drag ray
                float d = Vector3.Cross(ray.direction, snappedCenter - ray.origin).magnitude;
                
                bool isValid = CanPlace(cells, off);
                if (isValid)
                {
                    if (d < bestValidD)
                    {
                        bestValidD = d;
                        bestValidOff = off;
                        foundValid = true;
                    }
                }
                else
                {
                    if (d < bestInvalidD)
                    {
                        bestInvalidD = d;
                        bestInvalidOff = off;
                        foundInvalid = true;
                    }
                }
            }
        }

        if (foundValid)
        {
            result = bestValidOff;
            return true;
        }
        else if (foundInvalid)
        {
            result = bestInvalidOff;
            return true;
        }

        return false;
    }

    public bool IsSupported(List<Vector3Int> cells, Vector3Int offset)
    {
        // A rigid piece is structurally supported if at least one of its blocks is supported underneath (either floor or occupied cell)
        foreach (var c in cells)
        {
            var g = c + offset;
            // 1. Supported by floor
            if (g.y <= 0 || g.y <= gridMinY) return true;

            // 2. Supported by an occupied cell directly underneath
            if (occupiedCells.Contains(new Vector3Int(g.x, g.y - 1, g.z))) return true;
        }

        // If no blocks have any support underneath, the entire piece is floating in mid-air!
        return false;
    }

    public bool CanPlace(List<Vector3Int> cells, Vector3Int offset)
    {
        foreach (var c in cells)
        {
            var g = c + offset;
            if (!targetCells.Contains(g) || g.y != ActiveLayerY) return false;
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
                if (ShouldCellBeVisible(cell)) r.enabled = true;
            }
        }
    }

    private bool ShouldCellBeVisible(Vector3Int cell)
    {
        if (CameraOrbit.Instance != null && CameraOrbit.Instance.IsInPanelMode)
        {
            return cell.y == ActiveLayerY;
        }
        return true;
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
                        if (ShouldCellBeVisible(cell)) r.enabled = true;
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
                    if (ShouldCellBeVisible(cell)) r.enabled = true;
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

    // --- X-Ray Occlusion (Drag Görüş Açıklığı) ---

    /// <summary>
    /// Sürüklenen parçanın snap konumuna göre, kamera ile parça arasındaki
    /// boş hedef grid hücrelerini gizler. Yerleştirilmiş parçalara dokunamaz.
    /// </summary>
    /// <summary>
    /// Suruklenen parcanin snap konumuna gore, kamera ile parca arasindaki
    /// bos hedef grid hucrelerini gizler. Yerlestirilmis parcalara dokunamaz.
    /// </summary>
    public void UpdateOccludingCells(Vector3 cameraPos, List<Vector3Int> pieceCells, Vector3Int snapOffset)
    {
        // Onceki turda gizlenmis hucreleri geri ac
        foreach (var cell in occludedGridCells)
        {
            if (!occupiedCells.Contains(cell) && !temporarilyHiddenGridCells.Contains(cell))
            {
                if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                    if (ShouldCellBeVisible(cell)) r.enabled = true;
            }
        }
        occludedGridCells.Clear();

        if (pieceCells == null || pieceCells.Count == 0) return;

        // Parcanin snap sonrasi kapladigi hucreleri hesapla
        var pieceBoardCells = new HashSet<Vector3Int>();
        foreach (var c in pieceCells) pieceBoardCells.Add(c + snapOffset);

        // Kamera isini ile capraz mesafe esigi
        float perpThreshold = Step * 0.75f;

        foreach (var targetCell in targetCells)
        {
            if (occupiedCells.Contains(targetCell)) continue;
            if (pieceBoardCells.Contains(targetCell)) continue;

            Vector3 targetWorld = CellToWorld(targetCell);
            bool shouldOcclude = false;

            foreach (var pieceCell in pieceBoardCells)
            {
                Vector3 pieceWorld = CellToWorld(pieceCell);
                Vector3 camToPiece = pieceWorld - cameraPos;
                float pieceDist = camToPiece.magnitude;
                if (pieceDist < 0.001f) continue;

                Vector3 camDir = camToPiece / pieceDist;

                float targetProj = Vector3.Dot(targetWorld - cameraPos, camDir);
                if (targetProj <= 0f || targetProj >= pieceDist) continue;

                Vector3 closestPt = cameraPos + camDir * targetProj;
                float perpDist = (targetWorld - closestPt).magnitude;

                if (perpDist < perpThreshold)
                {
                    shouldOcclude = true;
                    break;
                }
            }

            if (shouldOcclude)
            {
                occludedGridCells.Add(targetCell);
                if (targetRenderers.TryGetValue(targetCell, out var r) && r != null)
                    r.enabled = false;
            }
        }
    }
    /// <summary>
    /// Occlusion gizlemesini sıfırlar — sürükleme bittiğinde veya snap kaybolduğunda çağrılır.
    /// </summary>
    public void ClearOccludingCells()
    {
        foreach (var cell in occludedGridCells)
        {
            if (!occupiedCells.Contains(cell) && !temporarilyHiddenGridCells.Contains(cell))
            {
                if (targetRenderers.TryGetValue(cell, out var r) && r != null)
                    if (ShouldCellBeVisible(cell)) r.enabled = true;
            }
        }
        occludedGridCells.Clear();
    }

    // --- Visual Focus System ---
    
    public enum VisualState
    {
        Normal,
        Darkened,
        HighlightedValid,
        HighlightedInvalid
    }

    private Dictionary<Renderer, Color> originalBaseColors = new Dictionary<Renderer, Color>();
    private Dictionary<Renderer, Color> originalEmissionColors = new Dictionary<Renderer, Color>();
    
    private Dictionary<Renderer, Color> pieceOriginalBaseColors = new Dictionary<Renderer, Color>();
    private Dictionary<Renderer, Color> pieceOriginalEmissionColors = new Dictionary<Renderer, Color>();

    private Dictionary<Renderer, VisualState> activeStates = new Dictionary<Renderer, VisualState>();
    private bool isFocusModeActive = false;

    public void StartVisualFocus(DraggablePiece piece)
    {
        if (isFocusModeActive) StopVisualFocus(piece);

        isFocusModeActive = true;
        activeStates.Clear();
        pieceOriginalBaseColors.Clear();
        pieceOriginalEmissionColors.Clear();

        // 3. Save original colors for the dragged piece's renderers
        if (piece != null)
        {
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                
                Material mat = r.material; // Instantiate material
                
                Color baseCol = Color.white;
                if (mat.HasProperty("_BaseColor")) baseCol = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color")) baseCol = mat.GetColor("_Color");
                pieceOriginalBaseColors[r] = baseCol;

                Color emissiveCol = Color.clear;
                if (mat.HasProperty("_EmissionColor")) emissiveCol = mat.GetColor("_EmissionColor");
                pieceOriginalEmissionColors[r] = emissiveCol;
            }
        }
    }

    public void UpdateVisualFocus(DraggablePiece piece, bool isSnapped, Vector3Int snapOffset)
    {
        if (!isFocusModeActive || piece == null) return;

        var currentCells = piece.CurrentCells;
        if (currentCells == null || currentCells.Count == 0) return;

        bool placementValid = isSnapped && CanPlace(currentCells, snapOffset);
        VisualState pieceState = (isSnapped && !placementValid) ? VisualState.HighlightedInvalid : VisualState.Normal;

        foreach (var r in piece.GetComponentsInChildren<Renderer>())
        {
            if (r != null)
            {
                if (!activeStates.TryGetValue(r, out VisualState currentState) || currentState != pieceState)
                {
                    activeStates[r] = pieceState;
                    TransitionToState(r, pieceState, 0.2f);
                }
            }
        }
    }

    public void StopVisualFocus(DraggablePiece piece)
    {
        if (!isFocusModeActive) return;

        isFocusModeActive = false;
        float fadeDuration = 0.2f;

        if (piece != null)
        {
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                TransitionToState(r, VisualState.Normal, fadeDuration);
            }
        }
        activeStates.Clear();
        pieceOriginalBaseColors.Clear();
        pieceOriginalEmissionColors.Clear();
    }

    private void TransitionToState(Renderer r, VisualState state, float duration)
    {
        if (r == null) return;

        r.material.DOKill();

        Color targetBase = Color.white;
        Color targetEmission = Color.clear;
        bool enableEmission = false;

        bool isPieceRenderer = pieceOriginalBaseColors.ContainsKey(r);
        
        if (isPieceRenderer)
        {
            Color origBase = pieceOriginalBaseColors[r];
            Color origEmis = pieceOriginalEmissionColors[r];

            switch (state)
            {
                case VisualState.Normal:
                default:
                    targetBase = origBase;
                    targetEmission = origEmis;
                    enableEmission = origEmis != Color.clear && origEmis.maxColorComponent > 0.01f;
                    break;

                case VisualState.HighlightedInvalid:
                    targetBase = Color.Lerp(origBase, new Color(0.9f, 0.2f, 0.2f, 1f), 0.5f);
                    targetEmission = new Color(0.9f, 0.2f, 0.2f) * 0.25f;
                    enableEmission = true;
                    break;
            }
        }
        else
        {
            return;
        }

        if (enableEmission)
        {
            r.material.EnableKeyword("_EMISSION");
        }
        else
        {
            r.material.DisableKeyword("_EMISSION");
        }

        if (r.material.HasProperty("_BaseColor"))
        {
            r.material.DOColor(targetBase, "_BaseColor", duration).SetEase(Ease.OutQuad);
        }
        else if (r.material.HasProperty("_Color"))
        {
            r.material.DOColor(targetBase, "_Color", duration).SetEase(Ease.OutQuad);
        }

        if (r.material.HasProperty("_EmissionColor"))
        {
            r.material.DOColor(targetEmission, "_EmissionColor", duration).SetEase(Ease.OutQuad);
        }
    }
}