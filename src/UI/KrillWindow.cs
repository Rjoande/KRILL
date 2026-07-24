using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KRILL.UI
{
	/// <summary>
	/// The single KRILL window — 3-column Miller-style layout (2026-07-18 redesign,
	/// replacing M3's flat list + expand-in-place detail view): Action Groups |
	/// Parts | Actions, mirroring the shape of the data itself (set → group → part
	/// → action IS the KrillAssignment hierarchy — this is a pure view change,
	/// Core is untouched).
	///
	/// Three INDEPENDENT ScrollLists (2026-07-19 rework — the original single
	/// shared row grid tied every column to the SAME absolute row index, so that
	/// once column 1 started listing every group up to the settings cap
	/// unconditionally, any selected group with more than one assigned part
	/// pushed its Parts/Actions rows down into whatever OTHER group column 1
	/// happened to be showing on those same rows — visually pairing unrelated
	/// data). Real Miller/Finder columns don't share a row grid at all: each
	/// column is simply its own top-anchored list, sized to its own content, with
	/// its own scroll position (also fixes the "every click resets scroll to top"
	/// report — see the save/restore in RebuildContent). Column 2/3 selection is
	/// still driven by selectedGroup/selectedPart exactly as before; it just no
	/// longer needs to know WHICH ROW its parent was on.
	///
	/// Selection state: selectedGroup drives what's shown in Parts; selectedPart
	/// (persistent, not just picker-hover — user clarification 2026-07-18) drives
	/// Actions. A part picked via "+Part" but not yet given an action is TRANSIENT:
	/// nothing is persisted until "+Action" creates a real KrillAssignment (accepted
	/// by design — it simply disappears if you close the window first).
	///
	/// Shell (canvas, titlebar, drag, hover focus-lock) unchanged from M3's first
	/// pass — ported from KRAB's KrabEditorWindow.cs in structure.
	/// </summary>
	public partial class KrillWindow : MonoBehaviour
	{
		private const float WindowWidth = 660f;
		private const float ColWidth = 195f;
		private const float RowHeight = 24f;
		private const float ListAreaHeight = 340f;
		private const string InputLockId = "KRILL_WINDOW";

		private static KrillWindow current;

		/// <summary>Set by KrillToolbarApp so the toolbar button un-presses itself when the window closes via its own ✕ instead of the toolbar (which would otherwise desync the button's visual state).</summary>
		public static System.Action OnClosed;

		private RectTransform windowRect;
		private Transform contentHost;

		/// <summary>Set by BuildColumn each rebuild; read back at the START of the NEXT RebuildContent to restore that column's scroll position (see class doc, 2026-07-19). Null while the picker overlay (BuildPickPrompt/BuildActionPicker) is showing instead of the 3 columns.</summary>
		private ScrollRect groupScrollRect, partScrollRect, actionScrollRect;

		/// <summary>
		/// Last known scroll position per column, 1f (top) by default. Separate from
		/// the ScrollRect refs above on purpose (2026-07-20 fix): "+Part"/"+Action"
		/// detour through the picker overlay, which has no columns at all, so the
		/// ScrollRect refs go null for that rebuild and stay null — reading straight
		/// from them once the picker closes would see null and silently reset to the
		/// top. These floats are updated from the live rects whenever one exists, so
		/// they still hold the last real position across a picker round-trip.
		/// </summary>
		private float groupScrollPos = 1f, partScrollPos = 1f, actionScrollPos = 1f;

		/// <summary>0 = Default, 1..4 = the stock override sets (Vessel.GroupOverride values).</summary>
		private int activeSet;

		/// <summary>Selected row in column 1, or null. Survives set-tab switches (comparing the same group across sets is useful); cleared on vessel change.</summary>
		private int? selectedGroup;

		/// <summary>Selected row in column 2 (persistent blue highlight — not just picker hover), or null. Cleared whenever the group selection or the active set changes: the Parts list it indexes into is scoped to (activeSet, selectedGroup).</summary>
		private Part selectedPart;

		/// <summary>Dark blue for the persistently SELECTED part — distinct from the picker's cyan hover so the two meanings never look the same (user clarification 2026-07-18).</summary>
		private static readonly Color SelectedPartColor = new Color(0.18f, 0.35f, 0.85f);

		/// <summary>Two-click confirm for the part [x] (removes every action of that part in this group+set).</summary>
		private bool pendingRemovePart;

		// Custom groups 1..10 map to these KSPActionGroup values in array order.
		private static readonly KSPActionGroup[] StockGroups =
		{
			KSPActionGroup.Custom01, KSPActionGroup.Custom02, KSPActionGroup.Custom03, KSPActionGroup.Custom04,
			KSPActionGroup.Custom05, KSPActionGroup.Custom06, KSPActionGroup.Custom07, KSPActionGroup.Custom08,
			KSPActionGroup.Custom09, KSPActionGroup.Custom10,
		};

		private struct GroupEntry
		{
			public int number;
			public bool isStock;
			public string name;
			public string bind;
		}

		/// <summary>Toolbar "pressed" callback: create the window if not already open (idempotent).</summary>
		public static void Open()
		{
			if (current != null)
			{
				return;
			}
			GameObject host = new GameObject("KrillWindow");
			current = host.AddComponent<KrillWindow>();
			current.Build();
		}

		/// <summary>Toolbar "unpressed" callback, and the titlebar's ✕ button.</summary>
		public static void CloseCurrent()
		{
			current?.Close();
		}

		private void OnDestroy()
		{
			ClearPickerState();
			ClearPartHighlight();
			// Safety net: don't leave a capture's ALLBUTCAMERAS lock stuck if the
			// window itself closes mid-capture (e.g. the editor scene starts
			// unloading) — nothing else would be left alive to release it.
			KrillCapture.ForceCancel();
			InputLockManager.RemoveControlLock(InputLockId);
			GameEvents.onGameSceneLoadRequested.Remove(OnSceneChange);
			GameEvents.OnVesselOverrideGroupChanged.Remove(OnVesselSetChanged);
			GameEvents.onVesselChange.Remove(OnActiveVesselChanged);
			if (current == this)
			{
				current = null;
				OnClosed?.Invoke();
			}
		}

		private void OnSceneChange(GameScenes scene)
		{
			Close();
		}

		/// <summary>Keeps the tab highlight in sync when the set changes from elsewhere — stock F6/F7, another mod, or our own tab click in flight (which itself goes through Vessel.SetGroupOverride, so this also handles that path — see OnTabClicked).</summary>
		private void OnVesselSetChanged(Vessel v)
		{
			if (v == null || v != FlightGlobals.ActiveVessel)
			{
				return;
			}
			activeSet = v.GroupOverride;
			DeselectPart();
			RebuildContent();
		}

		/// <summary>
		/// Switching the active vessel (e.g. '['/']') leaves selectedPart pointing
		/// at a craft that's no longer the one being shown — deselect rather than
		/// show a stale blue highlight on an unrelated ship. Also cancels any
		/// in-progress picker: HandlePartPicking's PartOnActiveCraft check would
		/// otherwise silently start scoping to the NEW vessel on the very next
		/// frame, letting the player pick a part on a completely different craft
		/// than the one they opened "+Part" on.
		/// </summary>
		private void OnActiveVesselChanged(Vessel v)
		{
			ClearPickerState();
			selectedGroup = null;
			DeselectPart();
			RebuildContent();
		}

		private void Close()
		{
			Destroy(gameObject);
		}

		private static string Loc(string key)
		{
			return Localizer.Format(key);
		}

		// -------------------------------------------------------- scene abstraction

		/// <summary>Parts of the craft being worked on — vessel in flight, ship in the editor.</summary>
		private static IList<Part> ActiveParts()
		{
			if (HighLogic.LoadedSceneIsFlight)
			{
				return FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.parts : null;
			}
			return EditorLogic.fetch != null && EditorLogic.fetch.ship != null ? EditorLogic.fetch.ship.parts : null;
		}

		/// <summary>Root part carrying group names/toggle state (design doc §5 convention). No ShipConstruct.rootPart in the editor — first parentless part.</summary>
		private static Part RootPart()
		{
			if (HighLogic.LoadedSceneIsFlight)
			{
				return FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.rootPart : null;
			}
			IList<Part> parts = ActiveParts();
			if (parts == null)
			{
				return null;
			}
			for (int i = 0; i < parts.Count; i++)
			{
				if (parts[i].parent == null)
				{
					return parts[i];
				}
			}
			return parts.Count > 0 ? parts[0] : null;
		}

		/// <summary>"Predefinito" / vessel's own override-group name / generic "Impostaz. N" — same stock keys AGSetHUD already verified.</summary>
		private static string SetLabel(int set)
		{
			if (set == 0)
			{
				return Localizer.Format("#autoLOC_6013000");
			}
			Vessel v = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
			if (v != null && v.OverrideGroupNames != null && set <= v.OverrideGroupNames.Length
				&& !string.IsNullOrEmpty(v.OverrideGroupNames[set - 1]))
			{
				return v.OverrideGroupNames[set - 1];
			}
			return Localizer.Format("#autoLOC_6013001", set.ToString());
		}

		// ------------------------------------------------------------------ build

		private void Build()
		{
			GameEvents.onGameSceneLoadRequested.Add(OnSceneChange);
			GameEvents.OnVesselOverrideGroupChanged.Add(OnVesselSetChanged);
			GameEvents.onVesselChange.Add(OnActiveVesselChanged);

			if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null)
			{
				// Open already showing the vessel's REAL current set, not always Default.
				activeSet = FlightGlobals.ActiveVessel.GroupOverride;
			}

			Canvas canvas = gameObject.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 900;
			CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
			scaler.scaleFactor = GameSettings.UI_SCALE;
			gameObject.AddComponent<GraphicRaycaster>();

			windowRect = KrillUi.Bordered("Window", transform, KrillUi.Win, KrillUi.Line);
			windowRect.anchorMin = windowRect.anchorMax = new Vector2(0.5f, 0.5f);
			windowRect.pivot = new Vector2(0.5f, 0.5f);
			windowRect.anchoredPosition = new Vector2(0f, 40f);
			windowRect.sizeDelta = new Vector2(WindowWidth, 100f);
			FocusLock focus = windowRect.gameObject.AddComponent<FocusLock>();
			focus.lockId = InputLockId;

			KrillUi.Vertical(windowRect.gameObject, 1, 0f);
			ContentSizeFitter fitter = windowRect.gameObject.AddComponent<ContentSizeFitter>();
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			BuildTitlebar();

			contentHost = KrillUi.Go("Content", windowRect).transform;
			KrillUi.Vertical(contentHost.gameObject, 10, 8f);

			RebuildContent();
		}

		private void BuildTitlebar()
		{
			RectTransform bar = KrillUi.Bordered("Titlebar", windowRect, KrillUi.HeadA, KrillUi.Line);
			KrillUi.Size(bar.gameObject, -1f, 34f);
			KrillUi.Horizontal(bar.gameObject, 8, 8f);

			Text title = KrillUi.Label(bar, Loc("#LOC_KRILL_ui_windowTitle"), 14, KrillUi.Tan,
				TextAnchor.MiddleLeft, FontStyle.Bold);
			KrillUi.Size(title.gameObject, -1f, 22f, 1f);

			Text badge = KrillUi.Label(bar,
				Loc(HighLogic.LoadedSceneIsFlight ? "#LOC_KRILL_ui_flightBadge" : "#LOC_KRILL_ui_editorBadge"),
				10, KrillUi.Malachite, TextAnchor.MiddleRight);
			KrillUi.Size(badge.gameObject, 90f, 22f);

			KrillUi.TextButton(bar, "✕", Close, KrillUi.Panel2, KrillUi.TanDim, 13, 26f, 24f);

			DragHandler drag = bar.gameObject.AddComponent<DragHandler>();
			drag.target = windowRect;
		}

		// --------------------------------------------------------- content rebuild

		internal void RebuildContent()
		{
			// Update the persisted positions from the OLD (about to be destroyed)
			// ScrollRects, when they exist — see groupScrollPos field doc. Left
			// UNCHANGED when a rect is already null (i.e. the picker overlay was
			// showing), so the picker never overwrites these with a stale default.
			if (groupScrollRect != null)
			{
				groupScrollPos = groupScrollRect.verticalNormalizedPosition;
			}
			if (partScrollRect != null)
			{
				partScrollPos = partScrollRect.verticalNormalizedPosition;
			}
			if (actionScrollRect != null)
			{
				actionScrollPos = actionScrollRect.verticalNormalizedPosition;
			}

			for (int i = contentHost.childCount - 1; i >= 0; i--)
			{
				Destroy(contentHost.GetChild(i).gameObject);
			}
			groupScrollRect = null;
			partScrollRect = null;
			actionScrollRect = null;

			if (pickerKind == PickerKind.PickingPart)
			{
				BuildSetTabs();
				BuildPickPrompt();
				return;
			}
			if (pickerKind == PickerKind.PickingAction)
			{
				BuildSetTabs();
				BuildActionPicker();
				return;
			}

			BuildSetTabs();
			BuildSetJumpRow();

			IList<Part> parts = ActiveParts();
			List<GroupEntry> groups = BuildGroupEntries(parts);
			int gIdx = selectedGroup.HasValue ? groups.FindIndex(g => g.number == selectedGroup.Value) : -1;
			if (selectedGroup.HasValue && gIdx < 0)
			{
				// Whatever we had selected no longer exists (removed, or fell above
				// the visible cap) — don't leave a stale, unreachable selection.
				selectedGroup = null;
				DeselectPart();
			}
			bool selIsStock = gIdx >= 0 && groups[gIdx].isStock;

			List<Part> assignedParts = (gIdx >= 0 && !selIsStock)
				? KrillQuery.GetAssignedParts(parts, activeSet, selectedGroup.Value) : new List<Part>();
			List<BaseAction> stockActions = (gIdx >= 0 && selIsStock)
				? KrillQuery.GetStockActions(parts, activeSet, StockGroups[selectedGroup.Value - 1]) : new List<BaseAction>();

			int pIdx = (selectedPart != null) ? assignedParts.IndexOf(selectedPart) : -1;
			bool partTransient = selectedPart != null && !selIsStock && pIdx < 0 && gIdx >= 0;
			if (selectedPart != null && !partTransient && pIdx < 0)
			{
				// Selected part belongs to a group/context that no longer applies
				// (e.g. group deselected, or it's a stock row) — drop it.
				DeselectPart();
			}

			List<KrillQuery.AssignmentEntry> actionEntries = (selectedPart != null && gIdx >= 0 && !selIsStock)
				? GetEntriesForPart(parts, selectedGroup.Value, selectedPart) : new List<KrillQuery.AssignmentEntry>();

			GameObject columnsRow = KrillUi.Go("Columns", contentHost);
			KrillUi.Horizontal(columnsRow, 0, 6f);

			RectTransform groupList = BuildColumn(columnsRow.transform, "#LOC_KRILL_ui_colGroups", out groupScrollRect);
			BuildGroupColumn(groupList, groups, gIdx);

			RectTransform partList = BuildColumn(columnsRow.transform, "#LOC_KRILL_ui_colParts", out partScrollRect);
			BuildPartColumn(partList, gIdx, selIsStock, assignedParts, partTransient, stockActions);

			RectTransform actionList = BuildColumn(columnsRow.transform, "#LOC_KRILL_ui_colActions", out actionScrollRect);
			BuildActionColumn(actionList, selIsStock, actionEntries);

			BuildFooter(parts, gIdx >= 0 ? (GroupEntry?)groups[gIdx] : null);

			// Layout groups / ContentSizeFitter haven't measured the new content on
			// this same frame yet — setting verticalNormalizedPosition before a
			// forced layout pass would be measured against a stale (usually zero)
			// content height and get silently ignored.
			Canvas.ForceUpdateCanvases();
			if (groupScrollRect != null)
			{
				groupScrollRect.verticalNormalizedPosition = groupScrollPos;
			}
			if (partScrollRect != null)
			{
				partScrollRect.verticalNormalizedPosition = partScrollPos;
			}
			if (actionScrollRect != null)
			{
				actionScrollRect.verticalNormalizedPosition = actionScrollPos;
			}
		}

		/// <summary>One column: header label + its own independent ScrollList, fixed to ColWidth. Returns the list's content transform to fill.</summary>
		private RectTransform BuildColumn(Transform parent, string headerLocKey, out ScrollRect scrollRect)
		{
			GameObject col = KrillUi.Go("Col", parent);
			KrillUi.Vertical(col, 0, 4f);
			KrillUi.Size(col, ColWidth, -1f);

			Text head = KrillUi.Label(col.transform, Loc(headerLocKey), 10, KrillUi.Faint, TextAnchor.MiddleLeft, FontStyle.Bold);
			KrillUi.Size(head.gameObject, -1f, 18f);

			RectTransform list = KrillUi.ScrollList(col.transform, ListAreaHeight);
			scrollRect = list.GetComponentInParent<ScrollRect>();
			return list;
		}

		private List<GroupEntry> BuildGroupEntries(IList<Part> parts)
		{
			List<GroupEntry> list = new List<GroupEntry>();
			for (int i = 1; i <= 10; i++)
			{
				list.Add(new GroupEntry
				{
					number = i,
					isStock = true,
					name = KrillQuery.GetGroupName(parts, activeSet, i) ?? DefaultGroupName(i),
					bind = StockBindDescribe(i),
				});
			}
			// 2026-07-19: every extended number up to the settings cap is listed
			// unconditionally — not just the ones with data (KrillQuery.GroupsInUse).
			// A group with zero assignments is just an empty row: nothing to persist,
			// nothing to delete, so there's no group-level [x] anymore (see BuildGroupCell).
			int cap = KrillParams.MaxVisibleGroup;
			for (int i = KrillGroups.FirstExtended; i <= cap; i++)
			{
				list.Add(new GroupEntry
				{
					number = i,
					isStock = false,
					name = KrillQuery.GetGroupName(parts, activeSet, i) ?? DefaultGroupName(i),
					bind = KrillKeymap.GetBind(i)?.Describe() ?? "-",
				});
			}
			return list;
		}

		private List<KrillQuery.AssignmentEntry> GetEntriesForPart(IList<Part> parts, int group, Part part)
		{
			List<KrillQuery.AssignmentEntry> all = KrillQuery.GetAssignmentEntries(parts, activeSet, group);
			List<KrillQuery.AssignmentEntry> mine = new List<KrillQuery.AssignmentEntry>();
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i].part == part)
				{
					mine.Add(all[i]);
				}
			}
			return mine;
		}

		// ------------------------------------------------------------------ tabs

		private void BuildSetTabs()
		{
			GameObject row = KrillUi.Go("Tabs", contentHost);
			KrillUi.Horizontal(row, 0, 4f);
			for (int s = 0; s <= Vessel.NumOverrideGroups; s++)
			{
				int captured = s;
				bool active = captured == activeSet;
				Button tab = KrillUi.TextButton(row.transform, SetLabel(captured), () => OnTabClicked(captured),
					active ? KrillUi.Panel2 : KrillUi.Inset, active ? KrillUi.GreenHi : KrillUi.Muted,
					11, 0f, 22f);
				KrillUi.Size(tab.gameObject, -1f, 22f, 1f);
				if (active)
				{
					tab.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
				}
			}
		}

		private void OnTabClicked(int set)
		{
			pendingRemovePart = false;
			DeselectPart();
			if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null)
			{
				// Actually switches the vessel's live set (F6/F7 do the same thing
				// internally) — OnVesselSetChanged picks up activeSet + rebuild from
				// the resulting event, so no need to duplicate that here.
				FlightGlobals.ActiveVessel.SetGroupOverride(set);
			}
			else
			{
				activeSet = set;
				RebuildContent();
			}
		}

		// ------------------------------------------------------------ set jump (M4)

		/// <summary>
		/// One small row under the tabs, always visible: a "jump directly to this
		/// set" bind per set, independent of which group/part is selected — global
		/// player keymap (KrillSetKeymap), same capture mechanism as group binds.
		/// Editable in both scenes (like the group Capture button); only fires in
		/// flight (KrillInputManager) since Vessel.SetGroupOverride needs a Vessel.
		///
		/// 2026-07-25 user report: an earlier version led with a fixed-width "Jump:"
		/// label cell, which threw the 5 buttons out of alignment with the 5 tabs
		/// directly above (different cell count/widths between the two rows).
		/// Dropped the label — an unbound cell now just shows the localized word
		/// "Jump" as its own text instead — and this row now mirrors BuildSetTabs'
		/// Horizontal/Size calls EXACTLY (same padding, spacing, cell count, each
		/// cell width=auto+flexible=1) so the two rows are structurally forced to
		/// produce identical column positions, not just visually close by eye.
		/// </summary>
		private void BuildSetJumpRow()
		{
			GameObject row = KrillUi.Go("SetJump", contentHost);
			KrillUi.Horizontal(row, 0, 4f);
			for (int s = 0; s <= Vessel.NumOverrideGroups; s++)
			{
				int captured = s;
				KrillBind bind = KrillSetKeymap.GetBind(captured);
				string text = bind != null ? bind.Describe() : Loc("#LOC_KRILL_ui_setJumpEmpty");
				Button btn = KrillUi.TextButton(row.transform, text, () => StartSetCapture(captured),
					KrillUi.Panel2, bind != null ? KrillUi.GreenHi : KrillUi.Muted, 10, 0f, 18f);
				KrillUi.Size(btn.gameObject, -1f, 18f, 1f);
			}
		}

		private void StartSetCapture(int set)
		{
			pendingRemovePart = false;
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_setCaptureStart", SetLabel(set)), 3f, ScreenMessageStyle.UPPER_CENTER);
			KrillCapture.Begin(
				bind => OnSetCaptured(set, bind),
				() => ScreenMessages.PostScreenMessage(Loc("#LOC_KRILL_ui_captureCancelled"), 3f, ScreenMessageStyle.UPPER_CENTER));
		}

		private void OnSetCaptured(int set, KrillBind bind)
		{
			string conflictSuffix = "";
			List<string> conflicts = KrillConflicts.Describe(bind, -1, set);
			if (conflicts.Count > 0)
			{
				conflictSuffix = " (" + Loc("#LOC_KRILL_ui_conflicts") + ": " + string.Join(", ", conflicts) + ")";
			}
			KrillSetKeymap.SetBind(set, bind);
			string msg = Localizer.Format("#LOC_KRILL_ui_setCaptureDone", SetLabel(set), bind.Describe()) + conflictSuffix;
			ScreenMessages.PostScreenMessage(msg, 5f, ScreenMessageStyle.UPPER_CENTER);
			RebuildContent();
		}

		// ------------------------------------------------------------ column 1

		private void BuildGroupColumn(Transform listContent, List<GroupEntry> groups, int gIdx)
		{
			for (int i = 0; i < groups.Count; i++)
			{
				GroupEntry g = groups[i];
				bool sel = i == gIdx;
				RectTransform cell = KrillUi.Bordered("G", listContent, sel ? KrillUi.Panel2 : KrillUi.Panel, KrillUi.Line);
				KrillUi.Size(cell.gameObject, -1f, RowHeight);
				KrillUi.Horizontal(cell.gameObject, 4, 4f);
				Text num = KrillUi.Label(cell, g.number.ToString(), 11, g.isStock ? KrillUi.Muted : KrillUi.Malachite,
					TextAnchor.MiddleCenter, FontStyle.Bold);
				KrillUi.Size(num.gameObject, 18f, RowHeight);
				Text nm = KrillUi.Label(cell, g.name, 11, sel ? KrillUi.Tan : KrillUi.Text);
				KrillUi.Size(nm.gameObject, -1f, RowHeight, 1f);
				Button btn = cell.gameObject.AddComponent<Button>();
				btn.targetGraphic = cell.GetComponent<Image>();
				btn.onClick.AddListener(() => OnGroupClicked(g.number, g.isStock));
			}
		}

		private void OnGroupClicked(int number, bool isStock)
		{
			pendingRemovePart = false;
			DeselectPart();
			selectedGroup = number;
			RebuildContent();
		}

		private static string DefaultGroupName(int group)
		{
			return Loc("#LOC_KRILL_ui_groupDefaultName") + " " + group;
		}

		// ------------------------------------------------------------ column 2

		private void BuildPartColumn(Transform listContent, int gIdx, bool selIsStock,
			List<Part> assignedParts, bool partTransient, List<BaseAction> stockActions)
		{
			if (gIdx < 0)
			{
				return;
			}

			if (selIsStock)
			{
				if (stockActions.Count == 0)
				{
					BuildReadOnlyCell(listContent, Loc("#LOC_KRILL_ui_noAssignments"));
					return;
				}
				// Read-only info: part + action combined onto one line rather than
				// split across columns 2/3 — with every column scrolling
				// independently now (2026-07-19 rework), keeping them split would
				// only stay visually paired by matching row index, exactly the kind
				// of shared-row assumption this rework removes everywhere else.
				// Part names routinely overflow ColWidth once combined with an
				// action name (2026-07-20 user report) — truncate the part side,
				// never the action side: the action is what a player is scanning
				// this list FOR, the part is usually recognizable from a few letters.
				for (int i = 0; i < stockActions.Count; i++)
				{
					Part owner = stockActions[i].listParent != null ? stockActions[i].listParent.part : null;
					string ownerName = owner != null ? owner.partInfo.title : "?";
					BuildReadOnlyCell(listContent, TruncateStockOwnerName(ownerName) + " → " + stockActions[i].guiName);
				}
				return;
			}

			for (int i = 0; i < assignedParts.Count; i++)
			{
				Part p = assignedParts[i];
				bool sel = p == selectedPart;
				BuildClickableCell(listContent, p.partInfo.title, sel, () => OnPartClicked(p),
					sel ? BuildPartRemoveButton(p) : null);
			}
			if (partTransient)
			{
				BuildClickableCell(listContent, selectedPart.partInfo.title, true, null, BuildPartRemoveButton(selectedPart));
			}
			KrillUi.TextButton(listContent, Loc("#LOC_KRILL_ui_addPart"), StartPartPick,
				KrillUi.Panel2, KrillUi.GreenHi, 11, -1f, RowHeight);
		}

		private System.Action BuildPartRemoveButton(Part p)
		{
			// Returned as a closure so BuildClickableCell can render it inline;
			// actual removal logic lives in OnRemovePartClicked.
			return () => OnRemovePartClicked(p);
		}

		private void OnPartClicked(Part p)
		{
			pendingRemovePart = false;
			SelectPart(p);
			RebuildContent();
		}

		private void OnRemovePartClicked(Part p)
		{
			if (pendingRemovePart)
			{
				ModuleKrill m = p.FindModuleImplementing<ModuleKrill>();
				if (m != null && selectedGroup.HasValue && m.Data.RemoveGroupInSet(activeSet, selectedGroup.Value))
				{
					m.MarkDirty();
				}
				pendingRemovePart = false;
				DeselectPart();
			}
			else
			{
				pendingRemovePart = true;
			}
			RebuildContent();
		}

		// ------------------------------------------------------------ column 3

		private void BuildActionColumn(Transform listContent, bool selIsStock, List<KrillQuery.AssignmentEntry> actionEntries)
		{
			// Stock groups: their actions are already shown paired with their owning
			// part in column 2 (BuildPartColumn) — nothing of substance to add here.
			// selectedPart is always null in stock context (RebuildContent's guard).
			if (selIsStock || selectedPart == null)
			{
				return;
			}

			for (int i = 0; i < actionEntries.Count; i++)
			{
				KrillQuery.AssignmentEntry entry = actionEntries[i];
				string label = entry.resolved != null ? entry.resolved.guiName : Loc("#LOC_KRILL_ui_unresolved");
				BuildClickableCell(listContent, label, false, null, () => RemoveActionEntry(entry));
			}
			KrillUi.TextButton(listContent, Loc("#LOC_KRILL_ui_addAction"), StartActionPick,
				KrillUi.Panel2, KrillUi.GreenHi, 11, -1f, RowHeight);
		}

		private void RemoveActionEntry(KrillQuery.AssignmentEntry entry)
		{
			if (entry.module != null && entry.module.Data.RemoveAssignment(entry.assignment))
			{
				entry.module.MarkDirty();
			}
			RebuildContent();
		}

		// -------------------------------------------------------------- cell helpers

		/// <summary>Part/action names in columns 2/3 for EXTENDED groups (2026-07-25 user request) — 30 real characters plus a single ellipsis glyph "…" (not three dots: 31 total, not 33). Separate from TruncateStockOwnerName below, which is shorter and scoped to the stock read-only view only.</summary>
		private const int ExtendedLabelMaxChars = 30;

		private static string TruncateExtendedLabel(string name)
		{
			if (name.Length <= ExtendedLabelMaxChars)
			{
				return name;
			}
			return name.Substring(0, ExtendedLabelMaxChars) + "…";
		}

		private void BuildClickableCell(Transform rowParent, string label, bool selected,
			System.Action onClick, System.Action onRemove)
		{
			RectTransform cell = KrillUi.Bordered("C", rowParent, selected ? KrillUi.Panel2 : KrillUi.Panel, KrillUi.Line);
			KrillUi.Size(cell.gameObject, -1f, RowHeight);
			KrillUi.Horizontal(cell.gameObject, 4, 4f);
			Text nm = KrillUi.Label(cell, TruncateExtendedLabel(label), 11, selected ? KrillUi.Tan : KrillUi.Text);
			KrillUi.Size(nm.gameObject, -1f, RowHeight, 1f);
			if (onRemove != null)
			{
				bool armedPart = selected && pendingRemovePart;
				KrillUi.TextButton(cell, armedPart ? "✕?" : "✕", () => onRemove(),
					KrillUi.Panel2, armedPart ? KrillUi.Danger : KrillUi.Muted, 10, 18f, RowHeight - 2f);
			}
			if (onClick != null)
			{
				Button btn = cell.gameObject.AddComponent<Button>();
				btn.targetGraphic = cell.GetComponent<Image>();
				btn.onClick.AddListener(() => onClick());
			}
		}

		private void BuildReadOnlyCell(Transform rowParent, string label)
		{
			RectTransform cell = KrillUi.Bordered("R", rowParent, KrillUi.Panel, KrillUi.Line);
			KrillUi.Size(cell.gameObject, -1f, RowHeight);
			KrillUi.Horizontal(cell.gameObject, 4, 4f);
			Text nm = KrillUi.Label(cell, label, 11, KrillUi.Faint, TextAnchor.MiddleLeft, FontStyle.Italic);
			KrillUi.Size(nm.gameObject, -1f, RowHeight, 1f);
		}

		/// <summary>Fits "PartName → ActionName" in one ColWidth-wide row (2026-07-20 user report) — picked by eye against the actual column width, not measured against the font metrics.</summary>
		private const int StockOwnerNameMaxChars = 8;

		private static string TruncateStockOwnerName(string name)
		{
			if (name.Length <= StockOwnerNameMaxChars)
			{
				return name;
			}
			return name.Substring(0, StockOwnerNameMaxChars) + "...";
		}

		// -------------------------------------------------------------- selection

		private void SelectPart(Part p)
		{
			ClearPartHighlight();
			selectedPart = p;
			ApplySelectedPartHighlight();
		}

		private void DeselectPart()
		{
			ClearPartHighlight();
			selectedPart = null;
			pendingRemovePart = false;
		}

		private void ApplySelectedPartHighlight()
		{
			if (selectedPart != null)
			{
				selectedPart.SetHighlightType(Part.HighlightType.AlwaysOn);
				selectedPart.SetHighlightColor(SelectedPartColor);
				selectedPart.SetHighlight(true, false);
			}
		}

		private void ClearPartHighlight()
		{
			if (selectedPart != null)
			{
				selectedPart.SetHighlightDefault();
			}
		}

		// ------------------------------------------------------------------ footer

		private static KeyBinding StockKeyBinding(int group)
		{
			switch (group)
			{
				case 1: return GameSettings.CustomActionGroup1;
				case 2: return GameSettings.CustomActionGroup2;
				case 3: return GameSettings.CustomActionGroup3;
				case 4: return GameSettings.CustomActionGroup4;
				case 5: return GameSettings.CustomActionGroup5;
				case 6: return GameSettings.CustomActionGroup6;
				case 7: return GameSettings.CustomActionGroup7;
				case 8: return GameSettings.CustomActionGroup8;
				case 9: return GameSettings.CustomActionGroup9;
				case 10: return GameSettings.CustomActionGroup10;
				default: return null;
			}
		}

		private static string StockBindDescribe(int group)
		{
			KeyBinding kb = StockKeyBinding(group);
			if (kb == null || kb.primary == null || kb.primary.isNone)
			{
				return "-";
			}
			return kb.primary.code.ToString();
		}

		/// <summary>The group's CURRENT bind as a KrillBind, for a persistent conflict check in the footer (KrillConflicts.Describe takes a candidate bind — stock groups don't have layered modifiers, so this is always primary-only for them).</summary>
		private static KrillBind CurrentBind(GroupEntry g)
		{
			if (g.isStock)
			{
				KeyBinding kb = StockKeyBinding(g.number);
				if (kb == null || kb.primary == null || kb.primary.isNone)
				{
					return null;
				}
				return new KrillBind { primary = kb.primary.code };
			}
			return KrillKeymap.GetBind(g.number);
		}

		private void BuildFooter(IList<Part> parts, GroupEntry? selected)
		{
			GameObject footer = KrillUi.Go("Footer", contentHost);
			KrillUi.Horizontal(footer, 0, 8f);
			KrillUi.Size(footer, -1f, 26f);

			if (selected.HasValue)
			{
				GroupEntry g = selected.Value;
				InputField nameField = KrillUi.Field(footer.transform, g.name, 130f, text => SetGroupName(g.number, text));
				KrillUi.Size(nameField.gameObject, 130f, 20f);

				if (HighLogic.LoadedSceneIsFlight && activeSet > 0 && FlightGlobals.ActiveVessel != null)
				{
					string setName = FlightGlobals.ActiveVessel.OverrideGroupNames != null
						&& activeSet <= FlightGlobals.ActiveVessel.OverrideGroupNames.Length
						? FlightGlobals.ActiveVessel.OverrideGroupNames[activeSet - 1]
						: null;
					InputField setField = KrillUi.Field(footer.transform,
						string.IsNullOrEmpty(setName) ? "" : setName, 100f, SetActiveSetName);
					KrillUi.Size(setField.gameObject, 100f, 20f);
				}

				KrillUi.TextButton(footer.transform, Loc("#LOC_KRILL_ui_capture"), () => StartCapture(g.number, g.isStock),
					KrillUi.Panel2, KrillUi.TanDim, 11, 55f, 22f);
				if (HighLogic.LoadedSceneIsFlight)
				{
					KrillUi.TextButton(footer.transform, Loc("#LOC_KRILL_ui_trigger"), () => Trigger(g.number, g.isStock),
						KrillUi.Panel2, KrillUi.GreenHi, 11, 50f, 22f);
				}

				string bindInfo = Localizer.Format("#LOC_KRILL_ui_bindInfo", g.number.ToString(), g.bind);
				List<string> conflicts = KrillConflicts.Describe(CurrentBind(g), g.isStock ? -1 : g.number);
				if (conflicts.Count > 0)
				{
					bindInfo += " (" + Loc("#LOC_KRILL_ui_conflicts") + ": " + string.Join(", ", conflicts) + ")";
				}
				Text info = KrillUi.Label(footer.transform, bindInfo, 11, KrillUi.Muted);
				KrillUi.Size(info.gameObject, -1f, 22f, 1f);
			}
			else
			{
				Text hint = KrillUi.Label(footer.transform, Loc("#LOC_KRILL_ui_hint"), 11, KrillUi.Muted);
				KrillUi.Size(hint.gameObject, -1f, 22f, 1f);
			}
		}

		private void SetGroupName(int group, string text)
		{
			Part root = RootPart();
			ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
			if (m == null)
			{
				return;
			}
			string trimmed = string.IsNullOrEmpty(text) ? null : text.Trim();
			string placeholder = DefaultGroupName(group);
			if (trimmed == placeholder)
			{
				return; // unchanged from placeholder — see the identical guard in the old row field, same reasoning
			}
			m.Data.SetName(activeSet, group, trimmed);
			m.MarkDirty();
			RebuildContent();
		}

		/// <summary>Set-name rename, "symmetric with stock" per the 2026-07-18 decision: writes the SAME field stock persists through ProtoVessel (verified on decompiled ProtoVessel.cs — Save/Load round-trip it, not a KRILL-only shadow name).</summary>
		private void SetActiveSetName(string text)
		{
			Vessel v = FlightGlobals.ActiveVessel;
			if (v == null || activeSet <= 0 || v.OverrideGroupNames == null || activeSet > v.OverrideGroupNames.Length)
			{
				return;
			}
			v.OverrideGroupNames[activeSet - 1] = string.IsNullOrEmpty(text) ? null : text.Trim();
			RebuildContent();
		}

		private void Trigger(int group, bool isStock)
		{
			Vessel v = FlightGlobals.ActiveVessel;
			if (v == null)
			{
				return;
			}
			if (isStock)
			{
				v.ActionGroups.ToggleGroup(StockGroups[group - 1]);
			}
			else
			{
				KrillActivation.Activate(v, group);
			}
		}

		// -------------------------------------------------------------- bind capture

		private void StartCapture(int group, bool isStock)
		{
			pendingRemovePart = false;
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_captureStart", group.ToString()), 3f, ScreenMessageStyle.UPPER_CENTER);
			KrillCapture.Begin(
				bind => OnCaptured(group, isStock, bind),
				() => ScreenMessages.PostScreenMessage(Loc("#LOC_KRILL_ui_captureCancelled"), 3f, ScreenMessageStyle.UPPER_CENTER));
		}

		private void OnCaptured(int group, bool isStock, KrillBind bind)
		{
			string conflictSuffix = "";
			List<string> conflicts = KrillConflicts.Describe(bind, isStock ? -1 : group);
			if (conflicts.Count > 0)
			{
				conflictSuffix = " (" + Loc("#LOC_KRILL_ui_conflicts") + ": " + string.Join(", ", conflicts) + ")";
			}

			if (isStock)
			{
				KeyBinding kb = StockKeyBinding(group);
				if (kb != null)
				{
					kb.primary = new KeyCodeExtended(bind.primary);
					GameSettings.SaveSettings();
				}
				string msgStock = Localizer.Format("#LOC_KRILL_ui_captureDoneStock", group.ToString(), bind.primary.ToString())
					+ (bind.modifiers.Count > 0 ? " " + Loc("#LOC_KRILL_ui_modifiersIgnored") : "") + conflictSuffix;
				ScreenMessages.PostScreenMessage(msgStock, 5f, ScreenMessageStyle.UPPER_CENTER);
			}
			else
			{
				KrillKeymap.SetBind(group, bind);
				string msg = Localizer.Format("#LOC_KRILL_ui_captureDone", group.ToString(), bind.Describe()) + conflictSuffix;
				ScreenMessages.PostScreenMessage(msg, 5f, ScreenMessageStyle.UPPER_CENTER);
			}
			RebuildContent();
		}

		// --------------------------------------------------------------- LateUpdate

		private void LateUpdate()
		{
			// Must drive KrillCapture ourselves: it's the only thing that ticks it in
			// the editor scene (KrillInputManager is flight-only). Redundant-but-safe
			// in flight, where KrillInputManager also ticks it (KrillCapture.Tick is
			// frame-guarded against double-driving).
			if (KrillCapture.NeedsTick)
			{
				KrillCapture.Tick();
				return;
			}
			if (pickerKind == PickerKind.PickingPart)
			{
				HandlePartPicking();
			}
			else if (pickerKind == PickerKind.PickingAction)
			{
				HandleActionPicking();
			}
			else if (pickUnlockWaitFramesLeft >= 0)
			{
				TickPickUnlockDelay();
			}
		}

		// ------------------------------------------------------------ drag / focus

		private class DragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
		{
			public RectTransform target;
			private Vector2 offset;

			public void OnBeginDrag(PointerEventData eventData)
			{
				RectTransformUtility.ScreenPointToLocalPointInRectangle(
					(RectTransform)target.parent, eventData.position, eventData.pressEventCamera, out Vector2 point);
				offset = target.anchoredPosition - point;
			}

			public void OnDrag(PointerEventData eventData)
			{
				if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
					(RectTransform)target.parent, eventData.position, eventData.pressEventCamera, out Vector2 point))
				{
					target.anchoredPosition = point + offset;
				}
			}
		}

		/// <summary>Blocks scene input while the pointer is over the window (UGUI already blocks UI clicks).</summary>
		private class FocusLock : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
		{
			public string lockId;

			public void OnPointerEnter(PointerEventData eventData)
			{
				InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, lockId);
			}

			public void OnPointerExit(PointerEventData eventData)
			{
				InputLockManager.RemoveControlLock(lockId);
			}

			private void OnDisable()
			{
				InputLockManager.RemoveControlLock(lockId);
			}
		}
	}
}
