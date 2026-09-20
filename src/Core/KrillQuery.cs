using System.Collections.Generic;

namespace KRILL
{
	/// <summary>
	/// Read-side helpers over a set of parts: groups in use, live actions of a
	/// (set, group), display names, axis state. Takes any part list, so the same
	/// code serves flight and the editor. Also hosts the career gate.
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

		/// <summary>One assignment row: where it lives, the raw assignment (for removal) and the resolved action, if any.</summary>
		public class AssignmentEntry
		{
			public Part part;
			public ModuleKrill module;
			public KrillAssignment assignment;
			public BaseAction resolved;
		}

		/// <summary>Every assignment entry for (set, group), one per assigned action: a part with two actions in the group yields two entries.</summary>
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

		/// <summary>
		/// Distinct parts with an assignment in (set, group), collapsed one row per
		/// symmetry group. Membership is read fresh, so a group broken apart later
		/// just stops collapsing on the next rebuild.
		/// </summary>
		public static List<Part> GetAssignedParts(IList<Part> parts, int set, int group)
		{
			List<Part> assigned = new List<Part>();
			foreach (ModuleKrill m in Modules(parts))
			{
				List<KrillAssignment> asg = m.Data.assignments;
				for (int i = 0; i < asg.Count; i++)
				{
					if (asg[i].set == set && asg[i].group == group && !assigned.Contains(m.part))
					{
						assigned.Add(m.part);
						break;
					}
				}
			}
			return CollapseSymmetry(assigned);
		}

		/// <summary>Axis twin of GetAssignedParts: distinct parts with at least one axis-field assignment in (set, axis), one row per symmetry group.</summary>
		public static List<Part> GetAssignedAxisParts(IList<Part> parts, int set, int axis)
		{
			List<Part> assigned = new List<Part>();
			foreach (ModuleKrill m in Modules(parts))
			{
				List<KrillAxisAssignment> asg = m.Data.axisAssignments;
				for (int i = 0; i < asg.Count; i++)
				{
					if (asg[i].set == set && asg[i].axis == axis && !assigned.Contains(m.part))
					{
						assigned.Add(m.part);
						break;
					}
				}
			}
			return CollapseSymmetry(assigned);
		}

		private static List<Part> CollapseSymmetry(List<Part> assigned)
		{
			List<Part> result = new List<Part>();
			HashSet<Part> covered = new HashSet<Part>();
			for (int i = 0; i < assigned.Count; i++)
			{
				Part p = assigned[i];
				if (covered.Contains(p))
				{
					continue;
				}
				result.Add(p);
				foreach (Part sibling in GetSymmetryGroup(p))
				{
					covered.Add(sibling);
				}
			}
			return result;
		}

		/// <summary>
		/// A part plus its current symmetry counterparts, read live and never stored,
		/// as one logical unit. A part with no siblings returns just itself, so no
		/// caller needs a separate path for "one part" and "a symmetric set".
		/// </summary>
		public static List<Part> GetSymmetryGroup(Part part)
		{
			List<Part> group = new List<Part> { part };
			group.AddRange(part.symmetryCounterparts);
			return group;
		}

		/// <summary>
		/// Read-only view of a stock group's (1-10) live membership, resolved with the
		/// same method stock uses to decide activation. Iterates each module's own
		/// Actions: the part-level aggregate can yield actions with no owning module.
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

		/// <summary>First display name found for exactly (set, group), or null. Names are per set, with no inheritance between sets.</summary>
		public static string GetGroupName(IList<Part> parts, int set, int group)
		{
			foreach (ModuleKrill m in Modules(parts))
			{
				string name = m.Data.GetName(set, group);
				if (name != null)
				{
					return name;
				}
			}
			return null;
		}

		/// <summary>Axis twin of GetGroupName, over the separate axis-name list.</summary>
		public static string GetAxisName(IList<Part> parts, int set, int axis)
		{
			foreach (ModuleKrill m in Modules(parts))
			{
				string name = m.Data.GetAxisName(set, axis);
				if (name != null)
				{
					return name;
				}
			}
			return null;
		}

