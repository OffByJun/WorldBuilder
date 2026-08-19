using System.Collections.Generic;
using UnityEditor;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor.BiomeSetter
{
    public sealed class EditorBiomeDataProvider : IBiomeDataProvider
    {
        public BiomeData[] GetAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:BiomeData");
            List<BiomeData> result = new List<BiomeData>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BiomeData data = AssetDatabase.LoadAssetAtPath<BiomeData>(path);
                if (data != null)
                {
                    result.Add(data);
                }
            }

            return result.ToArray();
        }
    }
}
