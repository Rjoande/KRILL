using System.Collections.Generic;
using System.IO;
using KSP.Localization;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// The player's global AXIS keymap: extended axis number -> the controller
	/// channel driving it. Each entry is a stock AxisBinding_Single, stored by
	/// device NAME and re-resolved at load time, with deadzone 0 and no curve.
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
		/// Current value of the channel bound to `axis` in -1..1, with the global dead
		/// zone applied here rather than baked into the bind, so changing the setting
		/// reaches every axis without a recapture. False when unbound or unplugged.
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

		/// <summary>Stock's own dead-zone shape: zero inside ±dz, then rescaled so the remaining travel still spans the full -1..1.</summary>
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

		/// <summary>A fresh binding for one physical channel: the same fields stock's settings screen fills in, with KRILL's raw-channel defaults.</summary>
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
				// Stock's Load rebuilds the title unlocalized when the device resolves, and
				// as "Not Found" when it doesn't — keep that one as a visible diagnostic
				// and restore ours otherwise, so the window shows its capture-time text.
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
