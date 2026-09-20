using System;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// "Move it now" axis capture, the analog twin of KrillCapture and the same
	/// algorithm stock's input screen uses: baseline every joyN.M channel on Begin,
	/// then the first to move past the threshold wins. Escape cancels, Delete unbinds.
	/// </summary>
	public static class KrillAxisCapture
	{
		// KSP's Unity InputManager defines joy0..joy10 with axes 0..19; stock scans
		// the same 20 channels per device.
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

		/// <summary>Escape cancel: the lock is held until Escape's key-up, or the pause menu (which opens on key-UP) would slip through.</summary>
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

		/// <summary>Device name exactly as stock derives it: GameSettings.INPUT_DEVICES resolves on it at load time, so the spelling must match.</summary>
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
