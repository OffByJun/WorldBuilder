using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor.PrefabBrush
{
    public sealed class PrefabBrushSettings : ScriptableObject
    {
        public int seed;
        public float brushRadius = 3f;
        public int brushDensity = 10;
        public bool eraseMode;
        public float chunkSize = 16f;
        public bool alignToNormal = true;
        public bool randomYaw = true;
        public Vector2 scaleRange = new Vector2(1f, 1f);
        public bool duplicateSharedScriptableObjects = true;

        public List<PrefabEntry> prefabEntries = new List<PrefabEntry>();
        public BrushMask mask = new BrushMask();
        public ModifierGraph modifierGraph;
        public List<BrushStroke> strokes = new List<BrushStroke>();

        private void OnEnable()
        {
            if (seed == 0)
                seed = Random.Range(1, 99999);
        }
    }
}