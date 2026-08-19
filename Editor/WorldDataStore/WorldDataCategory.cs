using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor
{
    [Serializable]
    public sealed class WorldDataCategory
    {
        [SerializeField] private string typeName;
        [SerializeReference] private List<IWorldDataEntry> entries = new List<IWorldDataEntry>();

        public string TypeName
        {
            get => typeName;
            set => typeName = value;
        }

        public List<IWorldDataEntry> Entries => entries;
    }
}
