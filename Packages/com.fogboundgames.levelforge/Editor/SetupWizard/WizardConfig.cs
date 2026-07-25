using System;
using System.Collections.Generic;

namespace LevelForge.EditorTools
{
    /// <summary>
    /// Plain data model collected by <see cref="LevelForgeSetupWizardWindow"/> and consumed by
    /// <see cref="LevelForgeAdapterCodeGenerator"/>. Deliberately has zero UnityEditor dependency
    /// so the generator (and this config) can be exercised outside the wizard UI - e.g. to
    /// regenerate a known-good adapter for a second project from a hardcoded config, as done for
    /// the Symmetric3D adapter shipped alongside this tool.
    /// </summary>
    [Serializable]
    public class WizardConfig
    {
        public string gameName = "";
        public string outputFolder = "";
        public string description = "";
        public List<ParamFieldSpec> paramFields = new List<ParamFieldSpec>();
        public List<MetricSpec> metrics = new List<MetricSpec>();
        public List<TierSpec> tiers = new List<TierSpec>();

        public static string DefaultOutputFolder(string gameName)
        {
            string safe = string.IsNullOrEmpty(gameName) ? "MyGame" : gameName;
            return $"Assets/Scripts/Editor/{safe}LevelForgeAdapter";
        }
    }

    public enum ParamFieldType { Float, Int, Bool }

    [Serializable]
    public class ParamFieldSpec
    {
        public string name = "";
        public ParamFieldType type = ParamFieldType.Float;
        public string description = "";
        public float min = 0f;
        public float max = 1f;
    }

    [Serializable]
    public class MetricSpec
    {
        public string name = "";
        public string description = "";
    }

    [Serializable]
    public class TierSpec
    {
        public string name = "";
        public float targetScore = 0.5f;
        public float tolerance = 0.08f;

        public static List<TierSpec> DefaultFour()
        {
            return new List<TierSpec>
            {
                new TierSpec { name = "Kolay", targetScore = 0.15f, tolerance = 0.08f },
                new TierSpec { name = "Orta",  targetScore = 0.40f, tolerance = 0.08f },
                new TierSpec { name = "Zor",   targetScore = 0.65f, tolerance = 0.08f },
                new TierSpec { name = "Uzman", targetScore = 0.85f, tolerance = 0.08f },
            };
        }
    }
}
