using Dalamud.Configuration;

namespace Blackjack14;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int MinRollSpacing = 2; // safety floor: never more than one roll or post every 2 s

    public int Version { get; set; } = 1;
    public RollChannel RollChannel { get; set; } = RollChannel.Say;
    public AnnounceTarget Announce { get; set; } = AnnounceTarget.Echo;
    public int RollSpacingSeconds { get; set; } = MinRollSpacing;
    public int RollTimeoutSeconds { get; set; } = 10;
    public Rules Rules { get; set; } = new();

    /// <summary>Pulls a hand-edited or stale file back inside the ranges the UI offers.</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(RollChannel)) RollChannel = RollChannel.Say;
        if (!Enum.IsDefined(Announce)) Announce = AnnounceTarget.Echo;
        RollSpacingSeconds = Math.Clamp(RollSpacingSeconds, MinRollSpacing, 10);
        RollTimeoutSeconds = Math.Clamp(RollTimeoutSeconds, 5, 30);
        Rules ??= new Rules();
        if (Rules.NaturalPays is not (1m or 1.2m or 1.5m or 2m)) Rules.NaturalPays = 1.5m;
    }
}
