namespace Blackjack14;

public enum Move { Stand, Hit, Double, Split }

/// <summary>
/// Exact odds for this table: every card is drawn with fixed probabilities (rolls are independent, so there is no deck
/// to deplete), the dealer has one up-card and no hole card, and the plugin's own settlement rules apply.
/// Pure maths, no Dalamud; cached per rule set because the Help tab reads it every frame.
/// </summary>
public sealed class Odds
{
    /// <summary>/random 10: each value 1-10 is 10%.</summary>
    public static readonly double[] Dice = [0, .1, .1, .1, .1, .1, .1, .1, .1, .1, .1];

    /// <summary>A shuffled deck for comparison: A-9 are 1/13 each, 10/J/Q/K together 4/13.</summary>
    public static readonly double[] Deck = [0, 1 / 13d, 1 / 13d, 1 / 13d, 1 / 13d, 1 / 13d, 1 / 13d, 1 / 13d, 1 / 13d, 1 / 13d, 4 / 13d];

    private static readonly Dictionary<(bool, bool, decimal, bool), Odds> Cache = new();

    public static Odds For(Rules rules, bool deck = false)
    {
        var key = (rules.DealerHitsSoft17, rules.DoubleAfterSplit, rules.NaturalPays, deck);
        if (!Cache.TryGetValue(key, out var odds)) Cache[key] = odds = new Odds(rules, deck ? Deck : Dice);
        return odds;
    }

    private readonly double[] p;
    private readonly bool h17;
    private readonly bool das;
    private readonly double pays;
    private readonly double[][] dealer = new double[11][];
    private readonly Dictionary<(int, int, bool, bool), double> memo = new();

    public Odds(Rules rules, double[] cardOdds)
    {
        p = cardOdds;
        h17 = rules.DealerHitsSoft17;
        das = rules.DoubleAfterSplit;
        pays = (double)rules.NaturalPays;
        for (var up = 1; up <= 10; up++) dealer[up] = DealerFrom(up);
        HouseEdge = -PlayerEdgeSum();
    }

    /// <summary>Expected loss per unit staked with best play (0.0167 = 1.67%).</summary>
    public double HouseEdge { get; }

    public double Natural => 2 * p[1] * p[10];

    public double CardAverage => Enumerable.Range(1, 10).Sum(v => v * p[v]);

    /// <summary>Dealer final result from one up-card: [0..4] = 17..21, [5] = bust, [6] = natural.</summary>
    public double[] DealerResult(int up) => dealer[up];

    public double DealerBust(int up) => dealer[up][5];

    public double DealerBustOverall => Enumerable.Range(1, 10).Sum(up => p[up] * dealer[up][5]);

    /// <summary>Chance one more card busts a hard total.</summary>
    public double BustOnHit(int hardTotal) => Enumerable.Range(1, 10).Where(v => hardTotal + v > 21).Sum(v => p[v]);

    public static int Total(int hard, bool ace) => ace && hard + 10 <= 21 ? hard + 10 : hard;

    private double[] DealerFrom(int up)
    {
        var r = new double[7];
        Walk(up, up == 1, 1, 1.0);
        return r;

        void Walk(int hard, bool ace, int cards, double w)
        {
            var t = Total(hard, ace);
            if (cards == 2 && t == 21) { r[6] += w; return; }
            if (t > 21) { r[5] += w; return; }
            var soft = t != hard;
            if (t > 17 || t == 17 && !(h17 && soft)) { r[t - 17] += w; return; }
            for (var v = 1; v <= 10; v++) Walk(hard + v, ace || v == 1, cards + 1, w * p[v]);
        }
    }

    // Player stands on t against this up-card. A dealer natural beats every non-natural hand.
    private double Stand(int t, int up)
    {
        if (t > 21) return -1;
        var d = dealer[up];
        var ev = d[5] - d[6];
        for (var k = 17; k <= 21; k++) ev += Math.Sign(t - k) * d[k - 17];
        return ev;
    }

    private double HitEv(int hard, bool ace, int up) =>
        Enumerable.Range(1, 10).Sum(v => p[v] * Best(hard + v, ace || v == 1, up, false));

    private double DoubleEv(int hard, bool ace, int up) =>
        2 * Enumerable.Range(1, 10).Sum(v => p[v] * Stand(Total(hard + v, ace || v == 1), up));

