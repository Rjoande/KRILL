using System;
using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// Runtime-only level of the extended axes that have NO persisted value
	/// (2026-09-09, notes/axes-design.md A4) — the axis twin of KrillSignal:
	///
	///   Spring - the live -1..1 level per (vessel, set, axis), written every
	///            physics tick by the driver from the controller, or by the
	///            window's slider while the mouse holds it; on release it
	///            RETURNS to the axis's rest value over a short real-time ramp
	///            (Tick), the one place where the Spring kind is functional
	///            rather than a reminder (§2: with a physical stick, the
	///            hardware itself springs back).
	///   Fixed  - not here: its level is the PERSISTED value on the vessel root
	///            (KrillAxisSetting.value), because it must survive save/load.
	///
	/// Never persisted, dies with the flight scene (KrillInputManager.OnDestroy
	/// -> Clear): a quicksave mid-deflection must not come back as a stuck axis.
	/// </summary>
	public static class KrillAxisSignal
	{
		/// <summary>Return-to-rest ramp for a released Spring axis, in full-scale units per REAL second (a -1..+1 swing takes ~0.33 s). Unscaled time: a UI animation must not slow down or speed up with physics warp.</summary>
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
			public bool returning;
			public float target;
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

		/// <summary>Sets the live level and stops any return ramp — the controller each tick, or the slider while held.</summary>
		public static void SetLive(Vessel v, int set, int axis, float value)
		{
			if (!TryKey(v, set, axis, out AxisKey key))
			{
				return;
			}
			if (!entries.TryGetValue(key, out Entry e))
			{
				e = new Entry();
				entries[key] = e;
			}
			e.value = Mathf.Clamp(value, -1f, 1f);
			e.returning = false;
		}

		/// <summary>Starts the return ramp toward `rest` (slider let go on an unbound Spring axis).</summary>
		public static void Release(Vessel v, int set, int axis, float rest)
		{
			if (!TryKey(v, set, axis, out AxisKey key) || !entries.TryGetValue(key, out Entry e))
			{
				return;
			}
			e.returning = true;
			e.target = Mathf.Clamp(rest, -1f, 1f);
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

		/// <summary>Advances every return ramp; call once per frame with Time.unscaledDeltaTime.</summary>
		public static void Tick(float unscaledDeltaTime)
		{
			if (entries.Count == 0 || unscaledDeltaTime <= 0f)
			{
				return;
			}
			float step = ReturnSpeed * unscaledDeltaTime;
			foreach (KeyValuePair<AxisKey, Entry> kv in entries)
			{
				Entry e = kv.Value;
				if (!e.returning)
				{
					continue;
				}
				e.value = Mathf.MoveTowards(e.value, e.target, step);
				if (Mathf.Approximately(e.value, e.target))
				{
					e.value = e.target;
					e.returning = false;
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
