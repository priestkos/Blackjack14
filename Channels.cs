using System.Text.RegularExpressions;

namespace Blackjack14;

/// <summary>Where cards are rolled. Values are saved as numbers: never renumber, only append.</summary>
public enum RollChannel
{
    Say = 0,      // /random 10, say radius
    Party = 1,    // /dice party 10
    Alliance = 2, // /dice alliance 10
    Tell = 3,     // /random 10 in say radius, each card relayed by /tell (the game cannot roll into a tell)
}

/// <summary>Where the one-line round result goes. Any combination.</summary>
[Flags]
public enum ResultTargets
{
    None = 0,
    Echo = 1,
    Say = 2,
    Party = 4,
    Alliance = 8,
    Tell = 16,
}

/// <summary>Pure channel text: roll commands, chat prefixes, labels, the tell target check and sender matching.</summary>
public static partial class Channels
{
    public static readonly ResultTargets[] ResultChoices = [ResultTargets.Echo, ResultTargets.Say, ResultTargets.Party, ResultTargets.Alliance, ResultTargets.Tell];

    /// <summary>Party and Alliance use /dice so only that group sees the roll; Say and Tell use /random.</summary>
    public static bool UsesDice(RollChannel c) => c is RollChannel.Party or RollChannel.Alliance;

    public static string RollCommand(RollChannel c) => c switch
    {
        RollChannel.Party => "/dice party 10",
        RollChannel.Alliance => "/dice alliance 10",
        _ => "/random 10",
    };

    public static string Label(RollChannel c) => c switch
    {
        RollChannel.Say => "Say (/random)",
        RollChannel.Party => "Party (/dice)",
        RollChannel.Alliance => "Alliance (/dice)",
        RollChannel.Tell => "Tell (/random + card by tell)",
        _ => c.ToString(),
    };

    public static string Label(ResultTargets t) => t switch
    {
        ResultTargets.Echo => "Echo (only you)",
        _ => t.ToString(),
    };

    /// <summary>The chat command prefix that posts a line to one result target; null for Echo or an unusable tell.</summary>
    public static string? ChatPrefix(ResultTargets t, string tellTarget) => t switch
    {
        ResultTargets.Say => "/s",
        ResultTargets.Party => "/p",
        ResultTargets.Alliance => "/a",
        ResultTargets.Tell when IsValidTellTarget(tellTarget) => $"/tell {tellTarget.Trim()}",
        _ => null,
    };

    // "Forename Surname@World": FFXIV name parts are letters with optional ' or -, 2-15 chars; worlds are letters.
    // This is a trust boundary: the text is typed by the user and ends up inside a chat command.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z'\-]{1,14} [A-Za-z][A-Za-z'\-]{1,14}@[A-Za-z]{3,16}$")]
    private static partial Regex TellTargetRegex();

    public static bool IsValidTellTarget(string? target) => target is not null && TellTargetRegex().IsMatch(target.Trim());

    /// <summary>True when a chat sender's text is exactly this character, allowing a non-letter prefix (party-slot glyph).</summary>
    public static bool SenderIs(string senderText, string characterName)
    {
        if (characterName.Length == 0 || !senderText.EndsWith(characterName, StringComparison.Ordinal)) return false;
        var start = senderText.Length - characterName.Length;
        return start == 0 || !char.IsLetter(senderText[start - 1]) && senderText[start - 1] is not ('\'' or '-' or ' ');
    }

    /// <summary>A plugin-composed line is safe to send only as one short line with no command of its own.</summary>
    public static bool IsSendable(string line) =>
        line.Length is > 0 and <= 400 && !line.Contains('\n') && !line.Contains('\r') && !line.StartsWith('/');

    /// <summary>
    /// Config v1 -> v2. v1 numbered roll channels Say 0, Party 1, Alliance 2, FreeCompany 3, PvPTeam 4, LS/CWLS 5-20,
    /// and had one result setting (0 Off, 1 Echo, 2 the roll channel). Removed channels fall back to Say / Echo.
    /// </summary>
    public static (RollChannel Roll, ResultTargets Results) MigrateV1(int oldRoll, int? oldAnnounce)
    {
        var roll = oldRoll is >= 0 and <= 2 ? (RollChannel)oldRoll : RollChannel.Say;
        var results = oldAnnounce switch
        {
            0 => ResultTargets.None,
            2 when oldRoll is >= 0 and <= 2 => roll switch
            {
                RollChannel.Party => ResultTargets.Party,
                RollChannel.Alliance => ResultTargets.Alliance,
                _ => ResultTargets.Say,
            },
            _ => ResultTargets.Echo,
        };
        return (roll, results);
    }
}
