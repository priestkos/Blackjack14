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

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IFramework framework;
    private readonly WindowSystem windowSystem = new("Blackjack14");
    private readonly BlackjackWindow window;
    private readonly Queue<string> outbox = new();

    public Configuration Config { get; }
    public BlackjackGame Game { get; }
    public bool Paused { get; private set; }
    public bool RollInFlight => sentAt != 0;

    private long sentAt;
    private long lastSentAt;
    private bool announced = true;

    public Blackjack14Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IClientState clientState,
        IPlayerState playerState,
        IFramework framework)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.clientState = clientState;
        this.playerState = playerState;
        this.framework = framework;

        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Normalize();
        Game = new BlackjackGame(Config.Rules);

        window = new BlackjackWindow(this);
        windowSystem.AddWindow(window);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        pluginInterface.UiBuilder.OpenConfigUi += window.OpenSettings;
        chatGui.ChatMessage += OnChatMessage;
        clientState.Logout += OnLogout;
        framework.Update += OnUpdate;

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Blackjack table. /bj config opens settings, /bj help the help tab.",
        });
    }

    public void Dispose()
    {
        commandManager.RemoveHandler(CommandName);
        framework.Update -= OnUpdate;
        clientState.Logout -= OnLogout;
        chatGui.ChatMessage -= OnChatMessage;
        pluginInterface.UiBuilder.OpenConfigUi -= window.OpenSettings;
        pluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        windowSystem.RemoveAllWindows();
    }

    public void SaveConfig() => pluginInterface.SavePluginConfig(Config);

    public void Deal()
    {
        Game.Deal();
        Paused = false;
        sentAt = 0;
        announced = false;
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
        announced = true;
        outbox.Clear();
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config": window.OpenSettings(); break;
            case "help": window.OpenHelp(); break;
            default: window.Toggle(); break;
        }
    }

    private void OnUpdate(IFramework _)
    {
        if (!clientState.IsLoggedIn) return;

        if (Game.Phase == Phase.Done && !announced)
        {
            announced = true;
            Announce();
        }

        var now = Environment.TickCount64;
        var spaced = now - lastSentAt >= Config.RollSpacingSeconds * 1000L;

        if (sentAt != 0)
        {
            if (now - sentAt > Config.RollTimeoutSeconds * 1000L)
            {
                Paused = true;
                sentAt = 0;
            }

            return;
        }

        if (!spaced) return;

        if (outbox.Count > 0)
        {
            if (Send(outbox.Peek())) { outbox.Dequeue(); lastSentAt = now; }
            return;
        }

        if (Paused || !Game.NeedsCard) return;
        if (!Send(Channels.RollCommand(Config.RollChannel))) return;
        sentAt = lastSentAt = now;
    }

    private void Announce()
    {
        var line = Game.Summary;
        switch (Config.Announce)
        {
            case AnnounceTarget.Echo:
                chatGui.Print(new XivChatEntry { Type = XivChatType.Echo, Message = line });
                break;
            case AnnounceTarget.RollChannel when Channels.IsSendable(line):
                outbox.Enqueue($"{Channels.ChatPrefix(Config.RollChannel)} {line}");
                break;
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        // Only a result we asked for is a card: a stray roll while idle never moves the game.
        if (sentAt == 0) return;

        var dice = Config.RollChannel != RollChannel.Say;
        if (dice)
        {
            if (!DiceKinds(Config.RollChannel).Contains(message.LogKind)) return;
            // Others roll in the same channel at a table: only our own /dice line is a card.
            if (!Channels.SenderIs(message.Sender.TextValue, playerState.CharacterName)) return;
        }
        else if (message.LogKind != XivChatType.RandomNumber)
        {
            return;
        }

        if (BlackjackGame.TryParseRoll(message.Message.TextValue, dice) is not { } roll) return;
        if (Game.AddCard(roll)) sentAt = 0;
    }

    // The chat types a /dice result lands in for each mode; Party includes cross-world party.
    private static XivChatType[] DiceKinds(RollChannel c) => c switch
    {
        RollChannel.Party => [XivChatType.Party, XivChatType.CrossParty],
        RollChannel.Alliance => [XivChatType.Alliance],
        RollChannel.FreeCompany => [XivChatType.FreeCompany],
        RollChannel.PvPTeam => [XivChatType.PvPTeam],
        RollChannel.Linkshell1 => [XivChatType.Ls1],
        RollChannel.Linkshell2 => [XivChatType.Ls2],
        RollChannel.Linkshell3 => [XivChatType.Ls3],
        RollChannel.Linkshell4 => [XivChatType.Ls4],
        RollChannel.Linkshell5 => [XivChatType.Ls5],
        RollChannel.Linkshell6 => [XivChatType.Ls6],
        RollChannel.Linkshell7 => [XivChatType.Ls7],
        RollChannel.Linkshell8 => [XivChatType.Ls8],
        RollChannel.CrossLinkshell1 => [XivChatType.CrossLinkShell1],
        RollChannel.CrossLinkshell2 => [XivChatType.CrossLinkShell2],
        RollChannel.CrossLinkshell3 => [XivChatType.CrossLinkShell3],
        RollChannel.CrossLinkshell4 => [XivChatType.CrossLinkShell4],
        RollChannel.CrossLinkshell5 => [XivChatType.CrossLinkShell5],
        RollChannel.CrossLinkshell6 => [XivChatType.CrossLinkShell6],
        RollChannel.CrossLinkshell7 => [XivChatType.CrossLinkShell7],
        RollChannel.CrossLinkshell8 => [XivChatType.CrossLinkShell8],
        _ => [],
    };

    private void OnLogout(int type, int code) => Reset();

    // Only plugin-composed text reaches here: a roll command or a summary checked by Channels.IsSendable.
    private static unsafe bool Send(string text)
    {
        var ui = UIModule.Instance();
        if (ui == null) return false;
        var utf8 = Utf8String.FromString(text);
        ui->ProcessChatBoxEntry(utf8);
        utf8->Dtor(true);
        return true;
    }
}
