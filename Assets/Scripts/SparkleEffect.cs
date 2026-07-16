using UnityEngine;
using DG.Tweening;

// GridManager, aynı türden 3+ hayvan birbirine bağlı (yan yana ve/veya üst üste) olduğunda
// bu bileşeni ilgili bloklara ekler; materyalin emisyonunu KISA SÜRELİ birkaç kez parlatıp
// söndürerek (sonsuz döngü YOK) bir "parıldama" hissi verir, sonra kendiliğinden orijinal
// haline döner. Grup daha grup oluşurken bozulursa GridManager StopAndRestore() çağırıp
// bileşeni siler.
public class SparkleEffect : MonoBehaviour
{
    private static readonly Color SparkleColor = new Color(1f, 0.92f, 0.6f) * 1.15f; // hafif, göz yormayan sıcak parıltı
    private const int PulseLoops = 4; // 2 tam parıltı (git-gel) sonra durur

    private Renderer[] renderers;
    private Color[] originalEmissionColors;
    private bool[] originalEmissionEnabled;
    private Tween[] tweens;

    public void Begin()
    {
        renderers = GetComponentsInChildren<Renderer>();
        originalEmissionColors = new Color[renderers.Length];
        originalEmissionEnabled = new bool[renderers.Length];
        tweens = new Tween[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var mat = r.material; // instance
            if (!mat.HasProperty("_EmissionColor")) continue;

            Color origColor = mat.GetColor("_EmissionColor");
            bool wasEnabled = mat.IsKeywordEnabled("_EMISSION");
            originalEmissionColors[i] = origColor;
            originalEmissionEnabled[i] = wasEnabled;
            mat.EnableKeyword("_EMISSION");

            // Bloklar aynı fazda parıldamasın diye küçük bir rastgele gecikme
            float delay = Random.Range(0f, 0.25f);
            tweens[i] = mat.DOColor(SparkleColor, "_EmissionColor", 0.4f)
                .SetDelay(delay)
                .SetEase(Ease.InOutSine)
                .SetLoops(PulseLoops, LoopType.Yoyo) // sonlu — biter ve orijinaline döner
                .OnComplete(() =>
                {
                    mat.SetColor("_EmissionColor", origColor);
                    if (wasEnabled) mat.EnableKeyword("_EMISSION");
                    else mat.DisableKeyword("_EMISSION");
                });
        }
    }

    public void StopAndRestore()
    {
        if (renderers == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            tweens[i]?.Kill();

            var r = renderers[i];
            if (r == null) continue;
            var mat = r.material;
            if (!mat.HasProperty("_EmissionColor")) continue;

            mat.SetColor("_EmissionColor", originalEmissionColors[i]);
            if (originalEmissionEnabled[i]) mat.EnableKeyword("_EMISSION");
            else mat.DisableKeyword("_EMISSION");
        }

        renderers = null;
    }

    private void OnDestroy()
    {
        StopAndRestore();
    }
}
