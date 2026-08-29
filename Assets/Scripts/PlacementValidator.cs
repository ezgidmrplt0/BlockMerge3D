using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PlacementValidator: Parçaların ızgaraya yerleştirilme kurallarını, katman kısıtlarını
/// ve snap (yapışma) koordinatlarını hesaplayan saf mantık (Pure C#) motorudur.
/// </summary>
public static class PlacementValidator
{
    /// <summary>
    /// Parçanın belirtilen ofset ile o anki aktif katmana yerleştirilip yerleştirilemeyeceğini kontrol eder.
    /// </summary>
    public static bool CanPlace(
        IReadOnlyCollection<Vector3Int> cells,
        Vector3Int offset,
        GridState state,
        int activeLayerY,
        bool isExplodingLayer = false)
    {
        if (isExplodingLayer || state == null || cells == null) return false;

        foreach (var c in cells)
        {
            var g = c + offset;
            if (!state.TargetCells.Contains(g) || g.y != activeLayerY) return false;
            if (state.OccupiedCells.Contains(g)) return false;
            if (state.FrozenCells.Contains(g)) return false;
        }

        return true;
    }

    /// <summary>
    /// Parçanın belirtilen herhangi bir katmana sığıp sığmadığını test eder (Fail kontrolü ve parça uyumluluğu için).
    /// </summary>
    public static bool CanPlaceOnLayer(
        IReadOnlyCollection<Vector3Int> cells,
        Vector3Int offset,
        int layerY,
        GridState state)
    {
        if (state == null || cells == null) return false;

        foreach (var c in cells)
        {
            var g = c + offset;
            if (!state.TargetCells.Contains(g) || g.y != layerY) return false;
            if (state.OccupiedCells.Contains(g)) return false;
            if (state.FrozenCells.Contains(g)) return false;
        }

        return true;
    }

    /// <summary>
    /// Bir parçanın belirtilen katmanda yerleşebileceği tüm geçerli ofsetleri döndürür.
    /// </summary>
    public static List<Vector3Int> GetPossibleOffsetsOnLayer(
        IReadOnlyCollection<Vector3Int> cells,
        int layerY,
        GridState state)
    {
        var valid = new List<Vector3Int>();
        if (state == null || cells == null) return valid;

        var seen = new HashSet<Vector3Int>();
        foreach (var t in state.TargetCells)
        {
            if (t.y != layerY) continue;
            if (state.OccupiedCells.Contains(t)) continue;

            foreach (var c in cells)
            {
                var off = t - c;
                if (!seen.Add(off)) continue;
                if (CanPlaceOnLayer(cells, off, layerY, state)) valid.Add(off);
            }
        }
        return valid;
    }

    /// <summary>
    /// Aktif katmana yerleştirilebilecek herhangi bir parça / geçerli hamle olup olmadığını kontrol eder.
    /// </summary>
    public static bool HasAnyValidMove(
        IReadOnlyList<List<Vector3Int>> pieceCellList,
        int activeLayerY,
        GridState state)
    {
        if (state == null || pieceCellList == null) return false;

        foreach (var cells in pieceCellList)
        {
            if (cells == null || cells.Count == 0) continue;
            var offsets = GetPossibleOffsetsOnLayer(cells, activeLayerY, state);
            if (offsets != null && offsets.Count > 0) return true;
        }

        return false;
    }

    /// <summary>
    /// Parçanın havada asılı kalmadığını, alttan desteklendiğini doğrular.
    /// </summary>
    public static bool IsSupported(
        IReadOnlyCollection<Vector3Int> cells,
        Vector3Int offset,
        GridState state)
    {
        if (state == null || cells == null) return false;

        foreach (var c in cells)
        {
            var g = c + offset;
            if (g.y <= 0 || g.y <= state.GridMinY) return true;
            if (state.OccupiedCells.Contains(new Vector3Int(g.x, g.y - 1, g.z))) return true;
        }

        return false;
    }

