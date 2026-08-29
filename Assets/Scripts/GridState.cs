using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GridState: Grid'in saf mantıksal ve geometrik veri modelini tutar.
/// Unity Rendering / GameObject bağlarından bağımsız, saf C# veri yapısıdır.
/// </summary>
public class GridState
{
    public float CellSize { get; set; } = 1f;
    public float Spacing { get; set; } = 0.1f;
    public float Step => CellSize + Spacing;
    public Vector3 Origin { get; set; } = Vector3.zero;

    public int GridMinX { get; set; }
    public int GridMaxX { get; set; }
    public int GridMinY { get; set; }
    public int GridMaxY { get; set; }
    public int GridMinZ { get; set; }
    public int GridMaxZ { get; set; }

    public HashSet<Vector3Int> TargetCells { get; } = new HashSet<Vector3Int>();
    public HashSet<Vector3Int> AllShapeCells { get; } = new HashSet<Vector3Int>();
    public HashSet<Vector3Int> OccupiedCells { get; } = new HashSet<Vector3Int>();
    public HashSet<Vector3Int> FrozenCells { get; } = new HashSet<Vector3Int>();
    public HashSet<Vector3Int> PrefilledCells { get; } = new HashSet<Vector3Int>();

    public Dictionary<Vector3Int, Color> CellColors { get; } = new Dictionary<Vector3Int, Color>();
    public Dictionary<Vector3Int, int> CellMatIndex { get; } = new Dictionary<Vector3Int, int>();
    public Dictionary<Vector3Int, int> IceRemainingHits { get; } = new Dictionary<Vector3Int, int>();

    public void Clear()
    {
        TargetCells.Clear();
        AllShapeCells.Clear();
        OccupiedCells.Clear();
        FrozenCells.Clear();
        PrefilledCells.Clear();
        CellColors.Clear();
        CellMatIndex.Clear();
        IceRemainingHits.Clear();
    }

