using System;
using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	public enum KrillActivationResult
	{
		Locked,
		NoRootModule,
		Activated,
		Deactivated,
	}

	/// <summary>
	/// The activation engine (M2), extracted to a shared static so both the
	/// keymap poll (KrillInputManager, key press) and the M3 UI (manual trigger
	/// button) invoke a KRILL group through the exact same path — one place to
	/// get the semantics right, no risk of the two drifting apart.
	///
	/// Mirrors stock's ActionGroupList.ToggleGroup line for line (verified on
	/// decompiled source): flip a persisted per-(set,group) bool, build a
	/// KSPActionParam with the resolved direction, invoke every BaseAction.
	/// KSPActionGroup.None is the placeholder group field on the param — AGExt
	/// does the same for its own virtual (non-bitmask) groups, confirmed via
	/// reflection, and the overwhelming majority of KSPAction method bodies
	/// never look at that field anyway (they act on param.type).
	///
	/// Toggle state lives on the vessel root part's ModuleKrill (design doc §5,
	/// "by convention on the vessel root" — the same rule already used for group
	/// display names) so it survives quicksave/quickload and scene changes
	/// exactly like stock's own ActionGroupList.
	/// </summary>
	public static class KrillActivation
	{
		/// <summary>
		/// Fired after a real Activate/Deactivate (never on Locked/NoRootModule,
		/// nothing changed there) — the KRILL window subscribes so its footer's
		/// State label stays live whether the group fired from a real keypress
		/// (KrillInputManager) or the UI's own Trigger button, without either path
		/// needing to know the window exists (2026-08-19: found by testing — the
		/// footer showed the right value only right after a manual force-set,
		/// because nothing ever told an open window a real activation had
		/// happened).
		/// </summary>
		public static event Action<Vessel, int> GroupActivated;

		/// <summary>Groups currently asserted "held" by a UI control (the Hold-kind Trigger button's press-and-hold, 2026-08-19) — merged with the physical key's own per-frame held state by KrillInputManager, so the two sources never fight over the same group (see HOLD design note on SetActive below).</summary>
		private static readonly HashSet<int> uiHeldGroups = new HashSet<int>();

		public static void SetUiHeld(int group, bool held)
		{
			if (held)
			{
				uiHeldGroups.Add(group);
			}
			else
			{
				uiHeldGroups.Remove(group);
			}
		}

		public static bool IsUiHeld(int group)
		{
			return uiHeldGroups.Contains(group);
		}

		/// <summary>Every group currently UI-held — KrillInputManager unions this with the keymap's own bound groups so a Hold-kind group with NO keybind (UI-only control) still gets polled, the same parity Switch/Toggle's Trigger already has.</summary>
		public static IEnumerable<int> UiHeldGroups => uiHeldGroups;

		/// <summary>Safety net for the UI hold source: called when the KRILL window is destroyed (closed, scene change) while a Hold-kind Trigger button might still be physically pressed — forces every UI-held group off instead of leaving the persisted bool stuck "on" (which, unlike stock's own transient BRAKES handling, KRILL would otherwise happily save to disk on the next quicksave).</summary>
		public static void ReleaseAllUiHeld(Vessel v)
		{
			if (uiHeldGroups.Count == 0)
			{
				return;
			}
			List<int> groups = new List<int>(uiHeldGroups);
			uiHeldGroups.Clear();
			if (v == null)
			{
				return;
			}
			for (int i = 0; i < groups.Count; i++)
			{
				SetActive(v, groups[i], false);
			}
		}

		public static KrillActivationResult Activate(Vessel v, int group)
		{
			if (!KrillQuery.ExtendedGroupsUnlockedAnywhere())
			{
				return Locked(group);
			}
			ModuleKrill rootData = v?.rootPart?.FindModuleImplementing<ModuleKrill>();
			if (rootData == null)
			{
				return KrillActivationResult.NoRootModule;
			}
			int set = GameSettings.ADDITIONAL_ACTION_GROUPS ? v.GroupOverride : 0;
			bool newState = !rootData.GetToggleState(set, group);
			return Apply(v, rootData, set, group, newState);
		}

		/// <summary>
		/// Forces a direction instead of flipping — the HOLD-kind engine. Mirrors
		/// stock's own BRAKES handling line for line (verified on decompiled
		/// FlightInputHandler.cs, 2026-08-19): GetKeyDown -&gt; SetGroup(true),
		/// GetKeyUp -&gt; SetGroup(false), never the ToggleGroup flip used by every
		/// other stock group. KrillInputManager drives this per-frame as a
		/// level-check (physical key currently held vs the persisted bool) rather
		/// than pure edge callbacks — self-healing if a key-up is ever missed
		/// (losing OS focus mid-hold, switching active vessel mid-hold), since the
		/// very next frame that notices the mismatch corrects it. The UI's Hold
		/// Trigger button (KrillUi.HoldButton) calls this directly instead, via
		/// SetUiHeld/IsUiHeld so it doesn't fight the physical-key resync loop for
		/// the same group.
		/// </summary>
		public static KrillActivationResult SetActive(Vessel v, int group, bool active)
		{
			if (!KrillQuery.ExtendedGroupsUnlockedAnywhere())
			{
				return Locked(group);
			}
			ModuleKrill rootData = v?.rootPart?.FindModuleImplementing<ModuleKrill>();
			if (rootData == null)
			{
				return KrillActivationResult.NoRootModule;
			}
			int set = GameSettings.ADDITIONAL_ACTION_GROUPS ? v.GroupOverride : 0;
			return Apply(v, rootData, set, group, active);
		}

		private static KrillActivationResult Locked(int group)
		{
			Debug.LogFormat("[KRILL] group {0} triggered but extended groups are locked (career facility tier)", group);
			ScreenMessages.PostScreenMessage(
				"KRILL group " + group + " locked (upgrade VAB/SPH)", 3f, ScreenMessageStyle.UPPER_CENTER);
			return KrillActivationResult.Locked;
		}

		/// <summary>Shared write+invoke+notify tail for both Activate (flip) and SetActive (forced direction) — one place that actually touches the persisted bool and calls BaseAction.Invoke, so the two entry points can never drift apart on what "activating" actually does.</summary>
		private static KrillActivationResult Apply(Vessel v, ModuleKrill rootData, int set, int group, bool newState)
		{
			rootData.SetToggleState(set, group, newState);

			KSPActionType actionType = newState ? KSPActionType.Activate : KSPActionType.Deactivate;
			KSPActionParam param = new KSPActionParam(KSPActionGroup.None, actionType);
			List<BaseAction> actions = KrillQuery.GetActions(v.parts, set, group);
			for (int i = 0; i < actions.Count; i++)
			{
				actions[i].Invoke(param);
			}
			Debug.LogFormat("[KRILL] group {0} set {1} -> {2} ({3} action(s))",
				group, set, newState ? "ON" : "OFF", actions.Count);
			GroupActivated?.Invoke(v, group);
			return newState ? KrillActivationResult.Activated : KrillActivationResult.Deactivated;
		}
	}
}
