using UnityEngine;
using System.Collections.Generic;

public class LevelManager : MonoBehaviour
{
    [Header("Configuration")]
    public Transform spawnPoint;
    public LevelData currentLevel;

    private GameObject activeMainPiece;
    private List<GameObject> activePieces = new List<GameObject>();

    void Start()
    {
        if (currentLevel != null)
            LoadLevel(currentLevel);
    }

    public void LoadLevel(LevelData level)
    {
        ClearCurrentLevel();

        if (spawnPoint == null)
        {
            Debug.LogError("LevelManager: Spawn Point atanmadı!");
            return;
        }

        if (level.mainShapePrefab != null)
        {
            activeMainPiece = Instantiate(level.mainShapePrefab, spawnPoint.position, Quaternion.identity, spawnPoint);
            activeMainPiece.name = "Main_Shape";
        }

        float offset = 5f;
        for (int i = 0; i < level.complementaryPieces.Count; i++)
        {
            if (level.complementaryPieces[i] != null)
            {
                Vector3 pos = spawnPoint.position + Vector3.right * (offset * (i + 1));
                GameObject piece = Instantiate(level.complementaryPieces[i], pos, Quaternion.identity, spawnPoint);
                piece.name = $"Complementary_Piece_{i}";
                activePieces.Add(piece);
            }
        }
    }

    public void ClearCurrentLevel()
    {
        if (activeMainPiece != null)
        {
            Destroy(activeMainPiece);
            activeMainPiece = null;
        }
        foreach (var p in activePieces) Destroy(p);
        activePieces.Clear();
    }
}
