# Changelog

## [0.1.0] — First public release

### What it is

KRILL (Kerbal Rebindable Inputs & Limitless groups) extends the stock
action-group system past its 10-group limit, transparently: extended groups
work exactly like groups 1-10 already do — same 5 override sets, same
activation semantics, same career gate.

### Added

- **Unlimited virtual action groups** on top of the stock 10, sparse by
  design (a group only exists once it holds an assignment or a name), with a
  configurable visible-group cap (20-99, difficulty settings page).
- **Full set symmetry**: every extended group can be assigned independently
  per override set, identical to how stock groups 1-10 already behave across
  the 5 sets (Default + 4).
- **A single 3-column window** (Action Groups | Parts | Actions), usable in
  both the VAB/SPH and in flight, with a persistent part-selection highlight
  distinct from the picker's momentary hover.
- **Global player keymap with "press it now" capture**: keyboard keys and
  joystick buttons interchangeably, with layered modifier combinations for
  extended groups (stock groups 1-10 keep using their own native keybind
  slots, unaffected).
- **Non-blocking conflict warnings**, checked against both the KRILL keymap
  and every stock keybind, shown live for the currently selected group.
- **Set-jump keymap**: a dedicated key per override set that jumps directly
  to it from anywhere, independent of the currently active set — flight
  only, since it targets a live vessel's own state.
- **Manual Trigger button**: fire any group immediately in flight, without
  its key.
- **Renaming**: groups 1-10 and extended groups alike, plus override set
  names (flight only — see Known limitations).
- **Groups 1-10 stay assigned from the stock Action Groups screen**; KRILL
  only manages their display name and keybind, never duplicating stock's own
  assignment UI.
- **Per-craft persistence** for every assignment (save/load, docking/
  undocking, symmetry parts), separate from the global, per-player keymap.
- **Toolbar button** (stock toolbar and Blizzy's Toolbar, via
  ToolbarControl).
- **Localization**: English and Italian, full parity (every player-facing
  string in both).

### Known limitations

- English and Italian only — no other localizations yet.
- Renaming an override set (1-4) works in flight only — no editor-side
  equivalent exists in stock to write that name to before a vessel is
  launched.
- No in-flight HUD button grid yet.
- No integration into the stock Action Groups app or the editor's Actions
  screen yet.

### Requirements

KSP 1.12.5, ModuleManager, ToolbarControl, and the stock "Action group sets"
general setting (`ADDITIONAL_ACTION_GROUPS`) enabled.
