using Xunit;

namespace Blackjack14.Tests;

public class BlackjackGameTests
{
    private static BlackjackGame Dealt(int p1, int p2, int d)
    {
        var g = new BlackjackGame();
        g.Deal();
        Assert.True(g.AddCard(p1));
        Assert.True(g.AddCard(p2));
        Assert.True(g.AddCard(d));
        return g;
    }

    [Theory]
    [InlineData(new[] { 1, 10 }, 21, true)]
    [InlineData(new[] { 1, 1 }, 12, true)]
    [InlineData(new[] { 1, 5, 10 }, 16, false)]
    [InlineData(new[] { 10, 10, 2 }, 22, false)]
    public void Totals_count_aces_as_one_or_eleven(int[] cards, int total, bool soft)
    {
        var h = new Hand();
        h.Cards.AddRange(cards);
        Assert.Equal(total, h.Total);
        Assert.Equal(soft, h.IsSoft);
    }

    [Theory]
    [InlineData("Random! You roll a 7 (out of 10).", false, 7)]
    [InlineData("Random! You roll a 10 (out of 10).", false, 10)]
    [InlineData("Random! You roll a 7 (out of 13).", false, null)]
    [InlineData("Random! Someone Else rolls a 7 (out of 10).", false, null)]
    [InlineData("Random! (1-10) 7", false, null)]
    [InlineData("Random! (1-10) 7", true, 7)]
    [InlineData("Random! (1-10) 10", true, 10)]
    [InlineData("Random! (1-13) 7", true, null)]
    [InlineData("Random! You roll a 7 (out of 10).", true, null)]
    [InlineData("Random! You roll a 99999999999 (out of 10).", false, null)] // too long for int: must not throw
    [InlineData("Random! (1-10) 99999999999", true, null)]
    [InlineData("Random! You roll a 100 (out of 10).", false, null)]
    public void Parses_only_d10_rolls_in_the_chosen_format(string text, bool dice, int? expected) =>
        Assert.Equal(expected, BlackjackGame.TryParseRoll(text, dice));

    [Fact]
    public void Deal_is_two_player_cards_then_one_dealer_card()
    {
        var g = Dealt(5, 6, 9);
        Assert.Equal(new[] { 5, 6 }, g.PlayerHands[0].Cards);
        Assert.Equal(new[] { 9 }, g.Dealer.Cards);
        Assert.Equal(Phase.Player, g.Phase);
        Assert.False(g.NeedsCard);
        Assert.True(g.CanDouble);
        Assert.False(g.CanSplit);
    }

    [Fact]
    public void Unexpected_or_out_of_range_rolls_are_refused()
    {
        var g = new BlackjackGame();
        Assert.False(g.AddCard(5)); // idle
        g.Deal();
        Assert.False(g.AddCard(0));
        Assert.False(g.AddCard(11));
        var d = Dealt(5, 6, 9);
        Assert.False(d.AddCard(4)); // waiting for a decision, not a card
    }

    [Fact]
    public void Hit_to_bust_settles_without_dealer_draws()
    {
        var g = Dealt(10, 6, 9);
        g.Hit();
        Assert.True(g.NeedsCard);
        g.AddCard(10);
        Assert.Equal(Phase.Done, g.Phase);
        Assert.Single(g.Dealer.Cards);
        Assert.Equal(-1, g.Net);
    }

    [Fact]
    public void Stand_makes_dealer_draw_to_17_then_settles()
    {
        var g = Dealt(10, 9, 5);
        g.Stand();
        Assert.Equal(Phase.Dealer, g.Phase);
        g.AddCard(6);  // 11
        Assert.Equal(Phase.Dealer, g.Phase);
        g.AddCard(7);  // 18
        Assert.Equal(Phase.Done, g.Phase);
        Assert.Equal(1, g.Net);
    }

    [Fact]
    public void Dealer_stands_on_soft_17()
    {
        var g = Dealt(10, 8, 1);
        g.Stand();
        g.AddCard(6);  // A+6 = soft 17
        Assert.Equal(Phase.Done, g.Phase);
        Assert.Equal(1, g.Net);
    }

    [Fact]
    public void Double_takes_one_card_and_doubles_the_stake()
    {
        var g = Dealt(5, 6, 10);
        g.Double();
        g.AddCard(10); // 21
        Assert.Equal(Phase.Dealer, g.Phase);
        Assert.False(g.CanHit);
        g.AddCard(8);  // dealer 18
        Assert.Equal(2, g.Net);
    }

    [Fact]
    public void Natural_pays_three_to_two_and_skips_the_decision()
    {
        var g = Dealt(1, 10, 9);
        Assert.Equal(Phase.Dealer, g.Phase);
        g.AddCard(9);  // 18
        Assert.Equal(1.5m, g.Net);
    }

