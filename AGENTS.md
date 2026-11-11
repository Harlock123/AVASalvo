# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

An artillery duel for two to four tanks, people or computer, in C# on .NET 10 and Avalonia 12, drawn with
Avalonia's `DrawingContext`. An original game in the genre. One self-contained executable per platform (Windows,
macOS and Linux, x64 and ARM). The graphics are drawn in code, and the sounds and music are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASalvo.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASalvo.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVASalvo          # play it; --no-audio for silence
SALVO_SHOTS=$PWD/Images dotnet test tests/AVASalvo.Tests --filter ScreenshotTests   # refresh the README's pictures
python3 tools/make_sounds.py              # rebuild src/AVASalvo/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASalvo/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, setup, demonstration, play, high scores), a match's rounds, whose
    turn it is, scoring and the fixed-step loop.
  - `World.cs` is the battlefield: the ground as a height per column, the tanks, the shots in flight, blasts and
    falls, and the weapons table. `Dice` is its seeded random source.
  - `Ai.cs` is the computer's gunner: it picks a target and searches angle and power with `Ai.Landing`.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASalvo/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame, scaled to the window,
  and runs the 60 Hz timer. `GameCanvas.Draw.cs` draws the field; `GameCanvas.Screens.cs` the panel and screens.
- `src/AVASalvo/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVASalvo.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay tests, and screenshot tests that
  draw the game in a headless window.
- `tools/make_sounds.py` writes the sounds.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` turns frame time into fixed 1/60 s steps (`World.Step`), and
  `World.Tick` advances the shots, blasts and falls by one. The field and its winds come from the world's
  `Dice`; the computer's misjudgements come from the engine's `Random`, seeded in tests.
- **The ground has no overhangs.** It's one height per column, so a hole under the surface lowers the column by
  the dirt it took out: the dirt above falls in. Keep `Carve` and `Pile` working on heights.
- **One flight.** `Ai.Landing` must fly a shot exactly as `World.Tick` does (gravity, `WindScale`, the muzzle,
  hitting tanks and ground), or the computer's aim drifts from what happens. A test holds them together.
- **Angles.** A barrel's angle is in degrees, 0 pointing right, 90 straight up and 180 left; screen y grows
  downward, so a shot's upward speed is negative. Right on the keys swings the barrel toward 0.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** Keys and the gamepad stick must agree, and up must mean up. Test new
  controls through `InputHandler`, not by setting `Controls` directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData`, `AVASalvo`); tests load the table from a fresh temporary folder and run with
  `Silent = true`.
- **Line endings.** The sources are LF; `app.manifest` is CRLF. Keep each file's existing line endings so diffs
  show only real changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
A release also asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
