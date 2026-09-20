using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KRILL.UI
{
	/// <summary>
	/// The single KRILL window: a 3-column Miller layout (Groups | Parts | Actions)
	/// mirroring the shape of the data itself. The three lists are INDEPENDENT, so
	/// no column ever depends on which row its parent sat on.
	/// </summary>
	public partial class KrillWindow : MonoBehaviour
	{
		private const float WindowWidth = 660f;
		private const float ColWidth = 195f;
		private const float RowHeight = 24f;
		private const float ListAreaHeight = 340f;
		private const string InputLockId = "KRILL_WINDOW";

		private static KrillWindow current;

		/// <summary>Set by KrillToolbarApp so the toolbar button un-presses itself when the window closes through its own ✕.</summary>
		public static System.Action OnClosed;

		private RectTransform windowRect;
		private Transform contentHost;

		/// <summary>Last on-screen position, so reopening lands where the window was left. Session-scoped like `current`, never persisted to disk.</summary>
		private static Vector2? lastWindowPosition;

		/// <summary>Set by BuildColumn, read back at the start of the next rebuild to restore scroll. Null while the picker overlay replaces the columns.</summary>
		private ScrollRect groupScrollRect, partScrollRect, actionScrollRect;

		/// <summary>
		/// Last known scroll position per column, top by default. Kept separately from
		/// the ScrollRect refs above because the picker overlay has no columns at all:
		/// reading the refs after it closes would see null and reset to the top.
		/// </summary>
		private float groupScrollPos = 1f, partScrollPos = 1f, actionScrollPos = 1f;

		/// <summary>0 = Default, 1..4 = the stock override sets (Vessel.GroupOverride values).</summary>
		private int activeSet;

		/// <summary>Selected row in column 1, or null. Survives set-tab switches, so the same group can be compared across sets; cleared on vessel change.</summary>
		private int? selectedGroup;

		/// <summary>
		/// Selected AXIS row in column 1, or null — mutually exclusive with
		/// selectedGroup: column 1 holds both lists but there is one selection, and
		/// columns 2/3 plus the footer render either view of it.
		/// </summary>
		private int? selectedAxis;

		/// <summary>
		/// Whether column 1's Axes section is unfolded. Session-scoped, never persisted,
		/// and undecided until the first rebuild: open if the craft already carries
		/// extended-axis data, folded otherwise.
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

		/// <summary>Selected row in column 2, or null. Cleared on any group or set change: the Parts list it indexes into is scoped to both.</summary>
		private Part selectedPart;

		/// <summary>Dark blue for the persistently selected part, distinct from the picker's cyan hover so the two meanings never look alike.</summary>
		private static readonly Color SelectedPartColor = new Color(0.18f, 0.35f, 0.85f);

		/// <summary>Two-click confirm for the part [x] (removes every action of that part in this group+set).</summary>
		private bool pendingRemovePart;

		/// <summary>
		/// The axis footer's value slider and readout, null outside axis mode. Followed
		/// live from LateUpdate — a bound axis moves with the controller, a released
		/// Spring ramps home — except while the mouse holds the handle.
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
			// Safety net: a capture's lock must not stay stuck if the window closes
			// mid-capture — nothing else would be left alive to release it.
			KrillCapture.ForceCancel();
			KrillAxisCapture.ForceCancel();
			// No Hold release needed here: a pressed Trigger button releases itself when
			// its GameObject is disabled or destroyed, closing the window included.
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
				// A part/action pick holds an input lock and needs its now-invisible prompt
				// to make sense, so cancel it. Key and axis captures are not touched: they
				// live in KrillInputManager and Escape cancels them before the pause menu.
				CancelPicker();
			}
			gameObject.SetActive(visible);
		}

		private void OnSceneChange(GameScenes scene)
		{
			Close();
		}

		/// <summary>Keeps the tab highlight in sync whoever changed the set: stock F6/F7, another mod, or our own tab click, which goes through stock too.</summary>
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
		/// Set by the event-driven rebuild paths and consumed in LateUpdate, never
		/// while the window's own Hold button is pressed: a rebuild would pull the
		/// held button from under the mouse. Purely UX — it releases itself anyway.
		/// </summary>
		private bool rebuildPending;

		private void RequestRebuild()
		{
			rebuildPending = true;
		}

		/// <summary>Keeps the footer live for a real activation whatever its source, keypress or our own buttons. Deferred through RequestRebuild.</summary>
		private void OnGroupActivated(Vessel v, int group)
		{
			if (v != FlightGlobals.ActiveVessel)
			{
				return;
			}
			RequestRebuild();
		}

		/// <summary>
		/// Switching the active vessel leaves selectedPart pointing at another craft,
		/// so drop it. Also cancels any picker: it would silently start scoping to the
		/// NEW vessel on the next frame, on a craft the player never opened it for.
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

		/// <summary>Root part carrying the group names and per-(set, number) state. The editor has no ShipConstruct.rootPart: first parentless part.</summary>
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
			// The window hides for the pause menu and for F2, like every other KSP UI.
			// Toggling gameObject.SetActive also stops Update/LateUpdate and tears down
			// no state; FocusLock.OnDisable drops the hover lock if the pointer was on it.
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
			// Update the persisted positions from the OLD ScrollRects while they exist,
			// and leave them alone when one is null, so the picker never writes a stale
			// default over them.
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

			// Not a plain IndexOf: assignedParts holds one representative per symmetry
			// group, while selectedPart may be whichever sibling was clicked in the
			// scene, so the whole group has to count as one.
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

			// The layout has not measured the new content on this frame yet: setting the
			// scroll position before a forced pass would be measured against a stale
			// content height and silently ignored.
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

		/// <summary>One column: header label plus its own ScrollList, fixed to ColWidth. Returns the list's content transform to fill.</summary>
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
			// Every extended number up to the settings cap is listed unconditionally, not
			// just the ones with data: a group with no assignments is simply an empty
			// row, with nothing to persist and nothing to delete.
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
		/// Column 1's second list: A1-A4 mirror the stock custom axes, then every
		/// extended axis number up to the axis cap, unconditionally, exactly like the
		/// group list above.
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
					// A1-A4 mirror GameSettings.AXIS_CUSTOM; extended axes read the KRILL keymap.
					bind = isStock ? StockAxisBindDescribe(i) : ExtendedAxisBindDescribe(i),
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
				// Switches the vessel's live set, exactly as F6/F7 do; the resulting event
				// brings activeSet and the rebuild with it.
				FlightGlobals.ActiveVessel.SetGroupOverride(set);
			}
			else
			{
				activeSet = set;
				RebuildContent();
			}
		}

		// ----------------------------------------------------------- set jump

		/// <summary>
		/// One always-visible row under the tabs: a "jump to this set" bind per set,
		/// editable in both scenes but firing only in flight. Mirrors BuildSetTabs'
		/// layout calls exactly, so both rows are forced to the same column positions.
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
				// Six equal shares of the row: a long bind description clips inside its own
				// button instead of pushing the neighbours out of the window.
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

			// Axes section: a fold header, then the axis rows — same shape as the groups
			// with an "A" prefix, so the two numberings can't be confused at a glance.
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

		/// <summary>Stock custom axes reuse stock's own localized names, the ones its editor shows; extended axes get "Axis N".</summary>
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
				// Read-only info: part and action on one line rather than split across two
				// independently scrolling columns, which would only look paired by row index.
				// The part side is truncated — the action is what a player scans this list for.
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

		/// <summary>Column 2 in axis mode: parts holding assignments for the selected axis; A1-A4 get the same read-only view groups 1-10 have.</summary>
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

		/// <summary>Player-facing name of an axis field: its localized PAW caption when it has one, else the raw field name.</summary>
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
				// Fans out to every current symmetry sibling, not just the part clicked:
				// each holds its own copy of the assignments, so removing from one only
				// would leave the group visually merged but functionally desynced.
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
			// Stock groups already show their actions paired with the owning part in
			// column 2, so there is nothing of substance to add here.
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
				// Same value on every other symmetric sibling: each has its own assignment
				// object with the same values, so it can't be removed by reference —
				// match by value instead.
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
		/// Column 3 in axis mode: two rows per assigned field — the field name with its
		/// [x], then the per-assignment options as cycling text buttons. The speed step
		/// shows only in incremental mode, the only mode it applies to, like stock.
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
				// runs on every sibling in turn and the source assignment is one of them,
				// so reading `!a.inverted` per call would flip the first and un-flip the rest.
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

		/// <summary>Applies an option change to the entry's assignment and to the matching one on every symmetry sibling, exactly as add/remove fan out.</summary>
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
				// By value on every sibling INCLUDING entry.part, so there is one loop
				// instead of two separate paths.
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

		/// <summary>Part/action names in columns 2/3 for extended groups: 30 characters plus one ellipsis glyph. The stock read-only view uses its own, shorter cap.</summary>
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

		/// <summary>Fits "PartName → ActionName" in one ColWidth-wide row. Picked by eye against the real column width, not from font metrics.</summary>
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

		/// <summary>Is p the selected part or one of its symmetry siblings? A plain "== selectedPart" would miss the rest of the group.</summary>
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

		// ------------------------------------------------------ stock custom axes

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

		/// <summary>The primary controller channel of stock custom axis `axis`: the slot a mirror row's Capture writes, in place, like stock's own screen.</summary>
		private static AxisBinding_Single StockAxisBinding(int axis)
		{
			AxisKeyBinding akb = StockAxisKeyBinding(axis);
			return akb != null && akb.axisBinding != null ? akb.axisBinding.primary : null;
		}

		/// <summary>
		/// Column-1 and footer text for a mirror row: the primary channel in stock's
		/// own wording, plus the +/- keys stock offers for its custom axes, so a
		/// keyboard-only player never sees "-" on an axis that does move.
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

		/// <summary>Column-1 and footer text of an extended axis: the channel plus its +/- keys, in the mirror rows' wording, so a keys-only axis is never bare.</summary>
		private static string ExtendedAxisBindDescribe(int axis)
		{
			string text = KrillAxisKeymap.Describe(axis);
			if (KrillAxisKeys.HasAny(axis))
			{
				text = Localizer.Format("#LOC_KRILL_ui_axisStockKeys", text, KrillAxisKeys.Describe(axis, true), KrillAxisKeys.Describe(axis, false));
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
		/// Writes or clears the primary channel of a stock custom axis and saves the
		/// settings. Copies only the channel IDENTITY: inversion, sensitivity, dead
		/// zone, scale and lock mask are stock's, and a recapture must not reset them.
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

		/// <summary>The group's current bind as a KrillBind, for the footer's conflict check. Always primary-only for stock groups, which have no modifiers.</summary>
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
			GameObject footer = BuildFooterShell(out Transform row);

			if (selected.HasValue)
			{
				GroupEntry g = selected.Value;
				InputField nameField = KrillUi.Field(row, g.name, 130f, text => SetGroupName(g.number, text));
				KrillUi.Size(nameField.gameObject, 130f, 20f);

				KrillUi.TextButton(row, Loc("#LOC_KRILL_ui_capture"), () => StartCapture(g.number, g.isStock),
					KrillUi.Panel2, KrillUi.TanDim, 11, 55f, 22f);

				// Kind resolved once, extended groups only: it feeds both the Trigger button
				// below (Hold needs press-and-hold, not a click) and the controls further
				// down. Stock 1-10 already have their own persisted state in vessel.ActionGroups.
				KrillQuery.GroupState? gs = !g.isStock ? KrillQuery.GetGroupState(RootPart(), activeSet, g.number) : null;
				KrillActuationKind kind = gs?.kind ?? KrillActuationKind.Pulse;

				if (HighLogic.LoadedSceneIsFlight)
				{
					if (kind == KrillActuationKind.Hold)
					{
						// Press-and-hold, not a toggle, so the meaning of Hold is the same from
						// every source: mouse-down is a Window press, mouse-up its release, and
						// the engine actuates on the group's 0->1 / 1->0 edges across all of them.
						KrillUi.HoldButton(row, Loc("#LOC_KRILL_ui_trigger"),
							() => KrillActivation.HoldPress(FlightGlobals.ActiveVessel, g.number, KrillHoldSource.Window),
							() => KrillActivation.HoldRelease(g.number, KrillHoldSource.Window),
							KrillUi.Panel2, KrillUi.GreenHi, 11, 50f, 22f);
					}
					else
					{
						KrillUi.TextButton(row, Loc("#LOC_KRILL_ui_trigger"), () => Trigger(g.number, g.isStock),
							KrillUi.Panel2, KrillUi.GreenHi, 11, 50f, 22f);
					}
				}

				if (!g.isStock)
				{
					string kindLabel = Loc(KindLocKey(kind));
					KrillUi.TextButton(row, kindLabel, () => CycleKind(g.number),
						KrillUi.Panel2, KrillUi.TanDim, 11, 65f, 22f);

					// Toggle only: the one kind whose signal is a persisted value the player can
					// declare. A Pulse signal is a timer and a Hold signal is "someone is
					// pressing right now" — neither has a stored value to force.
					if (kind == KrillActuationKind.Toggle)
					{
						bool state = gs.Value.signal;
						string stateLabel = Localizer.Format("#LOC_KRILL_ui_stateLabel", state ? "1" : "0");
						Color stateColor = state ? KrillUi.GreenHi : KrillUi.TanDim;
						KrillUi.TextButton(row, stateLabel, () => ForceState(g.number, !state),
							KrillUi.Panel2, stateColor, 11, 60f, 22f);
					}
				}

				// Console severity label: cosmetic only, with no dependency on the kind or
				// the activation engine. Unlike the controls above it is offered for stock
				// groups too, since the console grid will show 1-10 alongside the rest.
				Part rootForIndicator = RootPart();
				ModuleKrill mForIndicator = rootForIndicator != null ? rootForIndicator.FindModuleImplementing<ModuleKrill>() : null;
				KrillIndicatorType indicatorType = mForIndicator != null
					? mForIndicator.GetIndicatorType(activeSet, g.number)
					: KrillIndicatorType.Info;
				KrillUi.TextButton(row, Loc(IndicatorLocKey(indicatorType)), () => CycleIndicator(g.number),
					KrillUi.Panel2, KrillUi.TanDim, 11, 65f, 22f);

				string bindInfo = Localizer.Format("#LOC_KRILL_ui_bindInfo", g.number.ToString(), g.bind);
				List<string> conflicts = KrillConflicts.Describe(CurrentBind(g), g.isStock ? -1 : g.number);
				BuildFooterInfo(footer.transform, bindInfo, conflicts);
			}
			else
			{
				Text hint = KrillUi.Label(row, Loc("#LOC_KRILL_ui_hint"), 11, KrillUi.Muted);
				KrillUi.Size(hint.gameObject, -1f, 22f, 1f);
			}
		}

		/// <summary>Footer in axis mode: the same slots as the group footer — name, capture, the +/- keys, the kind/rest cycle, the value slider, the info line.</summary>
		private void BuildAxisFooter(AxisEntry a)
		{
			GameObject footer = BuildFooterShell(out Transform row);

			InputField nameField = KrillUi.Field(row, a.name, 130f, text => SetAxisName(a.number, text));
			KrillUi.Size(nameField.gameObject, 130f, 20f);

			string bindInfo = Localizer.Format("#LOC_KRILL_ui_axisInfo", a.number.ToString(), a.bind);
			// Same slot and look as the group Capture button, with "move an axis" as the
			// gesture. Offered for the mirror rows too, where it writes AXIS_CUSTOM the
			// way a stock group's Capture writes its stock KeyBinding.
			KrillUi.TextButton(row, Loc("#LOC_KRILL_ui_capture"), () => StartAxisCapture(a.number),
				KrillUi.Panel2, KrillUi.TanDim, 11, 55f, 22f);

			List<string> conflicts;
			if (a.isStock)
			{
				// Mirror row: the level is stock's own custom axis, so the slider is
				// read-only in flight and absent in the editor, there is no Kind/Rest (stock
				// has neither) and no assignment UI — that is stock's own editor.
				KrillQuery.AxisState? stockState = KrillQuery.GetAxisState(RootPart(), activeSet, a.number);
				if (stockState.HasValue)
				{
					float level = stockState.Value.value;
					axisSliderAxis = a.number;
					axisValueSlider = KrillUi.Slider(row, -1f, 1f, level, 100f, 20f, _ => { }, readOnly: true);
					axisValueLabel = KrillUi.Label(row, FormatAxisValue(level), 11, KrillUi.Muted, TextAnchor.MiddleCenter);
					KrillUi.Size(axisValueLabel.gameObject, 40f, 22f);
				}

				AxisBinding_Single stockBind = StockAxisBinding(a.number);
				conflicts = KrillConflicts.DescribeAxis(stockBind != null ? stockBind.idTag : null, -1, a.number);
			}
			else
			{
				// The +/- key slots: one KrillCapture each, Delete clears the slot, lit when set.
				BuildAxisKeyButton(row, a.number, true);
				BuildAxisKeyButton(row, a.number, false);

				KrillQuery.AxisState? st = KrillQuery.GetAxisState(RootPart(), activeSet, a.number);
				KrillAxisKind kind = st?.kind ?? KrillAxes.DefaultKind;

				// Value slider in the Trigger/State slots: the axis's live level, writable
				// only when no channel is bound — a bound axis belongs to the controller and
				// the slider just follows it. In the editor only a Fixed axis has a value.
				bool bound = KrillAxisKeymap.IsBound(a.number);
				if (kind == KrillAxisKind.Fixed || HighLogic.LoadedSceneIsFlight)
				{
					float level = st?.value ?? 0f;
					int axisNumber = a.number;
					int rest = st?.rest ?? 0;
					axisSliderAxis = axisNumber;
					axisValueSlider = KrillUi.Slider(row, -1f, 1f, level, 100f, 20f,
						v => OnAxisSliderChanged(axisNumber, kind, v),
						() => axisSliderHeld = true,
						() => OnAxisSliderReleased(axisNumber, kind, rest),
						readOnly: bound);
					axisValueLabel = KrillUi.Label(row, FormatAxisValue(level), 11, bound ? KrillUi.Muted : KrillUi.Tan, TextAnchor.MiddleCenter);
					KrillUi.Size(axisValueLabel.gameObject, 40f, 22f);
				}

				// Kind and rest in ONE four-state cycle, starting from the default:
				// Fixed -> Spring 0 -> Spring -1 -> Spring +1 -> Fixed. Rest only means
				// something for a Spring, so folding them together costs nothing.
				string kindText;
				if (kind == KrillAxisKind.Fixed)
				{
					kindText = Loc("#LOC_KRILL_ui_axisKindFixed");
				}
				else
				{
					int springRest = st?.rest ?? 0;
					kindText = Localizer.Format("#LOC_KRILL_ui_axisKindSpringRest", springRest > 0 ? "+1" : springRest.ToString());
				}
				KrillUi.TextButton(row, kindText, () => CycleAxisKindRest(a.number),
					KrillUi.Panel2, KrillUi.TanDim, 11, 75f, 22f);

				// Persistent conflict advisory on the CURRENT bind, like the group footer.
				AxisBinding_Single current = KrillAxisKeymap.GetBind(a.number);
				conflicts = KrillConflicts.DescribeAxis(current != null ? current.idTag : null, a.number);
				conflicts.AddRange(KrillConflicts.Describe(KrillAxisKeys.Get(a.number, true), -1, -1, a.number, true));
				conflicts.AddRange(KrillConflicts.Describe(KrillAxisKeys.Get(a.number, false), -1, -1, a.number, false));
			}

			BuildFooterInfo(footer.transform, bindInfo, conflicts);
		}

		/// <summary>
		/// Two-row footer: every control shares the first 26 px row and the bind/conflict
		/// line gets the whole second one, so long descriptions have room. The window's
		/// ContentSizeFitter absorbs the extra height.
		/// </summary>
		private GameObject BuildFooterShell(out Transform controls)
		{
			GameObject footer = KrillUi.Go("Footer", contentHost);
			KrillUi.Vertical(footer, 0, 4f);
			GameObject rowGo = KrillUi.Go("Controls", footer.transform);
			KrillUi.Horizontal(rowGo, 0, 8f);
			KrillUi.Size(rowGo, -1f, 26f);
			controls = rowGo.transform;
			return footer;
		}

		/// <summary>
		/// The footer's second-row info label, shared by the group and axis footers.
		/// With conflicts it turns Warn and lists them FIRST, so the colour is always
		/// visible and the detail stays readable for as long as it fits.
		/// </summary>
		private static void BuildFooterInfo(Transform parent, string bindInfo, List<string> conflicts)
		{
			bool warn = conflicts != null && conflicts.Count > 0;
			string text = warn
				? Loc("#LOC_KRILL_ui_conflicts") + ": " + string.Join(", ", conflicts) + " — " + bindInfo
				: bindInfo;
			Text info = KrillUi.Label(parent, text, 11, warn ? KrillUi.Warn : KrillUi.Muted);
			KrillUi.Size(info.gameObject, 0f, 20f, 1f);
			KrillUi.ClipText(info);
		}

		// ---------------------------------------------------------- axis value slider

		private static string FormatAxisValue(float v)
		{
			return v.ToString("+0.00;-0.00;0.00");
		}

		/// <summary>Slider change on an unbound axis: Fixed writes the persisted value, Spring sets the live level for as long as the mouse holds it.</summary>
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

		/// <summary>Keeps the slider and its readout on the axis's real level whenever the mouse is not the one moving it.</summary>
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

		// ---------------------------------------------------------- axis +/- keys

		private void BuildAxisKeyButton(Transform parent, int axis, bool plus)
		{
			bool set = KrillAxisKeys.Get(axis, plus) != null;
			KrillUi.TextButton(parent, plus ? "+" : "-", () => StartAxisKeyCapture(axis, plus),
				KrillUi.Panel2, set ? KrillUi.Tan : KrillUi.TanDim, 11, 22f, 22f);
		}

		/// <summary>Same gesture and class as a group capture: the two slots are just two more places a KrillBind can live.</summary>
		private void StartAxisKeyCapture(int axis, bool plus)
		{
			pendingRemovePart = false;
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_axisKeyCaptureStart", axis.ToString(), plus ? "+" : "-"), 4f, ScreenMessageStyle.UPPER_CENTER);
			KrillCapture.Begin(
				bind => OnAxisKeyCaptured(axis, plus, bind),
				() => ScreenMessages.PostScreenMessage(Loc("#LOC_KRILL_ui_captureCancelled"), 3f, ScreenMessageStyle.UPPER_CENTER),
				() => OnAxisKeyCleared(axis, plus));
		}

		private void OnAxisKeyCaptured(int axis, bool plus, KrillBind bind)
		{
			string conflictSuffix = "";
			List<string> conflicts = KrillConflicts.Describe(bind, -1, -1, axis, plus);
			if (conflicts.Count > 0)
			{
				conflictSuffix = " (" + Loc("#LOC_KRILL_ui_conflicts") + ": " + string.Join(", ", conflicts) + ")";
			}
			KrillAxisKeys.Set(axis, plus, bind);
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_axisKeyCaptureDone", axis.ToString(), plus ? "+" : "-", bind.Describe()) + conflictSuffix,
				5f, ScreenMessageStyle.UPPER_CENTER);
			RebuildContent();
		}

		private void OnAxisKeyCleared(int axis, bool plus)
		{
			KrillAxisKeys.Set(axis, plus, null);
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_axisKeyCaptureCleared", axis.ToString(), plus ? "+" : "-"), 4f, ScreenMessageStyle.UPPER_CENTER);
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

		/// <summary>The footer's kind/rest cycle: Fixed -> Spring 0 -> Spring -1 -> Spring +1 -> Fixed, entering Spring always from rest 0.</summary>
		private void CycleAxisKindRest(int axis)
		{
			Part root = RootPart();
			ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
			if (m == null)
			{
				return;
			}
			if (m.GetAxisKind(activeSet, axis) == KrillAxisKind.Fixed)
			{
				m.SetAxisKind(activeSet, axis, KrillAxisKind.Spring);
				m.SetAxisRest(activeSet, axis, 0);
			}
			else
			{
				switch (m.GetAxisRest(activeSet, axis))
				{
					case 0:
						m.SetAxisRest(activeSet, axis, -1);
						break;
					case -1:
						m.SetAxisRest(activeSet, axis, 1);
						break;
					default:
						m.SetAxisKind(activeSet, axis, KrillAxisKind.Fixed);
						break;
				}
			}
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

		/// <summary>Unlike CycleKind, valid for stock groups too: the indicator type does not depend on which engine runs the group.</summary>
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
		/// Force-sets a Toggle group's signal without invoking any action, for when the
		/// part's real state has drifted or the player wants to declare a starting
		/// value. Writes ONLY the signal, so the next press alternates as it would have.
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

		/// <summary>Delete during a group capture: stock groups get their KeyBinding primary set to None, extended groups drop their keymap entry.</summary>
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
			// The window must drive KrillCapture itself: nothing else ticks it in the
			// editor scene. Harmless in flight, where KrillInputManager ticks it too —
			// KrillCapture.Tick is frame-guarded against double-driving.
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