    [Fact]
    public void Natural_against_dealer_natural_pushes()
    {
        var g = Dealt(1, 10, 10);
        g.AddCard(1);
        Assert.Equal(0, g.Net);
    }

    [Fact]
    public void Split_plays_two_hands_and_21_after_split_is_not_a_natural()
    {
        var g = Dealt(8, 8, 10);
        Assert.True(g.CanSplit);
        g.Split();
        Assert.Equal(2, g.PlayerHands.Count);
        Assert.True(g.NeedsCard);
        g.AddCard(3);  // hand 1: 8+3 = 11
        Assert.False(g.CanSplit);
        g.Double();
        g.AddCard(10); // hand 1: 21, doubled
        Assert.Equal(1, g.ActiveIndex);
        Assert.True(g.NeedsCard);
        g.AddCard(9);  // hand 2: 17
        g.Stand();
        g.AddCard(10); // dealer 20
        Assert.Equal(Phase.Done, g.Phase);
        Assert.Equal(2 - 1, g.Net); // doubled 21 wins 2, 17 loses 1
    }

    [Fact]
    public void Every_round_ends_within_a_bounded_number_of_rolls()
    {
        // Exhaustive over random play: no state can ask for rolls forever.
        var rng = new Random(1234);
        for (var round = 0; round < 20000; round++)
        {
            var g = new BlackjackGame();
            g.Deal();
            var rolls = 0;
            while (g.Phase != Phase.Done)
            {
                if (g.NeedsCard) { Assert.True(g.AddCard(rng.Next(1, 11))); rolls++; }
                else
                {
                    switch (rng.Next(4))
                    {
                        case 0: g.Hit(); break;
                        case 1: g.Stand(); break;
                        case 2: if (g.CanDouble) g.Double(); else g.Stand(); break;
                        default: if (g.CanSplit) g.Split(); else g.Hit(); break;
                    }
                }

                Assert.True(rolls < 60, "round did not finish");
            }

            Assert.Equal(g.PlayerHands.Count, g.Results.Count);
        }
    }

    [Fact]
    public void Dealer_hits_soft_17_when_the_rule_is_on()
    {
        var g = new BlackjackGame(new Rules { DealerHitsSoft17 = true });
        g.Deal();
        g.AddCard(10); g.AddCard(8); g.AddCard(1);
        g.Stand();
        g.AddCard(6);  // soft 17: must draw again
        Assert.Equal(Phase.Dealer, g.Phase);
        g.AddCard(2);  // soft 19
        Assert.Equal(Phase.Done, g.Phase);
        Assert.Equal(-1, g.Net);
    }

    [Fact]
    public void Hard_17_stands_even_with_hit_soft_17()
    {
        var g = new BlackjackGame(new Rules { DealerHitsSoft17 = true });
        g.Deal();
        g.AddCard(10); g.AddCard(8); g.AddCard(10);
        g.Stand();
        g.AddCard(7);
        Assert.Equal(Phase.Done, g.Phase);
    }

    [Fact]
    public void Double_after_split_can_be_turned_off()
    {
        var g = new BlackjackGame(new Rules { DoubleAfterSplit = false });
        g.Deal();
        g.AddCard(8); g.AddCard(8); g.AddCard(10);
        Assert.True(g.CanDouble);
        g.Split();
        g.AddCard(3);
        Assert.False(g.CanDouble);
        Assert.True(g.CanHit);
    }

    [Fact]
    public void Natural_payout_follows_the_rule()
    {
        var g = new BlackjackGame(new Rules { NaturalPays = 1.2m });
        g.Deal();
        g.AddCard(1); g.AddCard(10); g.AddCard(9);
        g.AddCard(9);
        Assert.Equal(1.2m, g.Net);
    }

    [Fact]
    public void Summary_is_one_line_for_the_round()
    {
        var g = Dealt(10, 9, 5);
        g.Stand();
        g.AddCard(10); g.AddCard(10); // dealer 25
        Assert.Equal("Blackjack: you 19 win | dealer bust | +1 unit(s)", g.Summary);
        Assert.True(Channels.IsSendable(g.Summary));
    }

    [Theory]
    [InlineData(RollChannel.Say, "/random 10", false)]
    [InlineData(RollChannel.Party, "/dice party 10", true)]
    [InlineData(RollChannel.Alliance, "/dice alliance 10", true)]
    [InlineData(RollChannel.Tell, "/random 10", false)]
    public void Roll_commands(RollChannel c, string roll, bool dice)
    {
        Assert.Equal(roll, Channels.RollCommand(c));
        Assert.Equal(dice, Channels.UsesDice(c));
    }

    [Fact]
    public void Only_say_tell_party_alliance_are_roll_channels_and_their_numbers_are_stable()
    {
        Assert.Equal(new[] { 0, 1, 2, 3 }, Enum.GetValues<RollChannel>().Select(c => (int)c));
        Assert.Equal(new[] { "Say", "Party", "Alliance", "Tell" }, Enum.GetNames<RollChannel>());
    }

