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
	/// Level resolution per (set, axis), first match wins (§11.3: a held key
	/// wins; on release the axis goes back to whoever owns it):
	///   a +/- key held (KrillAxisKeys) -> the kind decides (§11.2): Spring ramps
	///       toward ±1 (KrillAxisSignal, attack from the settings, stock-like
	///       snap by default), Fixed integrates its persisted value at KeyRate
	///       and stays there;
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
	/// custom axes) minus KRILL's own locks (KrillLocks). Keys have a stricter
	/// gate than the channel: the typing lock and a running key capture stop
	/// them, a stick is neither the keyboard nor a candidate.
	/// </summary>
	public static class KrillAxisDriver
	{
		/// <summary>Precision mode (CapsLock) divides the Fixed key rate by this; stock has no Fixed-like axis to copy from, the factor is KRILL's.</summary>
		public const float PrecisionRateFactor = 0.25f;

		private struct Target
		{
			public ModuleKrill module;
			public KrillAxisAssignment assignment;
		}

		// Reused across ticks to avoid per-tick allocation.
		private static readonly Dictionary<int, List<Target>> byAxis = new Dictionary<int, List<Target>>();
		private static readonly HashSet<int> axesThisTick = new HashSet<int>();
		private static readonly List<int> axisList = new List<int>();
		// Axes a held key was driving last tick (K1): the release edge is what
		// starts a Spring's return when no channel re-asserts the level.
		private static readonly HashSet<int> keyDriven = new HashSet<int>();

		public static void Step(Vessel v, ModuleKrill root, int set)
		{
			if (v == null || root == null || v.parts == null)
			{
				return;
			}
			if (!KrillLocks.Unlocked(ControlTypes.CUSTOM_ACTION_GROUPS, honourTyping: false))
			{
				return;
			}
			bool keysAllowed = KrillLocks.KeysAllowed();

			CollectTargets(v, set);
			axesThisTick.Clear();
			foreach (int axis in byAxis.Keys)
			{
				axesThisTick.Add(axis);
			}
			// Bound axes (channel or keys) with no field assigned still get their
			// level refreshed: readers (KRAB, the console) want it regardless of
			// what it drives.
			foreach (KeyValuePair<int, AxisBinding_Single> kv in KrillAxisKeymap.Binds)
			{
				axesThisTick.Add(kv.Key);
			}
			foreach (KeyValuePair<int, KrillAxisKeys.Pair> kv in KrillAxisKeys.Binds)
			{
				axesThisTick.Add(kv.Key);
			}
			axisList.Clear();
			axisList.AddRange(axesThisTick);

			float dt = TimeWarp.fixedDeltaTime;
			for (int i = 0; i < axisList.Count; i++)
			{
				int axis = axisList[i];
				float value = ResolveLevel(v, root, set, axis, keysAllowed, dt);
				if (byAxis.TryGetValue(axis, out List<Target> targets))
				{
					for (int t = 0; t < targets.Count; t++)
					{
						Apply(targets[t], value, dt);
					}
				}
			}
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
		private static float ResolveLevel(Vessel v, ModuleKrill root, int set, int axis, bool keysAllowed, float dt)
		{
			KrillAxisKind kind = root.GetAxisKind(set, axis);
			int dir = keysAllowed ? KrillAxisKeys.HeldDirection(axis) : 0;
			if (dir != 0)
			{
				keyDriven.Add(axis);
				return KeyLevel(v, root, set, axis, kind, dir, dt);
			}
			bool keyReleased = keyDriven.Remove(axis);

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
			int rest = root.GetAxisRest(set, axis);
			if (keyReleased)
			{
				// No channel to fall back on: the key was the only hand, spring back.
				KrillAxisSignal.Release(v, set, axis, rest);
			}
			return KrillAxisSignal.TryGet(v, set, axis, out float live) ? live : rest;
		}

		/// <summary>
		/// Level while a +/- key is held (§11.2, the kind decides). Fixed: the
		/// persisted value integrates at KeyRate (full-scale units per physics
		/// second, precision mode slows it) and stays — same MarkDirty-free write
		/// as the channel path. Spring: a KrillAxisSignal ramp toward ±1. Stock's
		/// ProcessAxis SNAPS its custom axes to ±1 while a key is down and ramps
		/// only in precision mode, at INPUT_KEYBOARD_SENSIVITITY per second; KRILL
		/// does the same by default (attack time 0 = infinite speed) and lets the
		/// settings turn the snap into a ramp outside precision mode.
		/// </summary>
		private static float KeyLevel(Vessel v, ModuleKrill root, int set, int axis, KrillAxisKind kind, int dir, float dt)
		{
			bool precision = FlightInputHandler.fetch != null && FlightInputHandler.fetch.precisionMode;
			if (kind == KrillAxisKind.Fixed)
			{
				float rate = KrillParams.AxisKeyRate * (precision ? PrecisionRateFactor : 1f);
				float next = Mathf.Clamp(root.GetAxisValue(set, axis) + dir * rate * dt, -1f, 1f);
				root.Data.SetAxisValue(set, axis, next);
				return next;
			}

			float speed;
			if (precision)
			{
				speed = GameSettings.INPUT_KEYBOARD_SENSIVITITY;
			}
			else
			{
				// The setting is the time of a full -1..+1 swing: two full-scale units.
				float attack = KrillParams.AxisKeyAttackSeconds;
				speed = attack > 0f ? 2f / attack : float.PositiveInfinity;
			}
			if (!KrillAxisSignal.TryGet(v, set, axis, out float current))
			{
				// First touch of this axis in the scene: the ramp starts from rest.
				current = root.GetAxisRest(set, axis);
				KrillAxisSignal.SetLive(v, set, axis, current);
			}
			KrillAxisSignal.SetTarget(v, set, axis, dir, speed);
			return KrillAxisSignal.TryGet(v, set, axis, out float live) ? live : current;
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
