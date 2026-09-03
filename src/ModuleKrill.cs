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
		/// Direction bit — which of Activate/Deactivate the next Fire sends
		/// (design doc §5: by convention read/written on the vessel ROOT part
		/// only — callers are responsible for calling FindModuleImplementing on
		/// vessel.rootPart before using these). Private bookkeeping, not a state
		/// reading: see KrillGroupToggle.
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
		/// Persisted signal of a Toggle-kind group (2026-09-02) — the 0/1 readers
		/// see for that kind, flipped by KrillActivation.Fire and forced by the
		/// window's State button. Independent of the direction bit above. Same
		/// root-part convention.
		/// </summary>
		public bool GetToggleSignal(int set, int group)
		{
			return Data.GetSignal(set, group);
		}

		public void SetToggleSignal(int set, int group, bool value)
		{
			Data.SetSignal(set, group, value);
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

		/// <summary>
		/// Console severity label (2026-08-24 design discussion, "console" name settled 2026-08-27) — purely cosmetic, no
		/// effect on activation. Same root-part convention as the other per-group
		/// labels; unlike actuation kind, valid on stock groups (1..10) too.
		/// </summary>
		public KrillIndicatorType GetIndicatorType(int set, int group)
		{
			return Data.GetIndicatorType(set, group);
		}

		public void SetIndicatorType(int set, int group, KrillIndicatorType type)
		{
			Data.SetIndicatorType(set, group, type);
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
