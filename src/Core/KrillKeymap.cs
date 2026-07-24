using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// The player's global keymap: extended group number -> KrillBind. Global and
	/// per-player (not per-save, not per-craft) by design (§4): the same key always
	/// triggers the same group number, on any vessel, in any set — the active set
	/// decides WHAT the group does, never which key triggers it.
	///
	/// Persisted under PluginData/, not directly in GameData: ModuleManager never
	/// scans a "PluginData" folder for patches (a standing KSP-modding convention,
	/// the same reason AGSetHUD keeps its own settings.cfg there), so saving the
	/// keymap here never triggers an MM cache rebuild.
	/// </summary>
	public static class KrillKeymap
	{
		private const string RootNodeName = "KRILL_KEYMAP";
		private const string EntryNodeName = "BIND";

		private static readonly string FilePath =
			KSPUtil.ApplicationRootPath + "GameData/KRILL/PluginData/keymap.cfg";

		private static Dictionary<int, KrillBind> binds;

		public static IReadOnlyDictionary<int, KrillBind> Binds
		{
			get { EnsureLoaded(); return binds; }
		}

		public static KrillBind GetBind(int group)
		{
			EnsureLoaded();
			return binds.TryGetValue(group, out KrillBind b) ? b : null;
		}

		public static void SetBind(int group, KrillBind bind)
		{
			EnsureLoaded();
			binds[group] = bind;
			Save();
		}

		public static void RemoveBind(int group)
		{
			EnsureLoaded();
			if (binds.Remove(group))
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
				int group = 0;
				if (!entry.TryGetValue("group", ref group) || group < KrillGroups.FirstExtended)
				{
					Debug.LogWarning("[KRILL] dropping malformed keymap entry: " + entry);
					continue;
				}
				KrillBind bind = KrillBind.Load(entry);
				if (bind != null)
				{
					binds[group] = bind;
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
				entry.AddValue("group", kv.Key);
				kv.Value.Save(entry);
			}
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			root.Save(FilePath);
		}
	}
}