    public void SetBounds(int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
    {
        GridMinX = minX; GridMaxX = maxX;
        GridMinY = minY; GridMaxY = maxY;
        GridMinZ = minZ; GridMaxZ = maxZ;
    }

    public Vector3 CellToWorld(Vector3Int cell, Transform activeTransform = null)
    {
        Vector3 localPos = new Vector3(cell.x * Step, cell.y * Step, cell.z * Step);
        if (activeTransform != null)
        {
            return activeTransform.TransformPoint(localPos);
        }
        return Origin + localPos;
    }

    public Vector3Int WorldToCell(Vector3 worldPos, Transform activeTransform = null)
    {
        float s = Step <= 0.0001f ? 1f : Step;
        if (activeTransform != null)
        {
            Vector3 local = activeTransform.InverseTransformPoint(worldPos);
            return new Vector3Int(
                Mathf.RoundToInt(local.x / s),
                Mathf.RoundToInt(local.y / s),
                Mathf.RoundToInt(local.z / s)
            );
        }
        Vector3 localPos = worldPos - Origin;
        return new Vector3Int(
            Mathf.RoundToInt(localPos.x / s),
            Mathf.RoundToInt(localPos.y / s),
            Mathf.RoundToInt(localPos.z / s)
        );
    }

    public bool IsCellOccupied(Vector3Int cell) => OccupiedCells.Contains(cell);
    public bool IsCellFrozen(Vector3Int cell) => FrozenCells.Contains(cell);
    public bool IsCellPrefilled(Vector3Int cell) => PrefilledCells.Contains(cell);
    public bool IsCellTarget(Vector3Int cell) => TargetCells.Contains(cell);

    public bool IsLayerComplete(int layerY)
    {
        int cellsInLayer = 0;
        int occupiedInLayer = 0;

        foreach (var c in AllShapeCells)
        {
            if (c.y == layerY)
            {
                cellsInLayer++;
                if (OccupiedCells.Contains(c)) occupiedInLayer++;
            }
        }

        return cellsInLayer > 0 && occupiedInLayer >= cellsInLayer;
    }

    /// <summary>
    /// Sıralı katman akışı için doldurulması gereken ilk tamamlanmamış katmanı bulur (Alttan üste doğru).
    /// </summary>
    public bool TryFindFirstIncompleteLayer(out int layerY)
    {
        for (int y = GridMinY; y <= GridMaxY; y++)
        {
            bool hasCells = false;
            bool layerFull = true;

            foreach (var cell in AllShapeCells)
            {
                if (cell.y != y) continue;

                hasCells = true;
                if (!OccupiedCells.Contains(cell))
                {
                    layerFull = false;
                    break;
                }
            }

            if (!hasCells) continue;

            if (!layerFull)
            {
                layerY = y;
                return true;
            }
        }

        layerY = GridMinY;
        return false;
    }

    /// <summary>
    /// Temizlenen katmanın üzerindeki hücreleri 1 birim aşağı öteler (Drop mantığı).
    /// </summary>
    public void ShiftUpperLayersDown(int clearedY)
    {
        var newTargetCells = new HashSet<Vector3Int>();
        foreach (var cell in TargetCells)
        {
            newTargetCells.Add(cell.y > clearedY ? new Vector3Int(cell.x, cell.y - 1, cell.z) : cell);
        }
        TargetCells.Clear();
        foreach (var c in newTargetCells) TargetCells.Add(c);

        var newAllShapeCells = new HashSet<Vector3Int>();
        foreach (var cell in AllShapeCells)
        {
            newAllShapeCells.Add(cell.y > clearedY ? new Vector3Int(cell.x, cell.y - 1, cell.z) : cell);
        }
        AllShapeCells.Clear();
        foreach (var c in newAllShapeCells) AllShapeCells.Add(c);

        var newOccupied = new HashSet<Vector3Int>();
        foreach (var cell in OccupiedCells)
        {
            newOccupied.Add(cell.y > clearedY ? new Vector3Int(cell.x, cell.y - 1, cell.z) : cell);
        }
        OccupiedCells.Clear();
        foreach (var c in newOccupied) OccupiedCells.Add(c);

        var newFrozen = new HashSet<Vector3Int>();
        foreach (var cell in FrozenCells)
        {
            newFrozen.Add(cell.y > clearedY ? new Vector3Int(cell.x, cell.y - 1, cell.z) : cell);
        }
        FrozenCells.Clear();
        foreach (var c in newFrozen) FrozenCells.Add(c);

        var newPrefilled = new HashSet<Vector3Int>();
        foreach (var cell in PrefilledCells)
        {
            newPrefilled.Add(cell.y > clearedY ? new Vector3Int(cell.x, cell.y - 1, cell.z) : cell);
        }
        PrefilledCells.Clear();
        foreach (var c in newPrefilled) PrefilledCells.Add(c);

        var newColors = new Dictionary<Vector3Int, Color>();
        foreach (var kv in CellColors)
        {
            var cell = kv.Key;
            var targetKey = cell.y > clearedY ? new Vector3Int(cell.x, cell.y - 1, cell.z) : cell;
            newColors[targetKey] = kv.Value;
        }
        CellColors.Clear();
        foreach (var kv in newColors) CellColors[kv.Key] = kv.Value;

        var newMatIndex = new Dictionary<Vector3Int, int>();
        foreach (var kv in CellMatIndex)
        {
            var cell = kv.Key;
            var targetKey = cell.y > clearedY ? new Vector3Int(cell.x, cell.y - 1, cell.z) : cell;
            newMatIndex[targetKey] = kv.Value;
        }
        CellMatIndex.Clear();
        foreach (var kv in newMatIndex) CellMatIndex[kv.Key] = kv.Value;

        var newIceHits = new Dictionary<Vector3Int, int>();
        foreach (var kv in IceRemainingHits)
        {
            var cell = kv.Key;
            var targetKey = cell.y > clearedY ? new Vector3Int(cell.x, cell.y - 1, cell.z) : cell;
            newIceHits[targetKey] = kv.Value;
        }
        IceRemainingHits.Clear();
        foreach (var kv in newIceHits) IceRemainingHits[kv.Key] = kv.Value;

        if (GridMaxY > GridMinY)
        {
            GridMaxY--;
        }
    }
}
