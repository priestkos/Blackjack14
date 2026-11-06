using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Blackjack14;

public sealed class BlackjackWindow : Window
{
    private static readonly (decimal Value, string Label)[] Payouts = [(1.5m, "3:2"), (1.2m, "6:5"), (2m, "2:1"), (1m, "1:1")];

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

        using (ImRaii.Disabled(game.Phase is not (Phase.Idle or Phase.Done) || !plugin.CanRoll)) { if (ImGui.Button("Deal")) plugin.Deal(); }
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

        ImGui.TextUnformatted("Rolls");
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

        ImGui.TextDisabled($"Sends: {Channels.RollCommand(config.RollChannel)}"
            + (config.RollChannel == RollChannel.Tell ? ", then each card by /tell" : ""));

        ImGui.Separator();
        ImGui.TextUnformatted("Round result goes to");
        foreach (var target in Channels.ResultChoices)
        {
            var on = config.ResultTo.HasFlag(target);
            if (!ImGui.Checkbox(Channels.Label(target), ref on)) continue;
            config.ResultTo = on ? config.ResultTo | target : config.ResultTo & ~target;
            changed = true;
        }

        if (config.RollChannel == RollChannel.Tell || config.ResultTo.HasFlag(ResultTargets.Tell))
        {
            ImGui.Separator();
            var tell = config.TellTarget;
            if (ImGui.InputTextWithHint("Tell target", "Forename Surname@World", ref tell, 48))
            {
                config.TellTarget = tell.Trim();
                changed = true;
            }

            if (!Channels.IsValidTellTarget(config.TellTarget))
                ImGui.TextDisabled("Needs Forename Surname@World. Nothing is sent by tell until it does.");
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Pacing");
        var spacing = config.RollSpacingSeconds;
        if (ImGui.SliderInt("Seconds between sends", ref spacing, Configuration.MinRollSpacing, 10))
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
        Section("Cards", "One roll of 1-10 is one card. 1 is an Ace, 10 is a face card. Closest to 21 without going over wins. "
            + "Why: /random 10 gives every value the same 1-in-10 chance and anyone nearby can see the roll, so nobody has to trust a hidden deck.");
        Section("Ace counts 1 or 11", "It counts as 11 whenever that does not bust you. Why: the Ace is never a bad card, so a hand with one is 'soft' and can take a card without busting.");
        Section("Tens are rarer here", "A deck has four 10-value ranks (10, J, Q, K), so 31% of its cards are tens; a roll of 10 is 10%. "
            + "Why it matters: 20s and naturals come up less often than at a casino table, and the dealer busts less.");
        Section("The dealer draws to 17", "Below 17 the dealer must hit; on 17 or more the dealer must stand. "
            + "Why: a fixed rule means the dealer makes no choices and cannot favour anyone. 17 is the standard casino line.");
        Section("Dealer hits soft 17 (setting)", "Makes the dealer draw again on Ace+6. Why: it slightly improves the house's odds, so it is off by default.");
        Section("You act first", "If you bust you lose, even if the dealer busts afterwards. Why: this is where the house's edge comes from in blackjack.");
        Section("Double", "First two cards only: the stake doubles, you take exactly one card, then stand. "
            + "Why: you are betting more on a strong start (typically 10 or 11), so you give up the right to keep hitting.");
        Section("Split", "Only on a pair, once per round. Each card starts its own hand. "
            + "Why: a pair like 8+8 makes a poor 16, but two hands starting from 8 each can still win. One split keeps the round short.");
        Section("Double after split (setting)", "Lets each split hand double on its first two cards. On by default; turning it off favours the house.");
        Section("Blackjack (natural)", "Ace + 10 on your first two cards. It pays 3:2 by default and ties a dealer natural. "
            + "Why: it is the rarest strong hand (2% here), so it pays more. 6:5 or 1:1 favours the house. "
            + "A 21 after a split is an ordinary 21: it is not your original two-card hand, which is the standard casino rule.");
        Section("21 stands automatically", "Why: no card can improve 21.");
        Section("Channels", "Say rolls /random (say radius). Party and Alliance roll /dice there, so only that group sees it. "
            + "Tell rolls /random and tells each card to the named player. Why: the game cannot roll into a tell, so the roll stays public and the tell only relays it.");
        Section("Round result", "Tick any mix of Echo (only you), Say, Party, Alliance and Tell; each gets the same one-line result.");
        Section("Safety", "Nothing rolls until you press a button. Every roll or post is at least the set spacing apart (2 s minimum), to stay clear of chat spam limits. "
            + "Only your own roll counts as a card, so nobody else can feed you cards. "
            + "If no result arrives in time, rolling pauses rather than re-rolling, so a lost roll is never counted twice. Logging out abandons the round.");
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
        _ when !plugin.CanRoll => "Tell needs a target: Settings > Tell target.",
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
