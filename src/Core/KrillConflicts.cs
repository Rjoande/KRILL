using System.Collections.Generic;
using System.Reflection;

namespace KRILL
{
	/// <summary>
	/// Non-blocking conflict advisory: like stock, KRILL warns and never blocks —
	/// this only describes what else already uses a candidate's primary key or
	/// channel. Two binds conflict when they share the PRIMARY key, modifiers aside.
	/// </summary>
	public static class KrillConflicts
	{
		/// <summary>
		/// Everything else already bound to the candidate's primary key. The exclude*
		/// parameters name the one slot being written (group, set-jump, axis +/- key);
		/// -1 means "nothing to exclude in that dimension", never a valid number.
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

		/// <summary>One +/- key slot of an extended axis, skipped when it is the very slot being written.</summary>
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

		/// <summary>
		/// Axis twin of Describe: every other binding already reading channel `idTag`
		/// — KRILL's own axes, every stock AxisBinding on GameSettings, and the four
		/// stock custom axes, which live in a list rather than as fields.
		/// </summary>
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
