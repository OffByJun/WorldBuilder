using Unity.Mathematics;

namespace WorldBuilder.Entities.Creatures
{
    public enum CreatureBondingMode : byte
    {
        None,
        SwimTogether,
        PlayBall,
        FollowLight,
        WarmShell
    }

    /// <summary>생물군별 교감 조건과 진행도 계산입니다. 시간은 초 단위입니다.</summary>
    public static class CreatureBondingRules
    {
        public const float DefaultSeconds = 7.5f;
        public const float MaximumFollowSpeed = 4.5f;
        public static CreatureBondingMode ForSpecies(int prefabId) => prefabId switch
        {
            >= 2100 and <= 2104 or >= 3100 and <= 3104 => CreatureBondingMode.SwimTogether,
            2106 or 2110 or 2112 or 2113 or 2114 or 3106 => CreatureBondingMode.PlayBall,
            >= 2107 and <= 2109 or >= 3107 and <= 3109 => CreatureBondingMode.FollowLight,
            2111 => CreatureBondingMode.WarmShell,
            _ => CreatureBondingMode.None
        };

        public static float Duration(float seconds, CreatureGrade grade) => math.max(1f, seconds) *
            (grade == CreatureGrade.Legendary ? 2f : grade == CreatureGrade.Rare ? 1.5f : 1f);

        public static float Advance(float progress, float seconds, float deltaTime, bool valid) =>
            valid ? math.saturate(progress + math.max(0f, deltaTime) / math.max(1f, seconds)) : math.saturate(progress);
    }
}
