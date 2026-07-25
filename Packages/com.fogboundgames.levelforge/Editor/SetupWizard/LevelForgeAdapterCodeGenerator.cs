using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace LevelForge.EditorTools
{
    /// <summary>
    /// Turns a <see cref="WizardConfig"/> into a set of C# adapter files, following the same
    /// pattern documented in ADAPTER_GUIDE.md and demonstrated by BlockMerge3D's own adapter
    /// (Assets/Scripts/Editor/LevelForgeAdapter/ in that project). Pure string templating - no
    /// Roslyn/T4 - deliberately simple so the output is easy to read and hand-edit afterwards.
    ///
    /// Two things this does NOT attempt to generate, because they are inherently game-specific and
    /// cannot be inferred from a short questionnaire: the shape of the candidate's actual level data
    /// (left as a documented stub), and the real Evaluate() scoring logic (left as a TODO-annotated
    /// stub that still compiles with placeholder values). Everything mechanically derivable from the
    /// wizard's field/tier lists (params struct, parameter mutation, tier lookup) IS fully generated.
    /// </summary>
    public static class LevelForgeAdapterCodeGenerator
    {
        public static List<string> Validate(WizardConfig config)
        {
            var errors = new List<string>();
            if (config == null)
            {
                errors.Add("Config boş.");
                return errors;
            }
            if (string.IsNullOrWhiteSpace(config.gameName)) errors.Add("Oyun adı boş olamaz.");
            if (config.tiers == null || config.tiers.Count == 0) errors.Add("En az 1 zorluk seviyesi eklemelisin.");

            var seenFields = new HashSet<string>();
            foreach (var f in config.paramFields ?? new List<ParamFieldSpec>())
            {
                string id = SanitizeIdentifier(f.name, null);
                if (id == null) { errors.Add("Bir üretim parametresinin adı boş/geçersiz."); continue; }
                if (!seenFields.Add(id)) errors.Add($"Üretim parametresi adı tekrarlı (veya sanitize sonrası çakışıyor): '{f.name}'.");
                if (f.type != ParamFieldType.Bool && f.min > f.max) errors.Add($"'{f.name}': min > max.");
            }

            var seenMetrics = new HashSet<string>();
            foreach (var m in config.metrics ?? new List<MetricSpec>())
            {
                string id = SanitizeIdentifier(m.name, null);
                if (id == null) { errors.Add("Bir zorluk metriğinin adı boş/geçersiz."); continue; }
                if (!seenMetrics.Add(id)) errors.Add($"Metrik adı tekrarlı: '{m.name}'.");
            }

            var seenTiers = new HashSet<string>();
            foreach (var t in config.tiers ?? new List<TierSpec>())
            {
                if (string.IsNullOrWhiteSpace(t.name)) { errors.Add("Bir zorluk seviyesinin adı boş."); continue; }
                if (!seenTiers.Add(t.name)) errors.Add($"Zorluk seviyesi adı tekrarlı: '{t.name}'.");
            }

            return errors;
        }

        /// <summary>Pure - returns relativePath (within config.outputFolder) → file content. No disk I/O.</summary>
        public static Dictionary<string, string> GenerateFiles(WizardConfig config)
        {
            string game = SanitizeIdentifier(config.gameName, "MyGame");
            var files = new Dictionary<string, string>();

            files[$"{game}GenerationParams.cs"] = GenerateParams(game, config);
            files[$"{game}Candidate.cs"] = GenerateCandidate(game, config);
            files[$"{game}DifficultyEvaluator.cs"] = GenerateEvaluator(game, config);
            files[$"{game}ParameterSpace.cs"] = GenerateParameterSpace(game, config);
            files[$"{game}DifficultyTiers.cs"] = GenerateTiers(game, config);
            files["README_NEXT_STEPS.md"] = GenerateReadme(game, config);

            return files;
        }

        public static void WriteToDisk(WizardConfig config, Dictionary<string, string> files)
        {
            string folder = string.IsNullOrWhiteSpace(config.outputFolder)
                ? WizardConfig.DefaultOutputFolder(config.gameName)
                : config.outputFolder;

            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            foreach (var kv in files)
            {
                string path = Path.Combine(folder, kv.Key);
                File.WriteAllText(path, kv.Value);
            }

            AssetDatabase.Refresh();
        }

        // ── Kimlik/İsim Güvenliği ────────────────────────────────────────
        private static string SanitizeIdentifier(string raw, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            var sb = new StringBuilder();
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            }
            string result = sb.ToString();
            if (result.Length == 0) return fallback;
            if (char.IsDigit(result[0])) result = "_" + result;
            return result;
        }

        private static string CsTypeName(ParamFieldType type) => type switch
        {
            ParamFieldType.Float => "float",
            ParamFieldType.Int => "int",
            ParamFieldType.Bool => "bool",
            _ => "float"
        };

        private static string AutoGenHeader(string game, string purpose) =>
            "// ═══════════════════════════════════════════════════════════════════\n" +
            $"//  {game.ToUpperInvariant()} LEVELFORGE ADAPTER — {purpose}\n" +
            "//  LevelForge Setup Wizard tarafından üretildi (Packages/com.fogboundgames.levelforge/\n" +
            "//  Editor/SetupWizard/LevelForgeAdapterCodeGenerator.cs). Elle düzenleyebilirsin —\n" +
            "//  sihirbazı TEKRAR çalıştırırsan bu dosyanın üzerine yazılır, elle eklediğin\n" +
            "//  değişiklikler kaybolur. Bkz. Packages/com.fogboundgames.levelforge/ADAPTER_GUIDE.md.\n" +
            "// ═══════════════════════════════════════════════════════════════════\n";

        // ── {Game}GenerationParams.cs ────────────────────────────────────
        private static string GenerateParams(string game, WizardConfig config)
        {
            var sb = new StringBuilder();
            sb.Append(AutoGenHeader(game, "Üretim Parametreleri (TParams)"));
            sb.AppendLine();
            sb.AppendLine($"public struct {game}GenerationParams");
            sb.AppendLine("{");

            var fields = config.paramFields ?? new List<ParamFieldSpec>();
            foreach (var f in fields)
            {
                string id = SanitizeIdentifier(f.name, "param");
                string csType = CsTypeName(f.type);
                if (!string.IsNullOrWhiteSpace(f.description))
                    sb.AppendLine($"    // {f.description}");
                sb.AppendLine($"    public {csType} {id};");
            }

            sb.AppendLine();
            sb.AppendLine("    public override string ToString()");
            sb.Append("        => $\"");
            sb.Append(string.Join(" ", fields.Select(f =>
            {
                string id = SanitizeIdentifier(f.name, "param");
                return $"{id}={{{id}}}";
            })));
            sb.AppendLine("\";");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── {Game}Candidate.cs ────────────────────────────────────────────
        private static string GenerateCandidate(string game, WizardConfig config)
        {
            var sb = new StringBuilder();
            sb.Append(AutoGenHeader(game, "Aday (TCandidate) — İSKELET, elle doldurulmalı"));
            sb.AppendLine();
            sb.AppendLine("// Bu tip, bir üretim denemesinin sonucunu (senin oyununun kendi level/board verisini)");
            sb.AppendLine("// taşır — LevelForge.DifficultySearchEngine içeriğini hiç bilmez, sadece opak bir");
            sb.AppendLine("// TCandidate olarak taşır. Sihirbaz burada hangi alanların olması gerektiğini");
            sb.AppendLine("// TAHMİN EDEMEZ (oyununa özgü) — kendi level verini temsil eden alanları SEN ekle.");
            sb.AppendLine("// Örnek: mevcut bir LevelData/ScriptableObject asset referansı, ya da o an bellekte");
            sb.AppendLine("// tuttuğun ham grid/parça listesi. Evaluator (");
            sb.AppendLine($"//   {game}DifficultyEvaluator.Evaluate");
            sb.AppendLine("// ) bu alanları okuyup zorluk skorunu hesaplayacak.");
            sb.AppendLine($"public class {game}Candidate");
            sb.AppendLine("{");
            sb.AppendLine("    // TODO: level verini temsil eden alanları buraya ekle.");
            sb.AppendLine("    // public LevelData levelAsset;");
            sb.AppendLine("    // public List<Vector3Int> occupiedCells;");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── {Game}DifficultyEvaluator.cs ──────────────────────────────────
        private static string GenerateEvaluator(string game, WizardConfig config)
        {
            var sb = new StringBuilder();
            sb.Append(AutoGenHeader(game, "Zorluk Değerlendirici (IDifficultyEvaluator) — İSKELET"));
            sb.AppendLine();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using LevelForge;");
            sb.AppendLine();
            sb.AppendLine($"public class {game}DifficultyEvaluator : IDifficultyEvaluator<{game}Candidate>");
            sb.AppendLine("{");
            sb.AppendLine($"    public EvaluationResult Evaluate({game}Candidate candidate)");
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: adayın GEÇERLİ/tamamlanabilir olup olmadığını kontrol et. Değilse:");
            sb.AppendLine("        // return EvaluationResult.Invalid(FailureReasonCode.StructurallyUnsolvable,");
            sb.AppendLine("        //     \"neden\", definitive: true); // definitive=false ⇔ sadece arama bütçesi yetmedi, kanıtlanmış değil");
            sb.AppendLine();
            sb.AppendLine("        var metrics = new Dictionary<string, float>();");

            var metrics = config.metrics ?? new List<MetricSpec>();
            foreach (var m in metrics)
            {
                string id = SanitizeIdentifier(m.name, "metric");
                string comment = string.IsNullOrWhiteSpace(m.description) ? "" : $" // {m.description}";
                sb.AppendLine($"        metrics[\"{id}\"] = 0f; // TODO: hesapla{comment}");
            }

            sb.AppendLine();
            sb.AppendLine("        // TODO: metrics'ten 0..1 aralığında bir zorluk skoru hesapla (ör. ağırlıklı toplam,");
            sb.AppendLine("        // her metriği kendi makul üst sınırına göre normalize edip topla — bkz.");
            sb.AppendLine("        // BlockMerge3D örneğindeki LevelSolver.CalculateDifficulty ya da bu paketle birlikte");
            sb.AppendLine("        // gelen Symmetric3D örnek adapter'ındaki ağırlıklı-heuristik yaklaşımı).");
            sb.AppendLine("        float score = 0f;");
            sb.AppendLine();
            sb.AppendLine("        return EvaluationResult.Valid(score, metrics);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── {Game}ParameterSpace.cs ────────────────────────────────────────
        private static string GenerateParameterSpace(string game, WizardConfig config)
        {
            var sb = new StringBuilder();
            sb.Append(AutoGenHeader(game, "Parametre Mutasyonu (IParameterSpace) — mekanik, ÇALIŞIR haldedir"));
            sb.AppendLine();
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using LevelForge;");
            sb.AppendLine();
            sb.AppendLine("// Bu dosya (Candidate/Evaluator'ın aksine) TAM ÇALIŞIR bir strateji üretir: her sayısal");
            sb.AppendLine("// alanı, aralığının %10'u kadar hedefe doğru (skor çok yüksekse azalt, çok düşükse artır)");
            sb.AppendLine("// kaydırıp sınırlar içinde tutar. Bu JENERİK bir strateji — hangi alanın HANGİ");
            sb.AppendLine("// FailureReasonCode'a nasıl tepki vermesi gerektiğini SEN daha iyi bilirsin; gerekirse");
            sb.AppendLine("// hint.reason'a göre özelleştir (bkz. BlockMerge3D örneğindeki BlockMerge3DParameterSpace).");
            sb.AppendLine($"public class {game}ParameterSpace : IParameterSpace<{game}GenerationParams>");
            sb.AppendLine("{");
            sb.AppendLine($"    public {game}GenerationParams Mutate({game}GenerationParams current, MutationHint hint, System.Random rng)");
            sb.AppendLine("    {");
            sb.AppendLine("        var p = current;");
            sb.AppendLine("        // TooEasy ⇒ zorluğu artıracak yönde (+1), TooHard/Invalid ⇒ azaltacak yönde (-1).");
            sb.AppendLine("        float direction = hint.direction == MutationDirection.TooEasy ? 1f : -1f;");
            sb.AppendLine();

            var fields = config.paramFields ?? new List<ParamFieldSpec>();
            foreach (var f in fields)
            {
                string id = SanitizeIdentifier(f.name, "param");
                if (f.type == ParamFieldType.Bool)
                {
                    sb.AppendLine($"        // '{id}' bool — yön bilgisi anlamlı değil, deneme indeksine göre keşif amaçlı değiştirilir.");
                    sb.AppendLine($"        if (hint.attemptIndex % 2 == 1) p.{id} = !p.{id};");
                }
                else
                {
                    float range = Math.Abs(f.max - f.min);
                    string stepExpr = $"{(range * 0.1f).ToString(System.Globalization.CultureInfo.InvariantCulture)}f";
                    if (f.type == ParamFieldType.Int)
                    {
                        sb.AppendLine($"        p.{id} = Mathf.RoundToInt(Mathf.Clamp(p.{id} + direction * {stepExpr}, {FloatLit(f.min)}, {FloatLit(f.max)}));");
                    }
                    else
                    {
                        sb.AppendLine($"        p.{id} = Mathf.Clamp(p.{id} + direction * {stepExpr}, {FloatLit(f.min)}, {FloatLit(f.max)});");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("        return p;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string FloatLit(float v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f";

        // ── {Game}DifficultyTiers.cs ───────────────────────────────────────
        private static string GenerateTiers(string game, WizardConfig config)
        {
            var sb = new StringBuilder();
            sb.Append(AutoGenHeader(game, "Zorluk Seviyeleri (DifficultyTier fabrikası) — mekanik, ÇALIŞIR haldedir"));
            sb.AppendLine();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using LevelForge;");
            sb.AppendLine();
            sb.AppendLine($"public static class {game}DifficultyTiers");
            sb.AppendLine("{");
            sb.AppendLine("    private static readonly Dictionary<string, DifficultyTier> cache = new Dictionary<string, DifficultyTier>();");
            sb.AppendLine();
            sb.AppendLine("    public static DifficultyTier GetTier(string tierName)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (cache.TryGetValue(tierName, out var existing) && existing != null) return existing;");
            sb.AppendLine();
            sb.AppendLine("        float targetScore;");
            sb.AppendLine("        float tolerance;");
            sb.AppendLine("        switch (tierName)");
            sb.AppendLine("        {");

            var tiers = config.tiers ?? new List<TierSpec>();
            foreach (var t in tiers)
            {
                sb.AppendLine($"            case \"{t.name}\": targetScore = {FloatLit(t.targetScore)}; tolerance = {FloatLit(t.tolerance)}; break;");
            }

            sb.AppendLine("            default:");
            sb.AppendLine("                Debug.LogWarning($\"{nameof(" + game + "DifficultyTiers)}: bilinmeyen tier adı '{tierName}', varsayılan (0.5, 0.1) kullanılıyor.\");");
            sb.AppendLine("                targetScore = 0.5f; tolerance = 0.1f;");
            sb.AppendLine("                break;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        var tier = ScriptableObject.CreateInstance<DifficultyTier>();");
            sb.AppendLine("        tier.tierName = tierName;");
            sb.AppendLine("        tier.targetScore = targetScore;");
            sb.AppendLine("        tier.scoreTolerance = tolerance;");
            sb.AppendLine();
            sb.AppendLine("        cache[tierName] = tier;");
            sb.AppendLine("        return tier;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── README_NEXT_STEPS.md ───────────────────────────────────────────
        private static string GenerateReadme(string game, WizardConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {game} — LevelForge Adapter Sonraki Adımlar");
            sb.AppendLine();
            sb.AppendLine("LevelForge Setup Wizard tarafından üretildi. Durumu:");
            sb.AppendLine();
            sb.AppendLine($"- ✅ `{game}GenerationParams.cs` — hazır, elle değiştirmene gerek yok.");
            sb.AppendLine($"- ✅ `{game}ParameterSpace.cs` — çalışan jenerik bir strateji üretildi (isteğe bağlı özelleştir).");
            sb.AppendLine($"- ✅ `{game}DifficultyTiers.cs` — hazır, elle değiştirmene gerek yok.");
            sb.AppendLine($"- ⚠️ `{game}Candidate.cs` — İSKELET. Kendi level verini temsil eden alanları eklemelisin.");
            sb.AppendLine($"- ⚠️ `{game}DifficultyEvaluator.cs` — İSKELET. `Evaluate()` içindeki TODO'ları doldurmalısın:");
            sb.AppendLine("  gerçek geçerlilik kontrolü + gerçek metrik hesaplama + 0..1 zorluk skoru.");
            sb.AppendLine();
            sb.AppendLine("## Sonra ne yapmalısın");
            sb.AppendLine("1. Yukarıdaki iki İSKELET dosyayı doldur.");
            sb.AppendLine("2. Kendi Editor aracından (ya da yeni bir menü öğesinden) şunu çağır:");
            sb.AppendLine("```csharp");
            sb.AppendLine("var engine = new LevelForge.DifficultySearchEngine();");
            sb.AppendLine($"var tier = {game}DifficultyTiers.GetTier(\"Orta\");");
            sb.AppendLine($"var result = engine.Run(");
            sb.AppendLine($"    initialParams: new {game}GenerationParams {{ /* ... */ }},");
            sb.AppendLine("    tier: tier,");
            sb.AppendLine("    generate: p => /* senin üretici fonksiyonun */,");
            sb.AppendLine($"    evaluator: new {game}DifficultyEvaluator(),");
            sb.AppendLine($"    paramSpace: new {game}ParameterSpace(),");
            sb.AppendLine("    budget: new LevelForge.SearchBudget());");
            sb.AppendLine("```");
            sb.AppendLine("3. Ayrıntılı rehber için `Packages/com.fogboundgames.levelforge/ADAPTER_GUIDE.md`'ye bak.");
            if (!string.IsNullOrWhiteSpace(config.description))
            {
                sb.AppendLine();
                sb.AppendLine("## Sihirbaza girdiğin açıklama");
                sb.AppendLine(config.description);
            }
            return sb.ToString();
        }
    }
}
