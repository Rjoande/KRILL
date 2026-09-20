using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// Polls the global keymap every flight frame and drives KrillActivation
	/// (the actual engine, shared with the window's buttons and the console).
	/// Also polls the M4 set-jump keymap (KrillSetKeymap) the same way, calling
	/// Vessel.SetGroupOverride directly — jumping sets has no equivalent
	/// "engine" to share since it's a single stock call.
	///
	/// Hold-kind keys (2026-09-02 rework): the physical key is a LEVEL
	/// (Input.GetKey), the engine wants EDGES (HoldPress/HoldRelease). The
	/// conversion compares the live key state against whether the Key source
	/// is currently recorded for that group in KrillSignal — never against a
	/// persisted bit, never against what the UI is doing. That keeps the
	/// self-healing property the original design wanted (a key-up missed while
	/// the game lost focus is noticed the very next frame and released) without
	/// a reconciliation loop over a set of groups: only bound groups are ever
	/// looked at here, UI sources handle themselves through their own events.
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
		/// Safety nets for the scene ending mid-anything: don't leave a capture's
		/// input lock stuck (ForceCancel: immediate release, no PauseMenu race to
		/// protect against once the scene is tearing down), and release every
		/// Hold-kind group still held by ANY source — key, window or console —
		/// sending its Deactivate to the vessel it was activated on.
		/// </summary>
		public void OnDestroy()
		{
			KrillCapture.ForceCancel();
			KrillAxisCapture.ForceCancel();
			KrillActivation.ReleaseAllHolds();
			KrillAxisSignal.Clear();
		}

		/// <summary>Extended-axis engine, physics-rate like stock's own axis groups (see KrillAxisDriver). Paused while an axis capture is open: moving the stick then means "bind me", not "drive the servo".</summary>
		public void FixedUpdate()
		{
			if (KrillAxisCapture.IsCapturing)
			{
				return;
			}
			Vessel v = FlightGlobals.ActiveVessel;
			if (v == null || v.rootPart == null || !KrillQuery.ExtendedGroupsUnlockedAnywhere())
			{
				return;
			}
			ModuleKrill root = v.rootPart.FindModuleImplementing<ModuleKrill>();
			if (root != null)
			{
				KrillAxisDriver.Step(v, root, KrillActivation.ActiveSet(v));
			}
		}

		public void Update()
		{
			// Return ramps of released Spring axes: real time, every frame.
			KrillAxisSignal.Tick(Time.unscaledDeltaTime);

			if (KrillCapture.NeedsTick)
			{
				KrillCapture.Tick();
				return;
			}
			if (KrillAxisCapture.NeedsTick)
			{
				KrillAxisCapture.Tick();
				return;
			}

			Vessel v = FlightGlobals.ActiveVessel;
			if (v == null || v.rootPart == null)
			{
				return;
			}
			ModuleKrill root = v.rootPart.FindModuleImplementing<ModuleKrill>();
			int set = KrillActivation.ActiveSet(v);
			// Checked once per frame rather than per HoldPress: a held key on a
			// locked save would otherwise re-attempt (and re-post the locked
			// message) every single frame until released.
			bool unlocked = KrillQuery.ExtendedGroupsUnlockedAnywhere();
			// Key polling stops while a KRILL text field has focus or anything
			// outside KRILL locks the keyboard (2026-09-18, K1 — found while
			// gating the axis keys: a "k" typed into the name field used to fire
			// group K). Releases below stay unconditional: a hold that started
			// before the lock must still end on its key-up.
			bool keysAllowed = KrillLocks.KeysAllowed();

			foreach (KeyValuePair<int, KrillBind> kv in KrillKeymap.Binds)
			{
				int group = kv.Key;
				KrillBind bind = kv.Value;
				bool held = bind.IsHeldWithModifiers();

				// Release first, and regardless of the group's CURRENT kind: the
				// press was recorded under whatever kind/set was current then, and
				// if the player has since switched to a set where this group is
				// Pulse/Toggle, the Hold branch below would never run again for
				// it — the key-up must still end the hold where it started.
				if (!held && KrillSignal.HasSource(group, KrillHoldSource.Key))
				{
					KrillActivation.HoldRelease(group, KrillHoldSource.Key);
					continue;
				}

				KrillActuationKind kind = root != null ? root.GetActuationKind(set, group) : KrillActuationKind.Pulse;
				if (kind == KrillActuationKind.Hold)
				{
					if (unlocked && keysAllowed && held && !KrillSignal.HasSource(group, KrillHoldSource.Key))
					{
						KrillActivation.HoldPress(v, group, KrillHoldSource.Key);
					}
				}
				else if (keysAllowed && bind.Matches())
				{
					KrillActivation.Fire(v, group);
				}
			}

			foreach (KeyValuePair<int, KrillBind> kv in KrillSetKeymap.Binds)
			{
				if (keysAllowed && kv.Value.Matches())
				{
					// SetGroupOverride itself already no-ops if already on this set
					// (confirmed on decompiled Vessel.cs) — no need to guard here.
					v.SetGroupOverride(kv.Key);
				}
			}
		}
	}
}
