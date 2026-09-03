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

		/// <summary>
		/// Distinct parts with at least one assignment in (set, group), in first-seen
		/// order, collapsed one row per symmetry group (2026-07-27: symmetric parts
		/// carry identical, independently-persisted copies of the same assignment —
		/// see GetSymmetryGroup — so they'd otherwise show up as separate rows for
		/// what the player experiences as one part). The first member encountered
		/// becomes the representative; its symmetryCounterparts are looked up FRESH
		/// here, never cached, so a group broken apart later just stops collapsing
		/// on the next rebuild — no stale-membership cleanup needed anywhere.
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
		/// A part plus its CURRENT symmetry counterparts (Part.symmetryCounterparts,
		/// read live — never stored), as one logical unit for assignment/removal/
		/// highlighting. A part with no symmetry siblings returns just itself, so
		/// every caller can treat "one part" and "a symmetric set of parts" the same
		/// way without a separate code path.
		/// </summary>
		public static List<Part> GetSymmetryGroup(Part part)
		{
			List<Part> group = new List<Part> { part };
			group.AddRange(part.symmetryCounterparts);
			return group;
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

		/// <summary>
		/// Everything known about one (set, group), returned together so there's
		/// never a reason to call ModuleKrill directly instead of this query —
		/// same struct, same fields, whether the caller is the KRILL window itself
		/// or an external mod (2026-08-19: unifying internal/external reading was
		/// an explicit request, not just "add an external API").
		///
		/// READ `signal` (2026-08-31, storage per kind settled 2026-09-02). It is
		/// the plain 0/1 level this group is currently presenting, already derived
		/// from `kind`, and it is what both KRAB and the KRILL console consume —
		/// identical code for both, with no kind-specific branch anywhere in the
		/// consumer:
		///   Pulse  -> 1 for KrillSignal.PulseSeconds after the group fires, then
		///             back to 0 on its own (a momentary contact). Runtime only.
		///   Toggle -> its own persisted bool (KrillGroupSignal), flipped per
		///             press, forceable by the player from the window.
		///   Hold   -> 1 while at least one source (key, window, console) is
		///             holding it (KrillSignal). Runtime only.
		///
		/// `active` is the PRIVATE BOOKKEEPING direction bit and is NOT a state
		/// reading: KRILL flips it on every Pulse/Toggle press purely to know
		/// which of Activate/Deactivate to send next. For a Pulse group bound to
		/// a one-shot action (a decoupler) it alternates forever while
		/// corresponding to no physical state whatsoever — reading it as a level
		/// is exactly the bug the signal split was created to end (found
		/// 2026-08-30 via KRAB's bridge, which was reading `active`). Kept in the
		/// struct because it is still the honest raw value, but external readers
		/// should have no reason to touch it. The field names are part of the
		/// reflection contract KRAB's KrillGroupBridge resolves by name — don't
		/// rename them.
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

		/// <summary>Scene-agnostic form, used by the KRILL window itself (root part + already-resolved active set, works in both editor and flight — see KrillWindow.RootPart/activeSet). Null only if rootPart carries no KRILL data at all.</summary>
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
		/// Picks the kind's own storage for the single 0/1 level readers consume
		/// (see KrillSignal for why each kind keeps it where it does). In the
		/// editor (no vessel) Pulse and Hold never read lit — nothing actuates
		/// there in the first place; a Toggle's persisted signal still does, so
		/// a value forced before launch shows up correctly.
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

		/// <summary>
		/// Public read API for other mods (and the console/MFD): resolves the
		/// vessel's CURRENTLY ACTIVE override set automatically (same resolution
		/// KrillActivation uses) and delegates to the overload above.
		/// </summary>
		public static GroupState? GetGroupState(Vessel v, int group)
		{
			if (v == null)
			{
				return null;
			}
			return GetGroupState(v.rootPart, KrillActivation.ActiveSet(v), group);
		}
	}
}
