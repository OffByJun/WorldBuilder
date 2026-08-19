using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldBuilder.Baking.Core;
using WorldBuilder.Runtime.Grid;

namespace WorldBuilder.Tests
{
    public sealed class BakePipelineTests
    {
        private sealed class Step : IWorldBakeStep
        {
            public string StableId { get; }
            public int Order { get; }
            private readonly string value;
            public Step(string id, int order, string value) { StableId = id; Order = order; this.value = value; }
            public void Execute(WorldBakeContext context, WorldBakeReport report) => context.SetDeterministicOutput(StableId, value);
        }

        [Test]
        public void PipelineAndOutputHash_AreDeterministic()
        {
            WorldGridSettings settings = ScriptableObject.CreateInstance<WorldGridSettings>();
            WorldBakePipeline first = new WorldBakePipeline(new IWorldBakeStep[] { new Step("b", 10, "2"), new Step("a", 10, "1") });
            WorldBakePipeline second = new WorldBakePipeline(new IWorldBakeStep[] { new Step("a", 10, "1"), new Step("b", 10, "2") });
            WorldBakeContext a = new WorldBakeContext(settings);
            WorldBakeContext b = new WorldBakeContext(settings);
            Assert.That(first.Run(a).HasErrors, Is.False);
            Assert.That(second.Run(b).HasErrors, Is.False);
            Assert.That(a.BuildOutputHash(), Is.EqualTo(b.BuildOutputHash()));
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void DuplicateStepId_IsValidationError()
        {
            WorldGridSettings settings = ScriptableObject.CreateInstance<WorldGridSettings>();
            WorldBakePipeline pipeline = new WorldBakePipeline(new List<IWorldBakeStep>
            {
                new Step("same", 0, "1"), new Step("same", 1, "2")
            });
            Assert.That(pipeline.Run(new WorldBakeContext(settings)).HasErrors, Is.True);
            Object.DestroyImmediate(settings);
        }
    }
}
