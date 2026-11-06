using System.Text.RegularExpressions;

namespace Blackjack14;

public enum Phase { Idle, Dealing, Player, Dealer, Done }

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
    public const string RollCommand = "/random 10";
    public const int DealerStandsOn = 17;

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
    public bool CanDouble => Deciding && Active.Cards.Count == 2;
    public bool CanSplit => Deciding && PlayerHands.Count == 1 && Active.Cards.Count == 2 && Active.Cards[0] == Active.Cards[1];

    public void Deal()
    {
        PlayerHands.Clear();
        PlayerHands.Add(new Hand());
        Dealer = new Hand();
        ActiveIndex = 0;
        Results.Clear();
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
                if (PlayerHands[0].Cards.Count < 2) PlayerHands[0].Cards.Add(roll);
                else Dealer.Cards.Add(roll);
                if (Dealer.Cards.Count == 1)
                {
                    Phase = Phase.Player;
                    if (Active.Total == 21) NextHand();
                }
                break;

            case Phase.Player:
                Active.Cards.Add(roll);
                cardOwed = false;
                if (Active.Doubled || Active.Total >= 21) NextHand();
                break;

            case Phase.Dealer:
                Dealer.Cards.Add(roll);
                if (Dealer.Total >= DealerStandsOn) Settle();
                break;
        }

        return true;
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

    private bool IsNatural(Hand h) => PlayerHands.Count == 1 && h.IsTwoCard21;

    private void Settle()
    {
        Phase = Phase.Done;
        var dealerNatural = Dealer.IsTwoCard21;
        for (var i = 0; i < PlayerHands.Count; i++)
        {
            var h = PlayerHands[i];
            var (text, net) = h switch
            {
                _ when h.IsBust => ("bust", -h.Stake),
                _ when IsNatural(h) && dealerNatural => ("push (both blackjack)", 0m),
                _ when IsNatural(h) => ("blackjack, pays 3:2", 1.5m * h.Stake),
                _ when dealerNatural => ("dealer blackjack", -h.Stake),
                _ when Dealer.IsBust => ("dealer bust, win", h.Stake),
                _ when h.Total > Dealer.Total => ("win", h.Stake),
                _ when h.Total == Dealer.Total => ("push", 0m),
                _ => ("lose", -h.Stake),
            };
            Net += net;
            Results.Add($"Hand {i + 1}: {h.Total} vs {Dealer.Total} - {text} ({net:+0.#;-0.#;0} unit)");
        }
    }

    // Own /random result, English client: "Random! You roll a 7 (out of 10)." (format confirmed in GamblePyon and ChatDeathRoll2).
    [GeneratedRegex(@"^Random! You roll a (\d+) \(out of 10\)\.$")]
    private static partial Regex OwnRollRegex();

    public static int? TryParseOwnRoll(string text)
    {
        var m = OwnRollRegex().Match(text.Trim());
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    public static string CardName(int roll) => roll switch { 1 => "A", 10 => "10", _ => roll.ToString() };
}
