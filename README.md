# Blackjack14

Player-run blackjack in FFXIV, played with `/random 10`. The plugin rolls your cards, totals them, plays the dealer and settles the round. All you do is choose each move.

`/bj` · Dalamud API 15 · .NET 10

## What it does

- **Deals for you.** **Deal** rolls `/random 10` three times: two cards for you, then the dealer's up-card.
- **Reads the rolls back from chat.** Each `Random! You roll a N (out of 10).` result becomes a card. A roll of 1 is an Ace and counts 1 or 11, whichever is better. A roll of 10 is a face card.
- **Your moves are buttons.** **Hit** draws one card. **Stand** ends the hand. **Double** is offered only on your first two cards: it draws exactly one card at double stake and then stands. **Split** is offered only on a pair: it makes two hands, and each gets its next card rolled automatically.
- **Plays the dealer.** Once you stand, the dealer draws until reaching 17 or more, and stands on a soft 17. If every one of your hands has bust, the dealer rolls nothing.
- **Settles in units.** Each hand is scored win, lose or push at 1 unit, or 2 units if doubled. A natural (two-card 21) pays 3:2 and pushes against a dealer natural. A 21 made after a split counts as an ordinary 21, not a natural.

### What it is not meant to do

- **It does not roll by itself.** Nothing is rolled until you press Deal, Hit, Double or Split. One press rolls only the cards that move needs, and never more than one `/random` every 2 seconds.
- **It does not retry.** If no result appears within 10 seconds, rolling pauses until you press **Resume rolling**. It never sends a second `/random` on its own.
- **It does not handle gil, bets or trades.** Stakes are counted in units, and paying out is up to you.
- **It does not read anyone else's rolls.** Only your own `/random 10` results count as cards, so it cannot run a table where several players roll.
- **It does not choose your moves.** There is no strategy advice and no auto-play for your hand.

## How it works

| Piece | Mechanism |
| --- | --- |
| Rolling | Calls `UIModule.ProcessChatBoxEntry` from FFXIVClientStructs with the constant `/random 10`, on the framework thread. No signature of its own, no hook. |
| Reading | `IChatGui.ChatMessage`, `XivChatType.RandomNumber` (74), regex `^Random! You roll a (\d+) \(out of 10\)\.$`. Two plugins (GamblePyon, ChatDeathRoll2) agree on this format. |
| Gate | A result counts only while one of the plugin's own rolls is waiting for an answer. A `/random` you type yourself while idle changes nothing. |
| Pace | Rolls are at least 2 s apart. After 10 s with no result, rolling pauses. Logging out abandons the round. |

All of the round's logic lives in `BlackjackGame.cs`, which has no Dalamud dependency.

## Commands

| Command | Effect |
| --- | --- |
| `/bj` | Open or close the table window. |

## Build

Requires the .NET 10 SDK and the Dalamud dev distribution
(`%APPDATA%\XIVLauncher\addon\Hooks\dev`, or `DALAMUD_HOME`).

```
dotnet build -c Debug      # load bin/Debug/Blackjack14.dll as a dev plugin
dotnet build -c Release    # zip in bin/Release/Blackjack14/latest.zip
```

## Install (dev plugin)

1. `/xlsettings` → Experimental → Dev Plugin Locations → add `bin\Debug\Blackjack14.dll`.
2. `/xlplugins` → Dev Tools → enable **Blackjack14**.
3. `/bj` opens the table.

## Tests

The xUnit project compiles `BlackjackGame.cs` on its own, without Dalamud. It covers ace totals, the roll parser (it accepts only your own d10 rolls), the deal order, refusing unexpected rolls, bust, the dealer drawing to 17 including soft 17, double, naturals, and split. It also plays 20,000 random rounds to prove every round ends within a bounded number of rolls.

```
dotnet test Blackjack14.Tests    # 19 tests
```

## Limits

- **Not verified in game.** This description comes from the code and the offline tests, not from a live client.
- **English client only.** The roll regex matches the English message, so on JP, DE or FR clients no roll is recognised and rolling pauses after 10 s.
- **`/random` is visible to nearby players** in the /say radius. That is how the game works, and the plugin does not change it.
- **One split per round, and no re-split.** Split aces play on like any other split hand.
- **A very late result can be misread.** If a result arrives after the 10 s pause *and* after you press Resume, it can be taken as the next card.

## Layout

```
Blackjack14Plugin.cs    entry point: services, /bj, roll pacing, chat read, send
BlackjackGame.cs        pure round logic: hands, totals, moves, dealer, settlement, roll parser
BlackjackWindow.cs      the table window
Blackjack14.Tests/      xUnit tests over BlackjackGame.cs
```
