using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// One extended-group membership for one action of the owning part, mirroring
	/// the stock shape: (set, group) pairs per action, for the groups the stock
	/// bitmask cannot hold. Sets are independent, exactly as in stock.
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
	/// A player-facing display name for a group, per set and with no inheritance
	/// between sets — otherwise a set could never un-inherit a name. Allowed for
	/// stock groups (1..10) too.
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
	/// Activate or Deactivate. Private bookkeeping — the signal readers consume
	/// lives elsewhere. On the vessel ROOT part, per set, absent = false.
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
	/// Persisted SIGNAL of a Toggle-kind (set, group): the 0/1 the player means by
	/// it, forceable from the window and separate from the direction bit on purpose
	/// — a manual resync must not change which direction the next press sends.
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
	/// How one (set, group) produces the signal readers consume: they always see a
	/// plain 0/1 level, and the kind only decides where that level lives and, for
	/// Hold, how the part is actuated.
	/// </summary>
	public enum KrillActuationKind
	{
		/// <summary>Momentary contact: the action fires once per press and the signal returns to 0 by itself. Right for a one-shot action.</summary>
		Pulse = 0,

		/// <summary>The signal is its own persisted bool, and the only one the window lets the player force back in sync without invoking anything.</summary>
		Toggle = 1,

		/// <summary>Activate on the level's 0 -> 1 edge, Deactivate on 1 -> 0, like stock's Brakes. No minimum duration: a brief tap yields a brief 1.</summary>
		Hold = 2,
	}

	/// <summary>Sparse per-(set, group) actuation-kind label: independent per set, so a group can mean something different in each. Absent = Pulse.</summary>
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

		// The kind serializes BY NAME, and an unknown name falls back to the default.
		// That is deliberately the only compatibility handling there is.
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
	/// Console severity label for one (set, group): the kind says HOW a group
	/// activates, this says how the console should COLOR it. Purely cosmetic, and
	/// allowed on stock groups too — the console grid shows those the same way.
	/// </summary>
	public enum KrillIndicatorType
	{
		Info = 0,
		Caution = 1,
		Warning = 2,
	}

	/// <summary>Sparse per-(set, group) indicator-type label, same shape as the kind. Absent = Info, the least alarming value.</summary>
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
	/// The complete KRILL payload of one part: group and axis assignments for its
	/// own actions and fields, plus display names and settings on the vessel root.
	/// Pure data and ConfigNode I/O, so it round-trips without a live module.
	/// </summary>
	public class KrillPartData
	{
		public const string BackupNodeName = "KRILL_DATA";

		/// <summary>Axis display names reuse KrillGroupName under a distinct node name, so the two numberings never collide.</summary>
		public const string AxisNameNodeName = "KRILL_AXIS_NAME";

		public readonly List<KrillAssignment> assignments = new List<KrillAssignment>();
		public readonly List<KrillGroupName> names = new List<KrillGroupName>();
		public readonly List<KrillGroupToggle> toggles = new List<KrillGroupToggle>();
		public readonly List<KrillGroupKind> kinds = new List<KrillGroupKind>();
		public readonly List<KrillGroupIndicator> indicators = new List<KrillGroupIndicator>();
		public readonly List<KrillGroupSignal> signals = new List<KrillGroupSignal>();
		public readonly List<KrillAxisAssignment> axisAssignments = new List<KrillAxisAssignment>();
		public readonly List<KrillGroupName> axisNames = new List<KrillGroupName>();
		public readonly List<KrillAxisSetting> axisSettings = new List<KrillAxisSetting>();

		public bool IsEmpty => assignments.Count == 0 && names.Count == 0 && toggles.Count == 0 && kinds.Count == 0
			&& indicators.Count == 0 && signals.Count == 0
			&& axisAssignments.Count == 0 && axisNames.Count == 0 && axisSettings.Count == 0;

		public void Clear()
		{
			assignments.Clear();
			names.Clear();
			toggles.Clear();
			kinds.Clear();
			indicators.Clear();
			signals.Clear();
			axisAssignments.Clear();
			axisNames.Clear();
			axisSettings.Clear();
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
			for (int i = 0; i < axisAssignments.Count; i++)
			{
				axisAssignments[i].Save(node.AddNode(KrillAxisAssignment.NodeName));
			}
			for (int i = 0; i < axisNames.Count; i++)
			{
				axisNames[i].Save(node.AddNode(AxisNameNodeName));
			}
			for (int i = 0; i < axisSettings.Count; i++)
			{
				axisSettings[i].Save(node.AddNode(KrillAxisSetting.NodeName));
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
			ConfigNode[] axisAsgNodes = node.GetNodes(KrillAxisAssignment.NodeName);
			for (int i = 0; i < axisAsgNodes.Length; i++)
			{
				KrillAxisAssignment a = KrillAxisAssignment.Load(axisAsgNodes[i]);
				if (a != null)
				{
					axisAssignments.Add(a);
				}
			}
			ConfigNode[] axisNameNodes = node.GetNodes(AxisNameNodeName);
			for (int i = 0; i < axisNameNodes.Length; i++)
			{
				KrillGroupName n = KrillGroupName.Load(axisNameNodes[i]);
				if (n != null)
				{
					axisNames.Add(n);
				}
			}
			ConfigNode[] axisSettingNodes = node.GetNodes(KrillAxisSetting.NodeName);
			for (int i = 0; i < axisSettingNodes.Length; i++)
			{
				KrillAxisSetting s = KrillAxisSetting.Load(axisSettingNodes[i]);
				if (s != null)
				{
					axisSettings.Add(s);
				}
			}
		}

		// ---- Unity-serializable backup form: editor part instances are clones and
		// never run OnLoad, so the payload needs a [SerializeField]-able mirror.

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

		// ---- Mutation helpers

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

		/// <summary>Removes one specific assignment. True if something was actually removed.</summary>
		public bool RemoveAssignment(KrillAssignment assignment)
		{
			return assignments.Remove(assignment);
		}

		/// <summary>
		/// Removes assignments matching these VALUES rather than an object reference:
		/// each part of a symmetry group holds its own instance with the same values,
		/// so a fan-out can't reuse the reference-based overload on a sibling's list.
		/// </summary>
		public bool RemoveAssignmentMatching(int set, int group, string module, int occurrence, string action)
		{
			return assignments.RemoveAll(a => a.set == set && a.group == group && a.actionRef != null
				&& a.actionRef.module == module && a.actionRef.occurrence == occurrence && a.actionRef.action == action) > 0;
		}

		/// <summary>
		/// Strips assignments, name and toggle for one (set, group) on this part only:
		/// removing a part from the UI clears the set being looked at, never the other
		/// sets, and never the global keymap bind.
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
			SetNameIn(names, set, group, name);
		}

		/// <summary>Display name for exactly (set, group); null if none. No inheritance between sets.</summary>
		public string GetName(int set, int group)
		{
			return GetNameIn(names, set, group);
		}

		/// <summary>Axis display name: same storage class as group names, own list, since axes and groups are numbered independently.</summary>
		public string GetAxisName(int set, int axis)
		{
			return GetNameIn(axisNames, set, axis);
		}

		public void SetAxisName(int set, int axis, string name)
		{
			SetNameIn(axisNames, set, axis, name);
		}

		private static void SetNameIn(List<KrillGroupName> list, int set, int number, string name)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].set == set && list[i].group == number)
				{
					if (string.IsNullOrEmpty(name))
					{
						list.RemoveAt(i);
					}
					else
					{
						list[i].name = name;
					}
					return;
				}
			}
			if (!string.IsNullOrEmpty(name))
			{
				list.Add(new KrillGroupName { set = set, group = number, name = name });
			}
		}

		private static string GetNameIn(List<KrillGroupName> list, int set, int number)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].group == number && list[i].set == set)
				{
					return list[i].name;
				}
			}
			return null;
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

		// ---- Extended axes. Same shapes as the group helpers above: assignments per
		// part, everything else on the root part; per set, with no inheritance.

		public KrillAxisAssignment FindAxisAssignment(int set, int axis, KrillFieldRef fieldRef)
		{
			for (int i = 0; i < axisAssignments.Count; i++)
			{
				KrillAxisAssignment a = axisAssignments[i];
				if (a.set == set && a.axis == axis && a.fieldRef.SameField(fieldRef))
				{
					return a;
				}
			}
			return null;
		}

		/// <summary>Adds a field to (set, axis) with the given options if not already there; returns the live entry either way (so a symmetric fan-out can copy options from the representative).</summary>
		public KrillAxisAssignment AddAxisAssignment(int set, int axis, KrillFieldRef fieldRef, bool inverted, bool incremental, float speed)
		{
			KrillAxisAssignment existing = FindAxisAssignment(set, axis, fieldRef);
			if (existing != null)
			{
				return existing;
			}
			KrillAxisAssignment a = new KrillAxisAssignment
			{
				set = set, axis = axis, fieldRef = fieldRef,
				inverted = inverted, incremental = incremental, speed = speed > 0f ? speed : KrillAxes.DefaultSpeed,
			};
			axisAssignments.Add(a);
			return a;
		}

		/// <summary>Removes assignments by VALUE, not reference, for the same symmetry-sibling reason as RemoveAssignmentMatching.</summary>
		public bool RemoveAxisAssignmentMatching(int set, int axis, KrillFieldRef fieldRef)
		{
			return axisAssignments.RemoveAll(a => a.set == set && a.axis == axis && a.fieldRef != null && a.fieldRef.SameField(fieldRef)) > 0;
		}

		/// <summary>Strips assignments, name and setting for one (set, axis) on this part only, same single-set rule as RemoveGroupInSet.</summary>
		public bool RemoveAxisInSet(int set, int axis)
		{
			int removed = axisAssignments.RemoveAll(a => a.set == set && a.axis == axis);
			removed += axisNames.RemoveAll(n => n.set == set && n.group == axis);
			removed += axisSettings.RemoveAll(s => s.set == set && s.axis == axis);
			return removed > 0;
		}

		/// <summary>The (set, axis) setting record, or null if none was ever written (all defaults apply).</summary>
		public KrillAxisSetting FindAxisSetting(int set, int axis)
		{
			for (int i = 0; i < axisSettings.Count; i++)
			{
				if (axisSettings[i].set == set && axisSettings[i].axis == axis)
				{
					return axisSettings[i];
				}
			}
			return null;
		}

		/// <summary>The (set, axis) setting record, created with defaults if absent — for writers.</summary>
		public KrillAxisSetting EnsureAxisSetting(int set, int axis)
		{
			KrillAxisSetting s = FindAxisSetting(set, axis);
			if (s == null)
			{
				s = new KrillAxisSetting { set = set, axis = axis };
				axisSettings.Add(s);
			}
			return s;
		}

		public KrillAxisKind GetAxisKind(int set, int axis)
		{
			KrillAxisSetting s = FindAxisSetting(set, axis);
			return s != null ? s.kind : KrillAxes.DefaultKind;
		}

		public void SetAxisKind(int set, int axis, KrillAxisKind kind)
		{
			EnsureAxisSetting(set, axis).kind = kind;
		}

		/// <summary>Rest value of a Spring axis (-1, 0 or +1); 0 if never set. Meaningless for Fixed.</summary>
		public int GetAxisRest(int set, int axis)
		{
			KrillAxisSetting s = FindAxisSetting(set, axis);
			return s != null ? s.rest : 0;
		}

		public void SetAxisRest(int set, int axis, int rest)
		{
			EnsureAxisSetting(set, axis).rest = Mathf.Clamp(rest, KrillAxes.RestMin, KrillAxes.RestMax);
		}

		/// <summary>Persisted level of a Fixed axis in -1..1; 0 if never set. Meaningless for Spring.</summary>
		public float GetAxisValue(int set, int axis)
		{
			KrillAxisSetting s = FindAxisSetting(set, axis);
			return s != null ? s.value : 0f;
		}

		public void SetAxisValue(int set, int axis, float value)
		{
			EnsureAxisSetting(set, axis).value = Mathf.Clamp(value, -1f, 1f);
		}

		/// <summary>Silent slot (no UI, no consumer yet); Info if never set.</summary>
		public KrillIndicatorType GetAxisIndicatorType(int set, int axis)
		{
			KrillAxisSetting s = FindAxisSetting(set, axis);
			return s != null ? s.indicator : KrillIndicatorType.Info;
		}

		public void SetAxisIndicatorType(int set, int axis, KrillIndicatorType type)
		{
			EnsureAxisSetting(set, axis).indicator = type;
		}
	}
}
