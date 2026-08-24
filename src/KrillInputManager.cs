using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// Polls the global keymap every flight frame and fires a matched group
	/// through KrillActivation (the actual engine, extracted so the M3 UI's
	/// manual Trigger button and this keypress path share one implementation).
	/// Also polls the M4 set-jump keymap (KrillSetKeymap) the same way, calling
	/// Vessel.SetGroupOverride directly — jumping sets has no equivalent "engine"
	/// to share since it's a single stock call, unlike group activation.
	///
	/// Also drives KrillCapture while a bind capture is in progress — the only
	/// thing that does so in flight (KrillWindow itself drives it too, but only
	/// while open; this keeps a capture alive even if the player closes the
	/// window mid-capture from elsewhere).
	/// </summary>
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class KrillInputManager : MonoBehaviour
	{
		/// <summary>
		/// Safety net: if the flight scene ends mid-capture (Esc to main menu,
		/// vessel switch, ...) don't leave the capture's input lock stuck. Uses
		/// ForceCancel (immediate release), not Cancel (delayed release) — there is
		/// no PauseMenu race to protect against once the scene itself is tearing
		/// down, and nothing would be left alive to run the normal delay out.
		///
		/// Also releases any Hold-kind group still physically held when the scene
		/// ends (2026-08-19): unlike stock's own BRAKES (a transient control input,
		/// never saved), KRILL's persisted bool WOULD happily save to disk stuck
		/// "on" if left uncorrected here — worth the extra care stock itself
		/// doesn't need.
		/// </summary>
		public void OnDestroy()
		{
			KrillCapture.ForceCancel();
			Vessel v = FlightGlobals.ActiveVessel;
			if (v != null && v.rootPart != null)
			{
				int set = GameSettings.ADDITIONAL_ACTION_GROUPS ? v.GroupOverride : 0;
				foreach (KeyValuePair<int, KrillBind> kv in KrillKeymap.Binds)
				{
					KrillQuery.GroupState? gs = KrillQuery.GetGroupState(v.rootPart, set, kv.Key);
					if (gs?.kind == KrillActuationKind.Hold && gs.Value.active)
					{
						KrillActivation.SetActive(v, kv.Key, false);
					}
				}
			}
		}

		public void Update()
		{
			if (KrillCapture.NeedsTick)
			{
				KrillCapture.Tick();
				return;
			}

			Vessel v = FlightGlobals.ActiveVessel;
			if (v == null || v.rootPart == null)
			{
				return;
			}
			int set = GameSettings.ADDITIONAL_ACTION_GROUPS ? v.GroupOverride : 0;

			// Union, not just KrillKeymap.Binds.Keys: a Hold-kind group with no
			// keybind at all (UI-Trigger-only control) still needs to be polled here
			// — the bug found in testing 2026-08-19 was exactly this gap: the old
			// code skipped the physical-key resync for a UI-held group but never
			// applied the UI's own assertion anywhere, so the Hold Trigger button did
			// nothing. Held is now the OR of both sources, computed fresh every
			// frame, so neither source ever has to "win" over the other.
			HashSet<int> groups = new HashSet<int>(KrillKeymap.Binds.Keys);
			groups.UnionWith(KrillActivation.UiHeldGroups);
			foreach (int group in groups)
			{
				KrillBind bind = KrillKeymap.Binds.TryGetValue(group, out KrillBind b) ? b : null;
				KrillQuery.GroupState? gs = KrillQuery.GetGroupState(v.rootPart, set, group);
				if (gs?.kind == KrillActuationKind.Hold)
				{
					bool held = (bind != null && bind.IsHeld()) || KrillActivation.IsUiHeld(group);
					if (held != gs.Value.active)
					{
						KrillActivation.SetActive(v, group, held);
					}
				}
				else if (bind != null && bind.Matches())
				{
					KrillActivation.Activate(v, group);
				}
			}
			foreach (KeyValuePair<int, KrillBind> kv in KrillSetKeymap.Binds)
			{
				if (kv.Value.Matches())
				{
					// SetGroupOverride itself already no-ops if already on this set
					// (confirmed on decompiled Vessel.cs) — no need to guard here.
					v.SetGroupOverride(kv.Key);
				}
			}
		}
	}
}
