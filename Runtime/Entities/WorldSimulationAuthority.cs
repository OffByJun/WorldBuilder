namespace WorldBuilder.Entities
{
    public static class WorldSimulationAuthority
    {
        private struct State { }
        private static readonly Unity.Burst.SharedStatic<int> _guest = Unity.Burst.SharedStatic<int>.GetOrCreate<State>();
        public static bool Enabled { get => _guest.Data == 0; set => _guest.Data = value ? 0 : 1; }
    }
}
