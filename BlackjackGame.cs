using System.Text.RegularExpressions;

namespace Blackjack14;

public enum Phase { Idle, Dealing, Player, Dealer, Done }

/// <summary>Table rules. Saved in the plugin config; only editable between rounds.</summary>
public sealed class Rules
{
    public bool DealerHitsSoft17 { get; set; }
    public bool DoubleAfterSplit { get; set; } = true;
    public decimal NaturalPays { get; set; } = 1.5m;
}

public sealed class Hand
{
    public List<int> Cards { get; } = new();
    public int Stake { get; set; } = 1;
    public bool Doubled { get; set; }

    // A roll of 1 is an ace: counted as 11 when that does not bust the hand.
    public int Total
    {
        get
        {
            var sum = Cards.Sum();
            return Cards.Contains(1) && sum + 10 <= 21 ? sum + 10 : sum;
        }
    }

    public bool IsSoft => Cards.Contains(1) && Cards.Sum() + 10 <= 21;
    public bool IsBust => Total > 21;
    public bool IsTwoCard21 => Cards.Count == 2 && Total == 21;
}

/// <summary>One round: the player's hand(s) against a dealer who draws to 17. Pure logic, no Dalamud.</summary>
public sealed partial class BlackjackGame
{
    public const int DealerStandsOn = 17;

    public Rules Rules { get; }

    public BlackjackGame(Rules? rules = null) => Rules = rules ?? new Rules();

    public Phase Phase { get; private set; } = Phase.Idle;
    public List<Hand> PlayerHands { get; } = new();
    public Hand Dealer { get; private set; } = new();
    public int ActiveIndex { get; private set; }
    public Hand Active => PlayerHands[ActiveIndex];
    public List<string> Results { get; } = new();
    public decimal Net { get; private set; }

    private bool cardOwed;

    /// <summary>True while the game is waiting for the next /random 10 result.</summary>
    public bool NeedsCard => Phase switch
    {
        Phase.Dealing or Phase.Dealer => true,
        Phase.Player => cardOwed || Active.Cards.Count < 2,
        _ => false,
    };

    private bool Deciding => Phase == Phase.Player && !NeedsCard;
    public bool CanHit => Deciding;
    public bool CanStand => Deciding;
    public bool CanDouble => Deciding && Active.Cards.Count == 2 && (PlayerHands.Count == 1 || Rules.DoubleAfterSplit);
    public bool CanSplit => Deciding && PlayerHands.Count == 1 && Active.Cards.Count == 2 && Active.Cards[0] == Active.Cards[1];

    public void Deal()
    {
        PlayerHands.Clear();
        PlayerHands.Add(new Hand());
        Dealer = new Hand();
        ActiveIndex = 0;
        Results.Clear();
        outcomes.Clear();
        LastDraw = "";
        Net = 0;
        cardOwed = false;
        Phase = Phase.Dealing;
    }

    public void Reset()
    {
        Deal();
        Phase = Phase.Idle;
    }

    public void Hit() { if (CanHit) cardOwed = true; }

    public void Stand() { if (CanStand) NextHand(); }

    public void Double()
    {
        if (!CanDouble) return;
        Active.Stake = 2;
        Active.Doubled = true;
        cardOwed = true;
    }

    public void Split()
    {
        if (!CanSplit) return;
        var second = new Hand();
        second.Cards.Add(Active.Cards[1]);
        Active.Cards.RemoveAt(1);
        PlayerHands.Add(second);
    }

    /// <summary>Feeds one roll (1-10) to whichever hand is waiting. Returns false when no card was expected.</summary>
    public bool AddCard(int roll)
    {
        if (!NeedsCard || roll is < 1 or > 10) return false;

        switch (Phase)
        {
            case Phase.Dealing:
                if (PlayerHands[0].Cards.Count < 2) Draw(PlayerHands[0], roll);
                else Draw(Dealer, roll);
                if (Dealer.Cards.Count == 1)
                {
                    Phase = Phase.Player;
                    if (Active.Total == 21) NextHand();
                }
                break;

            case Phase.Player:
                Draw(Active, roll);
                cardOwed = false;
                if (Active.Doubled || Active.Total >= 21) NextHand();
                break;

            case Phase.Dealer:
                Draw(Dealer, roll);
                if (DealerStands) Settle();
                break;
        }

        return true;
    }

