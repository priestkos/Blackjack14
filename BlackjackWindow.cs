using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Blackjack14;

public sealed class BlackjackWindow : Window
{
    private static readonly (decimal Value, string Label)[] Payouts = [(1.5m, "3:2"), (1.2m, "6:5"), (2m, "2:1"), (1m, "1:1")];
    private static readonly string[] AnnounceLabels = ["Off", "Echo (only you)", "Roll channel"];

    private readonly Blackjack14Plugin plugin;
    private string? selectTab;

    public BlackjackWindow(Blackjack14Plugin plugin) : base("Blackjack###Blackjack14")
    {
        this.plugin = plugin;
        Size = new Vector2(360, 340);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(300, 240), MaximumSize = new Vector2(900, 1000) };
    }

    public void OpenSettings() => Open("Settings");

    public void OpenHelp() => Open("Help");

    private void Open(string tab)
    {
        selectTab = tab;
        IsOpen = true;
    }

    public override void Draw()
    {
        using var bar = ImRaii.TabBar("##tabs");
        if (!bar) return;
        Tab("Table", DrawTable);
        Tab("Settings", DrawSettings);
        Tab("Help", DrawHelp);
        selectTab = null;
    }

    private void Tab(string name, Action draw)
    {
        var flags = selectTab == name ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        using var item = ImRaii.TabItem(name, flags);
        if (item) draw();
    }

    private void DrawTable()
    {
        var game = plugin.Game;

        if (game.Phase == Phase.Idle)
        {
            ImGui.TextDisabled($"Deal to start. Rolls go to: {Channels.Label(plugin.Config.RollChannel)}.");
        }
        else
        {
            DrawHand("Dealer", game.Dealer, false);
            for (var i = 0; i < game.PlayerHands.Count; i++)
                DrawHand(game.PlayerHands.Count > 1 ? $"Hand {i + 1}" : "You", game.PlayerHands[i],
                    game.Phase == Phase.Player && i == game.ActiveIndex);
        }

        ImGui.Separator();
        ImGui.TextUnformatted(Status(game));

        if (plugin.Paused && ImGui.Button("Resume rolling")) plugin.Resume();

        using (ImRaii.Disabled(game.Phase is not (Phase.Idle or Phase.Done))) { if (ImGui.Button("Deal")) plugin.Deal(); }
        ImGui.SameLine();
        using (ImRaii.Disabled(!game.CanHit)) { if (ImGui.Button("Hit")) game.Hit(); }
        ImGui.SameLine();
        using (ImRaii.Disabled(!game.CanStand)) { if (ImGui.Button("Stand")) game.Stand(); }
        ImGui.SameLine();
        using (ImRaii.Disabled(!game.CanDouble)) { if (ImGui.Button("Double")) game.Double(); }
        ImGui.SameLine();
        using (ImRaii.Disabled(!game.CanSplit)) { if (ImGui.Button("Split")) game.Split(); }

        if (game.Phase != Phase.Idle && ImGui.Button("Abandon round")) plugin.Reset();

        foreach (var line in game.Results) ImGui.TextUnformatted(line);
        if (game.Phase == Phase.Done) ImGui.TextUnformatted($"Round: {game.Net:+0.#;-0.#;0} unit(s)");
    }

    private void DrawSettings()
    {
        var config = plugin.Config;
        var inRound = plugin.Game.Phase is not (Phase.Idle or Phase.Done);
        if (inRound) ImGui.TextDisabled("Locked while a round is in play.");
        using var locked = ImRaii.Disabled(inRound);
        var changed = false;

        ImGui.TextUnformatted("Channels");
        using (var combo = ImRaii.Combo("Rolls go to", Channels.Label(config.RollChannel)))
        {
            if (combo)
            {
                foreach (var c in Enum.GetValues<RollChannel>())
                {
                    if (!ImGui.Selectable(Channels.Label(c), c == config.RollChannel)) continue;
                    config.RollChannel = c;
                    changed = true;
                }
            }
        }

        ImGui.TextDisabled($"Sends: {Channels.RollCommand(config.RollChannel)}");

        var announce = (int)config.Announce;
        if (ImGui.Combo("Round result", ref announce, AnnounceLabels, AnnounceLabels.Length))
        {
            config.Announce = (AnnounceTarget)announce;
            changed = true;
        }

        if (config.Announce == AnnounceTarget.RollChannel)
            ImGui.TextDisabled($"Posts with {Channels.ChatPrefix(config.RollChannel)}, visible to that channel.");

        ImGui.Separator();
        ImGui.TextUnformatted("Pacing");
        var spacing = config.RollSpacingSeconds;
        if (ImGui.SliderInt("Seconds between rolls", ref spacing, Configuration.MinRollSpacing, 10))
        {
            config.RollSpacingSeconds = spacing;
            changed = true;
        }

        var timeout = config.RollTimeoutSeconds;
        if (ImGui.SliderInt("Pause if no result (s)", ref timeout, 5, 30))
        {
            config.RollTimeoutSeconds = timeout;
            changed = true;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Rules");
        var h17 = config.Rules.DealerHitsSoft17;
        if (ImGui.Checkbox("Dealer hits soft 17", ref h17))
        {
            config.Rules.DealerHitsSoft17 = h17;
            changed = true;
        }

        var das = config.Rules.DoubleAfterSplit;
        if (ImGui.Checkbox("Double after split", ref das))
        {
            config.Rules.DoubleAfterSplit = das;
            changed = true;
        }

        var payoutLabel = Payouts.FirstOrDefault(p => p.Value == config.Rules.NaturalPays).Label ?? "3:2";
        using (var combo = ImRaii.Combo("Blackjack pays", payoutLabel))
        {
            if (combo)
            {
                foreach (var (value, label) in Payouts)
                {
                    if (!ImGui.Selectable(label, value == config.Rules.NaturalPays)) continue;
                    config.Rules.NaturalPays = value;
                    changed = true;
                }
            }
        }

        if (changed) plugin.SaveConfig();
    }

    private static void DrawHelp()
    {
        Section("Cards", "Each card is one roll of 1-10. 1 = Ace (1 or 11). 10 = face card. Closest to 21 without going over wins.");
        Section("A round", "Deal rolls 3 cards: 2 yours, 1 dealer's. You choose moves; the plugin rolls the cards. "
            + "After you stand, the dealer draws to 17. 21 stands automatically.");
        Section("Moves", "Hit: one card.  Stand: end the hand.  Double: first two cards only; one card at 2x stake, then stand.  "
            + "Split: a pair becomes two hands, each dealt a second card.");
        Section("Channels", "Say radius uses /random 10. Any other choice uses /dice <channel> 10, seen only by that channel. "
            + "Only your own roll in that channel counts as a card.");
        Section("Round result", "Off, Echo (only you see it), or posted to the roll channel as one line.");
        Section("Safety", "Nothing rolls until you press a button, never faster than the set spacing (2 s minimum). "
            + "No result within the pause time stops rolling until you press Resume. Logging out abandons the round.");
        Section("Limits", "English client only. Bets and gil are not handled; stakes are units.");
    }

    private static void Section(string title, string body)
    {
        ImGui.TextUnformatted(title);
        ImGui.TextWrapped(body);
        ImGui.Spacing();
    }

    private static void DrawHand(string label, Hand hand, bool active)
    {
        var cards = hand.Cards.Count == 0 ? "-" : string.Join(" ", hand.Cards.Select(BlackjackGame.CardName));
        var total = hand.Cards.Count == 0 ? "" : $"  = {(hand.IsSoft ? "soft " : "")}{hand.Total}{(hand.IsBust ? " BUST" : "")}";
        var stake = hand.Doubled ? "  (doubled)" : "";
        ImGui.TextUnformatted($"{(active ? "> " : "  ")}{label}: {cards}{total}{stake}");
    }

    private string Status(BlackjackGame game) => game.Phase switch
    {
        _ when plugin.Paused => $"Paused: no roll result seen within {plugin.Config.RollTimeoutSeconds} s.",
        _ when plugin.RollInFlight => "Rolling...",
        Phase.Idle => "Ready.",
        Phase.Dealing => "Dealing...",
        Phase.Player when game.NeedsCard => "Drawing...",
        Phase.Player => "Your move.",
        Phase.Dealer => $"Dealer draws to {BlackjackGame.DealerStandsOn}...",
        _ => "Round over.",
    };
}
