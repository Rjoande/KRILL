using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// One extended-group membership for one action of the owning part.
	/// Mirrors the stock shape exactly: stock keeps actionGroup (set 0) plus
	/// overrideGroup0..3 (sets 1..4) per action; KRILL keeps (set, group) pairs
	/// per action for groups the stock bitmask cannot hold (11+). Sets are
	/// independent, like stock: set 0 is NOT inherited by sets 1..4 (verified on
	/// decompiled BaseAction.GetActionGroup).
	/// </summary>
	public class KrillAssignment
	{
		public const string NodeName = "KRILL_ACTION";

		/// <summary>0 = default set, 1..4 = stock override sets (Vessel.GroupOverride).</summary>
		public int set;

		/// <summary>Extended group index; always >= KrillGroups.FirstExtended (11).</summary>
		public int group;

		public KrillActionRef actionRef;

		public void Save(ConfigNode node)
		{
			node.AddValue("set", set);
			node.AddValue("group", group);
			actionRef.Save(node);
		}

		public static KrillAssignment Load(ConfigNode node)
		{
			KrillAssignment a = new KrillAssignment();
			if (!node.TryGetValue("set", ref a.set) || !node.TryGetValue("group", ref a.group))
			{
				Debug.LogWarning("[KRILL] dropping malformed assignment node: " + node);
				return null;
			}
			if (a.set < 0 || a.set > Vessel.NumOverrideGroups || a.group < KrillGroups.FirstExtended)
			{
				Debug.LogWarning("[KRILL] dropping out-of-range assignment node: " + node);
				return null;
			}
			a.actionRef = KrillActionRef.Load(node);
			return a.actionRef == null ? null : a;
		}
	}

	/// <summary>
	/// A player-facing display name for a group, per set. Names are a KRILL-only
	/// convenience and, unlike assignments, DO inherit: a name defined in set 0
	/// applies to all sets until overridden (display-only rule, see design doc §3).
	/// Names are allowed for stock groups (1..10) too.
	/// </summary>
	public class KrillGroupName
	{
		public const string NodeName = "KRILL_NAME";

		public int set;
		public int group;
		public string name = string.Empty;

		public void Save(ConfigNode node)
		{
			node.AddValue("set", set);
			node.AddValue("group", group);
			node.AddValue("name", name);
		}

		public static KrillGroupName Load(ConfigNode node)
		{
			KrillGroupName n = new KrillGroupName();
			if (!node.TryGetValue("set", ref n.set) || !node.TryGetValue("group", ref n.group)
				|| !node.TryGetValue("name", ref n.name) || string.IsNullOrEmpty(n.name)
				|| n.set < 0 || n.set > Vessel.NumOverrideGroups || n.group < 1)
			{
				Debug.LogWarning("[KRILL] dropping malformed group name node: " + node);
				return null;
			}
			return n;
		}
	}

	/// <summary>
	/// Persisted on/off state of one (set, group), the KRILL equivalent of stock's
	/// ActionGroupList.groups / groupStates bool arrays (verified on decompiled
	/// ActionGroupList.ToggleGroup). By convention lives on the vessel ROOT part's
	/// ModuleKrill only (same convention already used for KrillGroupName) so it
	/// survives quicksave/quickload and scene changes exactly like stock's own
	/// action group states. No inheritance rule here (unlike names): each (set,
	/// group) pair has its own independent state, defaulting to false if absent.
	/// </summary>
	public class KrillGroupToggle
	{
		public const string NodeName = "KRILL_TOGGLE";

		public int set;
		public int group;
		public bool active;

		public void Save(ConfigNode node)
		{
			node.AddValue("set", set);
			node.AddValue("group", group);
			node.AddValue("active", active);
		}

		public static KrillGroupToggle Load(ConfigNode node)
		{
			KrillGroupToggle t = new KrillGroupToggle();
			if (!node.TryGetValue("set", ref t.set) || !node.TryGetValue("group", ref t.group)
				|| !node.TryGetValue("active", ref t.active)
				|| t.set < 0 || t.set > Vessel.NumOverrideGroups || t.group < KrillGroups.FirstExtended)
			{
				Debug.LogWarning("[KRILL] dropping malformed group toggle node: " + node);
				return null;
			}
			return t;
		}
	}

	/// <summary>Group index constants shared by the whole mod.</summary>
	public static class KrillGroups
	{
		/// <summary>Groups 1..10 are the stock custom groups (delegated entirely to stock).</summary>
		public const int FirstExtended = 11;
	}

	/// <summary>
	/// The complete KRILL payload of one part: extended-group assignments for the
	/// part's own actions, plus (by convention, on the vessel root) group display
	/// names. Pure data + ConfigNode I/O — no Unity lifecycle, so it can be
	/// round-tripped and unit-tested (in-game self-test) without a live module.
	/// </summary>
	public class KrillPartData
	{
		public const string BackupNodeName = "KRILL_DATA";

		public readonly List<KrillAssignment> assignments = new List<KrillAssignment>();
		public readonly List<KrillGroupName> names = new List<KrillGroupName>();
		public readonly List<KrillGroupToggle> toggles = new List<KrillGroupToggle>();

		public bool IsEmpty => assignments.Count == 0 && names.Count == 0 && toggles.Count == 0;

		public void Clear()
		{
			assignments.Clear();
			names.Clear();
			toggles.Clear();
		}

		/// <summary>Write payload into the module's persistence node (additive; caller owns the node).</summary>
		public void Save(ConfigNode node)
		{
			for (int i = 0; i < assignments.Count; i++)
			{
				assignments[i].Save(node.AddNode(KrillAssignment.NodeName));
			}
			for (int i = 0; i < names.Count; i++)
			{
				names[i].Save(node.AddNode(KrillGroupName.NodeName));
			}
			for (int i = 0; i < toggles.Count; i++)
			{
				toggles[i].Save(node.AddNode(KrillGroupToggle.NodeName));
			}
		}

		/// <summary>Tolerant load: malformed child nodes are dropped with a log line, never fatal.</summary>
		public void Load(ConfigNode node)
		{
			Clear();
			ConfigNode[] actNodes = node.GetNodes(KrillAssignment.NodeName);
			for (int i = 0; i < actNodes.Length; i++)
			{
				KrillAssignment a = KrillAssignment.Load(actNodes[i]);
				if (a != null)
				{
					assignments.Add(a);
				}
			}
			ConfigNode[] nameNodes = node.GetNodes(KrillGroupName.NodeName);
			for (int i = 0; i < nameNodes.Length; i++)
			{
				KrillGroupName n = KrillGroupName.Load(nameNodes[i]);
				if (n != null)
				{
					names.Add(n);
				}
			}
			ConfigNode[] toggleNodes = node.GetNodes(KrillGroupToggle.NodeName);
			for (int i = 0; i < toggleNodes.Length; i++)
			{
				KrillGroupToggle t = KrillGroupToggle.Load(toggleNodes[i]);
				if (t != null)
				{
					toggles.Add(t);
				}
			}
		}

		// ---- Unity-serializable backup form (editor-clone lesson from KRAB:
		// editor part instances are Unity clones, OnLoad never runs on the clone,
		// so every complex payload needs a [SerializeField]-able string mirror).

		public string SaveToString()
		{
			ConfigNode root = new ConfigNode(BackupNodeName);
			Save(root);
			return root.ToString();
		}

		public void LoadFromString(string backup)
		{
			Clear();
			if (string.IsNullOrEmpty(backup))
			{
				return;
			}
			ConfigNode parsed = ConfigNode.Parse(backup);
			if (parsed == null)
			{
				Debug.LogWarning("[KRILL] could not parse data backup string");
				return;
			}
			ConfigNode payload = parsed.GetNode(BackupNodeName) ?? parsed;
			Load(payload);
		}

		// ---- Mutation helpers (used by self-test now, by the editor UI from M3 on).

		public bool HasAssignment(int set, int group, KrillActionRef actionRef)
		{
			for (int i = 0; i < assignments.Count; i++)
			{
				KrillAssignment a = assignments[i];
				if (a.set == set && a.group == group
					&& a.actionRef.module == actionRef.module
					&& a.actionRef.occurrence == actionRef.occurrence
					&& a.actionRef.action == actionRef.action)
				{
					return true;
				}
			}
			return false;
		}

		public void AddAssignment(int set, int group, KrillActionRef actionRef)
		{
			if (!HasAssignment(set, group, actionRef))
			{
				assignments.Add(new KrillAssignment { set = set, group = group, actionRef = actionRef });
			}
		}

		/// <summary>Removes one specific assignment (M3 detail view's per-row [x]). Returns true if something was actually removed.</summary>
		public bool RemoveAssignment(KrillAssignment assignment)
		{
			return assignments.Remove(assignment);
		}

		/// <summary>
		/// Removes assignment(s) matching these VALUES rather than a specific object
		/// reference — needed when a removal is fanned out to a symmetric sibling
		/// (2026-07-27): each part in a symmetry group holds its OWN independent
		/// KrillAssignment instance (same values, different object), so the
		/// reference-based RemoveAssignment above can't be reused directly on a
		/// sibling's list. Returns true if anything was removed.
		/// </summary>
		public bool RemoveAssignmentMatching(int set, int group, string module, int occurrence, string action)
		{
			return assignments.RemoveAll(a => a.set == set && a.group == group && a.actionRef != null
				&& a.actionRef.module == module && a.actionRef.occurrence == occurrence && a.actionRef.action == action) > 0;
		}

		/// <summary>
		/// Strips assignments/name/toggle for one (set, group) pair on THIS part only
		/// — scoped to a SINGLE set (2026-07-18 decision: removing a part's node in
		/// the 3-column UI only clears the set you're currently looking at, other
		/// sets' assignments for the same group number are untouched). Deliberately
		/// never touches the global keymap bind. Returns true if anything changed.
		/// </summary>
		public bool RemoveGroupInSet(int set, int group)
		{
			int removed = assignments.RemoveAll(a => a.set == set && a.group == group);
			removed += names.RemoveAll(n => n.set == set && n.group == group);
			removed += toggles.RemoveAll(t => t.set == set && t.group == group);
			return removed > 0;
		}

		public void SetName(int set, int group, string name)
		{
			for (int i = 0; i < names.Count; i++)
			{
				if (names[i].set == set && names[i].group == group)
				{
					if (string.IsNullOrEmpty(name))
					{
						names.RemoveAt(i);
					}
					else
					{
						names[i].name = name;
					}
					return;
				}
			}
			if (!string.IsNullOrEmpty(name))
			{
				names.Add(new KrillGroupName { set = set, group = group, name = name });
			}
		}

		/// <summary>Display name for (set, group) with the set-0 inheritance rule; null if none.</summary>
		public string GetName(int set, int group)
		{
			string fromDefault = null;
			for (int i = 0; i < names.Count; i++)
			{
				if (names[i].group != group)
				{
					continue;
				}
				if (names[i].set == set)
				{
					return names[i].name;
				}
				if (names[i].set == 0)
				{
					fromDefault = names[i].name;
				}
			}
			return fromDefault;
		}

		/// <summary>Current on/off state of (set, group); false if never toggled.</summary>
		public bool GetToggle(int set, int group)
		{
			for (int i = 0; i < toggles.Count; i++)
			{
				if (toggles[i].set == set && toggles[i].group == group)
				{
					return toggles[i].active;
				}
			}
			return false;
		}

		public void SetToggle(int set, int group, bool active)
		{
			for (int i = 0; i < toggles.Count; i++)
			{
				if (toggles[i].set == set && toggles[i].group == group)
				{
					toggles[i].active = active;
					return;
				}
			}
			toggles.Add(new KrillGroupToggle { set = set, group = group, active = active });
		}
	}
}