    [Theory]
    [InlineData(ResultTargets.Say, "/s")]
    [InlineData(ResultTargets.Party, "/p")]
    [InlineData(ResultTargets.Alliance, "/a")]
    [InlineData(ResultTargets.Tell, "/tell Ann Lee@Gilgamesh")]
    [InlineData(ResultTargets.Echo, null)]
    public void Result_prefixes(ResultTargets t, string? prefix) =>
        Assert.Equal(prefix, Channels.ChatPrefix(t, " Ann Lee@Gilgamesh "));

    [Fact]
    public void Tell_prefix_is_withheld_without_a_valid_target() =>
        Assert.Null(Channels.ChatPrefix(ResultTargets.Tell, "Ann Lee"));

    [Theory]
    [InlineData("Ann Lee@Gilgamesh", true)]
    [InlineData("J'ann O'Lee-Ra@Cactuar", true)]
    [InlineData("Ann Lee", false)]
    [InlineData("Ann@Gilgamesh", false)]
    [InlineData("Ann Lee Smith@Gilgamesh", false)]
    [InlineData("Ann Lee@Gilgamesh /random 999", false)]
    [InlineData("Ann Lee@Gilgamesh\n/shout hi", false)]
    [InlineData("A Lee@Gilgamesh", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Tell_target_must_be_forename_surname_at_world(string? target, bool ok) =>
        Assert.Equal(ok, Channels.IsValidTellTarget(target));

    [Fact]
    public void Every_result_choice_has_a_label_and_is_a_single_flag()
    {
        foreach (var t in Channels.ResultChoices)
        {
            Assert.False(string.IsNullOrWhiteSpace(Channels.Label(t)));
            Assert.True(((int)t & ((int)t - 1)) == 0);
        }
    }

    [Theory]
    [InlineData(0, 1, RollChannel.Say, ResultTargets.Echo)]
    [InlineData(1, 2, RollChannel.Party, ResultTargets.Party)]
    [InlineData(2, 2, RollChannel.Alliance, ResultTargets.Alliance)]
    [InlineData(0, 2, RollChannel.Say, ResultTargets.Say)]
    [InlineData(0, 0, RollChannel.Say, ResultTargets.None)]
    [InlineData(3, 1, RollChannel.Say, ResultTargets.Echo)]   // v1 FreeCompany must not become Tell
    [InlineData(3, 2, RollChannel.Say, ResultTargets.Echo)]   // results went to FC, which no longer exists
    [InlineData(20, 2, RollChannel.Say, ResultTargets.Echo)]  // v1 CrossLinkshell8
    [InlineData(1, null, RollChannel.Party, ResultTargets.Echo)]
    public void Config_v1_migrates(int oldRoll, int? oldAnnounce, RollChannel roll, ResultTargets results) =>
        Assert.Equal((roll, results), Channels.MigrateV1(oldRoll, oldAnnounce));

    [Fact]
    public void Each_card_is_described_for_the_tell_relay()
    {
        var g = new BlackjackGame();
        g.Deal();
        g.AddCard(1);
        Assert.Equal("Card: A (you soft 11)", g.LastDraw);
        g.AddCard(6);
        Assert.Equal("Card: 6 (you soft 17)", g.LastDraw);
        g.AddCard(10);
        Assert.Equal("Dealer card: 10 (dealer 10)", g.LastDraw);
        g.Hit();
        g.AddCard(10);
        Assert.Equal("Card: 10 (you 17)", g.LastDraw);
        Assert.True(Channels.IsSendable(g.LastDraw));
    }

    [Fact]
    public void Split_cards_name_their_hand()
    {
        var g = Dealt(8, 8, 10);
        g.Split();
        g.AddCard(10);
        Assert.Equal("Card: 10 (hand 1: 18)", g.LastDraw);
    }

    [Theory]
    [InlineData("Ann Lee", "Ann Lee", true)]
    [InlineData("\uE090Ann Lee", "Ann Lee", true)] // party-slot glyph before the name
    [InlineData("Jo Ann Lee", "Ann Lee", false)]
    [InlineData("Ryann Lee", "Ann Lee", false)]
    [InlineData("Ann Leer", "Ann Lee", false)]
    [InlineData("Ann Lee", "", false)]
    public void Sender_match_is_exact_on_the_name(string sender, string me, bool expected) =>
        Assert.Equal(expected, Channels.SenderIs(System.Text.RegularExpressions.Regex.Unescape(sender), me));

    [Theory]
    [InlineData("Blackjack: you 19 win", true)]
    [InlineData("", false)]
    [InlineData("/random 10", false)]
    [InlineData("line\nbreak", false)]
    public void Only_plain_single_lines_are_sendable(string line, bool expected) =>
        Assert.Equal(expected, Channels.IsSendable(System.Text.RegularExpressions.Regex.Unescape(line)));
}
