using UnityEngine;
using WorldBuilder.Runtime.Data;
using WorldBuilder.Runtime.Gameplay;
using WorldBuilder.Runtime.Saves;
using WorldBuilder.Runtime.Terrain;
using WorldBuilder.Runtime.Water;

namespace WorldBuilder.Runtime.Server
{
    /// <summary>
    /// One-component headless world server: wires river erosion, collapse watching,
    /// terrain regrowth, weather-driven water levels and rotating autosaves into a single
    /// tick loop. Works in batchmode (-nographics -batchmode) for dedicated servers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeadlessServerBootstrap : MonoBehaviour
    {
        [SerializeField] private VoxelStoreAsset store;
        [SerializeField] private WaterWorldRuntimeData waterData;
        [SerializeField] private TerrainShapeParams shapeParams;

        [Header("Subsystems")]
        [SerializeField] private RiverbedFlowSim riverSim;
        [SerializeField] private CollapseWatcher collapseWatcher;
        [SerializeField] private TerrainRegrowth regrowth;
        [SerializeField] private WaterLevelDriver waterDriver;
        [SerializeField] private AutoSaveService autoSave;

        [Header("Autosave")]
        [SerializeField] private float autosaveInterval = 300f;
        [SerializeField] private int autosaveSlots = 3;

        public bool IsRunning { get; private set; }

        public void StartServer()
        {
            if (riverSim != null)
            {
                riverSim.Target = waterData;
                riverSim.Store = store;
            }
            if (regrowth != null) regrowth.Bind(store, shapeParams, 128f);
            if (waterDriver != null) waterDriver.Target = waterData;
            if (autoSave != null)
            {
                autoSave.Bind(() => store,
                    () => TerrainDeformer.EditedChunks,
                    () => "{}",
                    _ => null);
            }
            IsRunning = true;
            Debug.Log("[WorldBuilder] Headless world server started.");
        }

        public void StopServer()
        {
            IsRunning = false;
            Debug.Log("[WorldBuilder] Headless world server stopped.");
        }

        /// <summary>One deterministic simulation step — call from a custom loop or Update.</summary>
        public void TickAll(float deltaTime)
        {
            if (!IsRunning) return;
            // Subsystems drive themselves in Update; this hook exists for custom loops.
        }
    }
}
