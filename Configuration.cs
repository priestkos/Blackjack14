using Dalamud.Configuration;

namespace Blackjack14;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 2;
    public const int MinRollSpacing = 2; // safety floor: never more than one roll or post every 2 s

    public int Version { get; set; } = CurrentVersion;
    public RollChannel RollChannel { get; set; } = RollChannel.Say;
    public ResultTargets ResultTo { get; set; } = ResultTargets.Echo;
    public string TellTarget { get; set; } = "";
    public int RollSpacingSeconds { get; set; } = MinRollSpacing;
    public int RollTimeoutSeconds { get; set; } = 10;
    public Rules Rules { get; set; } = new();

    /// <summary>v1 only: 0 Off, 1 Echo, 2 roll channel. Read once by the migration, then cleared.</summary>
    public int? Announce { get; set; }

    /// <summary>Migrates an older file and pulls hand-edited values back inside the ranges the UI offers.</summary>
    public void Normalize()
    {
        if (Version < 2)
        {
            (RollChannel, ResultTo) = Channels.MigrateV1((int)RollChannel, Announce);
            Version = CurrentVersion;
        }

        Announce = null;
        if (!Enum.IsDefined(RollChannel)) RollChannel = RollChannel.Say;
        ResultTo &= ResultTargets.Echo | ResultTargets.Say | ResultTargets.Party | ResultTargets.Alliance | ResultTargets.Tell;
        TellTarget = (TellTarget ?? "").Trim();
        RollSpacingSeconds = Math.Clamp(RollSpacingSeconds, MinRollSpacing, 10);
        RollTimeoutSeconds = Math.Clamp(RollTimeoutSeconds, 5, 30);
        Rules ??= new Rules();
        if (Rules.NaturalPays is not (1m or 1.2m or 1.5m or 2m)) Rules.NaturalPays = 1.5m;
    }
}
