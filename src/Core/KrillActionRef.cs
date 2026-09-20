using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// Identity of a BaseAction on a known part: module name + occurrence among
	/// same-named modules + action name. The part is implicit (the ref lives in its
	/// ModuleKrill). Never an absolute module index — that is AGExt's old defect.
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
		/// Build a ref from a live action, or null if it has no resolvable owning
		/// module: actions reached through the part-level aggregate Part.Actions can
		/// carry a listParent with a null .module, so callers must check.
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
		/// Resolve back to a live BaseAction: exact match on module name + occurrence,
		/// else any same-named module that still has the action, logging the drift
		/// instead of silently losing the assignment.
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
