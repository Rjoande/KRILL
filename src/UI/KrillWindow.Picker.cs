using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KRILL.UI
{
	/// <summary>
	/// Two independent steps. PickingPart is the KAL-style scene gesture and only
	/// selects a part — nothing is persisted. PickingAction lists the selected
	/// part's actions or axis fields, and writes a real assignment on pick.
	/// </summary>
	public partial class KrillWindow
	{
		private enum PickerKind
		{
			None,
			PickingPart,
			PickingAction,
		}

		private const string PickLockId = "KRILL_WINDOW_PICK";

		// Same pause-menu race KrillCapture works around: the menu opens on Escape's
		// key-UP and only then checks the lock, so the lock is held until the real
		// key-up is seen. This cap only covers a key-up that never arrives.
		private const int MaxUnlockWaitFrames = 180;
		private int pickUnlockWaitFramesLeft = -1;

		private PickerKind pickerKind;
		private Part hoverPart;
		private Part pendingPickPart;

		private void StartPartPick()
		{
			pendingRemovePart = false;
			pickerKind = PickerKind.PickingPart;
			InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, PickLockId);
			SetCrewHatchInterface(false);
			RebuildContent();
		}

		/// <summary>True while THIS picker has switched the stock crew-hatch interface off, so the matching re-enable never fires for a disable that wasn't ours.</summary>
		private bool hatchInterfaceDisabledByPicker;

		/// <summary>
		/// Stock's crew-hatch click never consults InputLockManager, so the picker's
		/// own lock does not stop it and clicking a hatch would open the crew dialog
		/// on top. The Disable/EnableInterface pair is the one CameraManager uses.
		/// </summary>
		private void SetCrewHatchInterface(bool enabled)
		{
			if (!HighLogic.LoadedSceneIsFlight || CrewHatchController.fetch == null)
			{
				return;
			}
			if (!enabled)
			{
				CrewHatchController.fetch.DisableInterface();
				hatchInterfaceDisabledByPicker = true;
				return;
			}
			if (!hatchInterfaceDisabledByPicker)
			{
				return;
			}
			hatchInterfaceDisabledByPicker = false;
			CameraManager cam = CameraManager.Instance;
			bool inIva = cam != null && (cam.currentCameraMode == CameraManager.CameraMode.IVA
				|| cam.currentCameraMode == CameraManager.CameraMode.Internal);
			if (!inIva)
			{
				CrewHatchController.fetch.EnableInterface();
			}
		}

		private void StartActionPick()
		{
			if (selectedPart == null)
			{
				return;
			}
			pendingRemovePart = false;
			pickerKind = PickerKind.PickingAction;
			// The action list needs a lock too, or Esc falls through to the pause menu.
			// Reusing PickLockId is safe: the two picker kinds are never active at once.
			InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, PickLockId);
			RebuildContent();
		}

		private void ClearPickerState()
		{
			pickerKind = PickerKind.None;
			pendingPickPart = null;
			pickUnlockWaitFramesLeft = -1;
			ClearHoverHighlight();
			InputLockManager.RemoveControlLock(PickLockId);
			SetCrewHatchInterface(true);
		}

		private void ClearHoverHighlight()
		{
			if (hoverPart != null)
			{
				RestoreHoverGroupHighlight(hoverPart);
				hoverPart = null;
			}
		}

		/// <summary>
		/// Un-hovering a part that is also the persistent selection (or one of its
		/// symmetry siblings) must restore the blue highlight, not clear it: otherwise
		/// hovering the selected part during a pick silently erases its highlight.
		/// </summary>
		private void RestoreHoverGroupHighlight(Part hovered)
		{
			if (IsSelectedPartOrSibling(hovered))
			{
				ApplySelectedPartHighlight();
				return;
			}
			foreach (Part p in KrillQuery.GetSymmetryGroup(hovered))
			{
				p.SetHighlightDefault();
			}
		}

		/// <summary>Cyan preview for the whole prospective symmetry group, not just the part under the cursor: it shows what is about to be selected.</summary>
		private static void ApplyHoverGroupHighlight(Part hovered)
		{
			foreach (Part p in KrillQuery.GetSymmetryGroup(hovered))
			{
				p.SetHighlightType(Part.HighlightType.AlwaysOn);
				p.SetHighlightColor(Color.cyan);
				p.SetHighlight(true, false);
			}
		}

		/// <summary>Button or programmatic cancel: no pause-menu race to protect against, so the lock goes right away.</summary>
		private void CancelPicker()
		{
			ClearPickerState();
			RebuildContent();
		}

		/// <summary>Escape cancel: the lock is held until Escape's key-up is observed, see MaxUnlockWaitFrames.</summary>
		private void CancelPickerFromEscape()
		{
			pickerKind = PickerKind.None;
			pendingPickPart = null;
			ClearHoverHighlight();
			pickUnlockWaitFramesLeft = MaxUnlockWaitFrames;
			SetCrewHatchInterface(true);
			RebuildContent();
		}

		/// <summary>Driven from LateUpdate while waiting for Escape's key-up after a cancel, when pickerKind is already None.</summary>
		private void TickPickUnlockDelay()
		{
			pickUnlockWaitFramesLeft--;
			if (Input.GetKeyUp(KeyCode.Escape) || pickUnlockWaitFramesLeft < 0)
			{
				pickUnlockWaitFramesLeft = -1;
				InputLockManager.RemoveControlLock(PickLockId);
			}
		}

		private void HandleActionPicking()
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				CancelPickerFromEscape();
			}
		}

		/// <summary>Scene-aware "does this part belong to the craft being worked on" check.</summary>
		private static bool PartOnActiveCraft(Part candidate)
		{
			if (HighLogic.LoadedSceneIsFlight)
			{
				return candidate.vessel != null && candidate.vessel == FlightGlobals.ActiveVessel;
			}
			return EditorLogic.fetch != null && EditorLogic.fetch.ship != null
				&& EditorLogic.fetch.ship.parts.Contains(candidate);
		}

		/// <summary>
		/// Confirms on mouse-UP, not mouse-down: the button stays physically held for
		/// the rest of the click, long enough for the editor's own part-drag — which
		/// re-checks its lock every frame — to grab the part and start dragging it.
		/// </summary>
		private void HandlePartPicking()
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				CancelPickerFromEscape();
				return;
			}

			if (pendingPickPart != null)
			{
				if (Input.GetMouseButtonUp(0))
				{
					Part picked = pendingPickPart;
					pendingPickPart = null;
					pickerKind = PickerKind.None;
					InputLockManager.RemoveControlLock(PickLockId);
					// Re-enabled on the confirming mouse-UP: the click that picked a hatch is
					// over by now, so stock will not see it.
					SetCrewHatchInterface(true);
					SelectPart(picked);
					// A newly picked part chains straight into the action list; re-selecting
					// an already assigned part from column 2 goes through OnPartClicked and
					// just shows what it has.
					StartActionPick();
				}
				return;
			}

			Part hovered = Mouse.HoveredPart;
			if (hovered != null && !PartOnActiveCraft(hovered))
			{
				hovered = null;
			}
			// In axis mode a part with no usable axis field can't be picked at all — no
			// highlight, no click. Evaluated only when the hover changes: the check
			// walks the part's Fields lists.
			if (hovered != null && hovered != hoverPart && selectedAxis.HasValue && !KrillQuery.HasAxisFields(hovered))
			{
				hovered = null;
			}
			if (hovered != hoverPart)
			{
				if (hoverPart != null)
				{
					RestoreHoverGroupHighlight(hoverPart);
				}
				hoverPart = hovered;
				if (hoverPart != null)
				{
					ApplyHoverGroupHighlight(hoverPart);
				}
			}
			if (Input.GetMouseButtonDown(0) && hoverPart != null
				&& (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
			{
				pendingPickPart = hoverPart;
				// Cleared HERE, not on a mouse-up that may never come: if Esc cancels between
				// down and up, hoverPart is already null and the part would stay lit forever.
				// Via ClearHoverHighlight, so a selected part goes back to blue, not default.
				ClearHoverHighlight();
				// Lock stays active — released only once mouse-up confirms the pick.
			}
		}

		private void BuildPickPrompt()
		{
			RectTransform panel = KrillUi.Bordered("PickPrompt", contentHost, KrillUi.Panel2, KrillUi.Line);
			KrillUi.Vertical(panel.gameObject, 12, 6f);
			KrillUi.Label(panel, Loc(selectedAxis.HasValue ? "#LOC_KRILL_ui_pickPromptAxis" : "#LOC_KRILL_ui_pickPrompt"),
				13, KrillUi.Tan, TextAnchor.MiddleCenter);
			KrillUi.TextButton(panel, Loc("#LOC_KRILL_ui_cancel"), CancelPicker, KrillUi.Panel, KrillUi.Muted, 12, 90f, 24f);
		}

		/// <summary>
		/// Axis-mode twin of BuildActionPicker: the part's usable axis fields minus
		/// those already assigned to this (set, axis). Shares PickerKind.PickingAction,
		/// since only the list differs and the Esc/lock handling is identical.
		/// </summary>
		private void BuildFieldPicker()
		{
			RectTransform panel = KrillUi.Bordered("FieldPicker", contentHost, KrillUi.Panel, KrillUi.Line);
			KrillUi.Vertical(panel.gameObject, 10, 6f);

			if (selectedPart == null || !selectedAxis.HasValue)
			{
				CancelPicker();
				return;
			}

			Text header = KrillUi.Label(panel,
				Localizer.Format("#LOC_KRILL_ui_pickFieldFor", selectedPart.partInfo.title, selectedAxis.Value.ToString()),
				12, KrillUi.Tan);
			KrillUi.Size(header.gameObject, -1f, 20f);

			List<KrillQuery.AxisFieldEntry> already = GetFieldEntriesForPart(ActiveParts(), selectedAxis.Value, selectedPart);
			List<BaseAxisField> candidates = KrillQuery.GetCandidateAxisFields(selectedPart);

			RectTransform list = KrillUi.ScrollList(panel, 180f);
			int shown = 0;
			for (int i = 0; i < candidates.Count; i++)
			{
				BaseAxisField f = candidates[i];
				bool taken = false;
				for (int j = 0; j < already.Count; j++)
				{
					if (already[j].resolved == f)
					{
						taken = true;
						break;
					}
				}
				if (taken)
				{
					continue;
				}
				shown++;
				// Field caption plus the owning module's name: a part can expose the same
				// caption from two modules, and the module name tells them apart.
				PartModule owner = f.host as PartModule;
				string label = FieldLabel(f) + (owner != null ? "  (" + owner.moduleName + ")" : string.Empty);
				KrillUi.TextButton(list, label, () => AssignField(f), KrillUi.Panel2, KrillUi.Text, 12, -1f, 22f);
			}
			if (shown == 0)
			{
				string emptyKey = candidates.Count == 0 ? "#LOC_KRILL_ui_noFields" : "#LOC_KRILL_ui_allFieldsAssigned";
				KrillUi.Label(list, Loc(emptyKey), 12, KrillUi.Muted, TextAnchor.MiddleCenter);
			}

			KrillUi.TextButton(panel, Loc("#LOC_KRILL_ui_cancel"), CancelPicker, KrillUi.Panel, KrillUi.Muted, 12, 90f, 24f);
		}

		/// <summary>Persists a field assignment on selectedPart and every symmetry sibling, with stock's defaults for a fresh assignment.</summary>
		private void AssignField(BaseAxisField f)
		{
			KrillFieldRef fieldRef = KrillFieldRef.FromField(f);
			if (fieldRef == null || selectedPart == null || !selectedAxis.HasValue)
			{
				CancelPicker();
				return;
			}
			bool incremental = KrillFieldRef.DefaultIncremental(f);
			bool assignedAny = false;
			foreach (Part p in KrillQuery.GetSymmetryGroup(selectedPart))
			{
				ModuleKrill m = p.FindModuleImplementing<ModuleKrill>();
				if (m == null)
				{
					continue;
				}
				KrillFieldRef refCopy = new KrillFieldRef { module = fieldRef.module, occurrence = fieldRef.occurrence, field = fieldRef.field };
				m.Data.AddAxisAssignment(activeSet, selectedAxis.Value, refCopy, false, incremental, KrillAxes.DefaultSpeed);
				m.MarkDirty();
				assignedAny = true;
			}
			if (assignedAny)
			{
				ScreenMessages.PostScreenMessage(
					Localizer.Format("#LOC_KRILL_ui_assignFieldDone", FieldLabel(f), selectedAxis.Value.ToString()),
					4f, ScreenMessageStyle.UPPER_CENTER);
			}
			pickerKind = PickerKind.None;
			InputLockManager.RemoveControlLock(PickLockId);
			RebuildContent();
		}

		private void BuildActionPicker()
		{
			if (selectedAxis.HasValue)
			{
				BuildFieldPicker();
				return;
			}

			RectTransform panel = KrillUi.Bordered("ActionPicker", contentHost, KrillUi.Panel, KrillUi.Line);
			KrillUi.Vertical(panel.gameObject, 10, 6f);

			if (selectedPart == null || !selectedGroup.HasValue)
			{
				CancelPicker();
				return;
			}

			Text header = KrillUi.Label(panel,
				Localizer.Format("#LOC_KRILL_ui_pickActionFor", selectedPart.partInfo.title, selectedGroup.Value.ToString()),
				12, KrillUi.Tan);
			KrillUi.Size(header.gameObject, -1f, 20f);

			// Actions already assigned for this (set, group) are hidden: adding a
			// duplicate serves no purpose, remove it from column 3 first instead.
			// Symmetry fan-out writes to every member, so checking selectedPart suffices.
			List<KrillQuery.AssignmentEntry> already = GetEntriesForPart(ActiveParts(), selectedGroup.Value, selectedPart);

			RectTransform list = KrillUi.ScrollList(panel, 180f);
			int total = 0;
			int shown = 0;
			foreach (PartModule pm in selectedPart.Modules)
			{
				foreach (BaseAction ba in pm.Actions)
				{
					total++;
					if (IsAlreadyAssigned(ba, already))
					{
						continue;
					}
					shown++;
					BaseAction captured = ba;
					KrillUi.TextButton(list, ba.guiName, () => AssignAction(captured), KrillUi.Panel2, KrillUi.Text, 12, -1f, 22f);
				}
			}
			if (shown == 0)
			{
				string emptyKey = total == 0 ? "#LOC_KRILL_ui_noActions" : "#LOC_KRILL_ui_allActionsAssigned";
				KrillUi.Label(list, Loc(emptyKey), 12, KrillUi.Muted, TextAnchor.MiddleCenter);
			}

			KrillUi.TextButton(panel, Loc("#LOC_KRILL_ui_cancel"), CancelPicker, KrillUi.Panel, KrillUi.Muted, 12, 90f, 24f);
		}

		private static bool IsAlreadyAssigned(BaseAction ba, List<KrillQuery.AssignmentEntry> already)
		{
			for (int i = 0; i < already.Count; i++)
			{
				if (already[i].resolved == ba)
				{
					return true;
				}
			}
			return false;
		}

		private void AssignAction(BaseAction ba)
		{
			KrillActionRef actionRef = KrillActionRef.FromAction(ba);
			if (actionRef == null || selectedPart == null || !selectedGroup.HasValue)
			{
				CancelPicker();
				return;
			}
			// Fans out to every current symmetry sibling, not just the instance clicked,
			// matching stock's "assign once, applies to the whole set". Each sibling gets
			// its own ref copy only to avoid sharing one mutable object.
			bool assignedAny = false;
			foreach (Part p in KrillQuery.GetSymmetryGroup(selectedPart))
			{
				ModuleKrill m = p.FindModuleImplementing<ModuleKrill>();
				if (m == null)
				{
					continue;
				}
				KrillActionRef refCopy = new KrillActionRef
				{
					module = actionRef.module,
					occurrence = actionRef.occurrence,
					action = actionRef.action
				};
				m.Data.AddAssignment(activeSet, selectedGroup.Value, refCopy);
				m.MarkDirty();
				assignedAny = true;
			}
			if (assignedAny)
			{
				ScreenMessages.PostScreenMessage(
					Localizer.Format("#LOC_KRILL_ui_assignDone", ba.guiName, selectedGroup.Value.ToString()),
					4f, ScreenMessageStyle.UPPER_CENTER);
			}
			pickerKind = PickerKind.None;
			InputLockManager.RemoveControlLock(PickLockId);
			// selectedPart stays selected — it now has real data instead of being transient.
			RebuildContent();
		}
	}
}
