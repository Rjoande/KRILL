using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// Identity of a single BaseAction on a known part.
	///
	/// The part itself is implicit: refs are stored inside that part's ModuleKrill,
	/// so the part identity travels with the part (this is what makes the scheme
	/// robust — no global part indices). Within the part, an action is identified by
	/// module name + occurrence index among same-named modules + action name.
	/// NEVER by the module's absolute position in the module list: that is the
	/// historical AGExt defect (pmIndex) that silently loses assignments whenever a
	/// mod update inserts or reorders modules.
	/// </summary>
	public class KrillActionRef
	{
		/// <summary>PartModule class name (PartModule.moduleName), e.g. "ModuleDeployableSolarPanel".</summary>
		public string module = string.Empty;

		/// <summary>Index among the part's modules with the SAME moduleName (0 for the first).
		/// Only a tiebreaker for the rare part with two identical modules.</summary>
		public int occurrence;

		/// <summary>BaseAction.name within the module, e.g. "ExtendPanelsAction".</summary>
		public string action = string.Empty;

		public const string NodeName = "KRILL_ACTION_REF";

		/// <summary>
		/// Build a ref from a live action, or null if the action has no resolvable
		/// owning module. Most actions reached via a PartModule's own .Actions list
		/// always have one, but actions reached via the part-level aggregate
		/// Part.Actions can carry a listParent with a null .module (no single owning
		/// module) — callers must not assume this always succeeds. Confirmed the
		/// hard way: a structural part's action list produced exactly this case,
		/// crashing the M1 self-test (NRE inside this method) before this guard.
		/// </summary>
		public static KrillActionRef FromAction(BaseAction ba)
		{
			if (ba == null || ba.listParent == null || ba.listParent.module == null || ba.listParent.part == null)
			{
				Debug.LogWarning("[KRILL] skipping action with no resolvable owning module"
					+ (ba != null ? " (" + ba.name + ")" : string.Empty));
				return null;
			}
			PartModule owner = ba.listParent.module;
			Part part = ba.listParent.part;
			int occ = 0;
			foreach (PartModule pm in part.Modules)
			{
				if (pm == owner)
				{
					break;
				}
				if (pm.moduleName == owner.moduleName)
				{
					occ++;
				}
			}
			return new KrillActionRef
			{
				module = owner.moduleName,
				occurrence = occ,
				action = ba.name
			};
		}

		/// <summary>
		/// Resolve back to a live BaseAction on the given part. Exact match first
		/// (module name + occurrence); if that module no longer has the action (a mod
		/// update changed the part), fall back to any same-named module that has it,
		/// logging the drift instead of silently losing the assignment.
		/// </summary>
		public BaseAction Resolve(Part part)
		{
			if (part == null || string.IsNullOrEmpty(module) || string.IsNullOrEmpty(action))
			{
				return null;
			}

			int occ = 0;
			BaseAction fallback = null;
			foreach (PartModule pm in part.Modules)
			{
				if (pm.moduleName != module)
				{
					continue;
				}
				BaseAction ba = FindAction(pm);
				if (ba != null)
				{
					if (occ == occurrence)
					{
						return ba;
					}
					if (fallback == null)
					{
						fallback = ba;
					}
				}
				occ++;
			}
			if (fallback != null)
			{
				Debug.LogWarningFormat(
					"[KRILL] action ref '{0}/{1}#{2}' resolved via fallback occurrence on part '{3}' (module layout changed?)",
					module, action, occurrence, part.partInfo != null ? part.partInfo.name : part.name);
			}
			return fallback;
		}

		private BaseAction FindAction(PartModule pm)
		{
			foreach (BaseAction ba in pm.Actions)
			{
				if (ba.name == action)
				{
					return ba;
				}
			}
			return null;
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("module", module);
			node.AddValue("occurrence", occurrence);
			node.AddValue("action", action);
		}

		/// <summary>Tolerant parse: returns null (and logs) instead of throwing on bad data.</summary>
		public static KrillActionRef Load(ConfigNode node)
		{
			KrillActionRef r = new KrillActionRef();
			if (!node.TryGetValue("module", ref r.module) || string.IsNullOrEmpty(r.module)
				|| !node.TryGetValue("action", ref r.action) || string.IsNullOrEmpty(r.action))
			{
				Debug.LogWarning("[KRILL] dropping malformed action ref node: " + node);
				return null;
			}
			node.TryGetValue("occurrence", ref r.occurrence);
			return r;
		}
	}
}
