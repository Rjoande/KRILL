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
		/// regardless of which kind of candidate this is. The axis +/- keys (K2,
		/// KrillAxisKeys) are a third dimension with the same rule: excludeAxis +
		/// excludeAxisPlus name the one slot being written, -1 means none.
		/// </summary>
		public static List<string> Describe(KrillBind candidate, int excludeGroup, int excludeSet = -1, int excludeAxis = -1, bool excludeAxisPlus = false)
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

			foreach (KeyValuePair<int, KrillAxisKeys.Pair> kv in KrillAxisKeys.Binds)
			{
				AddAxisKeyHit(hits, candidate, kv.Key, true, kv.Value.plus, excludeAxis, excludeAxisPlus);
				AddAxisKeyHit(hits, candidate, kv.Key, false, kv.Value.minus, excludeAxis, excludeAxisPlus);
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

		/// <summary>
		/// Axis twin of Describe (2026-09-08, A3): what else already reads the same
		/// physical channel (an AxisBinding_Single idTag such as "joy0.3"). Checks
		/// the other KRILL axes, every stock AxisBinding on GameSettings (primary
		/// and secondary, reflection again — no single list exists) and the four
		/// stock custom axes, which live in an AxisKeyBindingList rather than as
		/// AxisBinding fields. Advisory only, like everything here: a channel bound
		/// twice simply moves both things, exactly as stock lets you do.
		/// </summary>
		/// <summary>
		/// Every OTHER binding already reading the channel `idTag`: KRILL's extended
		/// axes (skipping `excludeAxis`), every stock AxisBinding field of
		/// GameSettings, and the four stock custom axes (skipping the slot
		/// `excludeStockCustom`, 1-4, when the candidate is being written INTO that
		/// slot by an A1-A4 mirror row — A5, 2026-09-13).
		/// </summary>
		/// <summary>One +/- key slot of an extended axis (K2), skipped when it is the very slot being written.</summary>
		private static void AddAxisKeyHit(List<string> hits, KrillBind candidate, int axis, bool plus, KrillBind slot, int excludeAxis, bool excludeAxisPlus)
		{
			if (slot == null || (axis == excludeAxis && plus == excludeAxisPlus))
			{
				return;
			}
			if (candidate.SharesPrimaryWith(slot))
			{
				hits.Add("KRILL axis " + axis + (plus ? " + key (" : " - key (") + slot.Describe() + ")");
			}
		}

		public static List<string> DescribeAxis(string idTag, int excludeAxis, int excludeStockCustom = -1)
		{
			List<string> hits = new List<string>();
			if (string.IsNullOrEmpty(idTag) || idTag == "None")
			{
				return hits;
			}

			foreach (KeyValuePair<int, AxisBinding_Single> kv in KrillAxisKeymap.Binds)
			{
				if (kv.Key != excludeAxis && kv.Value.idTag == idTag)
				{
					hits.Add("KRILL axis " + kv.Key + " (" + kv.Value.title + ")");
				}
			}

			foreach (FieldInfo field in typeof(GameSettings).GetFields(BindingFlags.Public | BindingFlags.Static))
			{
				if (field.FieldType != typeof(AxisBinding))
				{
					continue;
				}
				AddAxisHits(hits, (AxisBinding)field.GetValue(null), field.Name, idTag);
			}

			AxisKeyBindingList custom = GameSettings.AXIS_CUSTOM;
			if (custom != null)
			{
				for (int i = 0; i < custom.Length; i++)
				{
					if (i + 1 == excludeStockCustom)
					{
						continue;
					}
					AxisKeyBinding akb = custom[i];
					AddAxisHits(hits, akb != null ? akb.axisBinding : null, "AXIS_CUSTOM" + (i + 1), idTag);
				}
			}
			return hits;
		}

		private static void AddAxisHits(List<string> hits, AxisBinding ab, string label, string idTag)
		{
			if (ab == null)
			{
				return;
			}
			if (ab.primary != null && ab.primary.idTag == idTag)
			{
				hits.Add("stock '" + label + "'");
			}
			else if (ab.secondary != null && ab.secondary.idTag == idTag)
			{
				hits.Add("stock '" + label + "' (secondary)");
			}
		}
	}
}
