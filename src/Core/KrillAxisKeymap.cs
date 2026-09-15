using System.Collections.Generic;
using System.IO;
using KSP.Localization;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// The player's global AXIS keymap: extended axis number -> the physical
	/// controller channel driving it (2026-09-08, notes/axes-design.md A3). Same
	/// scoping as KrillKeymap — global, per-player, never per-craft: the craft
	/// only says what the axis does, the keymap says which stick moves it.
	///
	/// Each entry IS a stock AxisBinding_Single (reused, not reimplemented —
	/// decompiled 2026-09-07): it persists the device NAME plus the axis index
	/// and resolves the volatile "joyN" number back at load time through
	/// GameSettings.INPUT_DEVICES, so a bind survives Unity renumbering the
	/// joysticks between sessions. KRILL creates its entries with deadzone 0,
	/// sensitivity 1, scale 1 and no inversion (user decision: curves and dead
	/// zones belong to the device/HID software, KRILL reads the raw channel);
	/// the class's lock-mask and flight-mode switch handling come along for free.
	/// Its own file under PluginData/ (same reasoning as keymap.cfg: ModuleManager
	/// never scans that folder).
	/// </summary>
	public static class KrillAxisKeymap
	{
		private const string RootNodeName = "KRILL_AXIS_KEYMAP";
		private const string EntryNodeName = "AXIS";

		private static readonly string FilePath =
			KSPUtil.ApplicationRootPath + "GameData/KRILL/PluginData/axiskeymap.cfg";

		private static Dictionary<int, AxisBinding_Single> binds;

		public static IReadOnlyDictionary<int, AxisBinding_Single> Binds
		{
			get { EnsureLoaded(); return binds; }
		}

		public static AxisBinding_Single GetBind(int axis)
		{
			EnsureLoaded();
			return binds.TryGetValue(axis, out AxisBinding_Single b) ? b : null;
		}

		public static void SetBind(int axis, AxisBinding_Single bind)
		{
			EnsureLoaded();
			binds[axis] = bind;
			Save();
		}

		public static void RemoveBind(int axis)
		{
			EnsureLoaded();
			if (binds.Remove(axis))
			{
				Save();
			}
		}

		/// <summary>True if the axis has a bind that currently resolves to a real channel (a bind whose device is unplugged still exists, but reads nothing).</summary>
		public static bool IsBound(int axis)
		{
			AxisBinding_Single b = GetBind(axis);
			return b != null && b.idTag != "None";
		}

		/// <summary>
		/// Current value of the channel bound to `axis`, in -1..1, with KRILL's
		/// GLOBAL dead zone (KrillParams.AxisDeadzone) applied — the driver's read
		/// path (A4). False when the axis is unbound or its device isn't connected.
		/// The binding's own deadzone field stays 0 on purpose: the dead zone is a
		/// player setting applied here, never baked into the persisted bind, so a
		/// change in the settings page reaches every axis without a recapture.
		/// </summary>
		public static bool TryRead(int axis, out float value)
		{
			AxisBinding_Single b = GetBind(axis);
			if (b == null || b.idTag == "None")
			{
				value = 0f;
				return false;
			}
			value = ApplyDeadzone(b.GetAxis(), KrillParams.AxisDeadzone);
			return true;
		}

		/// <summary>Stock's own dead-zone shape (AxisBinding_Single.GetAxis, decompiled): zero inside ±dz, then rescaled so the remaining travel still spans the full -1..1.</summary>
		public static float ApplyDeadzone(float v, float dz)
		{
			if (dz <= 0f)
			{
				return v;
			}
			if (Mathf.Abs(v) < dz)
			{
				return 0f;
			}
			float scale = 1f / (1f - dz);
			return v > 0f ? (v - dz) * scale : (v + dz) * scale;
		}

		/// <summary>Player-facing description of an axis's bind: the stock-formatted "Device Axis N" title, or "-" when unbound.</summary>
		public static string Describe(int axis)
		{
			AxisBinding_Single b = GetBind(axis);
			return b != null ? b.title : "-";
		}

		/// <summary>
		/// A fresh KRILL-style binding for one physical channel — the same fields
		/// stock's own settings screen fills in (decompiled SettingsInputBinding.
		/// SetAxis), with KRILL's "raw channel" defaults on top.
		/// </summary>
		public static AxisBinding_Single Create(int deviceIdx, int axisIdx, string deviceName)
		{
			AxisBinding_Single b = new AxisBinding_Single
			{
				idTag = "joy" + deviceIdx + "." + axisIdx,
				name = deviceName,
				deviceIdx = deviceIdx,
				axisIdx = axisIdx,
				title = AxisTitle(deviceName, axisIdx),
				inverted = false,
				sensitivity = 1f,
				deadzone = 0f,
				scale = 1f,
				neutralPoint = 0f,
			};
			return b;
		}

		/// <summary>Stock's own "<device> Axis <n>" string (#autoLOC_6001495), the one the Input settings screen shows.</summary>
		public static string AxisTitle(string deviceName, int axisIdx)
		{
			return Localizer.Format("#autoLOC_6001495", deviceName, axisIdx.ToString());
		}

		private static void EnsureLoaded()
		{
			if (binds != null)
			{
				return;
			}
			binds = new Dictionary<int, AxisBinding_Single>();
			if (!File.Exists(FilePath))
			{
				return;
			}
			ConfigNode root = ConfigNode.Load(FilePath);
			ConfigNode keymap = root?.GetNode(RootNodeName);
			if (keymap == null)
			{
				return;
			}
			foreach (ConfigNode entry in keymap.GetNodes(EntryNodeName))
			{
				int axis = 0;
				if (!entry.TryGetValue("axis", ref axis) || axis < KrillAxes.FirstExtended)
				{
					Debug.LogWarning("[KRILL] dropping malformed axis keymap entry: " + entry);
					continue;
				}
				AxisBinding_Single b = new AxisBinding_Single();
				b.Load(entry);
				// Stock's Load rebuilds the title as name + "Axis " + idx (no space,
				// not localized) when the device resolves, and "Not Found" when it
				// doesn't — keep the latter as a visible diagnostic, restore ours
				// for the former so the window shows the same text it showed at
				// capture time.
				if (b.idTag != "None")
				{
					b.title = AxisTitle(b.name, b.axisIdx);
				}
				else
				{
					Debug.LogWarningFormat("[KRILL] axis {0} bind '{1}' axis {2}: device not connected, bind kept but inactive", axis, b.name, b.axisIdx);
				}
				binds[axis] = b;
			}
		}

		private static void Save()
		{
			ConfigNode root = new ConfigNode();
			ConfigNode keymap = root.AddNode(RootNodeName);
			foreach (KeyValuePair<int, AxisBinding_Single> kv in binds)
			{
				ConfigNode entry = keymap.AddNode(EntryNodeName);
				entry.AddValue("axis", kv.Key);
				kv.Value.Save(entry);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			root.Save(FilePath);
		}
	}
}
