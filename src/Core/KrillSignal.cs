using System;
using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// Where a Hold-kind press comes from. One record per (group, source): several
	/// sources can hold the same group together and the level is their OR, so
	/// releasing the mouse while the key is still down leaves the group held.
	/// </summary>
	public enum KrillHoldSource
	{
		Key = 0,
		Window = 1,
		Console = 2,
	}

	/// <summary>
	/// The signal layer: the one 0/1 level a (set, group) presents to readers,
	/// written directly by whoever produces it. A Pulse timestamp and the set of
	/// Hold sources live here, both transient; a Toggle's signal is persisted.
	/// </summary>
	public static class KrillSignal
	{
		// ------------------------------------------------------------------ pulse

		/// <summary>
		/// How long a Pulse-kind group reads as 1 after it fires, in real seconds. A
		/// single frame would be technically readable but useless: nothing the player
		/// or the console could ever light a lamp with.
		/// </summary>
		public const float PulseSeconds = 0.75f;

		/// <summary>Scopes a pulse to one (vessel, set, group), so switching vessel or set mid-pulse never lights a lamp on an unrelated craft.</summary>
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

		/// <summary>Expiry time per pulsing group. Never persisted: a timestamp in a craft file would come back as a stale half-finished pulse.</summary>
		private static readonly Dictionary<PulseKey, float> pulseExpiry = new Dictionary<PulseKey, float>();

		/// <summary>
		/// Starts, or restarts, the pulse for a group. Unscaled time on purpose:
		/// Time.time follows physics warp, and an annunciator must blink for the same
		/// wall-clock 750 ms at 1x and at 4x.
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

		/// <summary>Keeps the dictionary from growing: entries are short-lived, so a sweep whenever a new pulse starts is enough.</summary>
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

		/// <summary>True while a Pulse-kind group is still within its post-fire window: the level readers see for that kind.</summary>
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
		/// Where a press STARTED. A hold releases where it began, so switching set or
		/// vessel mid-press sends the eventual Deactivate to the same (vessel, set,
		/// group) that got the Activate, never to whatever is current by then.
		/// </summary>
		public struct HoldRecord
		{
			public Vessel vessel;
			public int set;
			public int group;
		}

		private static readonly Dictionary<HoldKey, HoldRecord> holds = new Dictionary<HoldKey, HoldRecord>();

		/// <summary>Is this source currently holding this group? The key poller diffs it against the live key state to get press/release edges.</summary>
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

		/// <summary>True while the window's Hold button is pressed on any group: it defers rebuilds until then, so the button is never torn down mid-press.</summary>
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

		/// <summary>Records a press. True only when it took the level 0 -> 1, i.e. the caller must send Activate; a repeated press is ignored.</summary>
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

		/// <summary>Records a release. True only when it took the level 1 -> 0; `record` is valid whenever the source was holding something (found).</summary>
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

		/// <summary>Empties every hold record and returns the distinct levels that were 1, for the caller to deactivate. Scene teardown only.</summary>
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
