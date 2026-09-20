using System;
using System.Collections.Generic;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// "Press it now" bind capture: the first non-modifier KeyCode freshly pressed
	/// becomes the primary, every other one held at that instant a modifier. Ticked
	/// by KrillInputManager in flight and by KrillWindow in any scene.
	/// </summary>
	public static class KrillCapture
	{
		// Frame-guard: two drivers may call Tick() in the same frame (both exist in
		// flight); only the first call per frame does anything.
		private static int lastTickFrame = -1;

		// Cached once: scanning ~500 enum values per frame is only acceptable because
		// it runs while a capture is open and never otherwise.
		private static readonly KeyCode[] AllKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));

		// Shift & co. fire their own GetKeyDown the instant they are pressed, so
		// without this the capture would complete on the modifier alone. They can
		// still be modifiers; joystick buttons stay out, either role is valid there.
		private static readonly HashSet<KeyCode> ModifierOnlyKeys = new HashSet<KeyCode>
		{
			KeyCode.LeftShift, KeyCode.RightShift,
			KeyCode.LeftControl, KeyCode.RightControl,
			KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.AltGr,
			KeyCode.LeftCommand, KeyCode.RightCommand,
			KeyCode.LeftApple, KeyCode.RightApple,
			KeyCode.LeftWindows, KeyCode.RightWindows,
		};

		// Excluded entirely, never primary and never modifier: the window stays
		// interactive during a capture, so a stray click would be captured — and a
		// group bound to a mouse button would fire on clicking anywhere in the game.
		private static readonly HashSet<KeyCode> ExcludedKeys = new HashSet<KeyCode>
		{
			KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2, KeyCode.Mouse3,
			KeyCode.Mouse4, KeyCode.Mouse5, KeyCode.Mouse6,
			// Delete during a capture clears the bind instead, so it can never be bound.
			KeyCode.Delete,
		};

		// While capturing, any key press belongs to the bind and must not leak into
		// the game — most visibly Escape opening the stock pause menu.
		private const string LockId = "KRILL_capture";

		// The stock pause menu opens on PAUSE's key-UP and only then checks the input
		// lock, while our Escape-cancel fires on key-DOWN — so the lock is held until
		// the key-up is seen. This cap only covers a key-up that never arrives.
		private const int MaxUnlockWaitFrames = 180;

		public static bool IsCapturing { get; private set; }

		/// <summary>True while a driver must keep calling Tick(): during a capture, or while waiting for Escape's key-up after a cancel.</summary>
		public static bool NeedsTick => IsCapturing || unlockWaitFramesLeft >= 0;

		private static Action<KrillBind> onCaptured;
		private static Action onCancelled;
		private static Action onCleared;
		private static int unlockWaitFramesLeft = -1;

		/// <summary>`cleared` (optional) runs on Delete: the caller removes the existing bind. Escape still cancels, leaving it as it was.</summary>
		public static void Begin(Action<KrillBind> captured, Action cancelled, Action cleared = null)
		{
			IsCapturing = true;
			unlockWaitFramesLeft = -1;
			onCaptured = captured;
			onCancelled = cancelled;
			onCleared = cleared;
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
			onCleared = null;
			cancelled?.Invoke();
		}

		/// <summary>Scene-teardown safety net: no pause-menu race left to protect against, and nobody to run out the wait — drop the lock now.</summary>
		public static void ForceCancel()
		{
			bool wasPending = IsCapturing || unlockWaitFramesLeft >= 0;
			IsCapturing = false;
			unlockWaitFramesLeft = -1;
			onCaptured = null;
			onCancelled = null;
			onCleared = null;
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
			if (Input.GetKeyDown(KeyCode.Delete))
			{
				// Not a PauseMenu key: no key-up wait needed, the lock can go now.
				IsCapturing = false;
				InputLockManager.RemoveControlLock(LockId);
				Action cleared = onCleared;
				onCaptured = null;
				onCancelled = null;
				onCleared = null;
				cleared?.Invoke();
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
				onCleared = null;
				captured?.Invoke(bind);
				return;
			}
		}
	}
}
