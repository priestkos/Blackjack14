using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Blackjack14;

public sealed class BlackjackWindow : Window
{
    private readonly Blackjack14Plugin plugin;

    public BlackjackWindow(Blackjack14Plugin plugin) : base("Blackjack###Blackjack14")
    {
        this.plugin = plugin;
        Size = new Vector2(320, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(260, 220), MaximumSize = new Vector2(800, 900) };
    }

    public override void Draw()
    {
        var game = plugin.Game;

        if (game.Phase == Phase.Idle)
        {
            ImGui.TextDisabled("Deal to start. Every card is a /random 10 from you.");
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

    private static void DrawHand(string label, Hand hand, bool active)
    {
        var cards = hand.Cards.Count == 0 ? "-" : string.Join(" ", hand.Cards.Select(BlackjackGame.CardName));
        var total = hand.Cards.Count == 0 ? "" : $"  = {(hand.IsSoft ? "soft " : "")}{hand.Total}{(hand.IsBust ? " BUST" : "")}";
        var stake = hand.Doubled ? "  (doubled)" : "";
        ImGui.TextUnformatted($"{(active ? "> " : "  ")}{label}: {cards}{total}{stake}");
    }

    private string Status(BlackjackGame game) => game.Phase switch
    {
        _ when plugin.Paused => "Paused: no roll result seen within 10 s.",
        _ when plugin.RollInFlight => "Rolling...",
        Phase.Idle => "Ready.",
        Phase.Dealing => "Dealing...",
        Phase.Player when game.NeedsCard => "Drawing...",
        Phase.Player => "Your move.",
        Phase.Dealer => $"Dealer draws to {BlackjackGame.DealerStandsOn}...",
        _ => "Round over.",
    };
}
