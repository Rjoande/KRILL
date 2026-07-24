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
		/// </summary>
		public void OnDestroy()
		{
			KrillCapture.ForceCancel();
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
			foreach (KeyValuePair<int, KrillBind> kv in KrillKeymap.Binds)
			{
				if (kv.Value.Matches())
				{
					KrillActivation.Activate(v, kv.Key);
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
