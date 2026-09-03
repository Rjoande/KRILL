using System;
using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	public enum KrillActivationResult
	{
		Locked,
		NoRootModule,
		/// <summary>Nothing to do: a Hold press on a group already held, or a release from a source that wasn't holding it.</summary>
		Unchanged,
		Activated,
		Deactivated,
	}

	/// <summary>
	/// The activation engine (M2), shared by every input path — keymap poll
	/// (KrillInputManager), the KRILL window's buttons, the future console — so
	/// there is exactly one place that touches a part's actions and one place
	/// that writes the reported signal (KrillSignal / the root part's persisted
	/// bools). Three entry points, one per way a group can be driven:
	///
	///   Fire        - Pulse/Toggle press (key edge or UI click). Mirrors stock's
	///                 ActionGroupList.ToggleGroup line for line (verified on
	///                 decompiled source): flip a persisted per-(set,group)
	///                 direction bit, invoke every BaseAction with the resolved
	///                 Activate/Deactivate. KSPActionGroup.None is the placeholder
	///                 group on the param — AGExt does the same for its virtual
	///                 groups, and nearly every KSPAction body only ever looks at
	///                 param.type anyway.
	///   HoldPress   - Hold-kind press from one source (key, window, console):
	///   HoldRelease   Activate when the group's level goes 0 -> 1, Deactivate
	///                 when it goes 1 -> 0, exactly like stock's own BRAKES
	///                 handling (FlightInputHandler.cs: SetGroup(true) on key
	///                 down, SetGroup(false) on key up). The level itself is the
	///                 set of sources currently pressing (KrillSignal), so the
	///                 signal changes in the same call that actuates — no
	///                 poller, no frame of lag, nothing persisted.
	///
	/// Signal vs direction (2026-09-02 rework, notes/kind-signal-analysis.md):
	/// the persisted direction bit (KrillGroupToggle) is PRIVATE bookkeeping —
	/// it only decides whether the next Fire sends Activate or Deactivate, for
	/// Pulse and Toggle alike, and nothing external reads it. What readers see
	/// is KrillQuery.GroupState.signal, kept per kind in its own storage:
	/// Pulse in a runtime timer, Toggle in a SEPARATE persisted bool
	/// (KrillGroupSignal, the one the player can force by hand), Hold in the
	/// live source set. Hold never touches the direction bit at all — there is
	/// nothing about a hold worth persisting.
	/// </summary>
	public static class KrillActivation
	{
		/// <summary>
		/// Fired after a real Activate/Deactivate (never on Locked/NoRootModule/
		/// Unchanged, nothing changed there) — the KRILL window subscribes so its
		/// footer stays live whether the group fired from a keypress or its own
		/// buttons, without either path needing to know the window exists.
		/// </summary>
		public static event Action<Vessel, int> GroupActivated;

		/// <summary>The set a press/release resolves against — the vessel's live override set, same resolution stock uses for its own groups.</summary>
		internal static int ActiveSet(Vessel v)
		{
			return GameSettings.ADDITIONAL_ACTION_GROUPS ? v.GroupOverride : 0;
		}

		/// <summary>
		/// Pulse/Toggle press. Flips the direction bit and actuates accordingly
		/// (stock parity: the part always sees alternating Activate/Deactivate,
		/// whatever the kind); then updates the kind's own signal — a Pulse
		/// starts its timer, a Toggle flips its persisted signal bool. Note the
		/// two Toggle bools are deliberately independent (user decision
		/// 2026-09-02): the signal is the player's declared meaning ("this reads
		/// as 1 to me"), the direction bit is what the part last received — a
		/// manual resync of the former must never change what the part gets next.
		/// </summary>
		public static KrillActivationResult Fire(Vessel v, int group)
		{
			if (!KrillQuery.ExtendedGroupsUnlockedAnywhere())
			{
				return Locked(group);
			}
			ModuleKrill root = RootModule(v);
			if (root == null)
			{
				return KrillActivationResult.NoRootModule;
			}
			int set = ActiveSet(v);
			bool direction = !root.GetToggleState(set, group);
			root.SetToggleState(set, group, direction);
			switch (root.GetActuationKind(set, group))
			{
				case KrillActuationKind.Pulse:
					KrillSignal.StartPulse(v, set, group);
					break;
				case KrillActuationKind.Toggle:
					root.SetToggleSignal(set, group, !root.GetToggleSignal(set, group));
					break;
			}
			return Apply(v, root, set, group, direction);
		}

		/// <summary>Hold press from one source. Actuates only on the 0 -> 1 edge of the group's level; a second source pressing an already-held group just joins it.</summary>
		public static KrillActivationResult HoldPress(Vessel v, int group, KrillHoldSource source)
		{
			if (!KrillQuery.ExtendedGroupsUnlockedAnywhere())
			{
				return Locked(group);
			}
			ModuleKrill root = RootModule(v);
			if (root == null)
			{
				return KrillActivationResult.NoRootModule;
			}
			int set = ActiveSet(v);
			if (!KrillSignal.AddSource(v, set, group, source))
			{
				return KrillActivationResult.Unchanged;
			}
			return Apply(v, root, set, group, true);
		}

		/// <summary>
		/// Hold release from one source. Deactivates on the 1 -> 0 edge only,
		/// and always where the press STARTED (the record's own vessel and set),
		/// not wherever the player is now. Never gated by the career lock: a
		/// source that managed to press can always release. A vessel that no
		/// longer exists simply has nothing left to deactivate.
		/// </summary>
		public static KrillActivationResult HoldRelease(int group, KrillHoldSource source)
		{
			if (!KrillSignal.RemoveSource(group, source, out KrillSignal.HoldRecord record, out bool found) || !found)
			{
				return KrillActivationResult.Unchanged;
			}
			ModuleKrill root = RootModule(record.vessel);
			if (root == null)
			{
				return KrillActivationResult.NoRootModule;
			}
			return Apply(record.vessel, root, record.set, record.group, false);
		}

		/// <summary>
		/// Scene teardown (KrillInputManager.OnDestroy): every group still held
		/// by any source gets its Deactivate now, on the vessel it was activated
		/// on, then all runtime signal state is dropped. Unlike stock's BRAKES
		/// (a transient control input the game never persists), a KRILL group's
		/// action might be something the craft file DOES remember (a light, a
		/// deployed part) — so the part must actually be told, not just forgotten.
		/// </summary>
		public static void ReleaseAllHolds()
		{
			List<KrillSignal.HoldRecord> held = KrillSignal.DrainHolds();
			for (int i = 0; i < held.Count; i++)
			{
				ModuleKrill root = RootModule(held[i].vessel);
				if (root != null)
				{
					Apply(held[i].vessel, root, held[i].set, held[i].group, false);
				}
			}
			KrillSignal.ClearPulses();
		}

		private static ModuleKrill RootModule(Vessel v)
		{
			return v?.rootPart?.FindModuleImplementing<ModuleKrill>();
		}

		private static KrillActivationResult Locked(int group)
		{
			Debug.LogFormat("[KRILL] group {0} triggered but extended groups are locked (career facility tier)", group);
			ScreenMessages.PostScreenMessage(
				"KRILL group " + group + " locked (upgrade VAB/SPH)", 3f, ScreenMessageStyle.UPPER_CENTER);
			return KrillActivationResult.Locked;
		}

		/// <summary>
		/// The one place that calls BaseAction.Invoke and raises GroupActivated.
		/// Touches no signal and no bookkeeping — each entry point above has
		/// already written whatever its kind keeps, so this can't drift from them.
		/// </summary>
		private static KrillActivationResult Apply(Vessel v, ModuleKrill root, int set, int group, bool activate)
		{
			KSPActionType actionType = activate ? KSPActionType.Activate : KSPActionType.Deactivate;
			KSPActionParam param = new KSPActionParam(KSPActionGroup.None, actionType);
			List<BaseAction> actions = KrillQuery.GetActions(v.parts, set, group);
			for (int i = 0; i < actions.Count; i++)
			{
				actions[i].Invoke(param);
			}
			Debug.LogFormat("[KRILL] group {0} set {1} -> {2} ({3} action(s))",
				group, set, activate ? "ON" : "OFF", actions.Count);
			GroupActivated?.Invoke(v, group);
			return activate ? KrillActivationResult.Activated : KrillActivationResult.Deactivated;
		}
	}
}
