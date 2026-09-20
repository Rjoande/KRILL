using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// The player's global "jump to this set" keymap: set number (0 = Default,
	/// 1..Vessel.NumOverrideGroups) -> KrillBind. Separate from KrillKeymap so a
	/// small int can never mean a group in one file and a set in the other.
	/// </summary>
	public static class KrillSetKeymap
	{
		private const string RootNodeName = "KRILL_SET_KEYMAP";
		private const string EntryNodeName = "BIND";

		private static readonly string FilePath =
			KSPUtil.ApplicationRootPath + "GameData/KRILL/PluginData/setkeymap.cfg";

		private static Dictionary<int, KrillBind> binds;

		public static IReadOnlyDictionary<int, KrillBind> Binds
		{
			get { EnsureLoaded(); return binds; }
		}

		public static KrillBind GetBind(int set)
		{
			EnsureLoaded();
			return binds.TryGetValue(set, out KrillBind b) ? b : null;
		}

		public static void SetBind(int set, KrillBind bind)
		{
			EnsureLoaded();
			binds[set] = bind;
			Save();
		}

		public static void RemoveBind(int set)
		{
			EnsureLoaded();
			if (binds.Remove(set))
			{
				Save();
			}
		}

		private static void EnsureLoaded()
		{
			if (binds != null)
			{
				return;
			}
			binds = new Dictionary<int, KrillBind>();
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
				int set = -1;
				if (!entry.TryGetValue("set", ref set) || set < 0 || set > Vessel.NumOverrideGroups)
				{
					Debug.LogWarning("[KRILL] dropping malformed set-keymap entry: " + entry);
					continue;
				}
				KrillBind bind = KrillBind.Load(entry);
				if (bind != null)
				{
					binds[set] = bind;
				}
			}
		}

		private static void Save()
		{
			ConfigNode root = new ConfigNode();
			ConfigNode keymap = root.AddNode(RootNodeName);
			foreach (KeyValuePair<int, KrillBind> kv in binds)
			{
				ConfigNode entry = keymap.AddNode(EntryNodeName);
				entry.AddValue("set", kv.Key);
				kv.Value.Save(entry);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			root.Save(FilePath);
		}
	}
}
