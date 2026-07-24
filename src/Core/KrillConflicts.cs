using System.Collections.Generic;
using System.Reflection;

namespace KRILL
{
	/// <summary>
	/// Non-blocking conflict advisory for a candidate bind (design doc §4): stock
	/// warns, never blocks, so KRILL does the same — this only produces human-
	/// readable descriptions of what else already uses the candidate's primary key.
	/// Checks both the KRILL keymap and every stock KeyBinding on GameSettings
	/// (reflection: stock exposes no single "all keybindings" list).
	///
	/// Conflict heuristic: two binds conflict if they share the same PRIMARY key,
	/// regardless of modifiers — see KrillBind.Matches for why this is the correct
	/// direction to err in (a modifier-less bind fires through another bind's
	/// modifier being held).
	/// </summary>
	public static class KrillConflicts
	{
		/// <summary>
		/// excludeGroup/excludeSet: -1 means "nothing to exclude in that dimension"
		/// (never a valid group or set number, so it's a safe sentinel for both).
		/// A group candidate only needs to exclude itself from KrillKeymap and a set
		/// candidate only from KrillSetKeymap — the two dimensions can never collide
		/// with EACH OTHER (see KrillSetKeymap class doc), so both scans always run
		/// regardless of which kind of candidate this is.
		/// </summary>
		public static List<string> Describe(KrillBind candidate, int excludeGroup, int excludeSet = -1)
		{
			List<string> hits = new List<string>();
			if (candidate == null || candidate.IsNone)
			{
				return hits;
			}

			foreach (KeyValuePair<int, KrillBind> kv in KrillKeymap.Binds)
			{
				if (kv.Key == excludeGroup)
				{
					continue;
				}
				if (candidate.SharesPrimaryWith(kv.Value))
				{
					hits.Add("KRILL group " + kv.Key + " (" + kv.Value.Describe() + ")");
				}
			}

			foreach (KeyValuePair<int, KrillBind> kv in KrillSetKeymap.Binds)
			{
				if (kv.Key == excludeSet)
				{
					continue;
				}
				if (candidate.SharesPrimaryWith(kv.Value))
				{
					hits.Add("KRILL set-jump " + kv.Key + " (" + kv.Value.Describe() + ")");
				}
			}

			foreach (FieldInfo field in typeof(GameSettings).GetFields(BindingFlags.Public | BindingFlags.Static))
			{
				if (field.FieldType != typeof(KeyBinding))
				{
					continue;
				}
				KeyBinding kb = (KeyBinding)field.GetValue(null);
				if (kb == null)
				{
					continue;
				}
				if (kb.primary != null && !kb.primary.isNone && kb.primary.code == candidate.primary)
				{
					hits.Add("stock '" + field.Name + "'");
				}
				else if (kb.secondary != null && !kb.secondary.isNone && kb.secondary.code == candidate.primary)
				{
					hits.Add("stock '" + field.Name + "' (secondary)");
				}
			}
			return hits;
		}
	}
}
