using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldBuilder.Editor.RigidbodyBatchTool
{
    public struct RigidbodyBatchSettings
    {
        public bool applyMass;
        public float mass;
        public bool applyLinearDamping;
        public float linearDamping;
        public bool applyAngularDamping;
        public float angularDamping;
        public bool applyUseGravity;
        public bool useGravity;
        public bool applyIsKinematic;
        public bool isKinematic;
        public bool applyConstraints;
        public RigidbodyConstraints constraints;
    }

    public static class RigidbodyBatchService
    {
        public static int Apply(bool sceneWide, RigidbodyBatchSettings settings)
        {
            List<Rigidbody> bodies = SceneObjectCollector.CollectComponents<Rigidbody>(sceneWide);
            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody body = bodies[i];
                Undo.RecordObject(body, "Rigidbody Batch");

                if (settings.applyMass)
                {
                    body.mass = settings.mass;
                }

                if (settings.applyLinearDamping)
                {
                    body.linearDamping = settings.linearDamping;
                }

                if (settings.applyAngularDamping)
                {
                    body.angularDamping = settings.angularDamping;
                }

                if (settings.applyUseGravity)
                {
                    body.useGravity = settings.useGravity;
                }

                if (settings.applyIsKinematic)
                {
                    body.isKinematic = settings.isKinematic;
                }

                if (settings.applyConstraints)
                {
                    body.constraints = settings.constraints;
                }

                EditorUtility.SetDirty(body);
            }

            return bodies.Count;
        }
    }
}
