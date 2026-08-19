using System.Collections.Generic;
using UnityEngine;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.SpawnEditing
{
    public sealed class SpawnerSceneQuery : ISpawnerSceneQuery
    {
        public IReadOnlyList<ISpawner> GetAll()
        {
            MonoBehaviour[] behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            List<ISpawner> result = new List<ISpawner>();

            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ISpawner spawner)
                {
                    result.Add(spawner);
                }
            }

            return result;
        }
    }
}
