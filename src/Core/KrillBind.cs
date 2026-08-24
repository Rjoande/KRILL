using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// One player keybind: a primary KeyCode plus zero or more modifier KeyCodes
	/// that must be held when the primary is freshly pressed. Works identically for
	/// keyboard keys and joystick buttons (Unity KeyCode covers both —
	/// Joystick1Button0..Joystick8Button19 — capture never distinguishes them,
	/// matching the "press it now" design, design doc §4).
	/// </summary>
	public class KrillBind
	{
		public KeyCode primary = KeyCode.None;
		public readonly List<KeyCode> modifiers = new List<KeyCode>();

		public bool IsNone => primary == KeyCode.None;

		/// <summary>
		/// True on the frame the primary is freshly pressed while every required
		/// modifier is held. Does NOT check that no OTHER key is held: a bind with
		/// no modifiers fires on any press of its primary regardless of what else
		/// is down. Two binds sharing a primary can therefore both fire together
		/// (see KrillConflicts) — accepted for M2, revisit only if this proves
		/// painful once the M3 UI makes it visible day to day.
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
		/// True while the primary is currently held — no edge, no modifier check
		/// (Hold-kind groups only, 2026-08-19). Deliberately a level check, not a
		/// GetKeyDown/GetKeyUp edge pair: KrillInputManager polls this every frame
		/// and resyncs the persisted bool to match, so a missed key-up (losing OS
		/// focus mid-hold, switching active vessel mid-hold) self-corrects on the
		/// next frame instead of leaving the group stuck. Mirrors stock's own
		/// BRAKES handling in spirit (FlightInputHandler.cs, verified on
		/// decompiled source) but as a resync loop instead of two edge callbacks.
		/// </summary>
		public bool IsHeld()
		{
			return !IsNone && Input.GetKey(primary);
		}

		/// <summary>True if this and other share the same primary key (see Matches doc for why this is the relevant conflict test).</summary>
		public bool SharesPrimaryWith(KrillBind other)
		{
			return other != null && !IsNone && primary == other.primary;
		}

		/// <summary>Human-readable form for logs and the (future) UI, e.g. "LeftShift+J".</summary>
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