    /// <summary>The last card dealt, as one line for the player, e.g. "Card: 7 (you 15)" or "Dealer card: A (dealer soft 17)".</summary>
    public string LastDraw { get; private set; } = "";

    private void Draw(Hand hand, int roll)
    {
        hand.Cards.Add(roll);
        var total = $"{(hand.IsSoft ? "soft " : "")}{hand.Total}{(hand.IsBust ? " bust" : "")}";
        LastDraw = hand == Dealer
            ? $"Dealer card: {CardName(roll)} (dealer {total})"
            : PlayerHands.Count > 1
                ? $"Card: {CardName(roll)} (hand {PlayerHands.IndexOf(hand) + 1}: {total})"
                : $"Card: {CardName(roll)} (you {total})";
    }

    private void NextHand()
    {
        cardOwed = false;
        if (ActiveIndex + 1 < PlayerHands.Count)
        {
            ActiveIndex++;
            return;
        }

        // Every hand bust: the dealer has nothing to beat, so no more rolls.
        if (PlayerHands.All(h => h.IsBust)) Settle();
        else Phase = Phase.Dealer;
    }

    private bool DealerStands =>
        Dealer.Total > DealerStandsOn || Dealer.Total == DealerStandsOn && !(Rules.DealerHitsSoft17 && Dealer.IsSoft);

    private bool IsNatural(Hand h) => PlayerHands.Count == 1 && h.IsTwoCard21;

    private void Settle()
    {
        Phase = Phase.Done;
        var dealerNatural = Dealer.IsTwoCard21;
        for (var i = 0; i < PlayerHands.Count; i++)
        {
            var h = PlayerHands[i];
            var (text, word, net) = h switch
            {
                _ when h.IsBust => ("bust", "bust", -h.Stake),
                _ when IsNatural(h) && dealerNatural => ("push (both blackjack)", "push", 0m),
                _ when IsNatural(h) => ("blackjack", "blackjack", Rules.NaturalPays * h.Stake),
                _ when dealerNatural => ("dealer blackjack", "lose", -h.Stake),
                _ when Dealer.IsBust => ("dealer bust, win", "win", h.Stake),
                _ when h.Total > Dealer.Total => ("win", "win", h.Stake),
                _ when h.Total == Dealer.Total => ("push", "push", 0m),
                _ => ("lose", "lose", -h.Stake),
            };
            Net += net;
            Results.Add($"Hand {i + 1}: {h.Total} vs {Dealer.Total} - {text} ({net:+0.#;-0.#;0} unit)");
            outcomes.Add($"{h.Total} {word}");
        }
    }

    private readonly List<string> outcomes = new();

    /// <summary>One chat line for the finished round, e.g. "Blackjack: you 19 win | dealer 18 | +1 unit".</summary>
    public string Summary =>
        $"Blackjack: you {string.Join(", ", outcomes)} | dealer {(Dealer.IsBust ? "bust" : Dealer.Total.ToString())} | {Net:+0.#;-0.#;0} unit(s)";

    // English client formats, each confirmed in both GamblePyon and ChatDeathRoll2:
    // /random 10 -> "Random! You roll a 7 (out of 10)." (RandomNumber channel, no sender)
    // /dice <mode> 10 -> "Random! (1-10) 7" (the chosen channel, sender = the roller)
    [GeneratedRegex(@"^Random! You roll a (\d+) \(out of 10\)\.$")]
    private static partial Regex OwnRandomRegex();

    [GeneratedRegex(@"^Random! \(1-10\) (\d+)$")]
    private static partial Regex DiceRegex();

    public static int? TryParseRoll(string text, bool dice)
    {
        var m = (dice ? DiceRegex() : OwnRandomRegex()).Match(text.Trim());
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    public static string CardName(int roll) => roll switch { 1 => "A", 10 => "10", _ => roll.ToString() };
}
