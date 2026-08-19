namespace WorldBuilder.Authoring.Water
{
    public sealed class AirOverrideVolume : BoxWaterBodyAuthoring
    {
        protected override void Reset()
        {
            base.Reset();
            Priority = 100;
        }
    }
}
