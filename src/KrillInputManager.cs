using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// Polls the global keymaps every flight frame: group keys drive
	/// KrillActivation, set-jump keys call Vessel.SetGroupOverride directly. Also
	/// ticks the axis driver, the return ramps and any capture in progress.
	/// </summary>
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class KrillInputManager : MonoBehaviour
	{
		/// <summary>
		/// Safety nets for the scene ending mid-anything: release a capture's input
		/// lock, and send Deactivate for every Hold group still held by any source,
		/// on the vessel it was activated on.
		/// </summary>
		public void OnDestroy()
		{
			KrillCapture.ForceCancel();
			KrillAxisCapture.ForceCancel();
			KrillActivation.ReleaseAllHolds();
			KrillAxisSignal.Clear();
		}

		/// <summary>Extended-axis engine, physics-rate like stock's own axis groups. Paused during an axis capture: moving the stick then means "bind me".</summary>
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
			// Checked once per frame rather than per press: a held key on a locked save
			// would otherwise re-post the locked message every single frame.
			bool unlocked = KrillQuery.ExtendedGroupsUnlockedAnywhere();
			// Key polling stops while a KRILL text field has focus or anything outside
			// KRILL locks the keyboard. Releases below stay unconditional: a hold that
			// started before the lock must still end on its key-up.
			bool keysAllowed = KrillLocks.KeysAllowed();

			foreach (KeyValuePair<int, KrillBind> kv in KrillKeymap.Binds)
			{
				int group = kv.Key;
				KrillBind bind = kv.Value;
				bool held = bind.IsHeldWithModifiers();

				// Release first and regardless of the group's CURRENT kind: the press was
				// recorded under the kind/set current then, and the key-up must still end
				// the hold even if that group is now Pulse/Toggle in the active set.
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
					// SetGroupOverride already no-ops when the set is unchanged.
					v.SetGroupOverride(kv.Key);
				}
			}
		}
	}
}
