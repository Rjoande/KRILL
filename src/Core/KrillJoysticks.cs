using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// Joystick buttons bound by device NAME instead of by Unity's joystick number.
	/// Windows hands out the joystick order anew at every launch (two sticks swap
	/// Joystick1/Joystick2 between sessions, visible in Player.log's "[Input
	/// Devices]" line), so a stored Joystick2Button5 would follow whatever device
	/// sits in slot 2 today. Stock solves this for axes only (INPUT_DEVICES +
	/// AxisBinding_Single store the device name); this does the same for buttons.
	///
	/// The K of a JoystickKButtonN in any KRILL keymap is a LOGICAL index: the
	/// DEVICE entry of devices.cfg with that index holds the device name. Capture
	/// translates physical -> logical (registering the device on first sight);
	/// every poll translates logical -> physical by looking the name up in
	/// Input.GetJoystickNames(), where position i is Joystick(i+1) and an unplugged
	/// device leaves an empty string without shifting the others. A logical index
	/// with no entry is used literally, which is what every older bind does.
	///
	/// Each entry also carries a short label for the window ("VKBsim.B10"), which
	/// KrillBind.Load accepts too; the files always keep the canonical KeyCode name,
	/// so renaming a label touches nothing but devices.cfg.
	/// </summary>
	public static class KrillJoysticks
	{
		public const int MaxLabelLength = 10;

		// Unity's KeyCode lays joystick buttons out as JoystickButton0..19 (any
		// joystick) followed by Joystick1Button0..19 .. Joystick8Button0..19.
		public const int MaxJoysticks = 8;
		public const int ButtonsPerJoystick = 20;

		// Input.GetJoystickNames() allocates a string[] on every call: cached, and
		// refreshed often enough to catch a device plugged in or back mid-flight.
		private const float RefreshSeconds = 2f;

		private const string RootNodeName = "KRILL_DEVICES";
		private const string EntryNodeName = "DEVICE";

		private static readonly string FilePath =
			KSPUtil.ApplicationRootPath + "GameData/KRILL/PluginData/devices.cfg";

		public class Device
		{
			public int index;
			public string name;
			public string label;
		}

		private static List<Device> devices;
		private static string[] names = new string[0];
		private static float namesRefreshedAt = -1f;
		private static readonly HashSet<string> warnedMissing = new HashSet<string>();

		/// <summary>Known devices in file order. Read-only use: edit through SetLabel / Forget.</summary>
		public static IReadOnlyList<Device> Devices
		{
			get { EnsureLoaded(); return devices; }
		}

		// ------------------------------------------------------------ KeyCode math

		public static bool IsSpecificButton(KeyCode code)
		{
			return code >= KeyCode.Joystick1Button0 && code <= KeyCode.Joystick8Button19;
		}

		/// <summary>1-based joystick number of a JoystickKButtonN code.</summary>
		public static int JoystickOf(KeyCode code)
		{
			return (code - KeyCode.Joystick1Button0) / ButtonsPerJoystick + 1;
		}

		public static int ButtonOf(KeyCode code)
		{
			return (code - KeyCode.Joystick1Button0) % ButtonsPerJoystick;
		}

		public static KeyCode Code(int joystick, int button)
		{
			return KeyCode.Joystick1Button0 + (joystick - 1) * ButtonsPerJoystick + button;
		}

		// --------------------------------------------------------------- lookups

		public static Device ByIndex(int index)
		{
			EnsureLoaded();
			return devices.Find(d => d.index == index);
		}

		public static Device ByName(string name)
		{
			EnsureLoaded();
			return devices.Find(d => d.name == name);
		}

		public static Device ByLabel(string label)
		{
			EnsureLoaded();
			return devices.Find(d => string.Equals(d.label, label, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>Short, ASCII, no blanks and none of the bind syntax's own separators ('+', '.').</summary>
		public static bool IsValidLabel(string label)
		{
			if (string.IsNullOrEmpty(label) || label.Length > MaxLabelLength)
			{
				return false;
			}
			foreach (char c in label)
			{
				if (c <= ' ' || c > '~' || c == '+' || c == '.')
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>First word of the device name kept to the allowed characters, made unique; "JoyN" if nothing usable is left.</summary>
		private static string DefaultLabel(string name, int index)
		{
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < name.Length && sb.Length < MaxLabelLength; i++)
			{
				char c = name[i];
				if (c <= ' ')
				{
					if (sb.Length > 0)
					{
						break;
					}
					continue;
				}
				if (c > '~' || c == '+' || c == '.')
				{
					continue;
				}
				sb.Append(c);
			}
			string label = sb.Length > 0 ? sb.ToString() : "Joy" + index;
			if (ByLabel(label) == null)
			{
				return label;
			}
			string stem = label.Length > MaxLabelLength - 1 ? label.Substring(0, MaxLabelLength - 1) : label;
			for (int n = 2; n < 100; n++)
			{
				if (ByLabel(stem + n) == null)
				{
					return stem + n;
				}
			}
			return "Joy" + index;
		}

		/// <summary>Renames a device's label and saves. False (nothing written) when the label is invalid or taken by another device.</summary>
		public static bool SetLabel(int index, string label)
		{
			Device d = ByIndex(index);
			if (d == null || !IsValidLabel(label))
			{
				return false;
			}
			Device other = ByLabel(label);
			if (other != null && other != d)
			{
				return false;
			}
			if (d.label != label)
			{
				d.label = label;
				Save();
			}
			return true;
		}

		/// <summary>Drops an entry: binds using its index fall back to that index taken literally.</summary>
		public static void Forget(int index)
		{
			Device d = ByIndex(index);
			if (d == null)
			{
				return;
			}
			devices.Remove(d);
			Save();
			Debug.Log("[KRILL] joystick " + index + " (" + d.name + ") forgotten: Joystick" + index + " binds are literal again");
		}

		// -------------------------------------------------------- current devices

		private static string[] Names()
		{
			float now = Time.realtimeSinceStartup;
			if (namesRefreshedAt < 0f || now - namesRefreshedAt > RefreshSeconds)
			{
				namesRefreshedAt = now;
				try
				{
					names = Input.GetJoystickNames() ?? new string[0];
				}
				catch (Exception e)
				{
					Debug.LogWarning("[KRILL] Input.GetJoystickNames failed: " + e.Message);
				}
			}
			return names;
		}

		/// <summary>Name Unity reports for joystick number k (1-based) right now, "" if none. Trimmed like stock's INPUT_DEVICES, so a name compares equal between a capture and a later launch.</summary>
		public static string NameOf(int joystick)
		{
			string[] n = Names();
			int i = joystick - 1;
			if (i < 0 || i >= n.Length || n[i] == null)
			{
				return "";
			}
			return InputDevices.TrimDeviceName(n[i]);
		}

		/// <summary>Joystick number (1-based) the named device occupies right now, 0 if absent. Two identical devices: the lowest number wins, as in stock.</summary>
		public static int CurrentIndexOf(string name)
		{
			string[] n = Names();
			for (int i = 0; i < n.Length && i < MaxJoysticks; i++)
			{
				if (n[i] != null && InputDevices.TrimDeviceName(n[i]) == name)
				{
					return i + 1;
				}
			}
			return 0;
		}

		// ------------------------------------------------------------ translation

		/// <summary>
		/// The KeyCode to poll for a stored code: a JoystickKButtonN whose K has a
		/// DEVICE entry becomes the button of wherever that device is now, or None
		/// (never matches) while it is not connected. Anything else is returned as is.
		/// </summary>
		public static KeyCode Physical(KeyCode logical)
		{
			if (!IsSpecificButton(logical))
			{
				return logical;
			}
			Device d = ByIndex(JoystickOf(logical));
			if (d == null)
			{
				return logical;
			}
			int now = CurrentIndexOf(d.name);
			if (now == 0)
			{
				if (warnedMissing.Add(d.name))
				{
					Debug.LogWarning("[KRILL] joystick " + d.name + " (" + d.label + ") is not connected: its binds are inert");
				}
				return KeyCode.None;
			}
			warnedMissing.Remove(d.name);
			return Code(now, ButtonOf(logical));
		}

		/// <summary>
		/// Capture-time counterpart: the code to STORE for a physical JoystickKButtonN
		/// just pressed. The device's name is looked up (or registered, keeping K as
		/// its logical index when that is free, else the first free one) and its
		/// logical index substituted. A device with no name (stock treats that as an
		/// anomaly too: "Device #N has no name") stays literal, with a warning.
		/// </summary>
		public static KeyCode Logical(KeyCode physical)
		{
			if (!IsSpecificButton(physical))
			{
				return physical;
			}
			int k = JoystickOf(physical);
			string name = NameOf(k);
			if (name.Length == 0)
			{
				Debug.LogWarning("[KRILL] joystick " + k + " reports no name: " + physical + " bound by index only");
				return physical;
			}
			Device d = ByName(name);
			if (d == null)
			{
				int index = ByIndex(k) == null ? k : 0;
				for (int i = 1; index == 0 && i <= MaxJoysticks; i++)
				{
					if (ByIndex(i) == null)
					{
						index = i;
					}
				}
				if (index == 0)
				{
					Debug.LogWarning("[KRILL] all " + MaxJoysticks + " logical joystick indexes are taken: " + physical + " bound by index only");
					return physical;
				}
				d = new Device { index = index, name = name, label = DefaultLabel(name, index) };
				devices.Add(d);
				Save();
				Debug.Log("[KRILL] joystick registered: Joystick" + index + " = " + name + " (" + d.label + "), now at " + k);
			}
			return Code(d.index, ButtonOf(physical));
		}

		/// <summary>Side-effect-free Logical(): the logical code if the device at that physical index is already known, the physical code otherwise. For reading a stock KeyBinding, which holds the physical code, in KRILL's own terms.</summary>
		public static KeyCode LogicalIfKnown(KeyCode physical)
		{
			if (!IsSpecificButton(physical))
			{
				return physical;
			}
			Device d = ByName(NameOf(JoystickOf(physical)));
			return d == null ? physical : Code(d.index, ButtonOf(physical));
		}

		/// <summary>Window text for one bind token: "VKBsim.B10" for a device known by name, the KeyCode name otherwise.</summary>
		public static string Describe(KeyCode logical)
		{
			if (IsSpecificButton(logical))
			{
				Device d = ByIndex(JoystickOf(logical));
				if (d != null)
				{
					return d.label + ".B" + ButtonOf(logical);
				}
			}
			return logical.ToString();
		}

		/// <summary>Parses the "label.Bn" form of a bind token (label case-insensitive). False if the token is not of that form or the label is unknown.</summary>
		public static bool TryParse(string token, out KeyCode code)
		{
			code = KeyCode.None;
			int dot = token.IndexOf(".B", StringComparison.OrdinalIgnoreCase);
			if (dot <= 0 || !int.TryParse(token.Substring(dot + 2), out int button) || button < 0 || button >= ButtonsPerJoystick)
			{
				return false;
			}
			Device d = ByLabel(token.Substring(0, dot));
			if (d == null)
			{
				return false;
			}
			code = Code(d.index, button);
			return true;
		}

		// ------------------------------------------------------------ persistence

		private static void EnsureLoaded()
		{
			if (devices != null)
			{
				return;
			}
			devices = new List<Device>();
			if (!File.Exists(FilePath))
			{
				return;
			}
			ConfigNode root = ConfigNode.Load(FilePath)?.GetNode(RootNodeName);
			if (root == null)
			{
				return;
			}
			foreach (ConfigNode entry in root.GetNodes(EntryNodeName))
			{
				int index = 0;
				string name = entry.GetValue("name");
				if (!entry.TryGetValue("index", ref index) || index < 1 || index > MaxJoysticks
					|| string.IsNullOrEmpty(name) || ByIndex(index) != null)
				{
					Debug.LogWarning("[KRILL] dropping malformed devices entry (index 1-" + MaxJoysticks + " unique and a name are required): " + entry);
					continue;
				}
				string label = entry.GetValue("label");
				devices.Add(new Device
				{
					index = index,
					name = name,
					label = IsValidLabel(label) && ByLabel(label) == null ? label : DefaultLabel(name, index),
				});
			}
			if (devices.Count == 0)
			{
				return;
			}
			StringBuilder sb = new StringBuilder();
			foreach (Device d in devices)
			{
				int now = CurrentIndexOf(d.name);
				sb.Append(sb.Length > 0 ? ", " : "").Append("Joystick").Append(d.index).Append('=').Append(d.label)
					.Append(" (").Append(d.name).Append(": ").Append(now == 0 ? "not connected" : "now Joystick" + now).Append(')');
			}
			Debug.Log("[KRILL] joysticks known by name: " + sb);
		}

		private static void Save()
		{
			ConfigNode root = new ConfigNode();
			ConfigNode node = root.AddNode(RootNodeName);
			foreach (Device d in devices)
			{
				ConfigNode entry = node.AddNode(EntryNodeName);
				entry.AddValue("index", d.index);
				entry.AddValue("name", d.name);
				entry.AddValue("label", d.label);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			root.Save(FilePath);
		}
	}
}