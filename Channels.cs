namespace Blackjack14;

/// <summary>Where cards are rolled. Say = /random (say radius); the rest are /dice chat modes.</summary>
public enum RollChannel
{
    Say, Party, Alliance, FreeCompany, PvPTeam,
    Linkshell1, Linkshell2, Linkshell3, Linkshell4, Linkshell5, Linkshell6, Linkshell7, Linkshell8,
    CrossLinkshell1, CrossLinkshell2, CrossLinkshell3, CrossLinkshell4,
    CrossLinkshell5, CrossLinkshell6, CrossLinkshell7, CrossLinkshell8,
}

public enum AnnounceTarget { Off, Echo, RollChannel }

/// <summary>Pure channel text: the roll command, the chat prefix for announcements, and own-sender matching.</summary>
public static class Channels
{
    // /dice chat-mode tokens from the Lodestone /dice entry: party, alliance, freecompany, pvpteam, linkshell1-8, cwlinkshell1-8.
    public static string RollCommand(RollChannel c) => c == RollChannel.Say ? "/random 10" : $"/dice {DiceMode(c)} 10";

    private static string DiceMode(RollChannel c) => c switch
    {
        RollChannel.Party => "party",
        RollChannel.Alliance => "alliance",
        RollChannel.FreeCompany => "freecompany",
        RollChannel.PvPTeam => "pvpteam",
        >= RollChannel.Linkshell1 and <= RollChannel.Linkshell8 => $"linkshell{c - RollChannel.Linkshell1 + 1}",
        >= RollChannel.CrossLinkshell1 and <= RollChannel.CrossLinkshell8 => $"cwlinkshell{c - RollChannel.CrossLinkshell1 + 1}",
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    /// <summary>Chat command that posts a line to the channel the rolls go to.</summary>
    public static string ChatPrefix(RollChannel c) => c switch
    {
        RollChannel.Say => "/s",
        RollChannel.Party => "/p",
        RollChannel.Alliance => "/a",
        RollChannel.FreeCompany => "/fc",
        RollChannel.PvPTeam => "/pt",
        >= RollChannel.Linkshell1 and <= RollChannel.Linkshell8 => $"/l{c - RollChannel.Linkshell1 + 1}",
        >= RollChannel.CrossLinkshell1 and <= RollChannel.CrossLinkshell8 => $"/cwl{c - RollChannel.CrossLinkshell1 + 1}",
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    public static string Label(RollChannel c) => c switch
    {
        RollChannel.Say => "Say radius (/random)",
        RollChannel.FreeCompany => "Free Company",
        RollChannel.PvPTeam => "PvP Team",
        >= RollChannel.Linkshell1 and <= RollChannel.Linkshell8 => $"Linkshell {c - RollChannel.Linkshell1 + 1}",
        >= RollChannel.CrossLinkshell1 and <= RollChannel.CrossLinkshell8 => $"Cross-world LS {c - RollChannel.CrossLinkshell1 + 1}",
        _ => c.ToString(),
    };

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
}
