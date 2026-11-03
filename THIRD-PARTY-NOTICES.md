# Third-party software

FM27 Live Editor includes or uses the following. Each keeps its own licence.

## BepInEx (optional in-game button)

The in-game button is set up with BepInEx 6 (bleeding-edge build for Unity IL2CPP), a free mod loader.
It's copied into your FM27 folder only when you choose **Settings → In-game button → Set up**.

- Licence: GNU Lesser General Public License v2.1. The full text is in `bepinex/BepInEx/LICENSE.txt`
  inside the app's install folder and in `BepInEx/LICENSE.txt` in your FM27 folder.
- Source: https://github.com/BepInEx/BepInEx
- BepInEx bundles UnityDoorstop, Il2CppInterop, HarmonyX, MonoMod, Mono.Cecil, Cpp2IL, AsmResolver,
  Iced, Dobby and a .NET runtime, each under its own open-source licence (MIT, LGPL or similar); see
  their projects for details.

## .NET

The editor's engine is a self-contained .NET 8 program. .NET is MIT licensed: https://github.com/dotnet/runtime

## Electron

The app window is built with Electron (MIT): https://github.com/electron/electron

## Research credits

Transfers build on findings from FMLibreEditor (https://github.com/tozmoz/FMLibreEditor) and fm-editor
(https://github.com/barak14/fm-editor), both open source (GPL-3.0).

FM27 Live Editor is a free fan-made tool. It isn't made by or affiliated with Sports Interactive or SEGA.