		/// <summary>One axis-field assignment row: where it lives, the raw assignment (for removal and option edits) and the resolved field.</summary>
		public class AxisFieldEntry
		{
			public Part part;
			public ModuleKrill module;
			public KrillAxisAssignment assignment;
			public BaseAxisField resolved;
		}

		/// <summary>Every axis-field assignment of (set, axis) across the given parts, one per field.</summary>
		public static List<AxisFieldEntry> GetAxisFieldEntries(IList<Part> parts, int set, int axis)
		{
			List<AxisFieldEntry> result = new List<AxisFieldEntry>();
			foreach (ModuleKrill m in Modules(parts))
			{
				List<KrillAxisAssignment> asg = m.Data.axisAssignments;
				for (int i = 0; i < asg.Count; i++)
				{
					if (asg[i].set != set || asg[i].axis != axis)
					{
						continue;
					}
					result.Add(new AxisFieldEntry
					{
						part = m.part,
						module = m,
						assignment = asg[i],
						resolved = asg[i].fieldRef.Resolve(m.part),
					});
				}
			}
			return result;
		}

		/// <summary>
		/// Axis fields a KRILL axis may drive on this part, by stock's own eligibility
		/// rule. Serves both the column-2 filter ("can this part take an axis at
		/// all?") and the column-3 picker.
		/// </summary>
		public static List<BaseAxisField> GetCandidateAxisFields(Part part)
		{
			List<BaseAxisField> result = new List<BaseAxisField>();
			if (part == null)
			{
				return result;
			}
			foreach (PartModule pm in part.Modules)
			{
				for (int i = 0; i < pm.Fields.Count; i++)
				{
					BaseAxisField f = pm.Fields[i] as BaseAxisField;
					if (f != null && KrillFieldRef.IsUsable(f))
					{
						result.Add(f);
					}
				}
			}
			return result;
		}

		public static bool HasAxisFields(Part part)
		{
			return GetCandidateAxisFields(part).Count > 0;
		}

		/// <summary>
		/// Read-only view of a stock custom axis's (A1-A4) live membership, the axis
		/// twin of GetStockActions: GetAxisGroup(set) is the per-set override mask
		/// stock consults, with no fallback between sets.
		/// </summary>
		public static List<BaseAxisField> GetStockAxisFields(IList<Part> parts, int set, KSPAxisGroup group)
		{
			List<BaseAxisField> result = new List<BaseAxisField>();
			if (parts == null)
			{
				return result;
			}
			for (int i = 0; i < parts.Count; i++)
			{
				foreach (PartModule pm in parts[i].Modules)
				{
					for (int j = 0; j < pm.Fields.Count; j++)
					{
						BaseAxisField f = pm.Fields[j] as BaseAxisField;
						if (f != null && (f.GetAxisGroup(set) & group) != 0)
						{
							result.Add(f);
						}
					}
				}
			}
			return result;
		}

