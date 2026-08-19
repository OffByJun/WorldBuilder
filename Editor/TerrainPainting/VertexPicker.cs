using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor.TerrainPainting
{
    public sealed class VertexPicker
    {
        public void FindWithinRadius(Mesh mesh, Transform owner, Vector3 center, float radius, List<int> results)
        {
            results.Clear();

            if (mesh == null || owner == null)
            {
                return;
            }

            Vector3[] verts = mesh.vertices;
            float sqr = radius * radius;

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 world = owner.TransformPoint(verts[i]);
                if ((world - center).sqrMagnitude <= sqr)
                {
                    results.Add(i);
                }
            }
        }
    }
}
