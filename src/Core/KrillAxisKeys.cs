using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// The player's global +/- KEYS for extended axes (2026-09-18, K1,
	/// notes/axes-design.md §11): axis number -> a plus bind and a minus bind,
	/// each a KrillBind (keyboard key or joystick button, with modifiers) —
	/// the third "hand" on an axis next to the controller channel
	/// (KrillAxisKeymap) and the footer slider. Stock gives its custom axes the
	/// same trio (AxisKeyBinding: axisBinding + plusKeyBinding + minusKeyBinding).
	///
	/// Deliberately its own class and file rather than a second field on
	/// KrillAxisKeymap: there, "an entry exists" means "a channel is bound"
	/// (IsBound, the driver's refresh loop, the slider's read-only state) and a
	/// keys-only axis must not look bound. What a held key DOES to the axis is
	/// the driver's business (KrillAxisDriver: Spring deflects toward ±1 and
	/// springs back, Fixed integrates and stays); this class only stores and
	/// polls. Global per player under PluginData/, never per craft, same
	/// reasoning as keymap.cfg (ModuleManager never scans that folder).
	/// </summary>
	public static class KrillAxisKeys
	{
		private const string RootNodeName = "KRILL_AXIS_KEYS";
		private const string EntryNodeName = "AXIS_KEYS";
		private const string PlusNodeName = "PLUS";
		private const string MinusNodeName = "MINUS";

		private static readonly string FilePath =
			KSPUtil.ApplicationRootPath + "GameData/KRILL/PluginData/axiskeys.cfg";

		public class Pair
		{
			public KrillBind plus;
			public KrillBind minus;

			public bool IsEmpty => plus == null && minus == null;
		}

		private static Dictionary<int, Pair> pairs;

		/// <summary>Every axis with at least one key. Entries are never empty.</summary>
		public static IReadOnlyDictionary<int, Pair> Binds
		{
			get { EnsureLoaded(); return pairs; }
		}

		public static KrillBind Get(int axis, bool plus)
		{
			EnsureLoaded();
			if (!pairs.TryGetValue(axis, out Pair p))
			{
				return null;
			}
			return plus ? p.plus : p.minus;
		}

		/// <summary>Sets (bind != null) or clears (bind == null) one of the two keys and saves; an axis left with neither key drops out of the map.</summary>
		public static void Set(int axis, bool plus, KrillBind bind)
		{
			EnsureLoaded();
			if (!pairs.TryGetValue(axis, out Pair p))
			{
				if (bind == null)
				{
					return;
				}
				p = new Pair();
				pairs[axis] = p;
			}
			if (plus)
			{
				p.plus = bind;
			}
			else
			{
				p.minus = bind;
			}
			if (p.IsEmpty)
			{
				pairs.Remove(axis);
			}
			Save();
		}

		public static bool HasAny(int axis)
		{
			EnsureLoaded();
			return pairs.ContainsKey(axis);
		}

		/// <summary>
		/// +1 while the plus key combination is down, -1 for minus, 0 for neither
		/// or BOTH (two hands pulling opposite ways cancel — design §11.2). A
		/// level check like stock's ProcessAxis (Input.GetKey, fine from
		/// FixedUpdate); the caller decides whether keys are allowed at all
		/// (typing lock, captures — KrillAxisDriver).
		/// </summary>
		public static int HeldDirection(int axis)
		{
			EnsureLoaded();
			if (!pairs.TryGetValue(axis, out Pair p))
			{
				return 0;
			}
			bool up = p.plus != null && p.plus.IsHeldWithModifiers();
			bool down = p.minus != null && p.minus.IsHeldWithModifiers();
			if (up == down)
			{
				return 0;
			}
			return up ? 1 : -1;
		}

		/// <summary>Player-facing text of one key ("LeftShift+K"), or "-" when unset.</summary>
		public static string Describe(int axis, bool plus)
		{
			KrillBind b = Get(axis, plus);
			return b != null ? b.Describe() : "-";
		}

		private static void EnsureLoaded()
		{
			if (pairs != null)
			{
				return;
			}
			pairs = new Dictionary<int, Pair>();
			if (!File.Exists(FilePath))
			{
				Debug.Log("[KRILL] axis keys: no file at " + FilePath);
				return;
			}
			ConfigNode root = ConfigNode.Load(FilePath);
			ConfigNode keys = root?.GetNode(RootNodeName);
			if (keys == null)
			{
				return;
			}
			foreach (ConfigNode entry in keys.GetNodes(EntryNodeName))
			{
				int axis = 0;
				if (!entry.TryGetValue("axis", ref axis) || axis < KrillAxes.FirstExtended)
				{
					Debug.LogWarning("[KRILL] dropping malformed axis keys entry: " + entry);
					continue;
				}
				ConfigNode plusNode = entry.GetNode(PlusNodeName);
				ConfigNode minusNode = entry.GetNode(MinusNodeName);
				Pair p = new Pair
				{
					plus = plusNode != null ? KrillBind.Load(plusNode) : null,
					minus = minusNode != null ? KrillBind.Load(minusNode) : null,
				};
				if (p.IsEmpty)
				{
					Debug.LogWarning("[KRILL] dropping axis keys entry with no usable key: " + entry);
					continue;
				}
				pairs[axis] = p;
			}
			Debug.Log("[KRILL] axis keys: " + pairs.Count + " axis entr" + (pairs.Count == 1 ? "y" : "ies") + " loaded from " + FilePath);
		}

		private static void Save()
		{
			ConfigNode root = new ConfigNode();
			ConfigNode keys = root.AddNode(RootNodeName);
			foreach (KeyValuePair<int, Pair> kv in pairs)
			{
				ConfigNode entry = keys.AddNode(EntryNodeName);
				entry.AddValue("axis", kv.Key);
				if (kv.Value.plus != null)
				{
					kv.Value.plus.Save(entry.AddNode(PlusNodeName));
				}
				if (kv.Value.minus != null)
				{
					kv.Value.minus.Save(entry.AddNode(MinusNodeName));
				}
			}
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			root.Save(FilePath);
		}
	}
}
