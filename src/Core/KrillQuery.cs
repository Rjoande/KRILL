using System.Collections.Generic;

namespace KRILL
{
	/// <summary>
	/// Read-side helpers over a set of parts: enumerate extended groups in use,
	/// resolve the live actions of a (set, group), look up display names. Works on
	/// any part list so the same code serves flight (vessel.parts) and the editor
	/// (EditorLogic.fetch.ship.parts). Also hosts the career gate, which is a pure
	/// delegation to the same stock check used for custom action groups.
	/// </summary>
	public static class KrillQuery
	{
		public static IEnumerable<ModuleKrill> Modules(IList<Part> parts)
		{
			if (parts == null)
			{
				yield break;
			}
			for (int i = 0; i < parts.Count; i++)
			{
				ModuleKrill m = parts[i].FindModuleImplementing<ModuleKrill>();
				if (m != null)
				{
					yield return m;
				}
			}
		}

		/// <summary>Live actions assigned to (set, group) across the given parts.</summary>
		public static List<BaseAction> GetActions(IList<Part> parts, int set, int group)
		{
			List<BaseAction> result = new List<BaseAction>();
			foreach (ModuleKrill m in Modules(parts))
			{
				List<KrillAssignment> asg = m.Data.assignments;
				for (int i = 0; i < asg.Count; i++)
				{
					if (asg[i].set != set || asg[i].group != group)
					{
						continue;
					}
					BaseAction ba = asg[i].actionRef.Resolve(m.part);
					if (ba != null)
					{
						result.Add(ba);
					}
				}
			}
			return result;
		}

		/// <summary>One assignment row for the M3 detail view: which part/module it lives on, the raw assignment (for removal), and the resolved action if any.</summary>
		public class AssignmentEntry
		{
			public Part part;
			public ModuleKrill module;
			public KrillAssignment assignment;
			public BaseAction resolved;
		}

		/// <summary>Every assignment entry for (set, group), one per assigned action (a part with two actions in the same group yields two entries) — the M3 detail view lists these directly, no extra part-then-action drill-down needed.</summary>
		public static List<AssignmentEntry> GetAssignmentEntries(IList<Part> parts, int set, int group)
		{
			List<AssignmentEntry> result = new List<AssignmentEntry>();
			foreach (ModuleKrill m in Modules(parts))
			{
				List<KrillAssignment> asg = m.Data.assignments;
				for (int i = 0; i < asg.Count; i++)
				{
					if (asg[i].set != set || asg[i].group != group)
					{
						continue;
					}
					result.Add(new AssignmentEntry
					{
						part = m.part,
						module = m,
						assignment = asg[i],
						resolved = asg[i].actionRef.Resolve(m.part),
					});
				}
			}
			return result;
		}

		/// <summary>Distinct parts with at least one assignment in (set, group), in first-seen order — the 3-column UI's middle column.</summary>
		public static List<Part> GetAssignedParts(IList<Part> parts, int set, int group)
		{
			List<Part> result = new List<Part>();
			foreach (ModuleKrill m in Modules(parts))
			{
				List<KrillAssignment> asg = m.Data.assignments;
				for (int i = 0; i < asg.Count; i++)
				{
					if (asg[i].set == set && asg[i].group == group && !result.Contains(m.part))
					{
						result.Add(m.part);
						break;
					}
				}
			}
			return result;
		}

		/// <summary>
		/// Read-only view of a STOCK group's (1-10) live membership, resolved via the
		/// SAME method stock itself uses to decide activation (BaseAction.GetActionGroup,
		/// confirmed on decompiled source — no fallback between sets). Iterates each
		/// module's own .Actions, never the part-level aggregate (M1 lesson: that
		/// aggregate can yield actions with no resolvable owning module).
		/// </summary>
		public static List<BaseAction> GetStockActions(IList<Part> parts, int set, KSPActionGroup group)
		{
			List<BaseAction> result = new List<BaseAction>();
			if (parts == null)
			{
				return result;
			}
			for (int i = 0; i < parts.Count; i++)
			{
				foreach (PartModule pm in parts[i].Modules)
				{
					foreach (BaseAction ba in pm.Actions)
					{
						if ((ba.GetActionGroup(set) & group) != 0)
						{
							result.Add(ba);
						}
					}
				}
			}
			return result;
		}

		/// <summary>First display name found for (set, group), honoring the set-0 inheritance; null if none.</summary>
		public static string GetGroupName(IList<Part> parts, int set, int group)
		{
			string inherited = null;
			foreach (ModuleKrill m in Modules(parts))
			{
				string exact = m.Data.GetName(set, group);
				if (exact == null)
				{
					continue;
				}
				// GetName already applies set-0 inheritance; prefer a module that has
				// the exact set entry over one that only inherited it.
				bool isExact = false;
				List<KrillGroupName> names = m.Data.names;
				for (int i = 0; i < names.Count; i++)
				{
					if (names[i].group == group && names[i].set == set)
					{
						isExact = true;
						break;
					}
				}
				if (isExact)
				{
					return exact;
				}
				if (inherited == null)
				{
					inherited = exact;
				}
			}
			return inherited;
		}

		/// <summary>
		/// Same unlock rule as stock custom action groups (VAB/SPH fully upgraded, or
		/// the "action groups always allowed" advanced option): delegates to the very
		/// method stock calls, so any difficulty option or mod override applies to
		/// KRILL groups identically. Verified on decompiled GameVariables (threshold
		/// editorNormLevel > 0.6 = Tier 3).
		/// </summary>
		public static bool ExtendedGroupsUnlocked(bool isVAB)
		{
			if (HighLogic.CurrentGame == null || GameVariables.Instance == null)
			{
				return true;
			}
			SpaceCenterFacility facility = isVAB
				? SpaceCenterFacility.VehicleAssemblyBuilding
				: SpaceCenterFacility.SpaceplaneHangar;
			float level = ScenarioUpgradeableFacilities.GetFacilityLevel(facility);
			return GameVariables.Instance.UnlockedActionGroupsCustom(level, isVAB);
		}

		/// <summary>
		/// True if EITHER facility has unlocked extended groups. Used at flight-time
		/// activation, where we don't know (and stock doesn't care) which editor
		/// scene originally assembled the active vessel.
		/// </summary>
		public static bool ExtendedGroupsUnlockedAnywhere()
		{
			return ExtendedGroupsUnlocked(true) || ExtendedGroupsUnlocked(false);
		}
	}
}
