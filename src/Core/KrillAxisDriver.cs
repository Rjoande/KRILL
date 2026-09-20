using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// The extended-axis engine: every physics tick it resolves the level of each
	/// extended axis and writes it into the assigned fields — the analog twin of
	/// KrillActivation, and the one place that touches a BaseAxisField.
	/// </summary>
	public static class KrillAxisDriver
	{
		/// <summary>Precision mode (CapsLock) divides the Fixed key rate by this; stock has no comparable axis, so the factor is KRILL's own.</summary>
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
		// Axes a held key was driving last tick: the release edge is what starts a
		// Spring's return when no channel re-asserts the level.
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
			// Bound axes with no field assigned still get their level refreshed:
			// readers want it regardless of what it drives.
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

		/// <summary>Groups the set's axis assignments by axis number. Rebuilt each tick: cheap, and never stale after docking or a set switch.</summary>
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

		/// <summary>
		/// The axis's current -1..1 level, stored where KrillQuery.GetAxisState reads
		/// it. A held +/- key wins, then the controller channel, then the axis's own
		/// memory (a Fixed value, a Spring's live signal or its rest).
		/// </summary>
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
					// No MarkDirty on purpose: the mirror only matters for editor clones and
					// OnSave serializes Data itself, so re-stringifying it 50 times a second
					// while a throttle moves would be pure waste.
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
		/// Level while a +/- key is held; the kind decides. Fixed integrates its
		/// persisted value at KeyRate and stays there, Spring ramps toward ±1 at the
		/// attack from the settings (0 = the instant snap stock's own keys do).
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

		/// <summary>
		/// Writes one assignment: absolute maps -1..1 onto the field's range,
		/// incremental nudges it by stock's own formula computed here — never
		/// IncrementAxis, which would use the field's own speed multiplier.
		/// </summary>
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
