using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Runtime.Saves
{
    /// <summary>
    /// Versioned save migration framework: register ordered steps (from → to) that
    /// transform a placements JSON string, then call <see cref="Migrate"/> when loading
    /// older slots. Steps must be contiguous; gaps abort with an error.
    /// </summary>
    public static class SaveMigrator
    {
        public const int CurrentVersion = 1;

        private sealed class Step
        {
            public int From;
            public Func<string, string> Transform;
        }

        private static readonly List<Step> steps = new List<Step>();

        /// <summary>Registers a transform upgrading placements JSON from one version to the next.</summary>
        public static void RegisterStep(int fromVersion, Func<string, string> transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            steps.Add(new Step { From = fromVersion, Transform = transform });
            steps.Sort((a, b) => a.From.CompareTo(b.From));
        }

        public static void ClearSteps() => steps.Clear();

        /// <summary>
        /// Walks the step chain until the payload reaches <paramref name="targetVersion"/>.
        /// Returns false with an error when a required step is missing.
        /// </summary>
        public static bool Migrate(string placementsJson, int fromVersion, int targetVersion,
            out string migratedJson, out string error)
        {
            migratedJson = placementsJson;
            error = null;

            if (fromVersion > targetVersion)
            {
                error = $"Save v{fromVersion} is newer than supported v{targetVersion}.";
                return false;
            }
            if (fromVersion == targetVersion) return true;

            int version = fromVersion;
            while (version < targetVersion)
            {
                Step step = steps.Find(s => s.From == version);
                if (step == null)
                {
                    error = $"Missing migration step v{version} → v{version + 1}.";
                    return false;
                }
                try { migratedJson = step.Transform(migratedJson); }
                catch (Exception exception)
                {
                    error = $"Migration v{version} failed: {exception.Message}";
                    return false;
                }
                version++;
            }
            return true;
        }
    }
}
