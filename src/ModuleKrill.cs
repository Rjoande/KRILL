using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// The per-part data carrier, attached to every part by Config/KRILL.cfg
	/// (mirroring how stock persists action assignments inside each part's ACTIONS
	/// nodes, which is what lets assignments travel with craft files). Completely
	/// inert unless the part actually holds KRILL data: no persistent placeholder
	/// fields, no per-frame work.
	///
	/// Persistence rules (design doc §5):
	/// - payload lives in KRILL_ACTION / KRILL_NAME child nodes of this module;
	/// - a [SerializeField] string mirror guards against the editor-clone trap
	///   (clones never run OnLoad) and against OnSave running before OnLoad;
	/// - loads are tolerant: malformed nodes are dropped with a log line.
	///
	/// 2026-07-20: the four debug PAW events (self-test/write sample/dump/clear)
	/// that made M1 testable before any UI existed, and the debugMode flag that
	/// gated them (Test/KrillDebug.cfg), were removed on request now that the real
	/// KRILL window covers the same ground.
	/// </summary>
	public class ModuleKrill : PartModule
	{
		[SerializeField]
		private string dataBackup = string.Empty;

		private KrillPartData data;

		public KrillPartData Data
		{
			get
			{
				EnsureLoaded();
				return data;
			}
		}

		/// <summary>Call after any mutation so the Unity-serializable mirror stays current.</summary>
		public void MarkDirty()
		{
			EnsureLoaded();
			dataBackup = data.IsEmpty ? string.Empty : data.SaveToString();
		}

		/// <summary>
		/// Group on/off state (design doc §5: by convention read/written on the
		/// vessel ROOT part only — callers, i.e. KrillInputManager, are responsible
		/// for calling FindModuleImplementing on vessel.rootPart before using these).
		/// </summary>
		public bool GetToggleState(int set, int group)
		{
			return Data.GetToggle(set, group);
		}

		public void SetToggleState(int set, int group, bool active)
		{
			Data.SetToggle(set, group, active);
			MarkDirty();
		}

		/// <summary>
		/// Actuation kind label (2026-08-19 design discussion) — whether the toggle
		/// state above is meant to be trusted as real informational state by
		/// external readers. Same root-part convention as the toggle state itself.
		/// </summary>
		public KrillActuationKind GetActuationKind(int set, int group)
		{
			return Data.GetKind(set, group);
		}

		public void SetActuationKind(int set, int group, KrillActuationKind kind)
		{
			Data.SetKind(set, group, kind);
			MarkDirty();
		}

		private void EnsureLoaded()
		{
			if (data != null)
			{
				return;
			}
			data = new KrillPartData();
			if (!string.IsNullOrEmpty(dataBackup))
			{
				data.LoadFromString(dataBackup);
			}
		}

		public override void OnLoad(ConfigNode node)
		{
			data = new KrillPartData();
			data.Load(node);
			dataBackup = data.IsEmpty ? string.Empty : data.SaveToString();
		}

		public override void OnSave(ConfigNode node)
		{
			EnsureLoaded();
			data.Save(node);
		}

		public override void OnStart(StartState state)
		{
			// Loads dataBackup for editor clones (see class doc — they never run
			// OnLoad), harmless no-op otherwise since OnLoad already ran first.
			EnsureLoaded();
		}
	}
}