    /// <summary>
    /// Raycast ve mesafe projeksiyonu ile sürüklenen parça için en uygun snap ofsetini hesaplar.
    /// </summary>
    public static bool TryFindSnapOffset(
        IReadOnlyList<Vector3Int> cells,
        Ray ray,
        float maxDist,
        GridState state,
        int activeLayerY,
        Camera mainCam,
        Transform draggedTransform,
        Transform boardTransform,
        out Vector3Int result)
    {
        result = Vector3Int.zero;
        if (state == null || cells == null || cells.Count == 0) return false;

        // 1. Raycast-based precision target alignment (hitCell + hitNormal)
        if (mainCam != null)
        {
            Ray mouseRay = mainCam.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(mouseRay, 100f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (draggedTransform != null &&
                    (hit.transform.IsChildOf(draggedTransform) || hit.transform == draggedTransform))
                {
                    continue;
                }

                // Convert world hit to cell
                Vector3Int hitCell = state.WorldToCell(hit.collider.transform.position, boardTransform);

                // Sadece o anki aktif katmandaki (activeLayerY) hedefleri dikkate al
                if (hitCell.y == activeLayerY && (state.TargetCells.Contains(hitCell) || state.OccupiedCells.Contains(hitCell)))
                {
                    Vector3Int normalInt = new Vector3Int(
                        Mathf.RoundToInt(hit.normal.x),
                        0, // Dikey ofset ekleme, aynı katmanda kalsın
                        Mathf.RoundToInt(hit.normal.z)
                    );

                    Vector3Int targetAnchorCell = state.OccupiedCells.Contains(hitCell)
                        ? (hitCell + normalInt)
                        : hitCell;

                    if (targetAnchorCell.y != activeLayerY) targetAnchorCell.y = activeLayerY;

                    int closestIndex = 0;
                    float minWorldDist = float.MaxValue;
                    for (int i = 0; i < cells.Count; i++)
                    {
                        Vector3 blockWorldPos = (draggedTransform != null && i < draggedTransform.childCount)
                            ? draggedTransform.GetChild(i).position
                            : state.CellToWorld(cells[i], boardTransform);

                        float dist = Vector3.Distance(blockWorldPos, hit.point);
                        if (dist < minWorldDist)
                        {
                            minWorldDist = dist;
                            closestIndex = i;
                        }
                    }

                    if (minWorldDist > maxDist * 2.0f) continue;

                    Vector3Int snapOff = targetAnchorCell - cells[closestIndex];

                    bool outOfBounds = false;
                    foreach (var cell in cells)
                    {
                        Vector3Int g = cell + snapOff;
                        if (!state.TargetCells.Contains(g) || g.y != activeLayerY)
                        {
                            outOfBounds = true;
                            break;
                        }
                    }

                    if (!outOfBounds && CanPlace(cells, snapOff, state, activeLayerY, false))
                    {
                        result = snapOff;
                        return true;
                    }
                }
            }
        }

        // 2. Proximity-based Snapping Fallback (SADECE activeLayerY)
        var seen = new HashSet<Vector3Int>();
        float bestValidD = maxDist * 1.8f;
        Vector3Int bestValidOff = Vector3Int.zero;
        bool foundValid = false;

        foreach (var t in state.TargetCells)
        {
            if (t.y != activeLayerY) continue; // Üst/alt katmanlara yapışmayı kesin olarak engelle

            foreach (var c in cells)
            {
                var off = t - c;
                if (!seen.Add(off)) continue;

                bool outOfBounds = false;
                foreach (var cell in cells)
                {
                    Vector3Int g = cell + off;
                    if (!state.TargetCells.Contains(g) || g.y != activeLayerY)
                    {
                        outOfBounds = true;
                        break;
                    }
                }
                if (outOfBounds) continue;
                if (!CanPlace(cells, off, state, activeLayerY, false)) continue;

                Vector3 snappedCenter = Vector3.zero;
                foreach (var cell in cells)
                {
                    snappedCenter += state.CellToWorld(cell + off, boardTransform);
                }
                snappedCenter /= cells.Count;

                float d = Vector3.Cross(ray.direction, snappedCenter - ray.origin).magnitude;
                if (d < bestValidD)
                {
                    bestValidD = d;
                    bestValidOff = off;
                    foundValid = true;
                }
            }
        }

        if (foundValid)
        {
            result = bestValidOff;
            return true;
        }

        return false;
    }
}
