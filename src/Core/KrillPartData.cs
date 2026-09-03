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
	/// Persisted DIRECTION bit of one (set, group): whether the next Fire sends
	/// Activate or Deactivate — the KRILL equivalent of stock's
	/// ActionGroupList.groups / groupStates bool arrays (verified on decompiled
	/// ActionGroupList.ToggleGroup), flipped on every Pulse/Toggle press exactly
	/// like stock. PRIVATE BOOKKEEPING (2026-09-02): nothing external reads it,
	/// the signal readers consume lives elsewhere (KrillGroupSignal for Toggle,
	/// KrillSignal for Pulse/Hold). Hold-kind groups never write it. By
	/// convention lives on the vessel ROOT part's ModuleKrill only (same
	/// convention already used for KrillGroupName) so it survives quicksave/
	/// quickload and scene changes exactly like stock's own group states. No
	/// inheritance rule (unlike names): each (set, group) pair is independent,
	/// defaulting to false if absent. Node name kept as KRILL_TOGGLE for
	/// craft-file continuity.
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

	/// <summary>
	/// Persisted SIGNAL of a Toggle-kind (set, group): the 0/1 the player means
	/// by it ("this reads as 1 to me"), flipped on every press and writable by
	/// hand from the KRILL window's State button. Deliberately a SEPARATE bool
	/// from KrillGroupToggle (user decision 2026-09-02): a manual resync
	/// reconciles the signal with the player's judgment, not with what the part
	/// last received — so forcing it must never change which direction the next
	/// press sends. Only ever written for Toggle groups; Pulse and Hold keep
	/// their (transient) signal in KrillSignal instead. Same root-part
	/// convention and same per-set independence as the direction bit.
	/// </summary>
	public class KrillGroupSignal
	{
		public const string NodeName = "KRILL_SIGNAL";

		public int set;
		public int group;
		public bool value;

		public void Save(ConfigNode node)
		{
			node.AddValue("set", set);
			node.AddValue("group", group);
			node.AddValue("value", value);
		}

		public static KrillGroupSignal Load(ConfigNode node)
		{
			KrillGroupSignal s = new KrillGroupSignal();
			if (!node.TryGetValue("set", ref s.set) || !node.TryGetValue("group", ref s.group)
				|| !node.TryGetValue("value", ref s.value)
				|| s.set < 0 || s.set > Vessel.NumOverrideGroups || s.group < KrillGroups.FirstExtended)
			{
				Debug.LogWarning("[KRILL] dropping malformed group signal node: " + node);
				return null;
			}
			return s;
		}
	}

	/// <summary>
	/// How one (set, group) produces the SIGNAL that external readers consume
	/// (other mods such as KRAB, and the KRILL console) — 2026-08-19, reworked
	/// 2026-08-31 and again 2026-09-02 (notes/kind-signal-analysis.md).
	///
	/// Key principle: what a reader sees is KrillQuery.GroupState.signal, a
	/// plain 0/1 level whatever the kind — consumers need no kind-specific
	/// logic at all: 1 means lit, 0 means not. The kind only decides WHERE that
	/// level lives and, for Hold alone, how the part is actuated. The persisted
	/// direction bit (KrillGroupToggle) is private bookkeeping for the
	/// Activate/Deactivate alternation and is never the signal.
	///
	/// Pulse (default; named "Switch" before 2026-08-31 — renamed because
	/// "pulse vs toggle" survives translation far better): a momentary contact.
	/// The action fires once per press exactly as before; the signal goes to 1
	/// and returns to 0 by itself after KrillSignal.PulseSeconds. Runtime only,
	/// never persisted. Correct for a group bound to a one-shot action (a
	/// decoupler), where an alternating bool corresponds to no physical state.
	///
	/// Toggle: the signal is its own PERSISTED bool (KrillGroupSignal), flipped
	/// per press, and the only kind where the window offers a manual
	/// force-correct of it (no action invoked, direction bit untouched) — for
	/// when the real part state drifts out of sync via another mod or a manual
	/// PAW click, or simply when the player decides what 1 means.
	///
	/// Hold (2026-08-19, after confirming stock's own Brakes group works this
	/// way — FlightInputHandler.cs, decompiled): the one kind with a different
	/// ACTUATION mechanism — Activate when the level goes 0 -> 1, Deactivate
	/// when it goes 1 -> 0 (KrillActivation.HoldPress/HoldRelease), never a
	/// press-to-flip. The level is the set of sources (key, window, console)
	/// currently pressing it (KrillSignal), runtime only: a hold can't survive
	/// a save because there is no key still down after a load. Deliberately NO
	/// minimum duration (user decision 2026-08-31): a brief tap yields a brief
	/// 1, because Hold is a level and a floor would break the "held means held"
	/// contract readers rely on — unlike Pulse, which IS a timed shape.
	/// </summary>
	public enum KrillActuationKind
	{
		Pulse = 0,
		Toggle = 1,
		Hold = 2,
	}

	/// <summary>
	/// Sparse per-(set, group) actuation-kind label, same shape/scoping as
	/// KrillGroupToggle (independent per set, no inheritance — a group can mean
	/// something different from one set to another). Absent = Pulse.
	/// </summary>
	public class KrillGroupKind
	{
		public const string NodeName = "KRILL_KIND";

		public int set;
		public int group;
		public KrillActuationKind kind;

		public void Save(ConfigNode node)
		{
			node.AddValue("set", set);
			node.AddValue("group", group);
			node.AddValue("kind", kind.ToString());
		}

		// Note: the kind serializes BY NAME. A craft saved before the 2026-08-31
		// Switch -> Pulse rename would carry `kind = Switch` and be dropped here
		// as malformed, falling back to the default (Pulse — the same thing). No
		// legacy shim by user decision (2026-09-02): the mechanic was only ever
		// tested on freshly built craft, and the fallback is already the right value.
		public static KrillGroupKind Load(ConfigNode node)
		{
			KrillGroupKind k = new KrillGroupKind();
			string kindStr = null;
			if (!node.TryGetValue("set", ref k.set) || !node.TryGetValue("group", ref k.group)
				|| !node.TryGetValue("kind", ref kindStr) || !System.Enum.TryParse(kindStr, out k.kind)
				|| k.set < 0 || k.set > Vessel.NumOverrideGroups || k.group < KrillGroups.FirstExtended)
			{
				Debug.LogWarning("[KRILL] dropping malformed group kind node: " + node);
				return null;
			}
			return k;
		}
	}

	/// <summary>
	/// Console severity label for one (set, group), independent of KrillActuationKind:
	/// kind describes HOW a group activates, this describes how the future flight
	/// console should COLOR it (2026-08-24 design discussion; named "console", not
	/// "HUD" — 2026-08-27, it's a clickable button grid, not a see-through overlay).
	/// Purely cosmetic — carries no behavior of its own until the console reads it.
	/// Unlike kind, allowed on stock groups (1..10) too: the console grid shows
	/// those groups exactly like extended ones (same precedent as KrillGroupName),
	/// and severity has no dependency on which engine (stock or KRILL) actually
	/// runs the group.
	/// </summary>
	public enum KrillIndicatorType
	{
		Info = 0,
		Caution = 1,
		Warning = 2,
	}

	/// <summary>
	/// Sparse per-(set, group) indicator-type label, same shape/scoping as
	/// KrillGroupKind (independent per set, no inheritance). Absent = Info, the
	/// least alarming value — same "absent = least invasive" convention as kind
	/// defaulting to Pulse.
	/// </summary>
	public class KrillGroupIndicator
	{
		public const string NodeName = "KRILL_INDICATOR";

		public int set;
		public int group;
		public KrillIndicatorType type;

		public void Save(ConfigNode node)
		{
			node.AddValue("set", set);
			node.AddValue("group", group);
			node.AddValue("type", type.ToString());
		}

		public static KrillGroupIndicator Load(ConfigNode node)
		{
			KrillGroupIndicator ind = new KrillGroupIndicator();
			string typeStr = null;
			if (!node.TryGetValue("set", ref ind.set) || !node.TryGetValue("group", ref ind.group)
				|| !node.TryGetValue("type", ref typeStr) || !System.Enum.TryParse(typeStr, out ind.type)
				|| ind.set < 0 || ind.set > Vessel.NumOverrideGroups || ind.group < 1)
			{
				Debug.LogWarning("[KRILL] dropping malformed group indicator node: " + node);
				return null;
			}
			return ind;
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
		public readonly List<KrillGroupKind> kinds = new List<KrillGroupKind>();
		public readonly List<KrillGroupIndicator> indicators = new List<KrillGroupIndicator>();
		public readonly List<KrillGroupSignal> signals = new List<KrillGroupSignal>();

		public bool IsEmpty => assignments.Count == 0 && names.Count == 0 && toggles.Count == 0 && kinds.Count == 0
			&& indicators.Count == 0 && signals.Count == 0;

		public void Clear()
		{
			assignments.Clear();
			names.Clear();
			toggles.Clear();
			kinds.Clear();
			indicators.Clear();
			signals.Clear();
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
			for (int i = 0; i < kinds.Count; i++)
			{
				kinds[i].Save(node.AddNode(KrillGroupKind.NodeName));
			}
			for (int i = 0; i < indicators.Count; i++)
			{
				indicators[i].Save(node.AddNode(KrillGroupIndicator.NodeName));
			}
			for (int i = 0; i < signals.Count; i++)
			{
				signals[i].Save(node.AddNode(KrillGroupSignal.NodeName));
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
			ConfigNode[] kindNodes = node.GetNodes(KrillGroupKind.NodeName);
			for (int i = 0; i < kindNodes.Length; i++)
			{
				KrillGroupKind k = KrillGroupKind.Load(kindNodes[i]);
				if (k != null)
				{
					kinds.Add(k);
				}
			}
			ConfigNode[] indicatorNodes = node.GetNodes(KrillGroupIndicator.NodeName);
			for (int i = 0; i < indicatorNodes.Length; i++)
			{
				KrillGroupIndicator ind = KrillGroupIndicator.Load(indicatorNodes[i]);
				if (ind != null)
				{
					indicators.Add(ind);
				}
			}
			ConfigNode[] signalNodes = node.GetNodes(KrillGroupSignal.NodeName);
			for (int i = 0; i < signalNodes.Length; i++)
			{
				KrillGroupSignal s = KrillGroupSignal.Load(signalNodes[i]);
				if (s != null)
				{
					signals.Add(s);
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
			removed += kinds.RemoveAll(k => k.set == set && k.group == group);
			removed += indicators.RemoveAll(ind => ind.set == set && ind.group == group);
			removed += signals.RemoveAll(s => s.set == set && s.group == group);
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

		/// <summary>Direction bit of (set, group) — private bookkeeping, see KrillGroupToggle; false if never fired.</summary>
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

		/// <summary>Persisted Toggle-kind signal of (set, group); false if never set. Meaningful only when the kind is Toggle (see KrillGroupSignal).</summary>
		public bool GetSignal(int set, int group)
		{
			for (int i = 0; i < signals.Count; i++)
			{
				if (signals[i].set == set && signals[i].group == group)
				{
					return signals[i].value;
				}
			}
			return false;
		}

		public void SetSignal(int set, int group, bool value)
		{
			for (int i = 0; i < signals.Count; i++)
			{
				if (signals[i].set == set && signals[i].group == group)
				{
					signals[i].value = value;
					return;
				}
			}
			signals.Add(new KrillGroupSignal { set = set, group = group, value = value });
		}

		/// <summary>Current actuation kind of (set, group); Pulse if never set.</summary>
		public KrillActuationKind GetKind(int set, int group)
		{
			for (int i = 0; i < kinds.Count; i++)
			{
				if (kinds[i].set == set && kinds[i].group == group)
				{
					return kinds[i].kind;
				}
			}
			return KrillActuationKind.Pulse;
		}

		public void SetKind(int set, int group, KrillActuationKind kind)
		{
			for (int i = 0; i < kinds.Count; i++)
			{
				if (kinds[i].set == set && kinds[i].group == group)
				{
					kinds[i].kind = kind;
					return;
				}
			}
			kinds.Add(new KrillGroupKind { set = set, group = group, kind = kind });
		}

		/// <summary>Current console severity of (set, group); Info if never set. Valid for stock groups too.</summary>
		public KrillIndicatorType GetIndicatorType(int set, int group)
		{
			for (int i = 0; i < indicators.Count; i++)
			{
				if (indicators[i].set == set && indicators[i].group == group)
				{
					return indicators[i].type;
				}
			}
			return KrillIndicatorType.Info;
		}

		public void SetIndicatorType(int set, int group, KrillIndicatorType type)
		{
			for (int i = 0; i < indicators.Count; i++)
			{
				if (indicators[i].set == set && indicators[i].group == group)
				{
					indicators[i].type = type;
					return;
				}
			}
			indicators.Add(new KrillGroupIndicator { set = set, group = group, type = type });
		}
	}
}
