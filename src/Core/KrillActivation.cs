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
		public static KrillActivationResult Activate(Vessel v, int group)
		{
			if (!KrillQuery.ExtendedGroupsUnlockedAnywhere())
			{
				Debug.LogFormat("[KRILL] group {0} triggered but extended groups are locked (career facility tier)", group);
				ScreenMessages.PostScreenMessage(
					"KRILL group " + group + " locked (upgrade VAB/SPH)", 3f, ScreenMessageStyle.UPPER_CENTER);
				return KrillActivationResult.Locked;
			}

			ModuleKrill rootData = v?.rootPart?.FindModuleImplementing<ModuleKrill>();
			if (rootData == null)
			{
				return KrillActivationResult.NoRootModule;
			}

			int set = GameSettings.ADDITIONAL_ACTION_GROUPS ? v.GroupOverride : 0;
			bool newState = !rootData.GetToggleState(set, group);
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
			return newState ? KrillActivationResult.Activated : KrillActivationResult.Deactivated;
		}
	}
}
