using UnityEngine;

/// <summary>Kendi üzerindeki tüm Renderer'ların .enabled durumunu, başka bir (kaynak) Renderer'ın
/// enabled durumuna göre her karede eşitler. Bkz. LevelManager.AttachSpeciesVisual — prefilled
/// hücrelerde katman gizle/göster (GridManager.RefreshLayerVisibility) orijinal küp renderer'ını
/// yönetmeye devam eder; asıl görünen hayvan modelinin görünürlüğünü buna göre senkron tutar.</summary>
public class RendererVisibilityMirror : MonoBehaviour
{
    public Renderer source;
    private Renderer[] targets;

    private void Awake()
    {
        targets = GetComponentsInChildren<Renderer>(true);
    }

    private void LateUpdate()
    {
        if (source == null || targets == null) return;

        bool isEnabled = source.enabled;
        foreach (var t in targets)
        {
            if (t != null && t.enabled != isEnabled)
                t.enabled = isEnabled;
        }
    }
}
