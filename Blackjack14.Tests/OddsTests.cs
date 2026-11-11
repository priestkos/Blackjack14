using Xunit;
using Xunit.Abstractions;

namespace Blackjack14.Tests;

public class OddsTests(ITestOutputHelper output)
{
    private static readonly Rules Default = new();

    // Reference values from an independent Python implementation of the same model (dynamic programming, infinite deck).
    [Theory]
    [InlineData(false, true, 1.5, false, 0.01668)]
    [InlineData(true, true, 1.5, false, 0.03266)]
    [InlineData(false, false, 1.5, false, 0.01668)]
    [InlineData(false, true, 1.2, false, 0.02256)]
    [InlineData(false, true, 2.0, false, 0.00688)]
    [InlineData(false, true, 1.0, false, 0.02648)]
    [InlineData(false, true, 1.5, true, 0.00494)]
    [InlineData(true, true, 1.5, true, 0.00713)]
    [InlineData(false, false, 1.5, true, 0.00619)]
    public void House_edge_matches_the_reference(bool h17, bool das, double pays, bool deck, double edge) =>
        Assert.Equal(edge, Odds.For(new Rules { DealerHitsSoft17 = h17, DoubleAfterSplit = das, NaturalPays = (decimal)pays }, deck).HouseEdge, 5);

    [Fact]
    public void Dealer_bust_matches_the_reference()
    {
        Assert.Equal(0.2238, Odds.For(Default).DealerBustOverall, 4);
        Assert.Equal(0.2816, Odds.For(Default, deck: true).DealerBustOverall, 4);
        Assert.Equal(0.2304, Odds.For(new Rules { DealerHitsSoft17 = true }).DealerBustOverall, 4);
        double[] byUp = [0, 0.0692, 0.2275, 0.2383, 0.2533, 0.2735, 0.2785, 0.2551, 0.2337, 0.2142, 0.1947];
        for (var up = 1; up <= 10; up++) Assert.Equal(byUp[up], Odds.For(Default).DealerBust(up), 4);
    }

    [Fact]
    public void Dealer_results_are_a_probability_distribution()
    {
        foreach (var deck in new[] { false, true })
        for (var up = 1; up <= 10; up++)
            Assert.Equal(1.0, Odds.For(Default, deck).DealerResult(up).Sum(), 12);
    }

    [Fact]
    public void Simple_quantities()
    {
        var dice = Odds.For(Default);
        Assert.Equal(0.02, dice.Natural, 12);
        Assert.Equal(5.5, dice.CardAverage, 12);
        Assert.Equal(0.1, dice.BustOnHit(12), 12);
        Assert.Equal(0.5, dice.BustOnHit(16), 12);
        Assert.Equal(0.0, dice.BustOnHit(11), 12);
        Assert.Equal(8 / 13d, Odds.For(Default, deck: true).BustOnHit(16), 12); // 6-9 and the four ten-ranks
        Assert.Equal(2 * (1 / 13d) * (4 / 13d), Odds.For(Default, deck: true).Natural, 12);
    }

    // The Help text states these as facts about every rule setting, so prove them for every combination.
    [Fact]
    public void Chart_claims_hold_for_every_rule_setting()
    {
        foreach (var h17 in new[] { false, true })
        foreach (var das in new[] { false, true })
        foreach (var pays in new[] { 1m, 1.2m, 1.5m, 2m })
        {
            var o = Odds.For(new Rules { DealerHitsSoft17 = h17, DoubleAfterSplit = das, NaturalPays = pays });
            for (var up = 1; up <= 10; up++)
            {
                for (var t = 5; t <= 20; t++)
                {
                    var m = o.ChartMove(t, false, 0, up);
                    Assert.NotEqual(Move.Double, m);
                    if (t <= 11) Assert.Equal(Move.Hit, m);
                    if (t >= 18) Assert.Equal(Move.Stand, m);
                }

                for (var t = 13; t <= 20; t++) Assert.NotEqual(Move.Double, o.ChartMove(t, true, 0, up));
                Assert.Equal(Move.Split, o.ChartMove(0, false, 1, up)); // always split aces
                Assert.NotEqual(Move.Split, o.ChartMove(0, false, 10, up)); // never split tens
                for (var t = 19; t <= 20; t++) Assert.Equal(Move.Stand, o.ChartMove(t, true, 0, up));
            }
        }
    }

