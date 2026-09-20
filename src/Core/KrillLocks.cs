using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// KRILL's view of InputLockManager (2026-09-18, K1 — generalizes the check
	/// KrillAxisDriver introduced on 2026-09-11): "is this control type unlocked,
	/// ignoring the locks KRILL itself holds?". Every KRILL lock id starts with
	/// "KRILL" and every one of them uses ALLBUTCAMERAS, which contains almost
	/// every bit — so the window's own hover lock (FocusLock) would otherwise
	/// freeze the axis driver while the player drags the footer slider, and a
	/// bound stick whenever the mouse crossed the window. Pause, modal dialogs
	/// and other mods' locks are always honoured.
	///
	/// The one KRILL lock that CAN count is the text-field typing lock
	/// (KrillUi's TypingLock, KEYBOARDINPUT): key polling must stop while the
	/// player types a group or axis name, or a "k" in the name field would fire
	/// group K / nudge an axis. The physical-channel read path keeps ignoring it
	/// (a stick is not the keyboard). lockStack is a small dictionary; walking
	/// it per frame or per physics tick is nothing.
	/// </summary>
	public static class KrillLocks
	{
		/// <summary>Id of the lock KrillUi's text fields hold while focused. Lives here so Core never depends on UI.</summary>
		public const string TypingLockId = "KRILL_EDITOR_TYPING";

		private static bool keysBlockedLogged;

		/// <param name="mask">Control type(s) to test.</param>
		/// <param name="honourTyping">True for key polling (the typing lock blocks it), false for controller-channel reads (it doesn't).</param>
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
		/// The one gate every key poll goes through (group keys, set-jump keys,
		/// axis +/- keys): no key capture in progress and nothing outside KRILL —
		/// or KRILL's own typing lock — holding the keyboard. Logs ONCE, on the
		/// transition into "blocked", which locks are responsible, so a silent
		/// "my keys stopped working" has an answer in KSP.log.
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
