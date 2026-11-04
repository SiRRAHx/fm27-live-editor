# FM27 Live Editor

A free live editor for **Football Manager 27**. Change players, clubs and
transfers in the career you have loaded, while FM is running.

> **Early access.** This is a test build. Back up your saves (the editor does this too, before your first
> edit each session) and expect rough edges. Please report anything odd.

## What it can do

- **Players:** attributes, CA/PA, positions, morale, condition, sharpness, reputation.
- **Contracts:** wage, end date, squad status, squad number, clauses.
- **Health:** heal injuries, lift bans.
- **Clubs:** finances, budgets, reputation, facilities, squad-wide morale and fitness.
- **Transfers:** move players between clubs (with a fee and new terms), swap two players, release a player.
- **Loans:** loan a player to any club (end date, wage share, monthly fee, future fee), edit a loan's
  terms, or end a loan early.

Edits only change the game while it runs. They're kept only if you **save in FM**.

## How it works

The editor runs beside FM27 and edits the career in the game's memory from outside, like other
live editors. Nothing in the game's files changes. Loans are made by FM's own loan code, so they behave
like loans agreed in the game.

**In-game button (optional):** Settings → In-game button sets up a *Live Editor* button on FM's main menu
and top bar. It uses BepInEx, a free mod loader. If FM ever refuses to start with mods, use **Turn off**
there and FM starts as normal; the editor keeps working.

## Install

1. Download `FM27-Live-Editor-Setup-<version>.exe` from [Releases](../../releases) and run it.
2. The installer asks where FM27 is (the folder with `fm.exe`). It usually finds it by itself; otherwise:
   - Steam: Steam > steamapps > common > Football Manager 27
   - Epic: Epic Games > Football Manager 27
   - Game Pass: XboxGames > Football Manager 27 > Content
3. Start FM27 and load your career. The editor connects by itself.

Windows may warn that the installer isn't signed: choose **More info → Run anyway**.

## Supported versions

- **Game Pass / Microsoft Store:** tested.
- **Steam and Epic:** supported, not yet confirmed by a tester.

The editor doesn't depend on one build of FM27: when FM starts, it finds what it needs in the game
itself. If it can't find something for sure (say, after a big FM update), editing stays off and the
log at `%LOCALAPPDATA%\FM27 Live Editor\engine.log` says what's missing; please attach it to an Issue.

## Building

- Engine: `dotnet publish engine -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o engine/bin/out`
- Button add-on: `dotnet build plugin -c Release -o plugin/bin/out` (needs BepInEx in your FM27 folder)
- BepInEx bundle: `node tools/bundle-bepinex.js "<FM27 Content folder>"`
- App: `cd app && npm install && npm run dist`

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for included software.

FM27 Live Editor is a free fan-made tool. It isn't made by or affiliated with Sports Interactive or SEGA.
