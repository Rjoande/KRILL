using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// How an extended axis behaves once the player lets go: Spring returns to its
	/// rest value, Fixed stays put. With a controller this is only a reminder; from
	/// the console by mouse it decides what happens on mouse-up. No Pulse twin.
	/// </summary>
	public enum KrillAxisKind
	{
		Spring = 0,
		Fixed = 1,
	}

	/// <summary>Axis index constants and the fixed option tables shared by the whole mod.</summary>
	public static class KrillAxes
	{
		/// <summary>
		/// Axes 1..4 mirror the four stock custom axis groups, which KRILL only shows;
		/// KRILL-owned axes start here, the same "virtual beside a full stock bitmask"
		/// scheme as extended groups 11+.
		/// </summary>
		public const int FirstExtended = 5;

		/// <summary>Values a Spring axis may rest at: full-scale low (VKB STECS-style
		/// return-to-end axes), center (a classic stick), full-scale high.</summary>
		public const int RestMin = -1;
		public const int RestMax = 1;

		/// <summary>
		/// Kind of an axis nobody has touched yet. Fixed, because the footer cycles
		/// Fixed -> Spring 0 -> Spring -1 -> Spring +1: from Fixed any Spring is one
		/// click away. Applies wherever no KRILL_AXIS record exists.
		/// </summary>
		public const KrillAxisKind DefaultKind = KrillAxisKind.Fixed;

		/// <summary>
		/// Incremental-mode speed steps, as a fraction of the field's full range per
		/// second: a hand-picked subset of stock's own list. Persisted as the VALUE,
		/// not an index, so this table can change without breaking existing craft.
		/// </summary>
		public static readonly float[] SpeedSteps = { 0.2f, 0.5f, 1f, 2f, 3f };
		public const float DefaultSpeed = 0.2f;

		/// <summary>Next entry of SpeedSteps after `current`, wrapping. A value not in the table restarts from the first step.</summary>
		public static float NextSpeed(float current)
		{
			for (int i = 0; i < SpeedSteps.Length; i++)
			{
				if (Mathf.Approximately(SpeedSteps[i], current))
				{
					return SpeedSteps[(i + 1) % SpeedSteps.Length];
				}
			}
			return SpeedSteps[0];
		}

		public static int NextRest(int current)
		{
			return current >= RestMax ? RestMin : current + 1;
		}
	}

	/// <summary>
	/// Identity of a BaseAxisField on a known part, the axis twin of KrillActionRef:
	/// module name + occurrence among same-named modules + field name, never the
	/// module's absolute index.
	/// </summary>
	public class KrillFieldRef
	{
		/// <summary>PartModule.moduleName of the owning module, e.g. "ModuleRoboticServoHinge".</summary>
		public string module = string.Empty;

		/// <summary>Index among the part's modules with the SAME moduleName (0 for the first).</summary>
		public int occurrence;

		/// <summary>BaseField.name of the axis field, e.g. "targetAngle".</summary>
		public string field = string.Empty;

		/// <summary>Build a ref from a live field, or null if its host is not a PartModule on a part.</summary>
		public static KrillFieldRef FromField(BaseAxisField f)
		{
			PartModule owner = f != null ? f.host as PartModule : null;
			if (owner == null || owner.part == null)
			{
				Debug.LogWarning("[KRILL] skipping axis field with no resolvable owning module"
					+ (f != null ? " (" + f.name + ")" : string.Empty));
				return null;
			}
			int occ = 0;
			foreach (PartModule pm in owner.part.Modules)
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
			return new KrillFieldRef { module = owner.moduleName, occurrence = occ, field = f.name };
		}

		/// <summary>
		/// Resolve back to the live field: exact match, else any same-named module that
		/// still carries it, logging the drift. Returns the field whether or not it is
		/// currently usable — what an inactive field means is the caller's business.
		/// </summary>
		public BaseAxisField Resolve(Part part)
		{
			if (part == null || string.IsNullOrEmpty(module) || string.IsNullOrEmpty(field))
			{
				return null;
			}
			int occ = 0;
			BaseAxisField fallback = null;
			foreach (PartModule pm in part.Modules)
			{
				if (pm.moduleName != module)
				{
					continue;
				}
				BaseAxisField f = pm.Fields[field] as BaseAxisField;
				if (f != null)
				{
					if (occ == occurrence)
					{
						return f;
					}
					if (fallback == null)
					{
						fallback = f;
					}
				}
				occ++;
			}
			if (fallback != null)
			{
				Debug.LogWarningFormat(
					"[KRILL] axis field ref '{0}/{1}#{2}' resolved via fallback occurrence on part '{3}' (module layout changed?)",
					module, field, occurrence, part.partInfo != null ? part.partInfo.name : part.name);
			}
			return fallback;
		}

		/// <summary>
		/// Stock's own eligibility filter for the axis-group lists: enabled module,
		/// float field, and not opted out through BaseAxisField.active (which some
		/// modules use to hide fields that make no sense in their current mode).
		/// </summary>
		public static bool IsUsable(BaseAxisField f)
		{
			PartModule pm = f != null ? f.host as PartModule : null;
			return pm != null && pm.isEnabled && f.active && f.FieldInfo != null && f.FieldInfo.FieldType == typeof(float);
		}

		/// <summary>Stock's default control mode for this field, which a fresh KRILL assignment starts with just like stock's own editor.</summary>
		public static bool DefaultIncremental(BaseAxisField f)
		{
			KSPAxisField attr = f != null ? f.Attribute as KSPAxisField : null;
			return attr != null && attr.axisMode == KSPAxisMode.Incremental;
		}

		public bool SameField(KrillFieldRef other)
		{
			return other != null && other.module == module && other.occurrence == occurrence && other.field == field;
		}

		public void Save(ConfigNode node)
		{
			node.AddValue("module", module);
			node.AddValue("occurrence", occurrence);
			node.AddValue("field", field);
		}

		/// <summary>Tolerant parse: returns null (and logs) instead of throwing on bad data.</summary>
		public static KrillFieldRef Load(ConfigNode node)
		{
			KrillFieldRef r = new KrillFieldRef();
			if (!node.TryGetValue("module", ref r.module) || string.IsNullOrEmpty(r.module)
				|| !node.TryGetValue("field", ref r.field) || string.IsNullOrEmpty(r.field))
			{
				Debug.LogWarning("[KRILL] dropping malformed axis field ref node: " + node);
				return null;
			}
			node.TryGetValue("occurrence", ref r.occurrence);
			return r;
		}
	}

	/// <summary>
	/// One extended-axis membership of one axis field, plus the options stock also
	/// keeps (inversion, absolute/incremental, speed). KRILL keeps all three per
	/// (set, axis, field) in its own data, never in stock's AXISGROUPS node.
	/// </summary>
	public class KrillAxisAssignment
	{
		public const string NodeName = "KRILL_AXIS_FIELD";

		/// <summary>0 = default set, 1..4 = stock override sets.</summary>
		public int set;

		/// <summary>Extended axis index; always >= KrillAxes.FirstExtended (5).</summary>
		public int axis;

		public KrillFieldRef fieldRef;

		/// <summary>Negate the axis value before applying it to this field.</summary>
		public bool inverted;

		/// <summary>true: the value is a RATE nudging the field each physics tick; false: it maps linearly onto min..max.</summary>
		public bool incremental;

		/// <summary>Incremental mode only: fraction of the field's full range moved per second at full deflection.</summary>
		public float speed = KrillAxes.DefaultSpeed;

		public void Save(ConfigNode node)
		{
			node.AddValue("set", set);
			node.AddValue("axis", axis);
			fieldRef.Save(node);
			node.AddValue("inverted", inverted);
			node.AddValue("incremental", incremental);
			node.AddValue("speed", speed);
		}

		public static KrillAxisAssignment Load(ConfigNode node)
		{
			KrillAxisAssignment a = new KrillAxisAssignment();
			if (!node.TryGetValue("set", ref a.set) || !node.TryGetValue("axis", ref a.axis))
			{
				Debug.LogWarning("[KRILL] dropping malformed axis assignment node: " + node);
				return null;
			}
			if (a.set < 0 || a.set > Vessel.NumOverrideGroups || a.axis < KrillAxes.FirstExtended)
			{
				Debug.LogWarning("[KRILL] dropping out-of-range axis assignment node: " + node);
				return null;
			}
			a.fieldRef = KrillFieldRef.Load(node);
			if (a.fieldRef == null)
			{
				return null;
			}
			node.TryGetValue("inverted", ref a.inverted);
			node.TryGetValue("incremental", ref a.incremental);
			if (!node.TryGetValue("speed", ref a.speed) || a.speed <= 0f)
			{
				a.speed = KrillAxes.DefaultSpeed;
			}
			return a;
		}
	}

	/// <summary>
	/// Everything about one (set, axis) that is not an assignment: kind, rest, the
	/// persisted level of a Fixed axis and a silent indicator slot. One sparse node
	/// per pair on the root part; a Spring never persists its level.
	/// </summary>
	public class KrillAxisSetting
	{
		public const string NodeName = "KRILL_AXIS";

		public int set;
		public int axis;
		public KrillAxisKind kind = KrillAxes.DefaultKind;
		public int rest;
		public float value;
		public KrillIndicatorType indicator = KrillIndicatorType.Info;

		public void Save(ConfigNode node)
		{
			node.AddValue("set", set);
			node.AddValue("axis", axis);
			node.AddValue("kind", kind.ToString());
			node.AddValue("rest", rest);
			node.AddValue("value", value);
			node.AddValue("indicator", indicator.ToString());
		}

		/// <summary>Tolerant: only set/axis are required, each option falls back to its default, and out-of-range rest/value are clamped, not dropped.</summary>
		public static KrillAxisSetting Load(ConfigNode node)
		{
			KrillAxisSetting s = new KrillAxisSetting();
			if (!node.TryGetValue("set", ref s.set) || !node.TryGetValue("axis", ref s.axis)
				|| s.set < 0 || s.set > Vessel.NumOverrideGroups || s.axis < KrillAxes.FirstExtended)
			{
				Debug.LogWarning("[KRILL] dropping malformed axis setting node: " + node);
				return null;
			}
			string kindStr = null;
			if (!node.TryGetValue("kind", ref kindStr) || !System.Enum.TryParse(kindStr, out s.kind))
			{
				s.kind = KrillAxes.DefaultKind;
			}
			node.TryGetValue("rest", ref s.rest);
			s.rest = Mathf.Clamp(s.rest, KrillAxes.RestMin, KrillAxes.RestMax);
			node.TryGetValue("value", ref s.value);
			s.value = Mathf.Clamp(s.value, -1f, 1f);
			string indStr = null;
			if (!node.TryGetValue("indicator", ref indStr) || !System.Enum.TryParse(indStr, out s.indicator))
			{
				s.indicator = KrillIndicatorType.Info;
			}
			return s;
		}
	}
}