    // Best value of a hand that may hit, stand and (if allowed) double. 21 stands automatically, as in the game.
    private double Best(int hard, bool ace, int up, bool canDouble)
    {
        var t = Total(hard, ace);
        if (t > 21) return -1;
        if (t == 21) return Stand(21, up);
        if (memo.TryGetValue((hard, up, ace, canDouble), out var v)) return v;
        v = Math.Max(Stand(t, up), HitEv(hard, ace, up));
        if (canDouble) v = Math.Max(v, DoubleEv(hard, ace, up));
        return memo[(hard, up, ace, canDouble)] = v;
    }

    // One split hand: the pair card plus one new card; a 21 here is not a natural. No re-split.
    private double SplitEv(int card, int up) =>
        2 * Enumerable.Range(1, 10).Sum(v => p[v] * Best(card + v, card == 1 || v == 1, up, das));

    private double PlayerEdgeSum()
    {
        var sum = 0.0;
        for (var up = 1; up <= 10; up++)
        for (var a = 1; a <= 10; a++)
        for (var b = 1; b <= 10; b++)
        {
            var hard = a + b;
            var ace = a == 1 || b == 1;
            double ev;
            if (Total(hard, ace) == 21) ev = pays * (1 - dealer[up][6]);
            else
            {
                ev = Best(hard, ace, up, true);
                if (a == b) ev = Math.Max(ev, SplitEv(a, up));
            }

            sum += p[up] * p[a] * p[b] * ev;
        }

        return sum;
    }

    /// <summary>The move with the highest expected value. canSplit implies a two-card pair.</summary>
    public Move BestMove(Hand hand, int up, bool canDouble, bool canSplit)
    {
        var hard = hand.Cards.Sum();
        var ace = hand.Cards.Contains(1);
        var t = Total(hard, ace);
        var best = (Move.Stand, Stand(t, up));
        var hit = HitEv(hard, ace, up);
        if (hit > best.Item2) best = (Move.Hit, hit);
        if (canDouble && DoubleEv(hard, ace, up) is var dbl && dbl > best.Item2) best = (Move.Double, dbl);
        if (canSplit && SplitEv(hand.Cards[0], up) is var split && split > best.Item2) best = (Move.Split, split);
        return best.Item1;
    }

    /// <summary>One round's result swings about this much (standard deviation in units, measured by simulating the game).</summary>
    public const double RoundSpread = 0.98;

    /// <summary>Rounds after which the edge's expected cost equals one standard deviation of luck: (spread / edge)^2.</summary>
    public double RoundsUntilEdgeShows => HouseEdge > 0 ? Math.Pow(RoundSpread / HouseEdge, 2) : double.PositiveInfinity;

    private List<(string Label, Move[] Moves)>? chart;

    /// <summary>Best-play chart rows (hard 12-17, soft 13-20, pairs A-10); columns are dealer up-cards 2..10 then A.</summary>
    public IReadOnlyList<(string Label, Move[] Moves)> Chart => chart ??= BuildChart();

    public static readonly int[] ChartUpCards = [2, 3, 4, 5, 6, 7, 8, 9, 10, 1];

    private List<(string, Move[])> BuildChart()
    {
        var rows = new List<(string, Move[])>();
        for (var t = 12; t <= 17; t++) rows.Add(($"Hard {t}", ChartUpCards.Select(u => ChartMove(t, false, 0, u)).ToArray()));
        for (var t = 13; t <= 20; t++) rows.Add(($"Soft {t}", ChartUpCards.Select(u => ChartMove(t, true, 0, u)).ToArray()));
        for (var c = 1; c <= 10; c++)
            rows.Add(($"Pair {BlackjackGame.CardName(c)}s", ChartUpCards.Select(u => ChartMove(0, false, c, u)).ToArray()));
        return rows;
    }

    /// <summary>Best move for a two-card hand, for the Help chart: hard or soft total, or a pair of this card.</summary>
    public Move ChartMove(int total, bool soft, int pairCard, int up)
    {
        var hand = new Hand();
        if (pairCard > 0) hand.Cards.AddRange([pairCard, pairCard]);
        else if (soft) hand.Cards.AddRange([1, total - 11]);
        else hand.Cards.AddRange(total > 11 ? [10, total - 10] : [total - 2, 2]);
        return BestMove(hand, up, true, pairCard > 0);
    }
}
