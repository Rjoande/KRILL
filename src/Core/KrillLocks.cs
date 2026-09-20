using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// "Is this control type unlocked, ignoring KRILL's own locks?" — every KRILL
	/// lock uses ALLBUTCAMERAS, which would otherwise freeze the axis driver while
	/// the mouse is over the window. Pause, modals and other mods always count.
	/// </summary>
	public static class KrillLocks
	{
		/// <summary>Id of the lock KrillUi's text fields hold while focused. Lives here so Core never depends on UI.</summary>
		public const string TypingLockId = "KRILL_EDITOR_TYPING";

		private static bool keysBlockedLogged;

		/// <param name="mask">Control type(s) to test.</param>
		/// <param name="honourTyping">True for key polling (KRILL's typing lock blocks it), false for controller-channel reads.</param>
		public static bool Unlocked(ControlTypes mask, bool honourTyping)
		{
			ulong others = 0;
			foreach (KeyValuePair<string, ulong> kv in InputLockManager.lockStack)
			{
				if (Counts(kv.Key, honourTyping))
				{
					others |= kv.Value;
				}
			}
			return (others & (ulong)mask) == 0;
		}

		/// <summary>
		/// The one gate every key poll goes through: no capture running and nothing
		/// outside KRILL holding the keyboard. Logs once per transition which locks
		/// are responsible, so a silent "my keys stopped working" has an answer.
		/// </summary>
		public static bool KeysAllowed()
		{
			if (KrillCapture.IsCapturing)
			{
				return false;
			}
			bool allowed = Unlocked(ControlTypes.KEYBOARDINPUT, honourTyping: true);
			if (!allowed && !keysBlockedLogged)
			{
				keysBlockedLogged = true;
				Debug.Log("[KRILL] key polling paused by input lock(s): " + DescribeBlocking(ControlTypes.KEYBOARDINPUT, honourTyping: true));
			}
			else if (allowed && keysBlockedLogged)
			{
				keysBlockedLogged = false;
				Debug.Log("[KRILL] key polling resumed");
			}
			return allowed;
		}

		/// <summary>"id (0x…), id (0x…)" of the counted locks whose mask overlaps `mask`.</summary>
		public static string DescribeBlocking(ControlTypes mask, bool honourTyping)
		{
			StringBuilder sb = new StringBuilder();
			foreach (KeyValuePair<string, ulong> kv in InputLockManager.lockStack)
			{
				if (!Counts(kv.Key, honourTyping) || (kv.Value & (ulong)mask) == 0)
				{
					continue;
				}
				if (sb.Length > 0)
				{
					sb.Append(", ");
				}
				sb.Append(kv.Key).Append(" (0x").Append(kv.Value.ToString("X")).Append(')');
			}
			return sb.Length > 0 ? sb.ToString() : "(none)";
		}

		private static bool Counts(string lockId, bool honourTyping)
		{
			bool ours = lockId.StartsWith("KRILL", System.StringComparison.Ordinal);
			return !ours || (honourTyping && lockId == TypingLockId);
		}
	}
}
