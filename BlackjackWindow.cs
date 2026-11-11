using System.Globalization;
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

    private static string Pct(double x, int decimals = 1) =>
        (x * 100).ToString(decimals switch { 0 => "0", 1 => "0.0", _ => "0.00" }, CultureInfo.InvariantCulture) + "%";

    private void DrawHelp()
    {
        var rules = plugin.Config.Rules;
        var dice = Odds.For(rules);
        var deck = Odds.For(rules, deck: true);
        Rules With(bool? h17 = null, bool? das = null, decimal? pays = null) => new()
        {
            DealerHitsSoft17 = h17 ?? rules.DealerHitsSoft17,
            DoubleAfterSplit = das ?? rules.DoubleAfterSplit,
            NaturalPays = pays ?? rules.NaturalPays,
        };

        Section("Cards", "One roll of 1-10 is one card. 1 is an Ace, 10 is a face card. Closest to 21 without going over wins. "
            + "Why: a 1-10 roll gives every value the same 10% chance and the roll is shown in chat (to everyone in say range for Say and Tell, "
            + "to the group for Party and Alliance), so nobody has to trust a hidden deck.");
        Section("Ace counts 1 or 11", "It counts as 11 whenever that does not bust you. Why: the Ace is never a bad card, so a hand with one is 'soft' and can take a card without busting.");
        Section("Tens are rarer here", $"A deck has four 10-value ranks (10, J, Q, K), so {Pct(Odds.Deck[10], 0)} of its cards are tens; a roll of 10 is {Pct(Odds.Dice[10], 0)}. "
            + $"The average card is {dice.CardAverage.ToString("0.0", CultureInfo.InvariantCulture)} here and {deck.CardAverage.ToString("0.0", CultureInfo.InvariantCulture)} in a deck. "
            + $"Why: 20s and naturals come up less often, and the dealer busts less: {Pct(dice.DealerBustOverall)} of dealer hands bust here, {Pct(deck.DealerBustOverall)} with a deck.");
        Section("The dealer draws to 17", "Below 17 the dealer must hit; on 17 or more the dealer must stand. "
            + "Why: a fixed rule means the dealer makes no choices and cannot favour anyone. 17 is the standard casino line.");
        Section("Dealer hits soft 17 (setting)", "Makes the dealer draw again on Ace+6. "
            + $"Why: it raises the house edge from {Pct(Odds.For(With(h17: false)).HouseEdge, 2)} to {Pct(Odds.For(With(h17: true)).HouseEdge, 2)}, so it is off by default. "
            + $"It costs far more here than with a deck ({Pct(Odds.For(With(h17: false), true).HouseEdge, 2)} to {Pct(Odds.For(With(h17: true), true).HouseEdge, 2)}): "
            + $"from soft 17 the next card makes 18-21 {Pct(1 - dice.BustOnHit(17), 0)} of the time here, {Pct(1 - deck.BustOnHit(17), 0)} with a deck.");
        Section("You act first", "If you bust you lose, even if the dealer busts afterwards. Why: this is where the house's edge comes from in blackjack.");
        Section("Double", "First two cards only: the stake doubles, you take exactly one card, then stand. "
            + "Why: it is meant for a strong start such as 10 or 11. With 1-10 rolls it never has the best expected value, under any setting: "
            + $"an 11 becomes 21 only {Pct(Odds.Dice[10], 0)} of the time ({Pct(Odds.Deck[10], 0)} with a deck), so one card at double stake is worse than hitting freely.");
        Section("Split", "Only on a pair, once per round. Each card starts its own hand. "
            + "Why: a pair like 8+8 makes a poor 16, but two hands starting from 8 can still win. One split keeps the round short. The chart below shows which pairs to split.");
        Section("Double after split (setting)", "Lets each split hand double on its first two cards. "
            + "With 1-10 rolls it changes nothing, because doubling never pays here. "
            + $"With a deck it is worth {Pct(Odds.For(With(das: false), true).HouseEdge - Odds.For(With(das: true), true).HouseEdge, 2)} to the player.");
        Section("Blackjack (natural)", $"Ace + 10 on your first two cards: {Pct(dice.Natural)} of hands here, {Pct(deck.Natural)} with a deck. "
            + "It pays 3:2 by default and ties a dealer natural. Why: it is the best hand and rare, so it pays more. "
            + "A 21 after a split is an ordinary 21: it is not your original two-card hand (the standard casino rule).");
        Section("21 stands automatically", "Why: no card can improve 21.");

        ImGui.Separator();
        ImGui.TextUnformatted("The maths (current settings, best play)");
        ImGui.Spacing();
        Section("House edge", $"You lose {Pct(dice.HouseEdge, 2)} of every unit staked on average: {(dice.HouseEdge * 100).ToString("0.0", CultureInfo.InvariantCulture)} units per 100. "
            + $"The same rules with a deck: {Pct(deck.HouseEdge, 2)}. "
            + $"By natural payout: 3:2 {Pct(Odds.For(With(pays: 1.5m)).HouseEdge, 2)}, 6:5 {Pct(Odds.For(With(pays: 1.2m)).HouseEdge, 2)}, "
            + $"2:1 {Pct(Odds.For(With(pays: 2m)).HouseEdge, 2)}, 1:1 {Pct(Odds.For(With(pays: 1m)).HouseEdge, 2)}.");
        Section("Luck against the edge", $"One round swings about {Odds.RoundSpread.ToString("0.00", CultureInfo.InvariantCulture)} units either way (standard deviation). "
            + $"Over n rounds the edge costs n x {Pct(dice.HouseEdge, 2)} of a unit, while luck spreads about {Odds.RoundSpread.ToString("0.00", CultureInfo.InvariantCulture)} x sqrt(n), "
            + $"so the edge only outweighs luck after about {dice.RoundsUntilEdgeShows.ToString("N0", CultureInfo.InvariantCulture)} rounds.");
        Section("Busting when you hit", "A hard total t of 12 or more busts on the next card (t - 11) x 10% of the time: "
            + $"12 {Pct(dice.BustOnHit(12), 0)}, 14 {Pct(dice.BustOnHit(14), 0)}, 16 {Pct(dice.BustOnHit(16), 0)}, 20 {Pct(dice.BustOnHit(20), 0)}. "
            + $"With a deck: 12 {Pct(deck.BustOnHit(12), 0)}, 16 {Pct(deck.BustOnHit(16), 0)}. That is why the chart hits more than a casino chart does.");

        ImGui.TextUnformatted("Dealer busts, by up-card");
        using (var table = ImRaii.Table("##bust", 11, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
        {
            if (table)
            {
                Header("");
                Row("Dice", u => Pct(dice.DealerBust(u), 0));
                Row("Deck", u => Pct(deck.DealerBust(u), 0));
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Best play: H hit, S stand, D double, P split");
        ImGui.TextWrapped("Hard 11 or less: always hit. Hard 18 or more, and soft 19 or more: always stand.");
        using (var table = ImRaii.Table("##chart", 11, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
        {
            if (table)
            {
                Header("You \\ Dealer");
                foreach (var (label, moves) in dice.Chart)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(label);
                    foreach (var m in moves)
                    {
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(m switch { Move.Hit => "H", Move.Stand => "S", Move.Double => "D", _ => "P" });
                    }
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        Section("Channels", "Say rolls /random (say radius). Party and Alliance roll /dice there, so only that group sees it. "
            + "Tell rolls /random and tells each card to the named player. Why: the game cannot roll into a tell, so the roll stays public and the tell only relays it.");
        Section("Round result", "Tick any mix of Echo (only you), Say, Party, Alliance and Tell; each gets the same one-line result.");
        Section("Safety", $"Nothing rolls until you press a button. Every roll or post is at least the set spacing apart ({Configuration.MinRollSpacing} s minimum), to stay clear of chat spam limits. "
            + "Only your own roll counts as a card, so nobody else can feed you cards. "
            + "If no result arrives in time, rolling pauses rather than re-rolling, so a lost roll is never counted twice. Logging out abandons the round.");
        Section("Limits", "English client only. Bets and gil are not handled; stakes are units. The maths assumes best play and the 1-10 rolls are fair.");
        return;

        void Header(string first)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(first);
            foreach (var u in Odds.ChartUpCards)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(BlackjackGame.CardName(u));
            }
        }

        void Row(string label, Func<int, string> cell)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(label);
            foreach (var u in Odds.ChartUpCards)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(cell(u));
            }
        }
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
