using UnityEngine;

namespace WorldBuilder.Editor.MeshEditing
{
    public sealed class MeshSelector
    {
        private MeshFilter target;

        public MeshFilter Target => target;

        public Mesh Mesh => target != null ? target.sharedMesh : null;

        public bool HasMesh => target != null && target.sharedMesh != null;

        public void SetTarget(MeshFilter value)
        {
            target = value;
        }
    }
}
