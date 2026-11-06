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
    [InlineData("Random! You roll a 7 (out of 10).", 7)]
    [InlineData("Random! You roll a 10 (out of 10).", 10)]
    [InlineData("Random! You roll a 7 (out of 13).", null)]
    [InlineData("Random! Someone Else rolls a 7 (out of 10).", null)]
    [InlineData("Random! (1-10) 7", null)]
    public void Parses_only_own_d10_rolls(string text, int? expected) =>
        Assert.Equal(expected, BlackjackGame.TryParseOwnRoll(text));

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
}
