# KRILL - Kerbal Rebindable Inputs & Limit-Less groups

A Kerbal Space Program mod that extends the stock action-group system past its 10-group limit transparently. Groups 11 and up work exactly like
groups 1-10 always have: same 5 override sets, same activation semantics, same career gate. If you already know how to use stock action groups, you already know how to use KRILL.

![KRILL logo](dev/header_KRILL1.png)

- **Unlimited virtual action groups**, on top of the stock 10 sparse by design (a group only exists once you actually assign something to it), with a visible-group cap you control from the difficulty settings page (20-99).
- **Full symmetry with the 5 stock override sets** (Default + 4): every extended group can be assigned differently per set, exactly like stock groups 1-10 already are.
- **A single 3-column window** (Action Groups | Parts | Actions), usable in the VAB/SPH and in flight: pick a group, pick a part, pick an action — no scrolling through six different screens.
- **A global player keymap with "press it now" capture** — click Capture, press the key or joystick button you want, done. Works with keyboard keys and joystick buttons interchangeably, including layered modifier combinations for extended groups.
- **Non-blocking conflict warnings**: if a key is already in use by another KRILL bind or by a stock keybind, you're told exactly what — nothing is ever silently overwritten or blocked.
- **Jump directly to any set** with its own dedicated key, independent of whichever set happens to be active right now (stock's F6/F7 only step through sets one at a time).
- **A manual Trigger button**, to fire a group instantly in flight without touching its key at all — useful for testing a setup before binding it.
- **Groups 1-10 stay exactly where you already assign them** (the stock Action Groups screen) — KRILL only adds naming and rebinding for them, it never duplicates or replaces that screen.
- Every assignment travels with the craft (save/load, docking/undocking, symmetry parts) the same way stock action-group data does; the player keymap itself is global, shared across every save and vessel.
- **Localization**: English and Italian, full parity (every player-facing string in both).

## Requirements

- Kerbal Space Program 1.12.5
- [ModuleManager](https://github.com/sarbian/ModuleManager)
- [ToolbarControl](https://github.com/linuxgurugamer/ToolbarControl) (adds the KRILL button to the stock toolbar / Blizzy's Toolbar)
- The stock **"Action group sets"** general setting (`Advanced Settings → General → ADDITIONAL_ACTION_GROUPS`) must be enabled for the 5-set system to be available at all — this is a base-game setting, not something KRILL adds.

## Installation

Copy the contents of this repository into your `GameData` folder, so you end up with `GameData/KRILL/...`. Make sure ModuleManager and ToolbarControl are installed alongside it.

## Configuring a HOTAS with a lot of buttons

KSP 1.12.x runs on Unity's legacy input system, which only recognizes **20 buttons and 8 axes per joystick device** — a Unity limitation, not a KSP or
KRILL one. On a HOTAS with 60-100+ physical buttons across a stick, throttle, and extension modules, anything past button #20 on a given device is invisible to the game if bound as a native joystick button.

A few tested ways around it, using your device's own mapping software:

- **Route extra buttons as keyboard emulation** instead of native joystick buttons. Keyboard input draws from a much larger key pool, so this sidesteps the 20-button cap entirely. One caveat: Unity only exposes `F1`-`F15` to KSP — the hidden Windows virtual keys `F16`-`F24` exist at the  OS level but have no Unity `KeyCode`, so they can't be bound in-game.
- **Stack modifiers on an otherwise-unused key** (Numpad digits are a good choice — stock KSP doesn't use them). Unity's input has no chord suppression, so `Ctrl+Num1`, `Shift+Num1`, `Alt+Num1` and so on each fire independently as long as bare `Num1` has no binding of its own — one bank of physical buttons multiplied by however many modifier combinations you use. Watch out for Num Lock: some emulation software sends a different key code depending on its state, which can silently break the binding — test with it both on and off, or force it on.
- **Layer a modifier chord onto an opposite-direction pair** already in use (e.g. a chord that includes both W and S, already bound to pitch). Since W and S drive opposite directions on the same axis, holding both together cancels out to zero on that axis while the chord action still fires independently — a way to reclaim already-used keys instead of needing free ones. There can be a few milliseconds of asymmetric input before the cancellation lands, likely imperceptible under SAS but worth checking if you fly manually at high precision.

## Known limitations & Future Plans

- English and Italian only — no other localizations yet.
- Renaming an override set (1-4) is only available in flight, not in the VAB/SPH: stock itself has nowhere to store that name before a vessel exists, so there's no editor-side equivalent to write it to.
- No in-flight HUD button grid yet — activation is by keybind or from the KRILL window's own Trigger button.
- No integration into the stock Action Groups app or the editor's Actions screen yet — extended groups are managed entirely from KRILL's own window.

## License

[MIT](LICENSE).

## Credits

Author: Rjoande. Built with the help of Claude Code.
