# Changelog

## v0.2.0

- **Console indicator type**: a per-group, per-set footer selector (Info/Caution/Warning) declaring how the future flight console should color a group's cell — cosmetic only for now, no visible effect until the console itself exists. Unlike the actuation-kind selector, available for stock groups 1-10 too, since they'll appear on the console grid alongside extended groups.
- **A group now reports a proper on/off signal** to other mods and to the flight console, instead of exposing KRILL's internal bookkeeping. Every kind produces a plain on/off level, so a reader needs no special handling per kind: a **Pulse** group reads as on for 750 ms after it fires and then returns to off by itself, a **Toggle** group reads its persisted state, and a **Hold** group reads on while the key or button is held. Previously all three exposed the raw internal flag that KRILL flips on every press to decide the next direction — which for a Pulse group alternates forever while corresponding to nothing real.
- **Hold groups reworked**: a Hold group is now on exactly while at least one of its sources (key, the window's Trigger button, later the console) is pressing it, and off the moment the last one lets go — holding the key and the button together and releasing one keeps it on. The state is never saved: a quicksave taken mid-press no longer comes back as a stuck-on group or fires a phantom deactivation on load, and closing the window, switching set, vessel or scene while pressing always releases cleanly. (The part itself keeps whatever state the save captured, exactly like stock's own Brakes held through a quicksave; the next press resyncs it.)
- **Fixed**: a Hold group driven from the window's Trigger button on a group with no key assigned stayed on forever after releasing the mouse.
- **Toggle "State" is now the player's own declaration**, independent of what the part last received: forcing it from the window no longer reverses the direction of the next press (previously the press after a forced state appeared to "do nothing" once).
- **Renamed** the "Switch" actuation kind to **"Pulse"**. A craft saved with the old name falls back to Pulse (the same behavior) with a one-line warning in the log. The old name described how it was triggered rather than what it does, and "pulse vs toggle" is a much clearer distinction to translate than "switch vs toggle".
- **Fixed**: the "State: 0/1" force-set button no longer appears for Hold-kind groups (0.1.3 regression). It's meaningless for Hold — there is no stored value to force, the signal is simply whether something is pressing right now.

## v0.1.3

- **Switch/Toggle/Hold actuation kind** for extended groups: a per-group, per-set footer selector declaring how the group behaves. Switch (default) behaves exactly as before. Toggle groups expose a real, meaningful persisted state through a new public read API for other mods (e.g. [KRAB](https://github.com/Rjoande/KRAB)) and through a manual "force state" control in the KRILL window, for resyncing after the real part state changes outside KRILL (another mod, ore the part's right-click menu) without firing the assigned action again. Hold groups stay active only while the key or the window's Trigger button is physically held, same idea as stock's own Brakes group.

## v0.1.2

- **IVA monitor support** via [MFD Extension](https://github.com/Rjoande/MFD-Extension) (bay D): if both mods are installed, KRILL gets a screen on any compatible multi-function display prop, reachable without leaving the cockpit. Purely additive: nothing changes if MFD Extension isn't installed.

## v0.1.1

- **Symmetric part selection**: picking a part that has symmetry counterparts (e.g. one of 4 landing legs placed with 4x symmetry) now selects and assigns the whole group at once. Hover during "+ Part" previews the entire group in cyan, the persistent selection highlights all of them in blue, and the window shows a single card for the group instead of one row per physical part.
- **Action picker hides already-assigned actions**: the "+ Action" list no longer offers an action that's already assigned to the selected part for the current group and set.

## v0.1.0 [First public release]

- **Unlimited virtual action groups** on top of the stock 10, sparse by design (a group only exists once it holds an assignment or a name), with a configurable visible-group cap (20-99, difficulty settings page).
- **Full set symmetry**: every extended group can be assigned independently per override set, identical to how stock groups 1-10 already behave across the 5 sets (Default + 4).
- **A single 3-column window** (Action Groups | Parts | Actions), usable in both the VAB/SPH and in flight, with a persistent part-selection highlight distinct from the picker's momentary hover.
- **Global player keymap with "press it now" capture**: keyboard keys and joystick buttons interchangeably, with layered modifier combinations for extended groups (stock groups 1-10 keep using their own native keybind slots, unaffected).
- **Non-blocking conflict warnings**, checked against both the KRILL keymap and every stock keybind, shown live for the currently selected group.
- **Set-jump keymap**: a dedicated key per override set that jumps directly to it from anywhere, independent of the currently active set (flight only).
- **Manual Trigger button**: fire any group immediately in flight, without its key.
- **Renaming**: groups 1-10 and extended groups alike, plus override set names (flight only; see Known limitations).
- **Groups 1-10 stay assigned from the stock Action Groups screen**; KRILL only manages their display name and keybind, never duplicating stock's own assignment UI.
- **Per-craft persistence** for every assignment (save/load, docking/undocking, symmetry parts), separate from the global, per-player keymap.
- **Toolbar button** (stock toolbar and Blizzy's Toolbar, via ToolbarControl).
- **Localization**: English and Italian.
