using System;
using System.Collections.Generic;
using System.Text;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Baking.Core
{
    public interface IWorldBakeStep
    {
        string StableId { get; }
        int Order { get; }
        void Execute(WorldBakeContext context, WorldBakeReport report);
    }

    public sealed class WorldBakeContext
    {
        private readonly SortedDictionary<string, string> deterministicOutputs =
            new SortedDictionary<string, string>(StringComparer.Ordinal);

        public WorldGridSettings GridSettings { get; }
        public IReadOnlyDictionary<string, string> DeterministicOutputs => deterministicOutputs;

        public WorldBakeContext(WorldGridSettings gridSettings)
        {
            GridSettings = gridSettings ?? throw new ArgumentNullException(nameof(gridSettings));
        }

        public void SetDeterministicOutput(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Output key is required.", nameof(key));
            deterministicOutputs[key] = value ?? string.Empty;
        }

        public string BuildOutputHash()
        {
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in deterministicOutputs)
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
            return DeterministicHash.Sha256(builder.ToString());
        }
    }

    public sealed class WorldBakePipeline
    {
        private readonly List<IWorldBakeStep> steps = new List<IWorldBakeStep>();

        public WorldBakePipeline(IEnumerable<IWorldBakeStep> steps)
        {
            if (steps != null) this.steps.AddRange(steps);
            this.steps.Sort(CompareSteps);
        }

        public WorldBakeReport Run(WorldBakeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            WorldBakeReport report = new WorldBakeReport();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (IWorldBakeStep step in steps)
            {
                if (step == null) continue;
                if (string.IsNullOrWhiteSpace(step.StableId) || !ids.Add(step.StableId))
                {
                    report.Add(BakeIssueSeverity.Error, "WB_PIPELINE_DUPLICATE_STEP", step?.StableId,
                        "Bake step IDs must be non-empty and unique.");
                    continue;
                }
                step.Execute(context, report);
            }
            report.Sort();
            return report;
        }

        private static int CompareSteps(IWorldBakeStep left, IWorldBakeStep right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;
            int order = left.Order.CompareTo(right.Order);
            return order != 0 ? order : string.CompareOrdinal(left.StableId, right.StableId);
        }
    }
}
