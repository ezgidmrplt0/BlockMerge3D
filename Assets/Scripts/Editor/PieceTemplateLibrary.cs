using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════
//  PIECE TEMPLATE LIBRARY
//  Tetris parça şablonları Assets/Pieces/ altında CubeShapeData (+ eşleşen
//  prefab) olarak saklanır — CubeShapeEditorWindow'un "Piece" kütüphane
//  panelinde de görünür/seçilebilirler. Level generator script'leri bu
//  sınıf üzerinden hücre desenini okuyup kendi seviyesine göre (kendi
//  cellSize/spacing değerleriyle) taze parça prefabı üretir.
// ═══════════════════════════════════════════════════════════════════

public static class PieceTemplateLibrary
{
    public const string FOLDER = "Assets/Pieces";

    public const string I = "Tetromino_I";
    public const string O = "Tetromino_O";
    public const string T = "Tetromino_T";
    public const string S = "Tetromino_S";
    public const string Z = "Tetromino_Z";
    public const string J = "Tetromino_J";
    public const string L = "Tetromino_L";

    // Tek hücrelik "dolgu" parçası — gerçek tetromino değil, tek sayılı karelerde
    // (3x3, 5x5, 7x7 gibi — alanı 4'e bölünmeyen) kalan artığı doldurmak için.
    public const string Filler = "Filler_1x1";

    /// <summary>Şablonun yerel (orijine göre) hücre desenini döndürür. Bulunamazsa null.</summary>
    public static List<Vector3Int> GetCells(string templateName)
    {
        var data = AssetDatabase.LoadAssetAtPath<CubeShapeData>($"{FOLDER}/{templateName}.asset");
        return data != null ? new List<Vector3Int>(data.occupiedCells) : null;
    }
}
