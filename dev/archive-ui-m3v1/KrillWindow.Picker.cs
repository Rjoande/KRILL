using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KRILL.UI
{
	/// <summary>
	/// Action assignment: the KAL-style scene-pick gesture (hover highlight, click
	/// to select — ported from KRAB's KrabEditorWindow.HandlePartPicking, same
	/// mouse-up-confirms fix for the same reason: the mouse button is still
	/// physically down for a few frames after GetMouseButtonDown, and the editor's
	/// own part-drag would otherwise grab the part in that unlocked window) followed
	/// by a plain list of that part's actions to choose from. Works in both editor
	/// and flight (2026-07-11 revision: M3's first pass restricted this to the
	/// editor; in-game feedback asked for full parity — assigning without leaving
	/// the vessel you're actually flying).
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

		private PickerKind pickerKind;
		private int pickerTargetGroup;
		private Part pickedPart;
		private Part hoverPart;
		private Part pendingPickPart;

		private void StartPartPick(int targetGroup)
		{
			pendingRemoveGroup = -1;
			CollapseDetail(); // avoid the detail view's blue and the picker's cyan fighting over the same part's highlight
			pickerTargetGroup = targetGroup;
			pickedPart = null;
			pickerKind = PickerKind.PickingPart;
			InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, PickLockId);
			RebuildContent();
		}

		private void ClearPickerState()
		{
			pickerKind = PickerKind.None;
			pickedPart = null;
			pendingPickPart = null;
			if (hoverPart != null)
			{
				hoverPart.SetHighlightDefault();
				hoverPart = null;
			}
			InputLockManager.RemoveControlLock(PickLockId);
		}

		private void CancelPicker()
		{
			ClearPickerState();
			RebuildContent();
		}

		/// <summary>Scene-aware "is this part part of the craft we're working on" check (KRAB's own PartOnSameCraft does the identical split).</summary>
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
		/// Confirms on mouse-UP, not mouse-down: the same fix KRAB needed (in-game
		/// report there, 2026-07-09) for the identical reason — releasing the
		/// picking lock the instant GetMouseButtonDown fires still leaves the mouse
		/// button physically held for the remaining frames of that click, long
		/// enough for the editor's own part-drag (which re-checks its lock every
		/// frame) to grab the part and start dragging it.
		/// </summary>
		private void HandlePartPicking()
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				CancelPicker();
				return;
			}

			if (pendingPickPart != null)
			{
				if (Input.GetMouseButtonUp(0))
				{
					pickedPart = pendingPickPart;
					pendingPickPart = null;
					pickerKind = PickerKind.PickingAction;
					InputLockManager.RemoveControlLock(PickLockId);
					// Keep the picked part visibly marked while its action list is shown.
					pickedPart.SetHighlightType(Part.HighlightType.AlwaysOn);
					pickedPart.SetHighlightColor(Color.cyan);
					pickedPart.SetHighlight(true, false);
					RebuildContent();
				}
				return;
			}

			Part hovered = Mouse.HoveredPart;
			if (hovered != null && !PartOnActiveCraft(hovered))
			{
				hovered = null;
			}
			if (hovered != hoverPart)
			{
				if (hoverPart != null)
				{
					hoverPart.SetHighlightDefault();
				}
				hoverPart = hovered;
				if (hoverPart != null)
				{
					hoverPart.SetHighlightType(Part.HighlightType.AlwaysOn);
					hoverPart.SetHighlightColor(Color.cyan);
					hoverPart.SetHighlight(true, false);
				}
			}
			if (Input.GetMouseButtonDown(0) && hoverPart != null
				&& (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
			{
				pendingPickPart = hoverPart;
				hoverPart = null;
				// Lock stays active — released only once mouse-up confirms the pick.
			}
		}

		private void BuildPickPrompt()
		{
			RectTransform panel = KrillUi.Bordered("PickPrompt", contentHost, KrillUi.Panel2, KrillUi.Line);
			KrillUi.Vertical(panel.gameObject, 12, 6f);
			KrillUi.Label(panel, Loc("#LOC_KRILL_ui_pickPrompt"), 13, KrillUi.Tan, TextAnchor.MiddleCenter);
			KrillUi.TextButton(panel, Loc("#LOC_KRILL_ui_cancel"), CancelPicker, KrillUi.Panel, KrillUi.Muted, 12, 90f, 24f);
		}

		private void BuildActionPicker()
		{
			RectTransform panel = KrillUi.Bordered("ActionPicker", contentHost, KrillUi.Panel, KrillUi.Line);
			KrillUi.Vertical(panel.gameObject, 10, 6f);

			if (pickedPart == null)
			{
				CancelPicker();
				return;
			}

			Text header = KrillUi.Label(panel,
				Localizer.Format("#LOC_KRILL_ui_pickActionFor", pickedPart.partInfo.title, pickerTargetGroup.ToString()),
				12, KrillUi.Tan);
			KrillUi.Size(header.gameObject, -1f, 20f);

			RectTransform list = KrillUi.ScrollList(panel, 180f);
			int shown = 0;
			foreach (PartModule pm in pickedPart.Modules)
			{
				foreach (BaseAction ba in pm.Actions)
				{
					shown++;
					BaseAction captured = ba;
					KrillUi.TextButton(list, ba.guiName, () => AssignAction(captured), KrillUi.Panel2, KrillUi.Text, 12, -1f, 22f);
				}
			}
			if (shown == 0)
			{
				KrillUi.Label(list, Loc("#LOC_KRILL_ui_noActions"), 12, KrillUi.Muted, TextAnchor.MiddleCenter);
			}

			KrillUi.TextButton(panel, Loc("#LOC_KRILL_ui_cancel"), CancelPicker, KrillUi.Panel, KrillUi.Muted, 12, 90f, 24f);
		}

		private void AssignAction(BaseAction ba)
		{
			KrillActionRef actionRef = KrillActionRef.FromAction(ba);
			if (actionRef == null || pickedPart == null)
			{
				CancelPicker();
				return;
			}
			ModuleKrill m = pickedPart.FindModuleImplementing<ModuleKrill>();
			if (m != null)
			{
				m.Data.AddAssignment(activeSet, pickerTargetGroup, actionRef);
				m.MarkDirty();
				ScreenMessages.PostScreenMessage(
					Localizer.Format("#LOC_KRILL_ui_assignDone", ba.guiName, pickerTargetGroup.ToString()),
					4f, ScreenMessageStyle.UPPER_CENTER);
			}
			ClearPickerState();
			RebuildContent();
		}
	}
}
