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
	/// The one activation engine, shared by every input path: Fire is a Pulse or
	/// Toggle press, HoldPress/HoldRelease actuate on the 0-1 edges of a group's
	/// level. Only place that invokes a part's actions and writes the signal.
	/// </summary>
	public static class KrillActivation
	{
		/// <summary>
		/// Raised after a real Activate/Deactivate only. The window subscribes so its
		/// footer stays live whichever path fired the group, without either path
		/// needing to know the window exists.
		/// </summary>
		public static event Action<Vessel, int> GroupActivated;

		/// <summary>The set a press/release resolves against: the vessel's live override set, as stock resolves its own groups.</summary>
		internal static int ActiveSet(Vessel v)
		{
			return GameSettings.ADDITIONAL_ACTION_GROUPS ? v.GroupOverride : 0;
		}

		/// <summary>
		/// Pulse/Toggle press: flips the direction bit and actuates accordingly, so the
		/// part always sees alternating Activate/Deactivate; then updates the kind's
		/// own signal, which for a Toggle is a separate, player-forceable bool.
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
		/// Hold release from one source, on the 1 -> 0 edge only and always where the
		/// press STARTED, not where the player is now. Never gated by the career lock:
		/// a source that managed to press can always release.
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
		/// Scene teardown: every group still held gets its Deactivate now, on the
		/// vessel it was activated on — unlike stock's transient BRAKES, a KRILL group
		/// may drive something the craft file remembers, so the part must be told.
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
		/// The one place that calls BaseAction.Invoke and raises GroupActivated. Keeps
		/// no state of its own: each entry point has already written what its kind
		/// persists, so this can never drift from them.
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
