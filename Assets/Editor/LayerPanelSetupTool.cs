using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public class LayerPanelSetupTool : EditorWindow
{
    [MenuItem("Tools/BlockMerge3D/Setup Layer Panel")]
    public static void SetupLayerPanel()
    {
        // 1. Find or create the LayerPanelController object
        LayerPanelController controller = Object.FindObjectOfType<LayerPanelController>();
        GameObject controllerObj;
        
        if (controller == null)
        {
            controllerObj = new GameObject("LayerPanelController");
            controller = controllerObj.AddComponent<LayerPanelController>();
            Undo.RegisterCreatedObjectUndo(controllerObj, "Create LayerPanelController");
            Debug.Log("[BM3D] Created LayerPanelController object.");
        }
        else
        {
            controllerObj = controller.gameObject;
            Undo.RecordObject(controller, "Update LayerPanelController");
            Debug.Log("[BM3D] Found existing LayerPanelController.");
        }

        // 2. Find or create a Canvas
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("Canvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasObj, "Create Canvas");
            Debug.Log("[BM3D] Created UI Canvas.");
            
            // Need an EventSystem too if we create a new Canvas
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(esObj, "Create EventSystem");
            }
        }
        
        controller.uiCanvas = canvas;

        // 3. Find or create the Back Button
        Button backButton = null;
        Transform existingBackBtn = canvas.transform.Find("LayerPanel_BackButton");
        
        if (existingBackBtn != null)
        {
            backButton = existingBackBtn.GetComponent<Button>();
        }
        else
        {
            GameObject btnObj = new GameObject("LayerPanel_BackButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(canvas.transform, false);
            
            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 50f);
            rt.sizeDelta = new Vector2(150, 50);

            Image img = btnObj.GetComponent<Image>();
            img.color = new Color(0.9f, 0.3f, 0.3f, 1f); // Reddish color for Back/Close

            GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(btnObj.transform, false);
            
            RectTransform txtRt = textObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.text = "Geri Dön (3D)";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.fontSize = 20;

            backButton = btnObj.GetComponent<Button>();
            Undo.RegisterCreatedObjectUndo(btnObj, "Create Back Button");
            Debug.Log("[BM3D] Created Back Button.");
        }

        controller.backButton = backButton;
        
        // Hide back button initially as it will be controlled by script
        backButton.gameObject.SetActive(false);

        EditorUtility.SetDirty(controllerObj);
        Debug.Log("[BM3D] LayerPanelController setup complete! The Back button is hidden by default.");
    }
}
