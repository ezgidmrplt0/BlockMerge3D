using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(CubeShapeDataHolder))]
public class CubeShapeDataHolderEditor : Editor
{
    private CubeShapeDataHolder holder;
    private IceHitCountUtility.IceDifficulty selectedDifficulty = IceHitCountUtility.IceDifficulty.Orta;
    private int fixedHitValue = 2;
    private bool showCellList = true;

    private void OnEnable()
    {
        holder = (CubeShapeDataHolder)target;
    }

    public override void OnInspectorGUI()
    {
        // Varsayılan inspector elemanlarını çiz
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("🧊 Buz Vuruş Sayısı Araçları (Ice Hit Count Tools)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        if (holder.frozenCells == null || holder.frozenCells.Count == 0)
        {
            EditorGUILayout.HelpBox("Bu objede tanımlı buz hücresi (frozenCells) bulunamadı.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.LabelField($"Toplam Buz Hücresi Sayısı: {holder.frozenCells.Count}", EditorStyles.boldLabel);

        // Listeyi doldur / eşitle
        if (holder.frozenHitCounts == null) holder.frozenHitCounts = new List<int>();
        while (holder.frozenHitCounts.Count < holder.frozenCells.Count)
        {
            holder.frozenHitCounts.Add(1);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("1. Zorluğa Göre Toplu Atama", EditorStyles.miniBoldLabel);
        selectedDifficulty = (IceHitCountUtility.IceDifficulty)EditorGUILayout.EnumPopup("Zorluk Seviyesi:", selectedDifficulty);

        if (GUILayout.Button($"🎲 Tüm Buzlara ({selectedDifficulty}) Zorluğuna Göre Rastgele Vuruş Ata"))
        {
            Undo.RecordObject(holder, "Randomize Ice Hit Counts");
            var range = IceHitCountUtility.GetRange(selectedDifficulty);
            for (int i = 0; i < holder.frozenCells.Count; i++)
            {
                holder.frozenHitCounts[i] = Random.Range(range.min, range.max + 1);
            }
            EditorUtility.SetDirty(holder);
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("2. Sabit Değer Atama", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        fixedHitValue = EditorGUILayout.IntSlider("Vuruş Sayısı:", fixedHitValue, 1, 10);
        if (GUILayout.Button("Tümüne Uygula"))
        {
            Undo.RecordObject(holder, "Set Fixed Ice Hit Counts");
            for (int i = 0; i < holder.frozenCells.Count; i++)
            {
                holder.frozenHitCounts[i] = fixedHitValue;
            }
            EditorUtility.SetDirty(holder);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
        showCellList = EditorGUILayout.Foldout(showCellList, "Hücre Bazlı Vuruş Sayıları", true);
        if (showCellList)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < holder.frozenCells.Count; i++)
            {
                Vector3Int cell = holder.frozenCells[i];
                int currentHits = i < holder.frozenHitCounts.Count ? holder.frozenHitCounts[i] : 1;

                EditorGUI.BeginChangeCheck();
                int newHits = EditorGUILayout.IntField($"Buz #{i+1} {cell}:", currentHits);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(holder, "Change Ice Hit Count");
                    holder.frozenHitCounts[i] = Mathf.Max(1, newHits);
                    EditorUtility.SetDirty(holder);
                }
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }
}
