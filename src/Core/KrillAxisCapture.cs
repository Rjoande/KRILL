using System;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// "Move it now" axis capture (2026-09-08, notes/axes-design.md A3) — the
	/// analog twin of KrillCapture, and the exact algorithm stock's own Input
	/// settings screen uses (decompiled SettingsInputBinding/InputSettings):
	/// sample every joystick channel Unity exposes when the capture starts, then
	/// the first channel that moves more than 0.5 from its baseline wins. The
	/// baseline makes a stick already resting off-center (a throttle parked at
	/// -1, a STECS axis returning to full scale) capturable without a false
	/// trigger, and the wide threshold ignores noise and lightly touched axes.
	///
	/// Keyboard and mouse are never candidates (only joyN.M channels are
	/// scanned — user decision: an axis comes from a controller or from the
	/// console, nothing else). Two keys are meaningful DURING a capture only:
	/// Escape cancels, Delete clears the axis's existing bind (an unbound axis
	/// is the one the console may drive by mouse, so "unbind" is a real
	/// operation here; since 2026-09-09 the key capture offers the same).
	///
	/// Same driver/lock/Escape mechanics as KrillCapture: ticked by KrillWindow
	/// (any scene) and KrillInputManager (flight), frame-guarded so both may
	/// tick in one frame; ALLBUTCAMERAS lock while pending; after an Escape
	/// cancel the lock stays until Escape's key-up is observed (PauseMenu opens
	/// on key-UP and only then checks the lock — see KrillCapture for the story).
	/// </summary>
	public static class KrillAxisCapture
	{
		// Unity's InputManager as shipped with KSP defines joy0..joy10, each with
		// axes 0..19 (verified in globalgamemanagers, 2026-09-07) — stock scans
		// 20 per device too (axisCount = 20 in its settings screens).
		private const int Devices = 11;
		private const int AxesPerDevice = 20;
		private const float Threshold = 0.5f;

		private const string LockId = "KRILL_axis_capture";
		private const int MaxUnlockWaitFrames = 180;

		private static int lastTickFrame = -1;
		private static readonly float[] baseline = new float[Devices * AxesPerDevice];

		public static bool IsCapturing { get; private set; }

		public static bool NeedsTick => IsCapturing || unlockWaitFramesLeft >= 0;

		private static Action<AxisBinding_Single> onCaptured;
		private static Action onCleared;
		private static Action onCancelled;
		private static int unlockWaitFramesLeft = -1;

		public static void Begin(Action<AxisBinding_Single> captured, Action cleared, Action cancelled)
		{
			for (int d = 0; d < Devices; d++)
			{
				for (int a = 0; a < AxesPerDevice; a++)
				{
					baseline[d * AxesPerDevice + a] = Input.GetAxis(ChannelId(d, a));
				}
			}
			IsCapturing = true;
			unlockWaitFramesLeft = -1;
			onCaptured = captured;
			onCleared = cleared;
			onCancelled = cancelled;
			InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, LockId);
		}

		/// <summary>Escape cancel: keeps the lock until Escape's key-up, see class doc.</summary>
		public static void Cancel()
		{
			if (!IsCapturing)
			{
				return;
			}
			IsCapturing = false;
			unlockWaitFramesLeft = MaxUnlockWaitFrames;
			Action cancelled = onCancelled;
			ClearCallbacks();
			cancelled?.Invoke();
		}

		/// <summary>Scene-teardown safety net: drop the lock now, no race to protect against.</summary>
		public static void ForceCancel()
		{
			bool wasPending = IsCapturing || unlockWaitFramesLeft >= 0;
			IsCapturing = false;
			unlockWaitFramesLeft = -1;
			ClearCallbacks();
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
				// Not a PauseMenu key: the lock can go right away.
				IsCapturing = false;
				InputLockManager.RemoveControlLock(LockId);
				Action cleared = onCleared;
				ClearCallbacks();
				cleared?.Invoke();
				return;
			}

			for (int d = 0; d < Devices; d++)
			{
				for (int a = 0; a < AxesPerDevice; a++)
				{
					float v = Input.GetAxis(ChannelId(d, a));
					if (Mathf.Abs(v - baseline[d * AxesPerDevice + a]) <= Threshold)
					{
						continue;
					}
					AxisBinding_Single bind = KrillAxisKeymap.Create(d, a, DeviceName(d));
					IsCapturing = false;
					InputLockManager.RemoveControlLock(LockId);
					Action<AxisBinding_Single> captured = onCaptured;
					ClearCallbacks();
					captured?.Invoke(bind);
					return;
				}
			}
		}

		private static string ChannelId(int device, int axis)
		{
			return "joy" + device + "." + axis;
		}

		/// <summary>Device name exactly as stock derives it (trimmed Unity name, or "Joystick N" for a nameless device) — it is the key GameSettings.INPUT_DEVICES resolves at load time, so it must match stock's spelling.</summary>
		private static string DeviceName(int device)
		{
			string[] names = Input.GetJoystickNames();
			if (device < names.Length && !string.IsNullOrEmpty(names[device]))
			{
				string trimmed = InputDevices.TrimDeviceName(names[device]);
				if (!string.IsNullOrEmpty(trimmed))
				{
					return trimmed;
				}
			}
			return "Joystick " + device;
		}

		private static void ClearCallbacks()
		{
			onCaptured = null;
			onCleared = null;
			onCancelled = null;
		}
	}
}
