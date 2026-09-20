KRILL - Kerbal Rebindable Inputs & Limitless groups
===================================================

A Kerbal Space Program mod that extends the stock action-group system past its
10-group limit transparently. Groups 11 and up work exactly like groups 1-10
always have: same 5 override sets, same activation semantics, same career
gate. If you already know how to use stock action groups, you already know how
to use KRILL. The same goes for controller axes: stock stops at four custom
axis groups, KRILL keeps counting.

What it does
------------

- Unlimited virtual action groups, on top of the stock 10 sparse by design (a
  group only exists once you actually assign something to it), with a
  visible-group cap you control from the difficulty settings page (20-99).
- Full symmetry with the 5 stock override sets (Default + 4): every extended
  group can be assigned differently per set, exactly like stock groups 1-10
  already are.
- A single 3-column window (Action Groups | Parts | Actions), usable in the
  VAB/SPH and in flight: pick a group, pick a part, pick an action. No
  scrolling through six different screens.
- A global player keymap with "press it now" capture: click Capture, press the
  key or joystick button you want, done. Works with keyboard keys and joystick
  buttons interchangeably, including layered modifier combinations for
  extended groups.
- Non-blocking conflict warnings: if a key is already in use by another KRILL
  bind or by a stock keybind, you're told exactly what — nothing is ever
  silently overwritten or blocked.
- Jump directly to any set with its own dedicated key, independent of
  whichever set happens to be active right now (stock's F6/F7 only step
  through sets one at a time).
- A manual Trigger button, to fire a group instantly in flight without
  touching its key at all (useful for testing a setup before binding it).
- Three actuation kinds per extended group (per set): Pulse (default — fires
  once per press, like any stock group), Toggle (keeps a persisted on/off
  state you can also set by hand from the window, e.g. to resync after
  changing a part from its right-click menu) and Hold (active only while the
  key or the Trigger button is physically held, like stock's own Brakes).
- A clean on/off signal for other mods: every extended group presents a plain
  on/off level. A Pulse reads on for 750 ms after firing, a Toggle its state,
  a Hold whether it's held right now. Works through a public read API
  (KrillQuery.GetGroupState), so a mod like KRAB can use a KRILL group as an
  input without knowing anything about kinds. A per-group Info/Caution/Warning
  label is also stored for the future flight console.
- Groups 1-10 stay exactly where you already assign them (the stock Action
  Groups screen): KRILL only adds naming and rebinding for them, it never
  duplicates or replaces that screen.
- Extended axis groups (A5 and up), for anything on your controller that is an
  axis rather than a button: robotic servos, engine thrust limiters,
  control-surface authority, light intensity. Any part field stock lets you
  put on a custom axis. Same window, same "+ Part" gesture (only parts with
  axis fields are offered), a "+ Field" list per part, symmetry handled for
  you, everything per craft and per override set. Each assigned field has its
  own Normal/Inverted, Absolute/Incremental and speed (20-300 %/s) settings,
  the three options stock has, so one axis can drive several fields in
  different ways.
- Two axis kinds: Spring returns to a rest position (-1, 0 or +1) when you let
  go of the stick; Fixed stays where you left it and the value is saved with
  the craft, like a throttle lever. A live value slider in the window follows
  the controller, or lets you set the axis by hand when no channel is bound.
- +/- keys for extended axes (0.3.1), next to the controller channel, like
  stock's own custom axes have: a keyboard key or joystick button (modifiers
  allowed) captured from the axis footer. What a held key does depends on the
  kind: a Spring axis snaps to ±1 and springs back when released (an optional
  travel time in the settings turns the snap into a ramp), a Fixed axis moves
  at a configurable speed and stays put, a trim you can set from a hat switch.
  A held key wins over the channel; on release the channel takes over again.
- Axis capture with a "move it now" gesture, a global per-player axis map that
  survives Unity renumbering your joysticks, conflict warnings against every
  stock axis binding, and a global dead zone setting (default 5 %). Delete
  during any capture removes the bind.
- Stock custom axes 1-4 appear as rows A1-A4: rename them, capture their
  channel from the KRILL window (it writes stock's own binding and leaves
  inversion, sensitivity and dead zone to the stock Input screen), watch their
  live value.
- An analog signal for other mods to match the on/off one:
  KrillQuery.GetAxisState(vessel, axis) gives the current level (-1..1) of any
  axis, stock 1-4 included.
- Every assignment travels with the craft (save/load, docking/undocking,
  symmetry parts) the same way stock action-group data does; the player keymap
  itself is global, shared across every save and vessel.
