using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KRILL.UI
{
	/// <summary>
	/// Two independent, decoupled steps (2026-07-18 redesign — M3's first pass
	/// combined them into one continuous flow):
	///
	/// PickingPart: the KAL-style scene-pick gesture (hover highlight, click to
	/// select — ported from KRAB's KrabEditorWindow.HandlePartPicking, same
	/// mouse-up-confirms fix: the mouse button is still physically down for a few
	/// frames after GetMouseButtonDown, and the editor's own part-drag would
	/// otherwise grab the part in that unlocked window). Ends by calling
	/// SelectPart — it does NOT chain into an action list; "+Part" only picks a
	/// part, nothing is persisted yet.
	///
	/// PickingAction: a plain list of the ALREADY-selected part's actions,
	/// entered directly via "+Action" — no scene interaction. Persists a real
	/// KrillAssignment only once the player actually picks one.
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

		// Same PauseMenu race KrillCapture.Cancel() already works around
		// (decompiled PauseMenu.cs, 2026-07-11): it opens on Escape's key-UP, not
		// key-down, and only THEN checks the lock — releasing it on the same frame
		// Escape-down is detected leaves it gone before that later check runs.
		// 2026-07-19: this picker had its own separate lock (PickLockId) that never
		// got the same delay when it was ported into the 3-column redesign, so the
		// bug reappeared here even though KrillCapture itself was already fixed.
		// 2026-07-25: that first fix used a FIXED frame count instead of waiting for
		// the actual key-up — reported broken elsewhere (KrillCapture, same root
		// cause). Fixed the same way here: wait for the real Input.GetKeyUp(Escape),
		// MaxUnlockWaitFrames is only a safety net in case it's never observed.
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
			RebuildContent();
		}

		private void StartActionPick()
		{
			if (selectedPart == null)
			{
				return;
			}
			pendingRemovePart = false;
			pickerKind = PickerKind.PickingAction;
			// 2026-07-25: this state never claimed a lock at all before, so Esc
			// during the action list fell straight through to the stock pause menu
			// — reusing PickLockId is safe, pickerKind is never PickingPart and
			// PickingAction at the same time, so the two never fight over it.
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
		/// Un-hovering a part that's ALSO the persistent selection (or one of ITS
		/// symmetry siblings, 2026-07-27) must restore the group's blue highlight,
		/// not clear it to default — confirmed the hard way while porting this:
		/// without the check, mousing over the already-selected part during a fresh
		/// pick and moving away again silently erased its selection highlight even
		/// though selectedPart itself never changed.
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

		/// <summary>Cyan preview for the WHOLE prospective symmetry group while picking, not just the part under the cursor — shows what "+ Part" is actually about to select (2026-07-27).</summary>
		private static void ApplyHoverGroupHighlight(Part hovered)
		{
			foreach (Part p in KrillQuery.GetSymmetryGroup(hovered))
			{
				p.SetHighlightType(Part.HighlightType.AlwaysOn);
				p.SetHighlightColor(Color.cyan);
				p.SetHighlight(true, false);
			}
		}

		/// <summary>Button/programmatic cancel (Cancel button, vessel switch, window close): no PauseMenu race to protect against, release the lock now.</summary>
		private void CancelPicker()
		{
			ClearPickerState();
			RebuildContent();
		}

		/// <summary>Escape-triggered cancel: keeps the lock until Escape's key-up is observed instead of releasing it immediately — see MaxUnlockWaitFrames.</summary>
		private void CancelPickerFromEscape()
		{
			pickerKind = PickerKind.None;
			pendingPickPart = null;
			ClearHoverHighlight();
			pickUnlockWaitFramesLeft = MaxUnlockWaitFrames;
			RebuildContent();
		}

		/// <summary>Driven from LateUpdate while waiting for Escape's key-up after a cancel (pickerKind is already None, so HandlePartPicking/HandleActionPicking are no longer being called).</summary>
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
					SelectPart(picked);
					// Chain straight into the action list for a NEWLY picked part
					// (2026-07-19 user feedback: two separate clicks for the common
					// case felt like busywork) — re-selecting an ALREADY assigned part
					// via its column-2 row still just shows its existing actions,
					// unaffected, that goes through OnPartClicked instead.
					StartActionPick();
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
				// Must clear the cyan hover highlight HERE, not rely on the mouse-up
				// confirm path to overwrite it later (bug report
				// notes/bug-report-picker-highlight-stuck.md, 2026-07-22): if Esc
				// cancels the pick while the mouse button is still down (between this
				// down-event and the up-event that would confirm it), hoverPart is
				// already null by the time CancelPickerFromEscape runs, so
				// ClearHoverHighlight has nothing left to clean up and the part stays
				// AlwaysOn+cyan forever. Reusing ClearHoverHighlight (not a bare
				// SetHighlightDefault, which is what KRAB's original does — it has no
				// persistent-selection concept) also correctly restores blue instead of
				// default if this part happens to already be selectedPart.
				ClearHoverHighlight();
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

			if (selectedPart == null || !selectedGroup.HasValue)
			{
				CancelPicker();
				return;
			}

			Text header = KrillUi.Label(panel,
				Localizer.Format("#LOC_KRILL_ui_pickActionFor", selectedPart.partInfo.title, selectedGroup.Value.ToString()),
				12, KrillUi.Tan);
			KrillUi.Size(header.gameObject, -1f, 20f);

			// Actions already assigned to this part for this (set, group) are hidden
			// from the list (2026-07-28 user request) — offering to add a duplicate
			// serves no purpose; use the ✕ in column 3 to remove one first if you
			// actually want to reassign it. selectedPart already carries its own full
			// copy of the group's assignments (symmetry fan-out writes to every
			// member), so checking against it alone is enough — no extra symmetry
			// handling needed here, same as column 3 already relies on.
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
			// Fans out to every CURRENT symmetry sibling of selectedPart (2026-07-27),
			// not just the one instance actually clicked in the scene — matches the
			// stock behavior this is meant to replicate ("assign once, applies to the
			// whole symmetric set"). Each part gets its OWN KrillActionRef value copy:
			// KrillActionRef has no part identity of its own (module+occurrence+action
			// only, see its class doc), so a fresh copy per sibling is only about not
			// sharing one mutable object across independent parts' data, not about
			// resolving correctly — the same value resolves fine on every sibling
			// since they're all the same part type with the same modules/actions.
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