    // Plays the real game engine with the computed best moves: the average result must match the computed edge.
    [Theory]
    [InlineData(false, 400_000)]
    [InlineData(true, 400_000)]
    public void Simulation_of_the_real_game_matches_the_edge(bool h17, int rounds)
    {
        var rules = new Rules { DealerHitsSoft17 = h17 };
        var odds = Odds.For(rules);
        var rng = new Random(20261106);
        double sum = 0, sumSq = 0;
        for (var i = 0; i < rounds; i++)
        {
            var g = new BlackjackGame(rules);
            g.Deal();
            while (g.Phase != Phase.Done)
            {
                if (g.NeedsCard) { g.AddCard(rng.Next(1, 11)); continue; }
                switch (odds.BestMove(g.Active, g.Dealer.Cards[0], g.CanDouble, g.CanSplit))
                {
                    case Move.Hit: g.Hit(); break;
                    case Move.Stand: g.Stand(); break;
                    case Move.Double: g.Double(); break;
                    case Move.Split: g.Split(); break;
                }
            }

            var net = (double)g.Net;
            sum += net;
            sumSq += net * net;
        }

        var mean = sum / rounds;
        var sd = Math.Sqrt(sumSq / rounds - mean * mean);
        var se = sd / Math.Sqrt(rounds);
        output.WriteLine($"h17={h17}: simulated {mean:F5} +/- {se:F5} (sd {sd:F3}), computed {-odds.HouseEdge:F5}");
        Assert.InRange(mean, -odds.HouseEdge - 4 * se, -odds.HouseEdge + 4 * se);
        Assert.InRange(sd, Odds.RoundSpread - 0.03, Odds.RoundSpread + 0.03); // Help quotes this spread
    }

    // Help derives "soft 17 improves to 18-21" as 1 - BustOnHit(17) and "11 becomes 21" as the ten share: check both with real hands.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Help_derivations_match_the_hands_they_describe(bool deck)
    {
        var p = deck ? Odds.Deck : Odds.Dice;
        double soft17Improves = 0, elevenMakes21 = 0;
        for (var v = 1; v <= 10; v++)
        {
            var soft = new Hand(); soft.Cards.AddRange([1, 6, v]);
            if (soft.Total is >= 18 and <= 21) soft17Improves += p[v];
            var eleven = new Hand(); eleven.Cards.AddRange([5, 6, v]);
            if (eleven.Total == 21) elevenMakes21 += p[v];
        }

        Assert.Equal(soft17Improves, 1 - Odds.For(Default, deck).BustOnHit(17), 12);
        Assert.Equal(elevenMakes21, p[10], 12);
    }

    [Fact]
    public void Chart_has_every_row_and_column()
    {
        var chart = Odds.For(Default).Chart;
        Assert.Equal(6 + 8 + 10, chart.Count);
        Assert.All(chart, row => Assert.Equal(10, row.Moves.Length));
        Assert.Equal("Pair As", chart[14].Label);
    }

    [Fact]
    public void Double_after_split_changes_nothing_with_dice_but_does_with_a_deck()
    {
        Assert.Equal(Odds.For(new Rules { DoubleAfterSplit = true }).HouseEdge, Odds.For(new Rules { DoubleAfterSplit = false }).HouseEdge, 12);
        Assert.NotEqual(Odds.For(new Rules { DoubleAfterSplit = true }, true).HouseEdge, Odds.For(new Rules { DoubleAfterSplit = false }, true).HouseEdge, 6);
    }
}
