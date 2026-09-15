using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// The extended-axis engine (2026-09-09, notes/axes-design.md A4): once per
	/// physics tick, resolves the level of every extended axis of the active
	/// vessel for its active set and writes it into the assigned part fields —
	/// the analog twin of KrillActivation, and the one place that touches a
	/// BaseAxisField. Runs from KrillInputManager.FixedUpdate because stock's
	/// own incremental mode integrates with TimeWarp.fixedDeltaTime (decompiled
	/// BaseAxisField.IncrementAxis) and this reproduces that formula exactly.
	///
	/// Level resolution per (set, axis):
	///   bound to a controller channel  -> KrillAxisKeymap.TryRead (raw channel
	///       + the global dead zone); Fixed stores it as the persisted value,
	///       Spring publishes it to KrillAxisSignal;
	///   unbound                        -> Fixed reads its persisted value (the
	///       window's slider writes it), Spring reads KrillAxisSignal (slider
	///       while held, return ramp after) or its rest value.
	/// Either way KrillQuery.GetAxisState reads the same storage, so readers
	/// (KRAB, the console) see exactly what the fields receive.
	///
	/// Field write, per assignment (user decisions §5, verified §8):
	///   absolute    -> BaseAxisField.SetAxis(v): -1..1 mapped linearly onto
	///                  min..max — skipped when the field already sits there;
	///   incremental -> the field is nudged by v × range × speed × fixedDeltaTime
	///                  (stock's IncrementAxis formula computed HERE, with KRILL's
	///                  per-assignment speed, so stock's per-field
	///                  incrementalSpeedMultiplier is never overwritten),
	///                  honoring the field's own ignoreIncrementByZero and
	///                  ignoreClampWhenIncremental flags like stock does.
	/// Gates: the same career unlock the groups use, and stock's own
	/// CUSTOM_ACTION_GROUPS input lock (FlightInputHandler applies it to the
	/// custom axes).
	/// </summary>
	public static class KrillAxisDriver
	{
		private struct Target
		{
			public ModuleKrill module;
			public KrillAxisAssignment assignment;
		}

		// Reused across ticks to avoid per-tick allocation.
		private static readonly Dictionary<int, List<Target>> byAxis = new Dictionary<int, List<Target>>();
		private static readonly HashSet<int> axesThisTick = new HashSet<int>();
		private static readonly List<int> axisList = new List<int>();

		public static void Step(Vessel v, ModuleKrill root, int set)
		{
			if (v == null || root == null || v.parts == null)
			{
				return;
			}
			if (!CustomAxesUnlockedIgnoringOwn())
			{
				return;
			}

			CollectTargets(v, set);
			axesThisTick.Clear();
			foreach (int axis in byAxis.Keys)
			{
				axesThisTick.Add(axis);
			}
			// Bound axes with no field assigned still get their level refreshed:
			// readers (KRAB, the console) want it regardless of what it drives.
			foreach (KeyValuePair<int, AxisBinding_Single> kv in KrillAxisKeymap.Binds)
			{
				axesThisTick.Add(kv.Key);
			}
			axisList.Clear();
			axisList.AddRange(axesThisTick);

			float dt = TimeWarp.fixedDeltaTime;
			for (int i = 0; i < axisList.Count; i++)
			{
				int axis = axisList[i];
				float value = ResolveLevel(v, root, set, axis);
				if (byAxis.TryGetValue(axis, out List<Target> targets))
				{
					for (int t = 0; t < targets.Count; t++)
					{
						Apply(targets[t], value, dt);
					}
				}
			}
		}

		/// <summary>
		/// Stock's CUSTOM_ACTION_GROUPS gate, minus KRILL's own locks (2026-09-11,
		/// A4 test): every KRILL lock id starts with "KRILL" and every one of them
		/// uses ALLBUTCAMERAS, which CONTAINS that bit — so the window's own hover
		/// lock (FocusLock) froze the driver while the player dragged the footer
		/// slider, and would freeze a bound stick whenever the mouse crossed the
		/// window. Pause, modal dialogs and other mods' locks still stop it.
		/// lockStack is a small dictionary; walking it per physics tick is nothing.
		/// </summary>
		private static bool CustomAxesUnlockedIgnoringOwn()
		{
			ulong others = 0;
			foreach (KeyValuePair<string, ulong> kv in InputLockManager.lockStack)
			{
				if (!kv.Key.StartsWith("KRILL", System.StringComparison.Ordinal))
				{
					others |= kv.Value;
				}
			}
			return (others & (ulong)ControlTypes.CUSTOM_ACTION_GROUPS) == 0;
		}

		/// <summary>Groups the active set's axis assignments of every part by axis number. Rebuilt each tick: cheap (a few list scans) and never stale after docking, part loss or a set switch.</summary>
		private static void CollectTargets(Vessel v, int set)
		{
			foreach (KeyValuePair<int, List<Target>> kv in byAxis)
			{
				kv.Value.Clear();
			}
			List<Part> parts = v.parts;
			for (int p = 0; p < parts.Count; p++)
			{
				ModuleKrill m = parts[p].FindModuleImplementing<ModuleKrill>();
				if (m == null)
				{
					continue;
				}
				List<KrillAxisAssignment> asg = m.Data.axisAssignments;
				for (int i = 0; i < asg.Count; i++)
				{
					KrillAxisAssignment a = asg[i];
					if (a.set != set)
					{
						continue;
					}
					if (!byAxis.TryGetValue(a.axis, out List<Target> list))
					{
						list = new List<Target>();
						byAxis[a.axis] = list;
					}
					list.Add(new Target { module = m, assignment = a });
				}
			}
		}

		/// <summary>The axis's current -1..1 level, stored where KrillQuery.GetAxisState reads it (see class doc).</summary>
		private static float ResolveLevel(Vessel v, ModuleKrill root, int set, int axis)
		{
			KrillAxisKind kind = root.GetAxisKind(set, axis);
			if (KrillAxisKeymap.TryRead(axis, out float read))
			{
				if (kind == KrillAxisKind.Fixed)
				{
					// Data write without MarkDirty on purpose: the [SerializeField]
					// mirror only matters for editor clones, while OnSave already
					// serializes `Data` itself — re-serializing the whole payload to
					// a string 50 times a second while a throttle moves would be waste.
					root.Data.SetAxisValue(set, axis, read);
				}
				else
				{
					KrillAxisSignal.SetLive(v, set, axis, read);
				}
				return Mathf.Clamp(read, -1f, 1f);
			}
			if (kind == KrillAxisKind.Fixed)
			{
				return root.GetAxisValue(set, axis);
			}
			return KrillAxisSignal.TryGet(v, set, axis, out float live) ? live : root.GetAxisRest(set, axis);
		}

		private static void Apply(Target target, float value, float dt)
		{
			KrillAxisAssignment a = target.assignment;
			BaseAxisField f = a.fieldRef != null ? a.fieldRef.Resolve(target.module.part) : null;
			if (f == null || !KrillFieldRef.IsUsable(f))
			{
				return;
			}
			float x = a.inverted ? -value : value;
			if (a.incremental)
			{
				if (f.ignoreIncrementByZero && x == 0f)
				{
					return;
				}
				float current = (float)f.GetValue(f.host);
				float next = current + x * (f.maxValue - f.minValue) * a.speed * dt;
				if (!f.ignoreClampWhenIncremental)
				{
					next = Mathf.Clamp(next, f.minValue, f.maxValue);
				}
				if (next != current)
				{
					f.SetValue(next, f.host);
				}
			}
			else
			{
				float mapped = f.minValue + (f.maxValue - f.minValue) * (x + 1f) * 0.5f;
				float current = (float)f.GetValue(f.host);
				if (!Mathf.Approximately(current, mapped))
				{
					f.SetAxis(x);
				}
			}
		}
	}
}
