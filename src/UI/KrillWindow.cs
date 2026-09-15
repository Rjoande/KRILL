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

		/// <summary>
		/// Last on-screen position, so reopening the window lands where it was left
		/// instead of snapping back to the default (2026-08-27, ported from KRAB's
		/// own recent addition — same pattern). Static and session-scoped like
		/// `current` itself: survives closing/reopening the window, resets on a KSP
		/// restart. Not persisted to disk.
		/// </summary>
		private static Vector2? lastWindowPosition;

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

		/// <summary>
		/// Selected AXIS row in column 1 (2026-09-07, A2), or null — mutually
		/// exclusive with selectedGroup: column 1 holds both lists but there is one
		/// selection, and columns 2/3 + footer render either the group view or the
		/// axis view of it. Same lifetime rules as selectedGroup.
		/// </summary>
		private int? selectedAxis;

		/// <summary>
		/// Whether column 1's "Axes" section is unfolded. Session-scoped (static,
		/// like `current`): survives closing/reopening the window, resets on a KSP
		/// restart, never persisted. Null until first decided, which happens on the
		/// first rebuild: open if the craft being shown already carries any
		/// extended-axis data, folded otherwise — so players who never touch axes
		/// don't pay for the rows (user decision 2026-09-07).
		/// </summary>
		private static bool? axesSectionOpen;

		// Stock custom axes 1..4 map to these KSPAxisGroup values in array order
		// (mirror rows A1-A4 in column 1, same read-only role as groups 1-10).
		private static readonly KSPAxisGroup[] StockAxisGroups =
		{
			KSPAxisGroup.Custom01, KSPAxisGroup.Custom02, KSPAxisGroup.Custom03, KSPAxisGroup.Custom04,
		};

		private struct AxisEntry
		{
			public int number;
			public bool isStock;
			public string name;
			public string bind;
		}

		/// <summary>Selected row in column 2 (persistent blue highlight — not just picker hover), or null. Cleared whenever the group selection or the active set changes: the Parts list it indexes into is scoped to (activeSet, selectedGroup).</summary>
		private Part selectedPart;

		/// <summary>Dark blue for the persistently SELECTED part — distinct from the picker's cyan hover so the two meanings never look the same (user clarification 2026-07-18).</summary>
		private static readonly Color SelectedPartColor = new Color(0.18f, 0.35f, 0.85f);

		/// <summary>Two-click confirm for the part [x] (removes every action of that part in this group+set).</summary>
		private bool pendingRemovePart;

		/// <summary>
		/// The axis footer's value slider and its readout (A4), null outside axis
		/// mode. Followed live from LateUpdate (a bound axis moves with the
		/// controller, a released Spring axis ramps home) except while the mouse
		/// holds the handle, when the slider is the source instead. Both refs
		/// die with every rebuild (RebuildContent nulls them before destroying
		/// the footer).
		/// </summary>
		private Slider axisValueSlider;
		private Text axisValueLabel;
		private int axisSliderAxis;
		private bool axisSliderHeld;

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
			if (windowRect != null)
			{
				lastWindowPosition = windowRect.anchoredPosition;
			}
			ClearPickerState();
			ClearPartHighlight();
			// Safety net: don't leave a capture's ALLBUTCAMERAS lock stuck if the
			// window itself closes mid-capture (e.g. the editor scene starts
			// unloading) — nothing else would be left alive to release it.
			KrillCapture.ForceCancel();
			KrillAxisCapture.ForceCancel();
			// No Hold release needed here: a pressed Hold Trigger button releases
			// itself when its GameObject is disabled/destroyed (KrillUi.HoldTracker.
			// OnDisable), whatever the reason — closing the window included.
			InputLockManager.RemoveControlLock(InputLockId);
			GameEvents.onGameSceneLoadRequested.Remove(OnSceneChange);
			GameEvents.OnVesselOverrideGroupChanged.Remove(OnVesselSetChanged);
			GameEvents.onVesselChange.Remove(OnActiveVesselChanged);
			KrillActivation.GroupActivated -= OnGroupActivated;
			GameEvents.onHideUI.Remove(HandleHideUI);
			GameEvents.onShowUI.Remove(HandleShowUI);
			GameEvents.onGamePause.Remove(HandleGamePause);
			GameEvents.onGameUnpause.Remove(HandleGameUnpause);
			if (current == this)
			{
				current = null;
				OnClosed?.Invoke();
			}
		}

		// Independent flags: F2 and Esc can each be toggled on their own, the window
		// stays hidden while either is active (ported from KrabEditorWindow).
		private bool hiddenByUI;
		private bool hiddenByPause;

		private void HandleHideUI()
		{
			hiddenByUI = true;
			UpdateVisibility();
		}

		private void HandleShowUI()
		{
			hiddenByUI = false;
			UpdateVisibility();
		}

		private void HandleGamePause()
		{
			hiddenByPause = true;
			UpdateVisibility();
		}

		private void HandleGameUnpause()
		{
			hiddenByPause = false;
			UpdateVisibility();
		}

		private void UpdateVisibility()
		{
			bool visible = !hiddenByUI && !hiddenByPause;
			if (!visible && pickerKind != PickerKind.None)
			{
				// A part/action pick holds an input lock and needs the (now invisible)
				// prompt to make sense of it — cancel rather than leave both stranded.
				// Key/axis captures are NOT touched: they live in KrillInputManager,
				// hold their own lock, and Escape cancels them before the pause menu
				// can even open (KrillCapture's key-up wait).
				CancelPicker();
			}
			gameObject.SetActive(visible);
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
			RequestRebuild();
		}

		/// <summary>
		/// Set by the three EVENT-driven rebuild paths (set change, vessel change,
		/// group activation) and consumed in LateUpdate — never while the window's
		/// own Hold Trigger button is pressed (KrillSignal.AnyWindowHeld). Rebuilding
		/// tears down every footer GameObject, so an unrelated event mid-press (a
		/// key elsewhere, F6/F7) would otherwise pull the held button from under the
		/// mouse; deferring keeps the press intact until the player lets go, and the
		/// release itself then triggers the pending rebuild (2026-09-02, replaces
		/// the earlier per-event AnyUiHeld guard that only covered one of the three
		/// paths). Should the button be destroyed anyway, it releases itself
		/// (KrillUi.HoldTracker.OnDisable) — this deferral is UX, not correctness.
		/// Click-driven rebuilds stay synchronous: the player can't click anything
		/// else while holding the mouse on the button.
		/// </summary>
		private bool rebuildPending;

		private void RequestRebuild()
		{
			rebuildPending = true;
		}

		/// <summary>Keeps the footer live for a real activation regardless of source (keypress via KrillInputManager, or our own buttons) — see KrillActivation.GroupActivated. Deferred via RequestRebuild, see its doc.</summary>
		private void OnGroupActivated(Vessel v, int group)
		{
			if (v != FlightGlobals.ActiveVessel)
			{
				return;
			}
			RequestRebuild();
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
			selectedAxis = null;
			DeselectPart();
			RequestRebuild();
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

		/// <summary>"Predefinito" / vessel's own override-group name / generic "Impostaz. N" — same stock #autoLOC_* keys the game itself uses for override sets.</summary>
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
			KrillActivation.GroupActivated += OnGroupActivated;
			// In-game report, 2026-09-11: the window stayed on top of the stock pause
			// menu (Esc) and ignored F2 (hide UI) — every other KSP UI hides for
			// both. Same pattern KRAB fixed on 2026-08-15 (KrabEditorWindow):
			// toggling gameObject.SetActive stops Update/LateUpdate too (cheap while
			// hidden) without tearing down any state; re-showing needs no rebuild.
			// FocusLock.OnDisable releases the hover lock if the pointer was over
			// the window at that moment, so no lock is left behind while hidden.
			GameEvents.onHideUI.Add(HandleHideUI);
			GameEvents.onShowUI.Add(HandleShowUI);
			GameEvents.onGamePause.Add(HandleGamePause);
			GameEvents.onGameUnpause.Add(HandleGameUnpause);

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
			windowRect.anchoredPosition = lastWindowPosition ?? new Vector2(0f, 40f);
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
			axisValueSlider = null;
			axisValueLabel = null;
			axisSliderHeld = false;

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
			List<AxisEntry> axes = BuildAxisEntries(parts);
			if (!axesSectionOpen.HasValue)
			{
				axesSectionOpen = KrillQuery.AnyAxisData(parts);
			}
			int gIdx = selectedGroup.HasValue ? groups.FindIndex(g => g.number == selectedGroup.Value) : -1;
			if (selectedGroup.HasValue && gIdx < 0)
			{
				// Whatever we had selected no longer exists (removed, or fell above
				// the visible cap) — don't leave a stale, unreachable selection.
				selectedGroup = null;
				DeselectPart();
			}
			int aIdx = selectedAxis.HasValue && axesSectionOpen.Value ? axes.FindIndex(a => a.number == selectedAxis.Value) : -1;
			if (selectedAxis.HasValue && aIdx < 0)
			{
				// Same as above, plus: folding the Axes section drops the axis selection
				// (an invisible selection driving columns 2/3 would be confusing).
				selectedAxis = null;
				DeselectPart();
			}
			bool axisMode = aIdx >= 0;
			bool selIsStock = axisMode ? axes[aIdx].isStock : gIdx >= 0 && groups[gIdx].isStock;

			List<Part> assignedParts;
			if (axisMode)
			{
				assignedParts = !selIsStock ? KrillQuery.GetAssignedAxisParts(parts, activeSet, selectedAxis.Value) : new List<Part>();
			}
			else
			{
				assignedParts = (gIdx >= 0 && !selIsStock)
					? KrillQuery.GetAssignedParts(parts, activeSet, selectedGroup.Value) : new List<Part>();
			}
			List<BaseAction> stockActions = (!axisMode && gIdx >= 0 && selIsStock)
				? KrillQuery.GetStockActions(parts, activeSet, StockGroups[selectedGroup.Value - 1]) : new List<BaseAction>();
			List<BaseAxisField> stockFields = (axisMode && selIsStock)
				? KrillQuery.GetStockAxisFields(parts, activeSet, StockAxisGroups[selectedAxis.Value - 1]) : new List<BaseAxisField>();

			// Not a plain IndexOf: assignedParts holds one REPRESENTATIVE per symmetry
			// group (KrillQuery.GetAssignedParts), but selectedPart may be whichever
			// sibling was actually clicked in the scene picker, not necessarily that
			// representative — IsSelectedPartOrSibling treats the whole group as one.
			int pIdx = (selectedPart != null) ? assignedParts.FindIndex(IsSelectedPartOrSibling) : -1;
			bool hasSelection = axisMode || gIdx >= 0;
			bool partTransient = selectedPart != null && !selIsStock && pIdx < 0 && hasSelection;
			if (selectedPart != null && !partTransient && pIdx < 0)
			{
				// Selected part belongs to a group/context that no longer applies
				// (e.g. group deselected, or it's a stock row) — drop it.
				DeselectPart();
			}

			List<KrillQuery.AssignmentEntry> actionEntries = (!axisMode && selectedPart != null && gIdx >= 0 && !selIsStock)
				? GetEntriesForPart(parts, selectedGroup.Value, selectedPart) : new List<KrillQuery.AssignmentEntry>();
			List<KrillQuery.AxisFieldEntry> fieldEntries = (axisMode && selectedPart != null && !selIsStock)
				? GetFieldEntriesForPart(parts, selectedAxis.Value, selectedPart) : new List<KrillQuery.AxisFieldEntry>();

			GameObject columnsRow = KrillUi.Go("Columns", contentHost);
			KrillUi.Horizontal(columnsRow, 0, 6f);

			RectTransform groupList = BuildColumn(columnsRow.transform, "#LOC_KRILL_ui_colGroups", out groupScrollRect);
			BuildGroupColumn(groupList, groups, gIdx, axes, aIdx);

			RectTransform partList = BuildColumn(columnsRow.transform, "#LOC_KRILL_ui_colParts", out partScrollRect);
			if (axisMode)
			{
				BuildAxisPartColumn(partList, selIsStock, assignedParts, partTransient, stockFields);
			}
			else
			{
				BuildPartColumn(partList, gIdx, selIsStock, assignedParts, partTransient, stockActions);
			}

			RectTransform actionList = BuildColumn(columnsRow.transform,
				axisMode ? "#LOC_KRILL_ui_colFields" : "#LOC_KRILL_ui_colActions", out actionScrollRect);
			if (axisMode)
			{
				BuildFieldColumn(actionList, selIsStock, fieldEntries);
			}
			else
			{
				BuildActionColumn(actionList, selIsStock, actionEntries);
			}

			if (axisMode)
			{
				BuildAxisFooter(axes[aIdx]);
			}
			else
			{
				BuildFooter(parts, gIdx >= 0 ? (GroupEntry?)groups[gIdx] : null);
			}

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

		/// <summary>
		/// Column 1's second list (2026-09-07, A2): A1-A4 mirror the stock custom
		/// axes (name only here — their bind/value come with A5), then every extended
		/// axis number up to the axis cap, unconditionally, exactly like groups. Bind
		/// column stays "-" until the axis keymap exists (A3).
		/// </summary>
		private List<AxisEntry> BuildAxisEntries(IList<Part> parts)
		{
			List<AxisEntry> list = new List<AxisEntry>();
			int cap = KrillParams.MaxVisibleAxis;
			for (int i = 1; i <= cap; i++)
			{
				bool isStock = i < KrillAxes.FirstExtended;
				list.Add(new AxisEntry
				{
					number = i,
					isStock = isStock,
					name = KrillQuery.GetAxisName(parts, activeSet, i) ?? DefaultAxisName(i),
					// Stock A1-A4 mirror GameSettings.AXIS_CUSTOM (A5); extended axes
					// read the KRILL axis keymap (A3).
					bind = isStock ? StockAxisBindDescribe(i) : KrillAxisKeymap.Describe(i),
				});
			}
			return list;
		}

		private List<KrillQuery.AxisFieldEntry> GetFieldEntriesForPart(IList<Part> parts, int axis, Part part)
		{
			List<KrillQuery.AxisFieldEntry> all = KrillQuery.GetAxisFieldEntries(parts, activeSet, axis);
			List<KrillQuery.AxisFieldEntry> mine = new List<KrillQuery.AxisFieldEntry>();
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
				// Six equal shares of the row (preferredWidth 0 + flexible 1): a long
				// modifier+key description clips inside its own button instead of
				// pushing the neighbours out of the window (2026-09-11).
				KrillUi.Size(btn.gameObject, 0f, 18f, 1f);
				KrillUi.ClipText(btn.GetComponentInChildren<Text>());
			}
		}

		private void StartSetCapture(int set)
		{
			pendingRemovePart = false;
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_setCaptureStart", SetLabel(set)), 3f, ScreenMessageStyle.UPPER_CENTER);
			KrillCapture.Begin(
				bind => OnSetCaptured(set, bind),
				() => ScreenMessages.PostScreenMessage(Loc("#LOC_KRILL_ui_captureCancelled"), 3f, ScreenMessageStyle.UPPER_CENTER),
				() => OnSetBindCleared(set));
		}

		private void OnSetBindCleared(int set)
		{
			KrillSetKeymap.RemoveBind(set);
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_setCaptureCleared", SetLabel(set)), 4f, ScreenMessageStyle.UPPER_CENTER);
			RebuildContent();
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

		private void BuildGroupColumn(Transform listContent, List<GroupEntry> groups, int gIdx, List<AxisEntry> axes, int aIdx)
		{
			for (int i = 0; i < groups.Count; i++)
			{
				GroupEntry g = groups[i];
				BuildNumberedRow(listContent, g.number.ToString(), 18f, g.isStock, g.name, i == gIdx,
					() => OnGroupClicked(g.number, g.isStock));
			}

			// Axes section (2026-09-07, A2): a fold header, then the axis rows. Same
			// row shape as groups with an "A" prefix on the number, so the two
			// numberings can't be confused at a glance.
			bool open = axesSectionOpen ?? false;
			RectTransform head = KrillUi.Bordered("AxesHead", listContent, KrillUi.HeadA, KrillUi.Line);
			KrillUi.Size(head.gameObject, -1f, RowHeight);
			KrillUi.Horizontal(head.gameObject, 4, 4f);
			Text headText = KrillUi.Label(head, (open ? "▾ " : "▸ ") + Loc("#LOC_KRILL_ui_axesSection"), 10, KrillUi.Faint,
				TextAnchor.MiddleLeft, FontStyle.Bold);
			KrillUi.Size(headText.gameObject, -1f, RowHeight, 1f);
			Button headBtn = head.gameObject.AddComponent<Button>();
			headBtn.targetGraphic = head.GetComponent<Image>();
			headBtn.onClick.AddListener(OnAxesHeaderClicked);
			if (!open)
			{
				return;
			}
			for (int i = 0; i < axes.Count; i++)
			{
				AxisEntry a = axes[i];
				BuildNumberedRow(listContent, "A" + a.number, 26f, a.isStock, a.name, i == aIdx,
					() => OnAxisClicked(a.number));
			}
		}

		/// <summary>One column-1 row: bold number cell (muted for stock, malachite for KRILL-owned) + name, whole row clickable.</summary>
		private void BuildNumberedRow(Transform listContent, string number, float numberWidth, bool isStock, string name, bool sel, UnityEngine.Events.UnityAction onClick)
		{
			RectTransform cell = KrillUi.Bordered("G", listContent, sel ? KrillUi.Panel2 : KrillUi.Panel, KrillUi.Line);
			KrillUi.Size(cell.gameObject, -1f, RowHeight);
			KrillUi.Horizontal(cell.gameObject, 4, 4f);
			Text num = KrillUi.Label(cell, number, 11, isStock ? KrillUi.Muted : KrillUi.Malachite,
				TextAnchor.MiddleCenter, FontStyle.Bold);
			KrillUi.Size(num.gameObject, numberWidth, RowHeight);
			Text nm = KrillUi.Label(cell, name, 11, sel ? KrillUi.Tan : KrillUi.Text);
			KrillUi.Size(nm.gameObject, -1f, RowHeight, 1f);
			Button btn = cell.gameObject.AddComponent<Button>();
			btn.targetGraphic = cell.GetComponent<Image>();
			btn.onClick.AddListener(onClick);
		}

		private void OnGroupClicked(int number, bool isStock)
		{
			pendingRemovePart = false;
			DeselectPart();
			selectedAxis = null;
			selectedGroup = number;
			RebuildContent();
		}

		private void OnAxisClicked(int number)
		{
			pendingRemovePart = false;
			DeselectPart();
			selectedGroup = null;
			selectedAxis = number;
			RebuildContent();
		}

		private void OnAxesHeaderClicked()
		{
			pendingRemovePart = false;
			axesSectionOpen = !(axesSectionOpen ?? false);
			// Folding drops an axis selection (RebuildContent does it when aIdx < 0);
			// a group selection is unaffected either way.
			RebuildContent();
		}

		private static string DefaultGroupName(int group)
		{
			return Loc("#LOC_KRILL_ui_groupDefaultName") + " " + group;
		}

		/// <summary>Stock custom axes reuse stock's own names (#autoLOC_6013013..16 = "Custom01".."Custom04", the same keys the stock editor shows); extended axes get "Axis N".</summary>
		private static string DefaultAxisName(int axis)
		{
			if (axis < KrillAxes.FirstExtended)
			{
				return Localizer.Format("#autoLOC_60130" + (12 + axis));
			}
			return Loc("#LOC_KRILL_ui_axisDefaultName") + " " + axis;
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
				bool sel = IsSelectedPartOrSibling(p);
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

		/// <summary>Column 2 in axis mode (2026-09-07, A2): parts holding axis-field assignments for the selected axis; stock axes A1-A4 get the same read-only "part → field" view groups 1-10 have.</summary>
		private void BuildAxisPartColumn(Transform listContent, bool selIsStock,
			List<Part> assignedParts, bool partTransient, List<BaseAxisField> stockFields)
		{
			if (selIsStock)
			{
				if (stockFields.Count == 0)
				{
					BuildReadOnlyCell(listContent, Loc("#LOC_KRILL_ui_noAssignments"));
					return;
				}
				for (int i = 0; i < stockFields.Count; i++)
				{
					PartModule owner = stockFields[i].host as PartModule;
					string ownerName = owner != null && owner.part != null ? owner.part.partInfo.title : "?";
					BuildReadOnlyCell(listContent, TruncateStockOwnerName(ownerName) + " → " + FieldLabel(stockFields[i]));
				}
				return;
			}

			for (int i = 0; i < assignedParts.Count; i++)
			{
				Part p = assignedParts[i];
				bool sel = IsSelectedPartOrSibling(p);
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

		/// <summary>Player-facing name of an axis field: its PAW caption when it has one (run through the Localizer — stock guiNames are often #autoLOC keys), else the raw field name.</summary>
		private static string FieldLabel(BaseAxisField f)
		{
			return string.IsNullOrEmpty(f.guiName) ? f.name : Localizer.Format(f.guiName);
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
				// Fans out to every CURRENT symmetry sibling of p, not just p itself
				// (2026-07-27) — each holds its own independent copy of the same
				// assignments, so removing from only one would leave the group
				// visually merged (still symmetric) but functionally desynced.
				// Axis mode (A2) is the same operation on the axis lists.
				if (selectedGroup.HasValue || selectedAxis.HasValue)
				{
					foreach (Part sibling in KrillQuery.GetSymmetryGroup(p))
					{
						ModuleKrill m = sibling.FindModuleImplementing<ModuleKrill>();
						if (m == null)
						{
							continue;
						}
						bool changed = selectedAxis.HasValue
							? m.Data.RemoveAxisInSet(activeSet, selectedAxis.Value)
							: m.Data.RemoveGroupInSet(activeSet, selectedGroup.Value);
						if (changed)
						{
							m.MarkDirty();
						}
					}
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
			if (entry.module != null && entry.assignment != null)
			{
				if (entry.module.Data.RemoveAssignment(entry.assignment))
				{
					entry.module.MarkDirty();
				}
				// Same value on every OTHER symmetric sibling of entry.part — each has
				// its own independent KrillAssignment object (same values), so it can't
				// be removed by the reference-based call above; match by value instead
				// (2026-07-27, same reasoning as OnRemovePartClicked).
				KrillActionRef r = entry.assignment.actionRef;
				if (r != null && entry.part != null)
				{
					foreach (Part sibling in KrillQuery.GetSymmetryGroup(entry.part))
					{
						if (sibling == entry.part)
						{
							continue;
						}
						ModuleKrill m = sibling.FindModuleImplementing<ModuleKrill>();
						if (m != null && m.Data.RemoveAssignmentMatching(entry.assignment.set, entry.assignment.group, r.module, r.occurrence, r.action))
						{
							m.MarkDirty();
						}
					}
				}
			}
			RebuildContent();
		}

		// ------------------------------------------------------ column 3, axis mode

		/// <summary>
		/// Column 3 in axis mode (2026-09-07, A2): two rows per assigned field — the
		/// field name with its [x], then the per-assignment options as cycling text
		/// buttons (user decision: text, not checkboxes): direct/inverted,
		/// absolute/incremental, and the speed step (shown only in incremental mode,
		/// the only mode it applies to — same as stock). Grows inside the scrolling
		/// column, so the footer and the window frame stay exactly as they are.
		/// </summary>
		private void BuildFieldColumn(Transform listContent, bool selIsStock, List<KrillQuery.AxisFieldEntry> fieldEntries)
		{
			if (selIsStock || selectedPart == null)
			{
				return;
			}

			for (int i = 0; i < fieldEntries.Count; i++)
			{
				KrillQuery.AxisFieldEntry entry = fieldEntries[i];
				string label = entry.resolved != null ? FieldLabel(entry.resolved) : Loc("#LOC_KRILL_ui_unresolved");
				BuildClickableCell(listContent, label, false, null, () => RemoveFieldEntry(entry));

				KrillAxisAssignment a = entry.assignment;
				// New values computed ONCE here, not inside the apply lambda: the lambda
				// runs on every symmetry sibling in turn, and the source assignment is
				// one of them — reading `!a.inverted` per call would flip the first and
				// un-flip the rest.
				bool newInverted = !a.inverted;
				bool newIncremental = !a.incremental;
				GameObject opts = KrillUi.Go("FieldOpts", listContent);
				KrillUi.Horizontal(opts, 0, 4f);
				KrillUi.Size(opts, -1f, RowHeight - 4f);
				KrillUi.TextButton(opts.transform, Loc(a.inverted ? "#LOC_KRILL_ui_fieldInverted" : "#LOC_KRILL_ui_fieldNormal"),
					() => EditFieldOptions(entry, x => x.inverted = newInverted),
					KrillUi.Panel2, a.inverted ? KrillUi.Warn : KrillUi.TanDim, 9, 58f, RowHeight - 4f);
				KrillUi.TextButton(opts.transform, Loc(a.incremental ? "#LOC_KRILL_ui_fieldIncremental" : "#LOC_KRILL_ui_fieldAbsolute"),
					() => EditFieldOptions(entry, x => x.incremental = newIncremental),
					KrillUi.Panel2, KrillUi.TanDim, 9, 66f, RowHeight - 4f);
				if (a.incremental)
				{
					float next = KrillAxes.NextSpeed(a.speed);
					KrillUi.TextButton(opts.transform, Localizer.Format("#LOC_KRILL_ui_fieldSpeed", Mathf.RoundToInt(a.speed * 100f).ToString()),
						() => EditFieldOptions(entry, x => x.speed = next),
						KrillUi.Panel2, KrillUi.TanDim, 9, 48f, RowHeight - 4f);
				}
			}
			KrillUi.TextButton(listContent, Loc("#LOC_KRILL_ui_addField"), StartActionPick,
				KrillUi.Panel2, KrillUi.GreenHi, 11, -1f, RowHeight);
		}

		/// <summary>
		/// Applies an option change to the entry's own assignment AND to the matching
		/// (by value) assignment on every symmetry sibling — options are part of the
		/// assignment, so they fan out exactly like adding/removing one does.
		/// </summary>
		private void EditFieldOptions(KrillQuery.AxisFieldEntry entry, System.Action<KrillAxisAssignment> apply)
		{
			if (entry.assignment == null || entry.part == null)
			{
				return;
			}
			KrillAxisAssignment src = entry.assignment;
			foreach (Part sibling in KrillQuery.GetSymmetryGroup(entry.part))
			{
				ModuleKrill m = sibling.FindModuleImplementing<ModuleKrill>();
				if (m == null)
				{
					continue;
				}
				KrillAxisAssignment target = sibling == entry.part ? src : m.Data.FindAxisAssignment(src.set, src.axis, src.fieldRef);
				if (target != null)
				{
					apply(target);
					m.MarkDirty();
				}
			}
			RebuildContent();
		}

		private void RemoveFieldEntry(KrillQuery.AxisFieldEntry entry)
		{
			if (entry.assignment != null && entry.part != null)
			{
				KrillAxisAssignment a = entry.assignment;
				// By value on every sibling INCLUDING entry.part itself — same
				// reasoning as RemoveActionEntry, one loop instead of two paths.
				foreach (Part sibling in KrillQuery.GetSymmetryGroup(entry.part))
				{
					ModuleKrill m = sibling.FindModuleImplementing<ModuleKrill>();
					if (m != null && m.Data.RemoveAxisAssignmentMatching(a.set, a.axis, a.fieldRef))
					{
						m.MarkDirty();
					}
				}
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

		/// <summary>Is p the selected part, or one of ITS current symmetry siblings (2026-07-27)? Used everywhere a plain "== selectedPart" would miss the rest of a symmetric group — selectedPart is whichever specific instance was clicked, not necessarily the group's representative row.</summary>
		private bool IsSelectedPartOrSibling(Part p)
		{
			return selectedPart != null && (p == selectedPart || KrillQuery.GetSymmetryGroup(p).Contains(selectedPart));
		}

		private void ApplySelectedPartHighlight()
		{
			if (selectedPart == null)
			{
				return;
			}
			foreach (Part p in KrillQuery.GetSymmetryGroup(selectedPart))
			{
				p.SetHighlightType(Part.HighlightType.AlwaysOn);
				p.SetHighlightColor(SelectedPartColor);
				p.SetHighlight(true, false);
			}
		}

		private void ClearPartHighlight()
		{
			if (selectedPart == null)
			{
				return;
			}
			foreach (Part p in KrillQuery.GetSymmetryGroup(selectedPart))
			{
				p.SetHighlightDefault();
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

		// ------------------------------------------------- stock custom axes (A5)

		/// <summary>Stock custom axis `axis` (1-4) as GameSettings holds it, or null out of range.</summary>
		private static AxisKeyBinding StockAxisKeyBinding(int axis)
		{
			AxisKeyBindingList list = GameSettings.AXIS_CUSTOM;
			if (list == null || axis < 1 || axis > list.Length)
			{
				return null;
			}
			return list[axis - 1];
		}

		/// <summary>The PRIMARY controller channel of stock custom axis `axis` — the slot the Capture button of a mirror row writes (stock's own settings screen fills the same object in place, decompiled InputSettings.SetAxis).</summary>
		private static AxisBinding_Single StockAxisBinding(int axis)
		{
			AxisKeyBinding akb = StockAxisKeyBinding(axis);
			return akb != null && akb.axisBinding != null ? akb.axisBinding.primary : null;
		}

		/// <summary>
		/// Column-1 / footer text for a mirror row: the primary channel in stock's
		/// "Device Axis N" wording (or "-"), plus the +/- keys stock also offers
		/// for its custom axes when either is set — a keyboard-only player would
		/// otherwise see "-" on an axis that does move.
		/// </summary>
		private static string StockAxisBindDescribe(int axis)
		{
			AxisKeyBinding akb = StockAxisKeyBinding(axis);
			AxisBinding_Single p = akb != null && akb.axisBinding != null ? akb.axisBinding.primary : null;
			string text = "-";
			if (p != null && p.idTag != "None")
			{
				text = p.axisIdx >= 0 ? KrillAxisKeymap.AxisTitle(p.name, p.axisIdx) : p.title;
			}
			string plus = StockKeyDescribe(akb != null ? akb.plusKeyBinding : null);
			string minus = StockKeyDescribe(akb != null ? akb.minusKeyBinding : null);
			if (plus != null || minus != null)
			{
				text = Localizer.Format("#LOC_KRILL_ui_axisStockKeys", text, plus ?? "-", minus ?? "-");
			}
			return text;
		}

		private static string StockKeyDescribe(KeyBinding kb)
		{
			if (kb == null || kb.primary == null || kb.primary.isNone)
			{
				return null;
			}
			return kb.primary.code.ToString();
		}

		/// <summary>
		/// Writes (bind != null) or clears (bind == null) the primary channel of
		/// stock custom axis `axis` and saves settings.cfg, exactly like a stock
		/// group's Capture writes its KeyBinding. Mutates the existing object in
		/// place and copies only the channel IDENTITY (id, device name/index, axis
		/// index, title): inversion, sensitivity, dead zone, scale and the lock
		/// mask are stock's per-axis settings, managed in stock's Input screen,
		/// and a recapture must not reset them. Clearing mirrors stock's own
		/// "Clear Assignment" (SetAxis("None", "None", -1, -1)).
		/// </summary>
		private static void WriteStockAxisBind(int axis, AxisBinding_Single bind)
		{
			AxisBinding_Single p = StockAxisBinding(axis);
			if (p == null)
			{
				return;
			}
			if (bind != null)
			{
				p.idTag = bind.idTag;
				p.name = bind.name;
				p.deviceIdx = bind.deviceIdx;
				p.axisIdx = bind.axisIdx;
				p.title = bind.title;
			}
			else
			{
				p.idTag = "None";
				p.name = "None";
				p.title = "None";
				p.deviceIdx = -1;
				p.axisIdx = -1;
			}
			GameSettings.SaveSettings();
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

				// Kind (Pulse/Toggle/Hold) resolved once, extended groups only — feeds
				// both the Trigger button just below (Hold needs press-and-hold, not a
				// click) and the kind/state controls further down. Same scope boundary
				// as everything else that manages assignments/keymaps — stock 1-10
				// already have their own public, persisted state via
				// vessel.ActionGroups, KRILL doesn't need to add anything there.
				KrillQuery.GroupState? gs = !g.isStock ? KrillQuery.GetGroupState(RootPart(), activeSet, g.number) : null;
				KrillActuationKind kind = gs?.kind ?? KrillActuationKind.Pulse;

				if (HighLogic.LoadedSceneIsFlight)
				{
					if (kind == KrillActuationKind.Hold)
					{
						// Press-and-hold, not a toggle (2026-08-19 design discussion):
						// mirrors stock's own BRAKES precedent and keeps the promise of
						// Hold consistent across every activation source (key, this
						// button, the console) — mouse-down is a Window-source press,
						// mouse-up (or the button going away) its release; the engine
						// actuates on the group's 0->1 / 1->0 edges across all sources.
						KrillUi.HoldButton(footer.transform, Loc("#LOC_KRILL_ui_trigger"),
							() => KrillActivation.HoldPress(FlightGlobals.ActiveVessel, g.number, KrillHoldSource.Window),
							() => KrillActivation.HoldRelease(g.number, KrillHoldSource.Window),
							KrillUi.Panel2, KrillUi.GreenHi, 11, 50f, 22f);
					}
					else
					{
						KrillUi.TextButton(footer.transform, Loc("#LOC_KRILL_ui_trigger"), () => Trigger(g.number, g.isStock),
							KrillUi.Panel2, KrillUi.GreenHi, 11, 50f, 22f);
					}
				}

				if (!g.isStock)
				{
					string kindLabel = Loc(KindLocKey(kind));
					KrillUi.TextButton(footer.transform, kindLabel, () => CycleKind(g.number),
						KrillUi.Panel2, KrillUi.TanDim, 11, 65f, 22f);

					// Toggle only: it's the one kind whose signal is a persisted value
					// the player can meaningfully declare. A Pulse signal is a timer
					// and a Hold signal is "someone is pressing right now" — neither
					// has a stored value to force (2026-08-25, reconfirmed 2026-09-02).
					if (kind == KrillActuationKind.Toggle)
					{
						bool state = gs.Value.signal;
						string stateLabel = Localizer.Format("#LOC_KRILL_ui_stateLabel", state ? "1" : "0");
						Color stateColor = state ? KrillUi.GreenHi : KrillUi.TanDim;
						KrillUi.TextButton(footer.transform, stateLabel, () => ForceState(g.number, !state),
							KrillUi.Panel2, stateColor, 11, 60f, 22f);
					}
				}

				// Indicator type (2026-08-24 design discussion) — console severity
				// label, cosmetic only, no dependency on kind/activation engine. Unlike
				// the kind/state controls above, offered for stock groups too: the
				// future console grid shows 1-10 alongside extended groups (same
				// reasoning as KrillGroupName already allowing stock groups). "Console",
				// not "HUD" (2026-08-27): it's a clickable button grid, not a
				// see-through overlay — the name was corrected before any of it got built.
				Part rootForIndicator = RootPart();
				ModuleKrill mForIndicator = rootForIndicator != null ? rootForIndicator.FindModuleImplementing<ModuleKrill>() : null;
				KrillIndicatorType indicatorType = mForIndicator != null
					? mForIndicator.GetIndicatorType(activeSet, g.number)
					: KrillIndicatorType.Info;
				KrillUi.TextButton(footer.transform, Loc(IndicatorLocKey(indicatorType)), () => CycleIndicator(g.number),
					KrillUi.Panel2, KrillUi.TanDim, 11, 65f, 22f);

				string bindInfo = Localizer.Format("#LOC_KRILL_ui_bindInfo", g.number.ToString(), g.bind);
				List<string> conflicts = KrillConflicts.Describe(CurrentBind(g), g.isStock ? -1 : g.number);
				BuildFooterInfo(footer.transform, bindInfo, conflicts);
			}
			else
			{
				Text hint = KrillUi.Label(footer.transform, Loc("#LOC_KRILL_ui_hint"), 11, KrillUi.Muted);
				KrillUi.Size(hint.gameObject, -1f, 22f, 1f);
			}
		}

		/// <summary>
		/// Footer in axis mode (2026-09-07, A2): the same slots as the group footer,
		/// filled per notes/axes-design.md §6 — name, set name, then Kind
		/// (Spring/Fixed) and Rest (Spring only) for extended axes, and the info line.
		/// Capture arrives with A3 and the value slider with A4: their slots stay
		/// empty until then rather than showing controls that do nothing.
		/// </summary>
		private void BuildAxisFooter(AxisEntry a)
		{
			GameObject footer = KrillUi.Go("Footer", contentHost);
			KrillUi.Horizontal(footer, 0, 8f);
			KrillUi.Size(footer, -1f, 26f);

			InputField nameField = KrillUi.Field(footer.transform, a.name, 130f, text => SetAxisName(a.number, text));
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

			string bindInfo = Localizer.Format("#LOC_KRILL_ui_axisInfo", a.number.ToString(), a.bind);
			// Same slot and look as the group Capture button; the gesture is
			// "move an axis" instead of "press a key" (KrillAxisCapture). Offered
			// for the stock mirror rows too (A5): there it writes AXIS_CUSTOM, the
			// way a stock group's Capture writes its stock KeyBinding.
			KrillUi.TextButton(footer.transform, Loc("#LOC_KRILL_ui_capture"), () => StartAxisCapture(a.number),
				KrillUi.Panel2, KrillUi.TanDim, 11, 55f, 22f);

			List<string> conflicts;
			if (a.isStock)
			{
				// A5 mirror row: the level is stock's own custom axis (FlightCtrlState.
				// custom_axes via KrillQuery) — read-only slider in flight, nothing in
				// the editor; no Kind/Rest (stock has neither) and no assignment UI
				// (that's stock's action-group editor, columns 2/3 are read-only).
				KrillQuery.AxisState? stockState = KrillQuery.GetAxisState(RootPart(), activeSet, a.number);
				if (stockState.HasValue)
				{
					float level = stockState.Value.value;
					axisSliderAxis = a.number;
					axisValueSlider = KrillUi.Slider(footer.transform, -1f, 1f, level, 100f, 20f, _ => { }, readOnly: true);
					axisValueLabel = KrillUi.Label(footer.transform, FormatAxisValue(level), 11, KrillUi.Muted, TextAnchor.MiddleCenter);
					KrillUi.Size(axisValueLabel.gameObject, 40f, 22f);
				}

				AxisBinding_Single stockBind = StockAxisBinding(a.number);
				conflicts = KrillConflicts.DescribeAxis(stockBind != null ? stockBind.idTag : null, -1, a.number);
			}
			else
			{
				KrillQuery.AxisState? st = KrillQuery.GetAxisState(RootPart(), activeSet, a.number);
				KrillAxisKind kind = st?.kind ?? KrillAxisKind.Spring;

				// Value slider (A4) in the Trigger/State slots: the axis's live level,
				// writable only when no controller channel is bound (design §4 — a
				// bound axis is the controller's, the slider just follows it). A Spring
				// axis has no stored value, so in the editor (no vessel, no runtime
				// level) only a Fixed axis gets the slider.
				bool bound = KrillAxisKeymap.IsBound(a.number);
				if (kind == KrillAxisKind.Fixed || HighLogic.LoadedSceneIsFlight)
				{
					float level = st?.value ?? 0f;
					int axisNumber = a.number;
					int rest = st?.rest ?? 0;
					axisSliderAxis = axisNumber;
					axisValueSlider = KrillUi.Slider(footer.transform, -1f, 1f, level, 100f, 20f,
						v => OnAxisSliderChanged(axisNumber, kind, v),
						() => axisSliderHeld = true,
						() => OnAxisSliderReleased(axisNumber, kind, rest),
						readOnly: bound);
					axisValueLabel = KrillUi.Label(footer.transform, FormatAxisValue(level), 11, bound ? KrillUi.Muted : KrillUi.Tan, TextAnchor.MiddleCenter);
					KrillUi.Size(axisValueLabel.gameObject, 40f, 22f);
				}

				KrillUi.TextButton(footer.transform, Loc(kind == KrillAxisKind.Fixed ? "#LOC_KRILL_ui_axisKindFixed" : "#LOC_KRILL_ui_axisKindSpring"),
					() => CycleAxisKind(a.number), KrillUi.Panel2, KrillUi.TanDim, 11, 65f, 22f);
				if (kind == KrillAxisKind.Spring)
				{
					int rest = st?.rest ?? 0;
					string restText = rest > 0 ? "+1" : rest.ToString();
					KrillUi.TextButton(footer.transform, Localizer.Format("#LOC_KRILL_ui_axisRest", restText),
						() => CycleAxisRest(a.number), KrillUi.Panel2, KrillUi.TanDim, 11, 60f, 22f);
				}

				// Persistent conflict advisory on the CURRENT bind, like the group footer.
				AxisBinding_Single current = KrillAxisKeymap.GetBind(a.number);
				conflicts = KrillConflicts.DescribeAxis(current != null ? current.idTag : null, a.number);
			}

			BuildFooterInfo(footer.transform, bindInfo, conflicts);
		}

		/// <summary>
		/// The footer's trailing info label ("Group N — bind: …"), shared by the
		/// group and axis footers. It gets only the leftover width and is clipped
		/// (2026-09-11), which had silently hidden the conflict list appended
		/// after the bind (A5.4 report, 2026-09-14): with conflicts the label now
		/// turns Warn (orange) and lists them FIRST, so the colour is always
		/// visible and the detail is readable for as long as it fits. Without
		/// conflicts it stays the muted bind line it always was.
		/// </summary>
		private static void BuildFooterInfo(Transform parent, string bindInfo, List<string> conflicts)
		{
			bool warn = conflicts != null && conflicts.Count > 0;
			string text = warn
				? Loc("#LOC_KRILL_ui_conflicts") + ": " + string.Join(", ", conflicts) + " — " + bindInfo
				: bindInfo;
			Text info = KrillUi.Label(parent, text, 11, warn ? KrillUi.Warn : KrillUi.Muted);
			KrillUi.Size(info.gameObject, 0f, 22f, 1f);
			KrillUi.ClipText(info);
		}

		// ---------------------------------------------------------- axis value slider

		private static string FormatAxisValue(float v)
		{
			return v.ToString("+0.00;-0.00;0.00");
		}

		/// <summary>Player-driven slider change on an UNBOUND axis: Fixed writes the persisted value (the same store the driver fills from a controller), Spring sets the live level for as long as the mouse holds it.</summary>
		private void OnAxisSliderChanged(int axis, KrillAxisKind kind, float value)
		{
			if (kind == KrillAxisKind.Fixed)
			{
				Part root = RootPart();
				ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
				if (m != null)
				{
					m.SetAxisValue(activeSet, axis, value);
				}
			}
			else if (HighLogic.LoadedSceneIsFlight)
			{
				KrillAxisSignal.SetLive(FlightGlobals.ActiveVessel, activeSet, axis, value);
			}
			if (axisValueLabel != null)
			{
				axisValueLabel.text = FormatAxisValue(value);
			}
		}

		private void OnAxisSliderReleased(int axis, KrillAxisKind kind, int rest)
		{
			axisSliderHeld = false;
			if (kind == KrillAxisKind.Spring && HighLogic.LoadedSceneIsFlight)
			{
				KrillAxisSignal.Release(FlightGlobals.ActiveVessel, activeSet, axis, rest);
			}
		}

		/// <summary>Keeps the slider and its readout on the axis's real level whenever the mouse isn't the one moving it (controller input, return ramp, a set switch).</summary>
		private void FollowAxisValue()
		{
			if (axisValueSlider == null || axisSliderHeld)
			{
				return;
			}
			KrillQuery.AxisState? st = KrillQuery.GetAxisState(RootPart(), activeSet, axisSliderAxis);
			if (!st.HasValue)
			{
				return;
			}
			float v = st.Value.value;
			if (!Mathf.Approximately(axisValueSlider.value, v))
			{
				axisValueSlider.SetValueWithoutNotify(v);
				if (axisValueLabel != null)
				{
					axisValueLabel.text = FormatAxisValue(v);
				}
			}
		}

		// ------------------------------------------------------- axis bind capture

		private void StartAxisCapture(int axis)
		{
			pendingRemovePart = false;
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_axisCaptureStart", axis.ToString()), 4f, ScreenMessageStyle.UPPER_CENTER);
			KrillAxisCapture.Begin(
				bind => OnAxisCaptured(axis, bind),
				() => OnAxisBindCleared(axis),
				() => ScreenMessages.PostScreenMessage(Loc("#LOC_KRILL_ui_captureCancelled"), 3f, ScreenMessageStyle.UPPER_CENTER));
		}

		private void OnAxisCaptured(int axis, AxisBinding_Single bind)
		{
			bool isStock = axis < KrillAxes.FirstExtended;
			string conflictSuffix = "";
			List<string> conflicts = KrillConflicts.DescribeAxis(bind.idTag, isStock ? -1 : axis, isStock ? axis : -1);
			if (conflicts.Count > 0)
			{
				conflictSuffix = " (" + Loc("#LOC_KRILL_ui_conflicts") + ": " + string.Join(", ", conflicts) + ")";
			}
			if (isStock)
			{
				WriteStockAxisBind(axis, bind);
			}
			else
			{
				KrillAxisKeymap.SetBind(axis, bind);
			}
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_axisCaptureDone", axis.ToString(), bind.title) + conflictSuffix,
				5f, ScreenMessageStyle.UPPER_CENTER);
			RebuildContent();
		}

		private void OnAxisBindCleared(int axis)
		{
			if (axis < KrillAxes.FirstExtended)
			{
				WriteStockAxisBind(axis, null);
			}
			else
			{
				KrillAxisKeymap.RemoveBind(axis);
			}
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_axisCaptureCleared", axis.ToString()), 4f, ScreenMessageStyle.UPPER_CENTER);
			RebuildContent();
		}

		private void SetAxisName(int axis, string text)
		{
			Part root = RootPart();
			ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
			if (m == null)
			{
				return;
			}
			string trimmed = string.IsNullOrEmpty(text) ? null : text.Trim();
			if (trimmed == DefaultAxisName(axis))
			{
				return; // unchanged from placeholder — same guard as SetGroupName
			}
			m.Data.SetAxisName(activeSet, axis, trimmed);
			m.MarkDirty();
			RebuildContent();
		}

		private void CycleAxisKind(int axis)
		{
			Part root = RootPart();
			ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
			if (m == null)
			{
				return;
			}
			KrillAxisKind next = m.GetAxisKind(activeSet, axis) == KrillAxisKind.Spring ? KrillAxisKind.Fixed : KrillAxisKind.Spring;
			m.SetAxisKind(activeSet, axis, next);
			RebuildContent();
		}

		private void CycleAxisRest(int axis)
		{
			Part root = RootPart();
			ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
			if (m == null)
			{
				return;
			}
			m.SetAxisRest(activeSet, axis, KrillAxes.NextRest(m.GetAxisRest(activeSet, axis)));
			RebuildContent();
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
				KrillActivation.Fire(v, group);
			}
		}

		private static string KindLocKey(KrillActuationKind kind)
		{
			switch (kind)
			{
				case KrillActuationKind.Toggle: return "#LOC_KRILL_ui_kindToggle";
				case KrillActuationKind.Hold: return "#LOC_KRILL_ui_kindHold";
				default: return "#LOC_KRILL_ui_kindPulse";
			}
		}

		private void CycleKind(int group)
		{
			Part root = RootPart();
			ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
			if (m == null)
			{
				return;
			}
			KrillActuationKind current = m.GetActuationKind(activeSet, group);
			KrillActuationKind next;
			switch (current)
			{
				case KrillActuationKind.Pulse: next = KrillActuationKind.Toggle; break;
				case KrillActuationKind.Toggle: next = KrillActuationKind.Hold; break;
				default: next = KrillActuationKind.Pulse; break;
			}
			m.SetActuationKind(activeSet, group, next);
			RebuildContent();
		}

		private static string IndicatorLocKey(KrillIndicatorType type)
		{
			switch (type)
			{
				case KrillIndicatorType.Caution: return "#LOC_KRILL_ui_indicatorCaution";
				case KrillIndicatorType.Warning: return "#LOC_KRILL_ui_indicatorWarning";
				default: return "#LOC_KRILL_ui_indicatorInfo";
			}
		}

		/// <summary>Unlike CycleKind, valid for stock groups too — indicator type has no dependency on which engine runs the group.</summary>
		private void CycleIndicator(int group)
		{
			Part root = RootPart();
			ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
			if (m == null)
			{
				return;
			}
			KrillIndicatorType current = m.GetIndicatorType(activeSet, group);
			KrillIndicatorType next;
			switch (current)
			{
				case KrillIndicatorType.Info: next = KrillIndicatorType.Caution; break;
				case KrillIndicatorType.Caution: next = KrillIndicatorType.Warning; break;
				default: next = KrillIndicatorType.Info; break;
			}
			m.SetIndicatorType(activeSet, group, next);
			RebuildContent();
		}

		/// <summary>
		/// Force-sets a Toggle group's SIGNAL without invoking any action — the
		/// resync control from the 2026-08-19 design discussion: another mod, or
		/// the player from the part's right-click menu, can change a part's real
		/// state without ever going through the KRILL group, leaving the reported
		/// signal stale. Works in either scene (editor: declare a starting value
		/// before launch; flight: resync after external drift).
		///
		/// Writes ONLY the signal (KrillGroupSignal), never the direction bit
		/// (user decision 2026-09-02): the signal is the player's declared meaning
		/// — "this reads as 1 to me" — not a statement about what the part last
		/// received, so the next real press keeps alternating Activate/Deactivate
		/// exactly as if this had never been touched. (Before 2026-09-02 the two
		/// were one bool, and forcing it silently reversed the next press's
		/// direction — the "took two clicks" note in test-switch-toggle.md.)
		/// Toggle-kind groups only, see the footer call site.
		/// </summary>
		private void ForceState(int group, bool value)
		{
			Part root = RootPart();
			ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
			if (m == null)
			{
				return;
			}
			m.SetToggleSignal(activeSet, group, value);
			Debug.LogFormat("[KRILL] group {0} set {1} signal forced to {2} by the player (no action invoked, direction bit untouched)",
				group, activeSet, value ? 1 : 0);
			RebuildContent();
		}

		// -------------------------------------------------------------- bind capture

		private void StartCapture(int group, bool isStock)
		{
			pendingRemovePart = false;
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_captureStart", group.ToString()), 3f, ScreenMessageStyle.UPPER_CENTER);
			KrillCapture.Begin(
				bind => OnCaptured(group, isStock, bind),
				() => ScreenMessages.PostScreenMessage(Loc("#LOC_KRILL_ui_captureCancelled"), 3f, ScreenMessageStyle.UPPER_CENTER),
				() => OnBindCleared(group, isStock));
		}

		/// <summary>Delete pressed during a group capture (2026-09-09): stock groups get their stock KeyBinding primary set to None (same slot OnCaptured writes), extended groups drop their keymap entry.</summary>
		private void OnBindCleared(int group, bool isStock)
		{
			if (isStock)
			{
				KeyBinding kb = StockKeyBinding(group);
				if (kb != null)
				{
					kb.primary = new KeyCodeExtended(KeyCode.None);
					GameSettings.SaveSettings();
				}
			}
			else
			{
				KrillKeymap.RemoveBind(group);
			}
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_captureCleared", group.ToString()), 4f, ScreenMessageStyle.UPPER_CENTER);
			RebuildContent();
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
			// Same deferral for a slider drag as for a held Hold button: a rebuild
			// would pull the handle from under the mouse.
			if (rebuildPending && !KrillSignal.AnyWindowHeld && !axisSliderHeld)
			{
				rebuildPending = false;
				RebuildContent();
			}
			FollowAxisValue();
			// Must drive KrillCapture ourselves: it's the only thing that ticks it in
			// the editor scene (KrillInputManager is flight-only). Redundant-but-safe
			// in flight, where KrillInputManager also ticks it (KrillCapture.Tick is
			// frame-guarded against double-driving).
			if (KrillCapture.NeedsTick)
			{
				KrillCapture.Tick();
				return;
			}
			if (KrillAxisCapture.NeedsTick)
			{
				KrillAxisCapture.Tick();
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
