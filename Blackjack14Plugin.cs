using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Blackjack14;

public sealed class Blackjack14Plugin : IDalamudPlugin
{
    private const string CommandName = "/bj";
    private const long RollSpacingMs = 2000; // at most one /random every 2 s
    private const long RollTimeoutMs = 10000; // no result seen in 10 s -> pause, never re-send on its own

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly WindowSystem windowSystem = new("Blackjack14");
    private readonly BlackjackWindow window;

    public BlackjackGame Game { get; } = new();
    public bool Paused { get; private set; }
    public bool RollInFlight => sentAt != 0;

    private long sentAt;
    private long lastSentAt;

    public Blackjack14Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IClientState clientState,
        IFramework framework)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.clientState = clientState;
        this.framework = framework;

        window = new BlackjackWindow(this);
        windowSystem.AddWindow(window);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        chatGui.ChatMessage += OnChatMessage;
        clientState.Logout += OnLogout;
        framework.Update += OnUpdate;

        commandManager.AddHandler(CommandName, new CommandInfo((_, _) => window.Toggle())
        {
            HelpMessage = "Open the Blackjack table.",
        });
    }

    public void Dispose()
    {
        commandManager.RemoveHandler(CommandName);
        framework.Update -= OnUpdate;
        clientState.Logout -= OnLogout;
        chatGui.ChatMessage -= OnChatMessage;
        pluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        windowSystem.RemoveAllWindows();
    }

    public void Deal()
    {
        Game.Deal();
        Paused = false;
        sentAt = 0;
    }

    public void Resume()
    {
        // shortcut: a result that arrives after the timeout but before this resume is ignored; a slower one could still
        // land as the next card. Upgrade to matching the chat Timestamp if that is ever seen in practice.
        Paused = false;
        sentAt = 0;
    }

    public void Reset()
    {
        Game.Reset();
        Paused = false;
        sentAt = 0;
    }

    private void OnUpdate(IFramework _)
    {
        if (Paused || !Game.NeedsCard || !clientState.IsLoggedIn) return;

        var now = Environment.TickCount64;
        if (sentAt != 0)
        {
            if (now - sentAt > RollTimeoutMs)
            {
                Paused = true;
                sentAt = 0;
            }

            return;
        }

        if (now - lastSentAt < RollSpacingMs) return;
        if (!SendRoll()) return;
        sentAt = lastSentAt = now;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        // Only a result we asked for is a card: a stray /random typed by hand while idle never moves the game.
        if (sentAt == 0 || message.LogKind != XivChatType.RandomNumber) return;
        if (BlackjackGame.TryParseOwnRoll(message.Message.TextValue) is not { } roll) return;
        if (Game.AddCard(roll)) sentAt = 0;
    }

    private void OnLogout(int type, int code) => Reset();

    // The command text is a constant, so nothing player-typed ever reaches the chat box through here.
    private static unsafe bool SendRoll()
    {
        var ui = UIModule.Instance();
        if (ui == null) return false;
        var text = Utf8String.FromString(BlackjackGame.RollCommand);
        ui->ProcessChatBoxEntry(text);
        text->Dtor(true);
        return true;
    }
}