- Localization: English and Italian, full parity (every player-facing string
  in both).

Requirements
------------

- Kerbal Space Program 1.12.5
- ModuleManager (https://github.com/sarbian/ModuleManager)
- ToolbarControl (https://github.com/linuxgurugamer/ToolbarControl) (adds the
  KRILL button to the stock toolbar / Blizzy's Toolbar)
- The stock "Action group sets" general setting (Advanced Settings → General →
  ADDITIONAL_ACTION_GROUPS) must be enabled for the 5-set system to be
  available at all.

Installation
------------

Extract this archive's contents into your GameData folder, so you end up with
GameData/KRILL/.... Make sure ModuleManager and ToolbarControl are installed
alongside it.

Configuring a HOTAS with a lot of buttons
-----------------------------------------

KSP 1.12.x runs on Unity's legacy input system, which only recognizes 20
buttons per joystick device (and 20 axes per device, up to 11 devices —
checked in the game's compiled input table). This is a Unity limitation, not a
KSP or KRILL one. On a HOTAS with 60-100+ physical buttons across a stick,
throttle, and extension modules, anything past button #20 on a given device is
invisible to the game if bound as a native joystick button.

A few tested ways around it, using your device's own mapping software:

- Route extra buttons as keyboard emulation instead of native joystick
  buttons. Keyboard input draws from a much larger key pool, so this sidesteps
  the 20-button cap entirely. One caveat: Unity only exposes F1-F15 to KSP:
  the hidden Windows virtual keys F16-F24 exist at the OS level but have no
  Unity KeyCode, so they can't be bound in-game.
- Stack modifiers on an otherwise-unused key (Numpad digits are a good choice:
  stock KSP doesn't use them). Unity's input has no chord suppression, so
  Ctrl+Num1, Shift+Num1, Alt+Num1 and so on each fire independently as long as
  bare Num1 has no binding of its own... one bank of physical buttons
  multiplied by however many modifier combinations you use. Just watch out for
  Num Lock.
- Turn rotary encoders and other "analog-ish" controls into axes in the
  device's software and give them to KRILL's extended axes instead of
  consuming two buttons each (up/down). Twenty axes per device is a lot of
  room, and each encoder moved off the button table frees two of your twenty
  buttons.
- Layer a modifier chord onto an opposite-direction pair already in use (e.g.
  a chord that includes both W and S, already bound to pitch). Since W and S
  drive opposite directions on the same axis, holding both together cancels
  out to zero on that axis while the chord action still fires independently —
  a way to reclaim already-used keys instead of needing free ones. There can
  be a few milliseconds of asymmetric input before the cancellation lands,
  likely imperceptible under SAS but worth checking if you fly manually at
  high precision.

Known limitations & Future Plans
--------------------------------

- English and Italian only. Contributions are welcome (I personally accept
  AI-assisted translation, as long as they are human-reviewed before issuing).
- Override sets (1-4) are renamed from stock's own UI, not from KRILL: the
  KRILL window shows the set names on its tabs but has no rename field of its
  own (stock has nowhere to store those names before a vessel exists anyway).
- No in-flight console (button grid) yet: activation is by keybind or from the
  KRILL window's own Trigger button.
- A Hold-kind group interrupted by a save (quicksave, or leaving to the Space
  Center while still pressing) reads as off after loading, but the part itself
  keeps whatever state the save captured (the same thing happens with stock's
  Brakes held through a quicksave). The next press resyncs it.
- No integration into the stock Action Groups app or the editor's Actions
  screen (yet?): extended groups and axes are managed entirely from KRILL's
  own window.
- The +/- keys of the stock custom axes A1-A4 are shown in the KRILL window
  but are still captured in stock's own Input settings screen: KRILL captures
  their channel, not (yet) their keys. There is no mouse-wheel "stepped"
  source for any axis.
- A Spring axis's live position is never saved: after a load it sits at its
  rest position until the stick moves. A Fixed axis's value is saved with the
  craft.
- While the mouse is over the KRILL window, stock's own custom axes 1-4 stop
  reading the controller (stock pauses them whenever a UI holds the input
  lock, its own Input screen included); KRILL's extended axes keep following.
- Not a KRILL bug, but easy to blame on it: in the stock editor's axis
  options, the speed slider can show absurd percentages (e.g. 66600 %) on
  systems whose locale uses a comma as decimal separator. Stock parses its own
  speed table with the system culture. KRILL's own speed steps are stored as
  numbers and are unaffected.

License
-------

MIT. See LICENSE.txt.

Credits
-------

Author: Rjoande. Built with the help of Claude Code.