		/// <summary>True if any part carries extended-axis data, in any set: decides whether the window's Axes section starts unfolded.</summary>
		public static bool AnyAxisData(IList<Part> parts)
		{
			foreach (ModuleKrill m in Modules(parts))
			{
				KrillPartData d = m.Data;
				if (d.axisAssignments.Count > 0 || d.axisNames.Count > 0 || d.axisSettings.Count > 0)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Same unlock rule as stock custom action groups, by delegating to the very
		/// method stock calls — so any difficulty option or mod override applies to
		/// KRILL groups identically.
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

		/// <summary>True if either facility has unlocked extended groups: at flight time nobody knows which editor assembled the vessel.</summary>
		public static bool ExtendedGroupsUnlockedAnywhere()
		{
			return ExtendedGroupsUnlocked(true) || ExtendedGroupsUnlocked(false);
		}

		/// <summary>
		/// Everything known about one (set, group), read the same way by the window and
		/// by other mods. Read `signal`, the plain 0/1 level; `active` is the private
		/// direction bit, not a state. Field names are a reflection contract.
		/// </summary>
		public readonly struct GroupState
		{
			public readonly KrillActuationKind kind;
			public readonly bool active;
			public readonly bool signal;

			public GroupState(KrillActuationKind kind, bool active, bool signal)
			{
				this.kind = kind;
				this.active = active;
				this.signal = signal;
			}
		}

		/// <summary>Scene-agnostic form (root part + resolved set) for the window itself. Null only if rootPart carries no KRILL data at all.</summary>
		public static GroupState? GetGroupState(Part rootPart, int set, int group)
		{
			ModuleKrill root = rootPart != null ? rootPart.FindModuleImplementing<ModuleKrill>() : null;
			if (root == null)
			{
				return null;
			}
			KrillActuationKind kind = root.GetActuationKind(set, group);
			bool active = root.GetToggleState(set, group);
			return new GroupState(kind, active, ReadSignal(root, rootPart.vessel, kind, set, group));
		}

		/// <summary>
		/// Picks the kind's own storage for the single 0/1 level readers consume. In
		/// the editor Pulse and Hold never read lit — nothing actuates there — while a
		/// Toggle's persisted signal still does, so a value forced before launch shows.
		/// </summary>
		private static bool ReadSignal(ModuleKrill root, Vessel v, KrillActuationKind kind, int set, int group)
		{
			switch (kind)
			{
				case KrillActuationKind.Pulse:
					return KrillSignal.IsPulsing(v, set, group);
				case KrillActuationKind.Hold:
					return KrillSignal.IsHeld(v, set, group);
				default:
					return root.GetToggleSignal(set, group);
			}
		}

		/// <summary>Public read API for other mods: resolves the vessel's active override set and delegates to the overload above.</summary>
		public static GroupState? GetGroupState(Vessel v, int group)
		{
			if (v == null)
			{
				return null;
			}
			return GetGroupState(v.rootPart, KrillActivation.ActiveSet(v), group);
		}

		/// <summary>
		/// Everything a reader needs about one extended axis: `value` IS the level in
		/// -1..1 whatever the kind, `kind` and `rest` are for display. Axes 1-4 report
		/// the same way. Field names are a reflection contract — don't rename them.
		/// </summary>
		public readonly struct AxisState
		{
			public readonly KrillAxisKind kind;
			public readonly int rest;
			public readonly float value;

			public AxisState(KrillAxisKind kind, int rest, float value)
			{
				this.kind = kind;
				this.rest = rest;
				this.value = value;
			}
		}

		/// <summary>Scene-agnostic form (root part + resolved set). Null without a KRILL module, or for axes 1-4 outside flight, where there is no control state.</summary>
		public static AxisState? GetAxisState(Part rootPart, int set, int axis)
		{
			if (axis < KrillAxes.FirstExtended)
			{
				// A1-A4 ARE stock's custom axes: the value is what FlightInputHandler wrote
				// into the control state this frame, the same number stock applies to the
				// fields. Flight only, and stock has no kind or rest, so they read Spring/0.
				Vessel v = rootPart != null ? rootPart.vessel : null;
				float[] custom = v != null && v.ctrlState != null ? v.ctrlState.custom_axes : null;
				if (custom == null || axis < 1 || axis > custom.Length)
				{
					return null;
				}
				return new AxisState(KrillAxisKind.Spring, 0, custom[axis - 1]);
			}
			ModuleKrill root = rootPart != null ? rootPart.FindModuleImplementing<ModuleKrill>() : null;
			if (root == null)
			{
				return null;
			}
			KrillAxisKind kind = root.GetAxisKind(set, axis);
			int rest = root.GetAxisRest(set, axis);
			float value;
			if (kind == KrillAxisKind.Fixed)
			{
				value = root.GetAxisValue(set, axis);
			}
			else if (!KrillAxisSignal.TryGet(rootPart.vessel, set, axis, out value))
			{
				value = rest;
			}
			return new AxisState(kind, rest, value);
		}

		/// <summary>Public read API for other mods: resolves the vessel's active override set and delegates to the overload above.</summary>
		public static AxisState? GetAxisState(Vessel v, int axis)
		{
			if (v == null)
			{
				return null;
			}
			return GetAxisState(v.rootPart, KrillActivation.ActiveSet(v), axis);
		}
	}
}
