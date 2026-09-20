using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// How an extended axis behaves once the player lets go of it — the analog
	/// twin of KrillActuationKind (notes/axes-design.md §2, 2026-09-07):
	///   Spring <-> Hold  : released, it returns to its rest value by itself;
	///   Fixed  <-> Toggle: stays where it was put.
	/// With a physical controller the kind is only a reminder (the hardware
	/// itself springs back or not, KRILL just reads the channel); driven from
	/// the console by mouse it becomes functional — it decides what the on-screen
	/// control does after mouse-up. There is no Pulse twin: a momentary contact
	/// has no analog meaning.
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
		/// Axes 1..4 mirror the four stock custom axis groups (KSPAxisGroup.Custom01..04,
		/// managed by stock's own axis-group UI, shown by KRILL for name/bind only).
		/// Extended, KRILL-owned axes start here — same "virtual, beside a full
		/// stock bitmask" scheme as extended groups 11+.
		/// </summary>
		public const int FirstExtended = 5;

		/// <summary>Values a Spring axis may rest at: full-scale low (VKB STECS-style
		/// return-to-end axes), center (a classic stick), full-scale high.</summary>
		public const int RestMin = -1;
		public const int RestMax = 1;

		/// <summary>
		/// Kind of an axis nobody has touched yet (user decision 2026-09-19): Fixed,
		/// because the footer's four-state cycle is Fixed -> Spring 0 -> Spring -1 ->
		/// Spring +1, so from Fixed any Spring is one click away while from Spring 0
		/// reaching Fixed took three. Applies wherever no KRILL_AXIS record exists.
		/// </summary>
		public const KrillAxisKind DefaultKind = KrillAxisKind.Fixed;

		/// <summary>
		/// Incremental-mode speed steps offered by the KRILL window, as a fraction
		/// of the field's full range per second — a hand-picked subset of stock's
		/// 18-value AXIS_INCREMENTAL_SPEED_MULTIPLIER_STORAGE list (user decision
		/// 2026-09-07: 20/50/100/200/300 %/s, default 20 like stock). Persisted as
		/// the VALUE, not an index, so this table can change without breaking craft.
		/// </summary>
		public static readonly float[] SpeedSteps = { 0.2f, 0.5f, 1f, 2f, 3f };
		public const float DefaultSpeed = 0.2f;

		/// <summary>Next entry of SpeedSteps after `current` (wrapping), for the window's cycle button. A value not in the table (older craft, edited file) restarts from the first step.</summary>
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
	/// Identity of a single BaseAxisField on a known part — the axis twin of
	/// KrillActionRef, same rules: the part is implicit (the ref lives inside that
	/// part's ModuleKrill), the field is module name + occurrence among same-named
	/// modules + field name. NEVER the module's absolute index (the AGExt defect).
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
		/// Resolve back to the live field on the given part. Exact match first; if
		/// that module no longer carries the field (a mod update changed the part),
		/// fall back to any same-named module that does, logging the drift instead
		/// of silently losing the assignment — same policy as KrillActionRef.Resolve.
		/// Returns the field whether or not it is currently usable (see IsUsable):
		/// the caller decides what an inactive field means for it.
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
		/// Stock's own eligibility filter for the axis-group assignment lists
		/// (BaseAxisField.CreateAxisList, decompiled 2026-09-07): the owning module
		/// is enabled, the field is a float, and the module hasn't opted it out via
		/// BaseAxisField.active (ModuleAeroSurface hides authorityLimiter/deployAngle
		/// that way, ModuleLight toggles its color fields with the light mode).
		/// </summary>
		public static bool IsUsable(BaseAxisField f)
		{
			PartModule pm = f != null ? f.host as PartModule : null;
			return pm != null && pm.isEnabled && f.active && f.FieldInfo != null && f.FieldInfo.FieldType == typeof(float);
		}

		/// <summary>Stock's default control mode for this field (KSPAxisField.axisMode) — what a fresh KRILL assignment starts with, like stock's own editor does.</summary>
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
	/// One extended-axis membership of one axis field of the owning part, plus the
	/// per-assignment options stock also keeps per field (decompiled BaseAxisField
	/// 2026-09-07: inversion and absolute/incremental mode per set, speed
	/// multiplier per field). KRILL keeps all three per (set, axis, field) — its
	/// own data, never stock's AXISGROUPS node. Sets are independent, like every
	/// other KRILL assignment.
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

		/// <summary>true: the value is a RATE that nudges the field each physics tick; false: the value maps linearly onto min..max (BaseAxisField.SetAxis).</summary>
		public bool incremental;

		/// <summary>Incremental mode only: fraction of the field's full range moved per second at full deflection (KrillAxes.SpeedSteps).</summary>
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
	/// Everything about one (set, axis) that is NOT an assignment: kind, rest
	/// value, the persisted value of a Fixed axis, and the (silent) indicator
	/// type. One sparse node per pair, by convention on the vessel ROOT part's
	/// ModuleKrill only (same convention as KrillGroupKind/Toggle/Signal). Absent
	/// = Spring, rest 0, value 0, Info. Independent per set, no inheritance.
	///
	/// `value` is the persisted level of a FIXED axis ("where the player left
	/// it", the analog of KrillGroupSignal) and is the only thing here the
	/// engine writes at runtime; a Spring axis never persists its level (it
	/// restarts at `rest`, like Hold restarts at 0 — a quicksave mid-deflection
	/// must not come back as a stuck axis). `indicator` is persisted but has no
	/// UI and no consumer yet (user decision 2026-09-07: keep the slot so the
	/// persistence needn't be redone if the console ever colors axes).
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

		/// <summary>Tolerant: only set/axis are required; every option falls back to its default individually, and out-of-range rest/value are clamped rather than dropped.</summary>
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
