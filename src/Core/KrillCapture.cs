using System;
using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// "Press it now" bind capture (design doc §4): the first non-modifier KeyCode
	/// (keyboard or joystick button) freshly pressed while capturing becomes the
	/// primary; every OTHER KeyCode already held at that same instant becomes a
	/// modifier. Driven by calling Tick() while NeedsTick is true.
	///
	/// Two independent drivers call Tick(): KrillInputManager (flight only) and
	/// KrillWindow (whichever scene it's open in, including the editor — where
	/// KrillInputManager does not exist at all, since it's KSPAddon(Startup.Flight)
	/// only). Confirmed the hard way: without a driver in the editor scene, a
	/// capture started from the M3 window there set the ALLBUTCAMERAS lock and
	/// then NEVER got ticked — no crash, no log line, just permanently locked
	/// input (had to force-close KSP). Tick() is frame-guarded below so having
	/// two drivers call it in the same frame (harmless in flight, where both
	/// exist) never double-fires a callback or double-counts the post-cancel wait.
	///
	/// Practical note for modifier combos: hold the modifier BEFORE pressing the
	/// primary. On the exact frame a key is first pressed its own GetKeyDown is
	/// true too, so if two keys were pressed on the very same frame either could
	/// be picked as primary; normal human input timing never hits this.
	/// </summary>
	public static class KrillCapture
	{
		// Tick() frame-guard: multiple drivers may call it in the same frame (see
		// class doc). Only the first call per frame does anything.
		private static int lastTickFrame = -1;

		// Cached once: scanning ~500 enum values with GetKeyDown/GetKey every frame
		// is only acceptable because this only runs while a capture is actually open.
		private static readonly KeyCode[] AllKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));

		// Confirmed the hard way (in-game test): a conventional modifier key (Shift
		// in particular) fires its OWN GetKeyDown the instant it's pressed, so
		// without this exclusion capture completes on the modifier alone before the
		// player ever reaches the intended primary. These keys can still end up in
		// a bind's modifier list (the scan below never excludes them there) — they
		// just can never BE the primary themselves, matching how every keybind
		// capture dialog elsewhere treats Shift/Ctrl/Alt/Cmd/Win. Joystick buttons
		// are deliberately NOT in this set: on a HOTAS any button can legitimately
		// be either a standalone primary or a held modifier (design doc §4).
		private static readonly HashSet<KeyCode> ModifierOnlyKeys = new HashSet<KeyCode>
		{
			KeyCode.LeftShift, KeyCode.RightShift,
			KeyCode.LeftControl, KeyCode.RightControl,
			KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.AltGr,
			KeyCode.LeftCommand, KeyCode.RightCommand,
			KeyCode.LeftApple, KeyCode.RightApple,
			KeyCode.LeftWindows, KeyCode.RightWindows,
		};

		// Excluded entirely (never primary, never modifier) — found while wiring
		// the 3-column window's Capture button: unlike M2's debug hooks (no UI
		// during capture, keyboard-only testing), the window stays fully
		// interactive while a capture is pending, so an incidental UI click
		// elsewhere in the window (or just moving the mouse toward the keyboard)
		// fires a real Mouse0/Mouse1 GetKeyDown that this same scan would
		// otherwise happily bind. Binding a group to a mouse click would also be
		// actively harmful even if deliberate — it would fire on ordinary
		// clicking everywhere in the game, not just in KRILL's own UI.
		private static readonly HashSet<KeyCode> ExcludedKeys = new HashSet<KeyCode>
		{
			KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2, KeyCode.Mouse3,
			KeyCode.Mouse4, KeyCode.Mouse5, KeyCode.Mouse6,
		};

		// While capturing, ANY key press is meant to be consumed by the bind, not
		// leak into the game — most visibly Escape opening the stock pause menu.
		// Same lock KRAB already uses for its own modal picking gesture.
		private const string LockId = "KRILL_capture";

		// Confirmed on decompiled PauseMenu.cs: it opens on GameSettings.PAUSE's
		// KEY-UP (release), not key-down, and only then checks
		// InputLockManager.IsUnlocked(ControlTypes.PAUSE). Our Escape-cancel fires
		// on Escape's key-DOWN, which happens on an earlier frame than that
		// key-up — releasing the lock right there (as a first attempt did) leaves
		// it gone well before PauseMenu's later check runs, so Esc still opened
		// the pause menu despite the lock.
		//
		// 2026-07-25: the original fix here held the lock for a FIXED number of
		// frames (3) instead of waiting for the actual key-up. That's a race
		// against however long the player physically holds Escape down, which is
		// NOT bounded to a handful of frames (3 frames is ~50ms at 60 FPS, well
		// under a normal keypress) — reported broken in flight for both the
		// group-bind and set-jump captures, both driven by this class. Fixed
		// properly: keep the lock until Input.GetKeyUp(Escape) is actually
		// observed. MaxUnlockWaitFrames is only a safety net for the rare case
		// the key-up is never seen (e.g. focus lost while Escape is still held),
		// so the lock can never get stuck forever.
		private const int MaxUnlockWaitFrames = 180;

		public static bool IsCapturing { get; private set; }

		/// <summary>True while a driver must keep calling Tick(): during an active capture, or while waiting for Escape's key-up after a cancel.</summary>
		public static bool NeedsTick => IsCapturing || unlockWaitFramesLeft >= 0;

		private static Action<KrillBind> onCaptured;
		private static Action onCancelled;
		private static int unlockWaitFramesLeft = -1;

		public static void Begin(Action<KrillBind> captured, Action cancelled)
		{
			IsCapturing = true;
			unlockWaitFramesLeft = -1;
			onCaptured = captured;
			onCancelled = cancelled;
			InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, LockId);
		}

		/// <summary>User-facing cancel (Escape): keeps the lock until Escape's key-up is observed, see MaxUnlockWaitFrames.</summary>
		public static void Cancel()
		{
			if (!IsCapturing)
			{
				return;
			}
			IsCapturing = false;
			unlockWaitFramesLeft = MaxUnlockWaitFrames;
			Action cancelled = onCancelled;
			onCaptured = null;
			onCancelled = null;
			cancelled?.Invoke();
		}

		/// <summary>
		/// Scene-teardown safety net (KrillInputManager.OnDestroy): no PauseMenu
		/// race to protect against once the scene itself is going away, and nothing
		/// will be left alive to run out the normal wait — drop the lock now.
		/// </summary>
		public static void ForceCancel()
		{
			bool wasPending = IsCapturing || unlockWaitFramesLeft >= 0;
			IsCapturing = false;
			unlockWaitFramesLeft = -1;
			onCaptured = null;
			onCancelled = null;
			if (wasPending)
			{
				InputLockManager.RemoveControlLock(LockId);
			}
		}

		public static void Tick()
		{
			if (Time.frameCount == lastTickFrame)
			{
				return;
			}
			lastTickFrame = Time.frameCount;

			if (unlockWaitFramesLeft >= 0)
			{
				unlockWaitFramesLeft--;
				if (Input.GetKeyUp(KeyCode.Escape) || unlockWaitFramesLeft < 0)
				{
					unlockWaitFramesLeft = -1;
					InputLockManager.RemoveControlLock(LockId);
				}
				return;
			}
			if (!IsCapturing)
			{
				return;
			}
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				Cancel();
				return;
			}
			for (int i = 0; i < AllKeyCodes.Length; i++)
			{
				KeyCode candidate = AllKeyCodes[i];
				if (candidate == KeyCode.None || candidate == KeyCode.Escape
					|| ModifierOnlyKeys.Contains(candidate) || ExcludedKeys.Contains(candidate))
				{
					continue;
				}
				if (!Input.GetKeyDown(candidate))
				{
					continue;
				}

				KrillBind bind = new KrillBind { primary = candidate };
				for (int j = 0; j < AllKeyCodes.Length; j++)
				{
					KeyCode modCandidate = AllKeyCodes[j];
					if (modCandidate == candidate || modCandidate == KeyCode.None || ExcludedKeys.Contains(modCandidate))
					{
						continue;
					}
					if (Input.GetKey(modCandidate))
					{
						bind.modifiers.Add(modCandidate);
					}
				}

				IsCapturing = false;
				InputLockManager.RemoveControlLock(LockId);
				Action<KrillBind> captured = onCaptured;
				onCaptured = null;
				onCancelled = null;
				captured?.Invoke(bind);
				return;
			}
		}
	}
}
