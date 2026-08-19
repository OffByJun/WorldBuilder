using System.Collections.Generic;
using UnityEngine;

namespace WorldBuilder.Editor
{
    public interface IEnvironmentZoneProvider
    {
        IReadOnlyList<Vector3> GetZonePositions();
        float GetIntensityAt(int index);
        Color GetColorAt(int index);
    }
}
