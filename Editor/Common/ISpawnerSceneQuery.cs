using System.Collections.Generic;
using WorldBuilder.Runtime.Data;

namespace WorldBuilder.Editor
{
    public interface ISpawnerSceneQuery
    {
        IReadOnlyList<ISpawner> GetAll();
    }
}
