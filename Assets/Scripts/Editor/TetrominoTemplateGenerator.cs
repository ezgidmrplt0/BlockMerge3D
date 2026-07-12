using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  TETROMINO TEMPLATE GENERATOR
//  Klasik 7 Tetris parçasını (I, O, T, S, Z, J, L) + 1 hücrelik dolgu
//  parçasını (Filler_1x1 — tek sayılı kare katmanların artığı için)
//  Assets/Pieces/ altında yeniden kullanılabilir CubeShapeData + prefab
//  şablonları olarak üretir.
//  Tüm parçalar tek katmanlı (düz, Y=0) — oyunun katman-katman oynanışıyla
//  uyumlu. Level generator script'leri PieceTemplateLibrary.GetCells(...)
//  ile buradan okur; CubeShapeEditorWindow'un Piece kütüphane panelinde de
//  görünürler.
// ═══════════════════════════════════════════════════════════════════

public static class TetrominoTemplateGenerator
{
    private static readonly (string name, Vector3Int[] cells)[] Templates =
    {
        (PieceTemplateLibrary.I, new[] { new Vector3Int(0,0,0), new Vector3Int(1,0,0), new Vector3Int(2,0,0), new Vector3Int(3,0,0) }),
        (PieceTemplateLibrary.O, new[] { new Vector3Int(0,0,0), new Vector3Int(1,0,0), new Vector3Int(0,0,1), new Vector3Int(1,0,1) }),
        (PieceTemplateLibrary.T, new[] { new Vector3Int(0,0,0), new Vector3Int(1,0,0), new Vector3Int(2,0,0), new Vector3Int(1,0,1) }),
        (PieceTemplateLibrary.S, new[] { new Vector3Int(0,0,0), new Vector3Int(1,0,0), new Vector3Int(1,0,1), new Vector3Int(2,0,1) }),
        (PieceTemplateLibrary.Z, new[] { new Vector3Int(1,0,0), new Vector3Int(2,0,0), new Vector3Int(0,0,1), new Vector3Int(1,0,1) }),
        (PieceTemplateLibrary.J, new[] { new Vector3Int(0,0,0), new Vector3Int(0,0,1), new Vector3Int(1,0,1), new Vector3Int(2,0,1) }),
        (PieceTemplateLibrary.L, new[] { new Vector3Int(2,0,0), new Vector3Int(0,0,1), new Vector3Int(1,0,1), new Vector3Int(2,0,1) }),
        (PieceTemplateLibrary.Filler, new[] { new Vector3Int(0,0,0) }),
    };

    private const float CellSize = 1f;
    private const float Spacing = 0.1f;

    [MenuItem("BlockMerge3D/Tetris Parça Şablonlarını Oluştur (Claude)")]
    public static void Generate()
    {
        if (!Directory.Exists(PieceTemplateLibrary.FOLDER)) Directory.CreateDirectory(PieceTemplateLibrary.FOLDER);
        AssetDatabase.Refresh();

        GameObject cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultCubePrefabSetup.DEFAULT_CUBE_PATH);
        float step = CellSize + Spacing;

        foreach (var (name, cellArr) in Templates)
        {
            var cells = new List<Vector3Int>(cellArr);
            int maxX = 0, maxZ = 0;
            foreach (var c in cells) { maxX = Mathf.Max(maxX, c.x); maxZ = Mathf.Max(maxZ, c.z); }
            var gridSize = new Vector3Int(maxX + 1, 1, maxZ + 1);

            // ── CubeShapeData (kütüphane girdisi) ──
            string assetPath = $"{PieceTemplateLibrary.FOLDER}/{name}.asset";
            CubeShapeData data = AssetDatabase.LoadAssetAtPath<CubeShapeData>(assetPath);
            bool isNewAsset = data == null;
            if (isNewAsset) data = ScriptableObject.CreateInstance<CubeShapeData>();

            data.shapeName = name;
            data.gridSize = gridSize;
            data.cellSize = CellSize;
            data.spacing = Spacing;
            data.occupiedCells = new List<Vector3Int>(cells);
            data.prefilledCells = new List<Vector3Int>();
            data.prefilledColors = new List<Color>();
            data.prefilledMaterialIndices = new List<int>();
            data.cubePrefab = cubePrefab;

            if (isNewAsset) AssetDatabase.CreateAsset(data, assetPath);
            else EditorUtility.SetDirty(data);

            // ── Eşleşen prefab (görsel önizleme + diğer araçların kütüphane listesi için) ──
            string prefabPath = $"{PieceTemplateLibrary.FOLDER}/{name}.prefab";
            GameObject root = new GameObject(name);
            var holder = root.AddComponent<CubeShapeDataHolder>();
            holder.shapeName = name;
            holder.gridSize = gridSize;
            holder.cellSize = CellSize;
            holder.spacing = Spacing;
            holder.occupiedCells = new List<Vector3Int>(cells);

            foreach (var cell in cells)
            {
                GameObject cube = cubePrefab != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(cubePrefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform);
                cube.transform.localPosition = (Vector3)cell * step + Vector3.one * (CellSize * 0.5f);
                cube.transform.localScale = Vector3.one * CellSize;
                cube.name = $"Cube_{cell.x}_{cell.y}_{cell.z}";
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Tetris Şablonları Oluşturuldu",
            $"{Templates.Length} parça şablonu (I, O, T, S, Z, J, L) {PieceTemplateLibrary.FOLDER}/ altında oluşturuldu.",
            "Tamam");
        Debug.Log($"[TetrominoTemplateGenerator] {Templates.Length} şablon oluşturuldu: {PieceTemplateLibrary.FOLDER}");
    }
}
