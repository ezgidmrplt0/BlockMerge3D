using UnityEngine;
using UnityEditor;
using System.IO;

public class SpriteDump
{
    [MenuItem("Tools/Dump Sprites")]
    public static void Dump()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("Assets/2D Casual UI/Sprite/GUI.png");
        string log = "";
        foreach (var a in assets)
        {
            if (a is Sprite s)
            {
                log += $"Sprite: {s.name}, Rect: {s.rect.width}x{s.rect.height}, Border: {s.border}\n";
            }
        }
        File.WriteAllText("SpriteDump.txt", log);
        Debug.Log("Dumped to SpriteDump.txt");
    }
}
