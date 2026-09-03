using System;
using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// Where a Hold-kind press comes from. One record per (group, source): a
	/// given source can hold a given group once at a time, but several sources
	/// can hold the same group together — the level is their OR, and no source
	/// ever has to "win" over another (releasing the mouse while the key is
	/// still down leaves the group held, exactly as the "held means held"
	/// contract promises readers).
	/// </summary>
	public enum KrillHoldSource
	{
		Key = 0,
		Window = 1,
		Console = 2,
	}

	/// <summary>
	/// The signal layer (2026-09-02 rework, notes/kind-signal-analysis.md): the
	/// ONE 0/1 level a (set, group) presents to readers — KRAB, the console, any
	/// other mod — kept in the storage class each kind's contract demands, and
	/// written directly by whoever produces it. No reconciliation poller, no
	/// transient state on disk.
	///
	///   Pulse  - runtime expiry timestamp per (vessel, set, group), here.
	///   Hold   - runtime set of asserted sources per group, here; the level
	///            is "at least one source is holding it right now".
	///   Toggle - a PERSISTED bool on the vessel root part (KrillGroupSignal,
	///            read via ModuleKrill.GetToggleSignal) — not here, because it
	///            must survive save/load; nothing else in this class may.
	///
	/// Everything here dies with the flight scene
	/// (KrillInputManager.OnDestroy -> KrillActivation.ReleaseAllHolds), which
	/// is correct by design for Pulse and Hold: both are transient.
	///
	/// The previous design kept Hold's level in the persisted direction bit and
	/// relied on a per-frame poller to reconcile it against the key/UI state —
	/// that produced the whole family of "stuck at 1" reports (a UI-only group
	/// left the polled set on release before the poller could see it; a
	/// quicksave mid-hold came back as a real Deactivate on load; any window
	/// rebuild mid-press orphaned the release). None of those can exist here:
	/// the level IS the set of sources, and each source adds/removes itself.
	/// </summary>
	public static class KrillSignal
	{
		// ------------------------------------------------------------------ pulse

		/// <summary>
		/// How long a Pulse-kind group reads as 1 after it fires (user decision
		/// 2026-08-31, reconfirmed 2026-09-02: constant, no slider). Real seconds,
		/// not game seconds — see StartPulse. A single frame would be technically
		/// readable but useless: the player can't see it, and it gives the
		/// console nothing to light a lamp with.
		/// </summary>
		public const float PulseSeconds = 0.75f;

		/// <summary>Scopes a live pulse to one (vessel, set, group) so switching vessels or sets mid-pulse can never show a phantom lit lamp on an unrelated craft.</summary>
		private struct PulseKey : IEquatable<PulseKey>
		{
			public Guid vessel;
			public int set;
			public int group;

			public bool Equals(PulseKey other)
			{
				return vessel == other.vessel && set == other.set && group == other.group;
			}

			public override bool Equals(object obj)
			{
				return obj is PulseKey other && Equals(other);
			}

			public override int GetHashCode()
			{
				return (vessel.GetHashCode() * 397 ^ set) * 397 ^ group;
			}
		}

		/// <summary>
		/// Expiry time (Time.unscaledTime) per currently-pulsing group. Runtime
		/// only, NEVER persisted: a pulse is transient by nature, and a timestamp
		/// written into a craft file would come back as a stale half-finished
		/// pulse on load. Dying with the scene is the correct behavior.
		/// </summary>
		private static readonly Dictionary<PulseKey, float> pulseExpiry = new Dictionary<PulseKey, float>();

		/// <summary>
		/// Starts (or restarts, if one is already running) the pulse for a group.
		/// Deliberately unscaled time: Time.time is scaled by Unity's timeScale,
		/// which KSP drives for physics warp — a 750ms lamp would flash for a
		/// wildly different wall-clock duration at 4x. An annunciator blinks in
		/// real seconds regardless of warp.
		/// </summary>
		internal static void StartPulse(Vessel v, int set, int group)
		{
			if (v == null)
			{
				return;
			}
			PruneExpiredPulses();
			pulseExpiry[new PulseKey { vessel = v.id, set = set, group = group }] =
				Time.unscaledTime + PulseSeconds;
		}

		/// <summary>Keeps the dictionary from growing across a long flight — entries are short-lived by construction, so a sweep whenever a new one starts is enough.</summary>
		private static void PruneExpiredPulses()
		{
			if (pulseExpiry.Count == 0)
			{
				return;
			}
			float now = Time.unscaledTime;
			List<PulseKey> expired = null;
			foreach (KeyValuePair<PulseKey, float> kv in pulseExpiry)
			{
				if (kv.Value <= now)
				{
					(expired ?? (expired = new List<PulseKey>())).Add(kv.Key);
				}
			}
			if (expired == null)
			{
				return;
			}
			for (int i = 0; i < expired.Count; i++)
			{
				pulseExpiry.Remove(expired[i]);
			}
		}

		/// <summary>True while a Pulse-kind group is still within its post-fire window — the level KrillQuery.GroupState.signal reports for that kind.</summary>
		public static bool IsPulsing(Vessel v, int set, int group)
		{
			if (v == null)
			{
				return false;
			}
			return pulseExpiry.TryGetValue(new PulseKey { vessel = v.id, set = set, group = group }, out float expiry)
				&& expiry > Time.unscaledTime;
		}

		internal static void ClearPulses()
		{
			pulseExpiry.Clear();
		}

		// ------------------------------------------------------------------- hold

		private struct HoldKey : IEquatable<HoldKey>
		{
			public int group;
			public KrillHoldSource source;

			public bool Equals(HoldKey other)
			{
				return group == other.group && source == other.source;
			}

			public override bool Equals(object obj)
			{
				return obj is HoldKey other && Equals(other);
			}

			public override int GetHashCode()
			{
				return group * 397 ^ (int)source;
			}
		}

		/// <summary>
		/// Where a press STARTED: the vessel and set current at press time. A
		/// hold releases where it began (user decision 2026-09-02) — switching
		/// set or active vessel mid-press neither moves it nor leaks it: the
		/// eventual release sends Deactivate to exactly the (vessel, set, group)
		/// that received Activate, never to whatever happens to be current then.
		/// </summary>
		public struct HoldRecord
		{
			public Vessel vessel;
			public int set;
			public int group;
		}

		private static readonly Dictionary<HoldKey, HoldRecord> holds = new Dictionary<HoldKey, HoldRecord>();

		/// <summary>Is this source currently holding this group? The key poller compares this against the physical key's live state to turn a level into press/release edges (KrillInputManager).</summary>
		public static bool HasSource(int group, KrillHoldSource source)
		{
			return holds.ContainsKey(new HoldKey { group = group, source = source });
		}

		/// <summary>The Hold-kind level: at least one source is holding (vessel, set, group) right now.</summary>
		public static bool IsHeld(Vessel v, int set, int group)
		{
			if (v == null)
			{
				return false;
			}
			foreach (KeyValuePair<HoldKey, HoldRecord> kv in holds)
			{
				if (kv.Key.group == group && kv.Value.vessel == v && kv.Value.set == set)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>True while the KRILL window's own Hold button is pressed on ANY group — the window defers its content rebuilds until this is false, so the button being held is never torn down from under the mouse by an unrelated event (see KrillWindow.LateUpdate).</summary>
		public static bool AnyWindowHeld
		{
			get
			{
				foreach (KeyValuePair<HoldKey, HoldRecord> kv in holds)
				{
					if (kv.Key.source == KrillHoldSource.Window)
					{
						return true;
					}
				}
				return false;
			}
		}

		/// <summary>Records a press. Returns true only if this press turned the (vessel, set, group) level 0 -> 1 — i.e. the caller must send Activate. A repeated press from the same source is ignored.</summary>
		internal static bool AddSource(Vessel v, int set, int group, KrillHoldSource source)
		{
			HoldKey key = new HoldKey { group = group, source = source };
			if (holds.ContainsKey(key))
			{
				return false;
			}
			bool wasHeld = IsHeld(v, set, group);
			holds[key] = new HoldRecord { vessel = v, set = set, group = group };
			return !wasHeld;
		}

		/// <summary>Records a release. Returns true only if this release turned the recorded (vessel, set, group) level 1 -> 0 — i.e. the caller must send Deactivate there. `record` is valid whenever the source was actually holding something (found), even when the level stays 1 because another source still holds it.</summary>
		internal static bool RemoveSource(int group, KrillHoldSource source, out HoldRecord record, out bool found)
		{
			HoldKey key = new HoldKey { group = group, source = source };
			found = holds.TryGetValue(key, out record);
			if (!found)
			{
				return false;
			}
			holds.Remove(key);
			return !IsHeld(record.vessel, record.set, record.group);
		}

		/// <summary>Empties every hold record and returns the distinct (vessel, set, group) levels that were 1 — the caller deactivates each of them. Scene teardown only.</summary>
		internal static List<HoldRecord> DrainHolds()
		{
			List<HoldRecord> levels = new List<HoldRecord>();
			foreach (KeyValuePair<HoldKey, HoldRecord> kv in holds)
			{
				bool dup = false;
				for (int i = 0; i < levels.Count; i++)
				{
					if (levels[i].vessel == kv.Value.vessel && levels[i].set == kv.Value.set && levels[i].group == kv.Value.group)
					{
						dup = true;
						break;
					}
				}
				if (!dup)
				{
					levels.Add(kv.Value);
				}
			}
			holds.Clear();
			return levels;
		}
	}
}
