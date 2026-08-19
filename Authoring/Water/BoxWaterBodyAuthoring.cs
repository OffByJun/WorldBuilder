using UnityEngine;

namespace WorldBuilder.Authoring.Water
{
    public abstract class BoxWaterBodyAuthoring : WaterBodyAuthoring
    {
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size = new Vector3(4f, 4f, 4f);

        public Vector3 Center { get => center; set => center = value; }
        public Vector3 Size { get => size; set => size = MaxSize(value); }

        public Matrix4x4 GetWorldToUnitBoxMatrix()
        {
            Matrix4x4 local = Matrix4x4.TRS(center, Quaternion.identity, MaxSize(size));
            return (transform.localToWorldMatrix * local).inverse;
        }

        public Bounds GetWorldBounds()
        {
            Matrix4x4 matrix = transform.localToWorldMatrix * Matrix4x4.TRS(center, Quaternion.identity, MaxSize(size));
            Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(Vector3.zero), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(x, y, z) * 0.5f));
            return bounds;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            size = MaxSize(size);
        }

        private static Vector3 MaxSize(Vector3 value) => new Vector3(
            Mathf.Max(0.01f, value.x), Mathf.Max(0.01f, value.y), Mathf.Max(0.01f, value.z));
    }
}
