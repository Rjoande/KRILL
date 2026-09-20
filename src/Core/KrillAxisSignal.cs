using System;
using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// Runtime-only level of the Spring axes, the axis twin of KrillSignal: one
	/// entry per (vessel, set, axis) ramping toward a target at a given speed, so
	/// a release returns to rest. Never persisted — a quicksave must not stick.
	/// </summary>
	public static class KrillAxisSignal
	{
		/// <summary>Return-to-rest ramp, in units per REAL second (~0.33 s end to end): unscaled, so it never slows down with physics warp.</summary>
		public const float ReturnSpeed = 6f;

		private struct AxisKey : IEquatable<AxisKey>
		{
			public Guid vessel;
			public int set;
			public int axis;

			public bool Equals(AxisKey other)
			{
				return vessel == other.vessel && set == other.set && axis == other.axis;
			}

			public override bool Equals(object obj)
			{
				return obj is AxisKey other && Equals(other);
			}

			public override int GetHashCode()
			{
				return (vessel.GetHashCode() * 397 ^ set) * 397 ^ axis;
			}
		}

		private class Entry
		{
			public float value;
			public bool ramping;
			public float target;
			public float speed;
		}

		private static readonly Dictionary<AxisKey, Entry> entries = new Dictionary<AxisKey, Entry>();
		private static readonly List<AxisKey> scratchKeys = new List<AxisKey>();

		private static bool TryKey(Vessel v, int set, int axis, out AxisKey key)
		{
			key = default;
			if (v == null)
			{
				return false;
			}
			key = new AxisKey { vessel = v.id, set = set, axis = axis };
			return true;
		}

		private static Entry GetOrAdd(AxisKey key)
		{
			if (!entries.TryGetValue(key, out Entry e))
			{
				e = new Entry();
				entries[key] = e;
			}
			return e;
		}

		/// <summary>Sets the live level and stops any ramp — the controller each tick, or the slider while held.</summary>
		public static void SetLive(Vessel v, int set, int axis, float value)
		{
			if (!TryKey(v, set, axis, out AxisKey key))
			{
				return;
			}
			Entry e = GetOrAdd(key);
			e.value = Mathf.Clamp(value, -1f, 1f);
			e.ramping = false;
		}

		/// <summary>
		/// Starts (or retargets) a ramp toward `target` at `speed` units per real
		/// second; infinity lands immediately. Retargeting a running ramp to the same
		/// target is a no-op, so a key held down across ticks never jitters.
		/// </summary>
		public static void SetTarget(Vessel v, int set, int axis, float target, float speed)
		{
			if (!TryKey(v, set, axis, out AxisKey key))
			{
				return;
			}
			Entry e = GetOrAdd(key);
			target = Mathf.Clamp(target, -1f, 1f);
			if (float.IsInfinity(speed) || speed <= 0f)
			{
				e.value = target;
				e.ramping = false;
				return;
			}
			e.target = target;
			e.speed = speed;
			e.ramping = !Mathf.Approximately(e.value, target);
		}

		/// <summary>Starts the return ramp toward `rest`: the slider or a +/- key was let go with no channel to fall back on.</summary>
		public static void Release(Vessel v, int set, int axis, float rest)
		{
			if (!TryKey(v, set, axis, out AxisKey key) || !entries.ContainsKey(key))
			{
				return;
			}
			SetTarget(v, set, axis, rest, ReturnSpeed);
		}

		public static bool TryGet(Vessel v, int set, int axis, out float value)
		{
			value = 0f;
			if (!TryKey(v, set, axis, out AxisKey key) || !entries.TryGetValue(key, out Entry e))
			{
				return false;
			}
			value = e.value;
			return true;
		}

		/// <summary>Advances every ramp; call once per frame with Time.unscaledDeltaTime.</summary>
		public static void Tick(float unscaledDeltaTime)
		{
			if (entries.Count == 0 || unscaledDeltaTime <= 0f)
			{
				return;
			}
			foreach (KeyValuePair<AxisKey, Entry> kv in entries)
			{
				Entry e = kv.Value;
				if (!e.ramping)
				{
					continue;
				}
				e.value = Mathf.MoveTowards(e.value, e.target, e.speed * unscaledDeltaTime);
				if (Mathf.Approximately(e.value, e.target))
				{
					e.value = e.target;
					e.ramping = false;
				}
			}
		}

		/// <summary>Drops every entry of one vessel (it left the scene) or, with null, everything (scene teardown).</summary>
		public static void Clear(Vessel v = null)
		{
			if (v == null)
			{
				entries.Clear();
				return;
			}
			scratchKeys.Clear();
			foreach (AxisKey key in entries.Keys)
			{
				if (key.vessel == v.id)
				{
					scratchKeys.Add(key);
				}
			}
			for (int i = 0; i < scratchKeys.Count; i++)
			{
				entries.Remove(scratchKeys[i]);
			}
		}
	}
}
