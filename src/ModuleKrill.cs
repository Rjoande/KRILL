using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// The per-part data carrier attached to every part by Config/KRILL.cfg, so
	/// assignments travel with the craft file. Inert unless the part holds KRILL
	/// data; a [SerializeField] mirror covers editor clones, which never run OnLoad.
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
		/// Direction bit — which of Activate/Deactivate the next Fire sends. Private
		/// bookkeeping, not a state reading (see KrillGroupToggle). Like every label
		/// below, by convention read and written on the vessel ROOT part only.
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
		/// Persisted signal of a Toggle-kind group: the 0/1 readers see, flipped by
		/// Fire and forced by the window's State button. Independent of the bit above.
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

		/// <summary>Actuation kind: whether the toggle state above is meant to be trusted as real state by external readers.</summary>
		public KrillActuationKind GetActuationKind(int set, int group)
		{
			return Data.GetKind(set, group);
		}

		public void SetActuationKind(int set, int group, KrillActuationKind kind)
		{
			Data.SetKind(set, group, kind);
			MarkDirty();
		}

		/// <summary>Console severity label: purely cosmetic, and unlike the actuation kind it is valid on stock groups 1-10 too.</summary>
		public KrillIndicatorType GetIndicatorType(int set, int group)
		{
			return Data.GetIndicatorType(set, group);
		}

		public void SetIndicatorType(int set, int group, KrillIndicatorType type)
		{
			Data.SetIndicatorType(set, group, type);
			MarkDirty();
		}

		/// <summary>Extended-axis settings: kind, rest, the persisted level of a Fixed axis, and the silent indicator slot.</summary>
		public KrillAxisKind GetAxisKind(int set, int axis)
		{
			return Data.GetAxisKind(set, axis);
		}

		public void SetAxisKind(int set, int axis, KrillAxisKind kind)
		{
			Data.SetAxisKind(set, axis, kind);
			MarkDirty();
		}

		public int GetAxisRest(int set, int axis)
		{
			return Data.GetAxisRest(set, axis);
		}

		public void SetAxisRest(int set, int axis, int rest)
		{
			Data.SetAxisRest(set, axis, rest);
			MarkDirty();
		}

		public float GetAxisValue(int set, int axis)
		{
			return Data.GetAxisValue(set, axis);
		}

		public void SetAxisValue(int set, int axis, float value)
		{
			Data.SetAxisValue(set, axis, value);
			MarkDirty();
		}

		public KrillIndicatorType GetAxisIndicatorType(int set, int axis)
		{
			return Data.GetAxisIndicatorType(set, axis);
		}

		public void SetAxisIndicatorType(int set, int axis, KrillIndicatorType type)
		{
			Data.SetAxisIndicatorType(set, axis, type);
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
			// Loads dataBackup for editor clones, which never run OnLoad; a no-op
			// otherwise, since OnLoad has already run.
			EnsureLoaded();
		}
	}
}
