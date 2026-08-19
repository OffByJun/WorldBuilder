using System;
using System.Security.Cryptography;
using System.Text;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WorldBuilder.Entities.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("WorldBuilder/Entities/World Entity")]
    public sealed class WorldEntityAuthoring : MonoBehaviour
    {
        [SerializeField] private int prefabId;
        [SerializeField] private WorldEntityKind kind;
        [SerializeField] private WorldEntityFlags flags = WorldEntityFlags.RegionStreamed;
        [SerializeField] private bool trackChunk = true;
        [SerializeField] private bool useVelocity;
        [SerializeField] private Vector3 initialVelocity;
        [Min(0f), SerializeField] private float lifetimeSeconds;

        public int PrefabId => prefabId;
        public WorldEntityKind Kind => kind;
        public WorldEntityFlags Flags => flags;
        public float LifetimeSeconds => lifetimeSeconds;

        private sealed class WorldEntityBaker : Baker<WorldEntityAuthoring>
        {
            public override void Bake(WorldEntityAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, CreateStableIdentity(authoring));
                AddComponent(entity, new WorldEntityDescriptor
                {
                    PrefabId = authoring.prefabId,
                    Kind = authoring.kind,
                    Flags = authoring.flags
                });
                AddComponent(entity, new WorldEntityChunk());
                AddComponent<WorldEntityActive>(entity);
                if (authoring.trackChunk) AddComponent<WorldEntityTrackChunk>(entity);
                if (authoring.useVelocity)
                    AddComponent(entity, new WorldEntityVelocity { Value = authoring.initialVelocity });
                if (authoring.lifetimeSeconds > 0f)
                    AddComponent(entity, new WorldEntityLifetime { RemainingSeconds = authoring.lifetimeSeconds });
            }

            private static WorldEntityIdentity CreateStableIdentity(WorldEntityAuthoring authoring)
            {
#if UNITY_EDITOR
                string source = GlobalObjectId.GetGlobalObjectIdSlow(authoring).ToString();
                using SHA256 algorithm = SHA256.Create();
                byte[] bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(source));
                return new WorldEntityIdentity
                {
                    High = BitConverter.ToUInt64(bytes, 0),
                    Low = BitConverter.ToUInt64(bytes, 8)
                };
#else
                return default;
#endif
            }
        }
    }
}
