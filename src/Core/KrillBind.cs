using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// One player keybind: a primary KeyCode plus zero or more modifiers that must
	/// be held with it. Keyboard keys and joystick buttons are the same thing here
	/// (Unity KeyCode covers both), and capture never distinguishes them.
	/// </summary>
	public class KrillBind
	{
		public KeyCode primary = KeyCode.None;
		public readonly List<KeyCode> modifiers = new List<KeyCode>();

		public bool IsNone => primary == KeyCode.None;

		/// <summary>
		/// True on the frame the primary is freshly pressed with every modifier held.
		/// Does not check that nothing ELSE is down, so two binds sharing a primary
		/// can fire together — surfaced to the player by KrillConflicts instead.
		/// </summary>
		public bool Matches()
		{
			if (IsNone || !Input.GetKeyDown(primary))
			{
				return false;
			}
			for (int i = 0; i < modifiers.Count; i++)
			{
				if (!Input.GetKey(modifiers[i]))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// True while the primary and every modifier are held — a level, not an edge:
		/// callers diff it against the recorded source each frame, so a missed key-up
		/// (lost focus, vessel switch) still releases on the next frame.
		/// </summary>
		public bool IsHeldWithModifiers()
		{
			if (IsNone || !Input.GetKey(primary))
			{
				return false;
			}
			for (int i = 0; i < modifiers.Count; i++)
			{
				if (!Input.GetKey(modifiers[i]))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// True if a single press can fire both binds: same primary, and one modifier
		/// set contained in the other. Matches() does not check that nothing else is
		/// down, so Ctrl+J also fires a bare J — but Ctrl+J and Alt+J never fire from
		/// each other's combination, and are not a conflict.
		/// </summary>
		public bool ConflictsWith(KrillBind other)
		{
			if (other == null || IsNone || primary != other.primary)
			{
				return false;
			}
			return Covers(modifiers, other.modifiers) || Covers(other.modifiers, modifiers);
		}

		private static bool Covers(List<KeyCode> set, List<KeyCode> subset)
		{
			for (int i = 0; i < subset.Count; i++)
			{
				if (!set.Contains(subset[i]))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Human-readable form for logs and the UI, e.g. "LeftShift+J".</summary>
		public string Describe()
		{
			if (IsNone)
			{
				return "-";
			}
			if (modifiers.Count == 0)
			{
				return primary.ToString();
			}
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < modifiers.Count; i++)
			{
				sb.Append(modifiers[i]).Append('+');
			}
			sb.Append(primary);
			return sb.ToString();
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("primary", primary.ToString());
			for (int i = 0; i < modifiers.Count; i++)
			{
				node.AddValue("modifier", modifiers[i].ToString());
			}
		}

		/// <summary>Tolerant parse: returns null (and logs) instead of throwing on bad data.</summary>
		public static KrillBind Load(ConfigNode node)
		{
			KrillBind b = new KrillBind();
			string p = node.GetValue("primary");
			if (string.IsNullOrEmpty(p) || !Enum.TryParse(p, out b.primary) || b.primary == KeyCode.None)
			{
				Debug.LogWarning("[KRILL] dropping bind node with unparsable primary key: " + node);
				return null;
			}
			foreach (string m in node.GetValues("modifier"))
			{
				if (Enum.TryParse(m, out KeyCode kc) && kc != KeyCode.None)
				{
					b.modifiers.Add(kc);
				}
			}
			return b;
		}
	}
}
