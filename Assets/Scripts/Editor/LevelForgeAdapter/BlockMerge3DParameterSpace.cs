using UnityEngine;
using LevelForge;

/// <summary>
/// LevelForge.IParameterSpace implementasyonu — bir deneme hedefi tutturamadığında bir sonraki
/// denemenin parametrelerini NASIL değiştireceğimizi tanımlar. Eskiden AutoAdjustAndRegenerate
/// içinde failureReason üzerinde ".Contains("yetersiz")" gibi kırılgan string eşleştirmesiyle
/// yapılan işin yapılandırılmış (FailureReasonCode bazlı) karşılığıdır — bkz.
/// BlockMerge3DDifficultyEvaluator.MapFailureReason (TEK merkezi eşleme noktası).
/// </summary>
public class BlockMerge3DParameterSpace : IParameterSpace<BlockMerge3DGenerationParams>
{
    private const float MinPct = 0f;
    private const float MaxPct = 0.5f;

    public BlockMerge3DGenerationParams Mutate(BlockMerge3DGenerationParams current, MutationHint hint, System.Random rng)
    {
        var p = current;

        switch (hint.reason)
        {
            case FailureReasonCode.InsufficientContent:
                // Kütüphaneden yeterince hücre döşenemedi — havuzu büyük parçalara doğru genişlet
                // (bkz. eski AutoAdjustAndRegenerate'in "yetersiz/fazla" dalı, aynı fikir).
                p.minPieceSize = Mathf.Max(1, p.minPieceSize - 1);
                p.maxPieceSize = Mathf.Min(10, p.maxPieceSize + 1);
                break;

            case FailureReasonCode.ExcessContent:
                // Parçalar hedeften fazla hücre kapsıyor — üst sınırı daralt.
                p.maxPieceSize = Mathf.Max(p.minPieceSize, p.maxPieceSize - 1);
                break;

            case FailureReasonCode.SearchBudgetExceeded:
                // Arama zaman/durum limitini aştı — geometriyi basitleştir (daha az engel), bkz.
                // eski AutoAdjustAndRegenerate'in "limit" dalı.
                p.icePercentage = Mathf.Max(MinPct, p.icePercentage - 0.05f);
                p.prefillPercentage = Mathf.Max(MinPct, p.prefillPercentage - 0.05f);
                break;

            case FailureReasonCode.ConstraintViolation:
                // Monte Carlo buz doğrulaması başarısız oldu (bkz. BlockMerge3DIceRevalidator) —
                // riskin doğrudan kaynağı buz oranı, onu azalt.
                p.icePercentage = Mathf.Max(MinPct, p.icePercentage - 0.05f);
                break;

            case FailureReasonCode.None:
                // Aday GEÇERLİ ama zorluk skoru hedeften sapmış — skoru hedefe doğru it.
                if (hint.direction == MutationDirection.TooHard)
                {
                    p.maxPieceSize = Mathf.Max(p.minPieceSize, p.maxPieceSize - 1);
                    p.icePercentage = Mathf.Max(MinPct, p.icePercentage - 0.04f);
                    p.prefillPercentage = Mathf.Max(MinPct, p.prefillPercentage - 0.02f);
                }
                else if (hint.direction == MutationDirection.TooEasy)
                {
                    p.maxPieceSize = Mathf.Min(10, p.maxPieceSize + 1);
                    p.icePercentage = Mathf.Min(MaxPct, p.icePercentage + 0.04f);
                    p.prefillPercentage = Mathf.Min(MaxPct, p.prefillPercentage + 0.02f);
                }
                break;

            case FailureReasonCode.StructurallyUnsolvable:
            case FailureReasonCode.Unknown:
            default:
                // Kanıtlanmış geometrik çözülemezlik — geometriyi belirgin biçimde gevşet.
                p.icePercentage = Mathf.Max(MinPct, p.icePercentage - 0.06f);
                p.prefillPercentage = Mathf.Max(MinPct, p.prefillPercentage - 0.06f);
                p.minPieceSize = Mathf.Max(1, p.minPieceSize - 1);
                p.maxPieceSize = Mathf.Min(10, p.maxPieceSize + 1);
                break;
        }

        // Eski "SmartPieceSplitting" strateji varyantları fikri (Standart / Sıkı Triplet / Biraz
        // Daha Büyük / Orta Boyutlar / Karma Boyutlar — bkz. AILevelDesignerWindow.cs eski
        // switch(attempt), hesaplanıp hiç kullanılmadığı için ölü koddu) burada CANLI hale
        // getirildi: yukarıdaki yönlendirilmiş ayarlama üstüne, deneme ilerledikçe döngüsel bir
        // pencere varyasyonu ekler — sadece obstacle oranını değil, parça geometrisinin KENDİSİNİ
        // de çeşitlendirerek aynı yön ısrarla yetmediğinde arama uzayını genişletir.
        switch (hint.attemptIndex % 5)
        {
            case 1: p.minPieceSize = 3; p.maxPieceSize = 3; break; // Sıkı Triplet (3 blok)
            case 2: p.minPieceSize = Mathf.Max(1, p.minPieceSize + 1); p.maxPieceSize = Mathf.Min(10, p.maxPieceSize + 2); break; // Daha büyük/kolay
            case 3: p.minPieceSize = Mathf.Max(3, p.minPieceSize); p.maxPieceSize = Mathf.Min(8, p.maxPieceSize + 1); break; // Orta boyutlar
            case 4: p.minPieceSize = Mathf.Max(2, p.minPieceSize - 1); break; // Karma boyutlar
            default: break; // 0: son yönlendirilmiş ayarı olduğu gibi bırak
        }

        p.minPieceSize = Mathf.Clamp(p.minPieceSize, 1, 10);
        p.maxPieceSize = Mathf.Clamp(p.maxPieceSize, p.minPieceSize, 10);
        p.icePercentage = Mathf.Clamp(p.icePercentage, MinPct, MaxPct);
        p.prefillPercentage = Mathf.Clamp(p.prefillPercentage, MinPct, MaxPct);

        return p;
    }
}
