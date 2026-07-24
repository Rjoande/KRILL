using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KRILL.UI
{
	/// <summary>
	/// The single KRILL window (design doc §6): one shell, two contexts driven by
	/// scene rather than by separate windows. Assign/Trigger/rename/rebind all work
	/// in both editor and flight now (2026-07-11 revision: M3's first pass had
	/// Assign editor-only and Trigger flight-only; in-game feedback asked for both
	/// everywhere except stock groups 1-10, which still delegate assignment to
	/// stock's own Action Groups screen — deliberately not duplicated here).
	///
	/// Shell (canvas, titlebar, drag, hover focus-lock) is ported from KRAB's
	/// KrabEditorWindow.cs verbatim in structure — same skin, same shell, per the
	/// design decision to share KRAB's look and feel.
	/// </summary>
	public partial class KrillWindow : MonoBehaviour
	{
		private const float WindowWidth = 620f;
		private const float ListAreaHeight = 320f;
		private const string InputLockId = "KRILL_WINDOW";

		private static KrillWindow current;

		/// <summary>Set by KrillToolbarApp so the toolbar button un-presses itself when the window closes via its own ✕ instead of the toolbar (which would otherwise desync the button's visual state).</summary>
		public static System.Action OnClosed;

		private RectTransform windowRect;
		private Transform contentHost;

		/// <summary>0 = Default, 1..4 = the stock override sets (Vessel.GroupOverride values).</summary>
		private int activeSet;

		/// <summary>Extended group whose assignment detail is expanded in-line (2026-07-11 addition), or null if none. Only one at a time — opening another collapses this one.</summary>
		private int? expandedGroup;

		/// <summary>Parts currently marked with the detail-view highlight, tracked so ClearDetailHighlights can turn off exactly these and no others.</summary>
		private readonly List<Part> detailHighlightedParts = new List<Part>();

		/// <summary>Dark blue for "this part has an assignment shown in the open detail view" — distinct from the picker's cyan hover so the two meanings never look the same.</summary>
		private static readonly Color DetailColor = new Color(0.18f, 0.35f, 0.85f);

		/// <summary>Two-click confirm for "Remove group" (destructive, no undo system in KRILL): first click arms it, second click on the SAME group executes; anything else resets it.</summary>
		private int pendingRemoveGroup = -1;

		// Custom groups 1..10 map to these KSPActionGroup values in array order.
		private static readonly KSPActionGroup[] StockGroups =
		{
			KSPActionGroup.Custom01, KSPActionGroup.Custom02, KSPActionGroup.Custom03, KSPActionGroup.Custom04,
			KSPActionGroup.Custom05, KSPActionGroup.Custom06, KSPActionGroup.Custom07, KSPActionGroup.Custom08,
			KSPActionGroup.Custom09, KSPActionGroup.Custom10,
		};

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
			ClearDetailHighlights();
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
			CollapseDetail();
			RebuildContent();
		}

		/// <summary>Switching the active vessel (e.g. '['/']') leaves the detail view's highlighted parts pointing at a craft that's no longer the one being shown — collapse rather than show a stale blue highlight on an unrelated ship.</summary>
		private void OnActiveVesselChanged(Vessel v)
		{
			CollapseDetail();
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
			KrillUi.Vertical(contentHost.gameObject, 10, 9f);

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
			for (int i = contentHost.childCount - 1; i >= 0; i--)
			{
				Destroy(contentHost.GetChild(i).gameObject);
			}

			if (pickerKind == PickerKind.PickingAction)
			{
				BuildActionPicker();
				return;
			}
			if (pickerKind == PickerKind.PickingPart)
			{
				BuildPickPrompt();
				return;
			}

			IList<Part> parts = ActiveParts();

			// Defensive, not just for the removal path: if whatever emptied this
			// group out of GroupsInUse happened some other way in the future, an
			// expandedGroup pointing at a now-nonexistent row would otherwise sit
			// stale (its highlight already cleared elsewhere, but the field itself
			// never reset) until something else happened to touch it.
			if (expandedGroup.HasValue && !KrillQuery.GroupsInUse(parts).Contains(expandedGroup.Value))
			{
				CollapseDetail();
			}

			BuildSetTabs();
			RectTransform list = KrillUi.ScrollList(contentHost, ListAreaHeight);

			for (int i = 1; i <= 10; i++)
			{
				BuildGroupRow(list, i, true, parts, false);
			}

			List<int> extended = KrillQuery.GroupsInUse(parts);
			int cap = KrillParams.MaxVisibleGroup;
			int lastVisible = -1;
			for (int i = 0; i < extended.Count; i++)
			{
				if (extended[i] <= cap)
				{
					lastVisible = extended[i];
				}
			}
			for (int i = 0; i < extended.Count; i++)
			{
				if (extended[i] > cap)
				{
					continue;
				}
				BuildGroupRow(list, extended[i], false, parts, extended[i] == lastVisible);
				if (expandedGroup == extended[i])
				{
					BuildAssignmentDetail(list, extended[i], parts);
				}
			}

			int next = KrillGroups.FirstExtended;
			for (int i = 0; i < extended.Count; i++)
			{
				if (extended[i] >= next)
				{
					next = extended[i] + 1;
				}
			}
			// Global, not just this craft's: a number already bound (on ANY vessel,
			// via the player-wide keymap) already has meaning — offering it again
			// here would silently collide with whatever that key/name means elsewhere.
			foreach (KeyValuePair<int, KrillBind> kv in KrillKeymap.Binds)
			{
				if (kv.Key >= next)
				{
					next = kv.Key + 1;
				}
			}
			if (next <= cap)
			{
				KrillUi.TextButton(contentHost, Loc("#LOC_KRILL_ui_newGroup"), () => CreateGroup(next),
					KrillUi.Panel2, KrillUi.Muted, 12, -1f, 24f);
			}
			else
			{
				KrillUi.Label(contentHost, Loc("#LOC_KRILL_ui_capReached"), 11, KrillUi.Faint,
					TextAnchor.MiddleCenter);
			}

			GameObject footer = KrillUi.Go("Footer", contentHost);
			KrillUi.Horizontal(footer, 0, 10f);
			Text hint = KrillUi.Label(footer.transform, Loc("#LOC_KRILL_ui_hint"), 11, KrillUi.Muted);
			KrillUi.Size(hint.gameObject, -1f, 20f, 1f);
		}

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
					// Bold is the "unmissable at a glance" cue the flight badge alone can't give (in-game feedback: the active set must be obvious even when Panel2/Inset read similarly under some lighting).
					tab.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
				}
			}
		}

		private void OnTabClicked(int set)
		{
			pendingRemoveGroup = -1;
			CollapseDetail();
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

		private void BuildGroupRow(Transform parent, int group, bool isStock, IList<Part> parts, bool isLastExtended)
		{
			RectTransform row = KrillUi.Bordered("Row" + group, parent, KrillUi.Panel, KrillUi.Line);
			KrillUi.Horizontal(row.gameObject, 6, 6f);
			KrillUi.Size(row.gameObject, -1f, 28f);

			if (!isStock)
			{
				bool expanded = expandedGroup == group;
				KrillUi.TextButton(row, expanded ? "–" : "+", () => ToggleDetail(group, parts),
					KrillUi.Panel2, KrillUi.Muted, 13, 20f, 22f);
			}
			else
			{
				KrillUi.Size(KrillUi.Go("Spacer", row), 20f, 22f);
			}

			Text number = KrillUi.Label(row, group.ToString(), 12, isStock ? KrillUi.Muted : KrillUi.Malachite,
				TextAnchor.MiddleCenter, FontStyle.Bold);
			KrillUi.Size(number.gameObject, 20f, 22f);

			string currentName = KrillQuery.GetGroupName(parts, activeSet, group);
			string placeholder = currentName ?? DefaultGroupName(group);
			InputField nameField = KrillUi.Field(row, placeholder, 100f, text =>
			{
				// Field loses focus (and fires onEndEdit) on any stray click elsewhere in
				// the window, not just on a real edit — skip the write when the text is
				// still the placeholder, so merely clicking into an unnamed group's field
				// and away again doesn't silently persist "Group 14" as a real name.
				if (text != placeholder)
				{
					SetGroupName(group, text);
				}
			});
			KrillUi.Size(nameField.gameObject, 100f, 19f, 1f);

			string bindText = isStock ? StockBindDescribe(group) : (KrillKeymap.GetBind(group)?.Describe() ?? "-");
			Text bind = KrillUi.Label(row, bindText, 11, KrillUi.Text, TextAnchor.MiddleCenter);
			KrillUi.Size(bind.gameObject, 80f, 22f);

			KrillUi.TextButton(row, Loc("#LOC_KRILL_ui_capture"), () => StartCapture(group, isStock),
				KrillUi.Panel2, KrillUi.TanDim, 11, 55f, 22f);

			if (!isStock)
			{
				KrillUi.TextButton(row, Loc("#LOC_KRILL_ui_assign"), () => StartPartPick(group),
					KrillUi.Panel2, KrillUi.GreenHi, 11, 50f, 22f);
			}
			if (HighLogic.LoadedSceneIsFlight)
			{
				KrillUi.TextButton(row, Loc("#LOC_KRILL_ui_trigger"), () => Trigger(group, isStock),
					KrillUi.Panel2, KrillUi.GreenHi, 11, 50f, 22f);
			}
			if (isLastExtended)
			{
				bool armed = pendingRemoveGroup == group;
				KrillUi.TextButton(row, Loc(armed ? "#LOC_KRILL_ui_removeConfirm" : "#LOC_KRILL_ui_remove"),
					() => OnRemoveClicked(group), KrillUi.Panel2, armed ? KrillUi.Danger : KrillUi.Muted, 11, 50f, 22f);
			}
		}

		private static string DefaultGroupName(int group)
		{
			return Loc("#LOC_KRILL_ui_groupDefaultName") + " " + group;
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

		private void SetGroupName(int group, string text)
		{
			pendingRemoveGroup = -1;
			Part root = RootPart();
			ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
			if (m == null)
			{
				return;
			}
			string trimmed = string.IsNullOrEmpty(text) ? null : text.Trim();
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
				KrillActivation.Activate(v, group);
			}
		}

		/// <summary>"+ New Group": creates an empty, named row without forcing the assign picker open (in-game feedback: creating a placeholder for a key you'll wire up later, e.g. group 13 deliberately left empty between 12 and 14, was previously impossible without going through — and then cancelling — a part pick).</summary>
		private void CreateGroup(int group)
		{
			pendingRemoveGroup = -1;
			Part root = RootPart();
			ModuleKrill m = root != null ? root.FindModuleImplementing<ModuleKrill>() : null;
			if (m == null)
			{
				return;
			}
			m.Data.SetName(activeSet, group, DefaultGroupName(group));
			m.MarkDirty();
			RebuildContent();
		}

		// -------------------------------------------------------------- bind capture

		private void StartCapture(int group, bool isStock)
		{
			pendingRemoveGroup = -1;
			ScreenMessages.PostScreenMessage(
				Localizer.Format("#LOC_KRILL_ui_captureStart", group.ToString()), 3f, ScreenMessageStyle.UPPER_CENTER);
			KrillCapture.Begin(
				bind => OnCaptured(group, isStock, bind),
				() => ScreenMessages.PostScreenMessage(Loc("#LOC_KRILL_ui_captureCancelled"), 3f, ScreenMessageStyle.UPPER_CENTER));
		}

		private void OnCaptured(int group, bool isStock, KrillBind bind)
		{
			pendingRemoveGroup = -1;
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

		// ---------------------------------------------------------- assignment detail

		private void ToggleDetail(int group, IList<Part> parts)
		{
			pendingRemoveGroup = -1;
			if (expandedGroup == group)
			{
				CollapseDetail();
			}
			else
			{
				CollapseDetail();
				expandedGroup = group;
				ApplyDetailHighlights(group, parts);
			}
			RebuildContent();
		}

		private void CollapseDetail()
		{
			expandedGroup = null;
			ClearDetailHighlights();
		}

		private void ApplyDetailHighlights(int group, IList<Part> parts)
		{
			List<KrillQuery.AssignmentEntry> entries = KrillQuery.GetAssignmentEntries(parts, activeSet, group);
			for (int i = 0; i < entries.Count; i++)
			{
				Part p = entries[i].part;
				if (p == null || detailHighlightedParts.Contains(p))
				{
					continue;
				}
				p.SetHighlightType(Part.HighlightType.AlwaysOn);
				p.SetHighlightColor(DetailColor);
				p.SetHighlight(true, false);
				detailHighlightedParts.Add(p);
			}
		}

		private void ClearDetailHighlights()
		{
			for (int i = 0; i < detailHighlightedParts.Count; i++)
			{
				if (detailHighlightedParts[i] != null)
				{
					detailHighlightedParts[i].SetHighlightDefault();
				}
			}
			detailHighlightedParts.Clear();
		}

		private void BuildAssignmentDetail(Transform parent, int group, IList<Part> parts)
		{
			List<KrillQuery.AssignmentEntry> entries = KrillQuery.GetAssignmentEntries(parts, activeSet, group);
			RectTransform panel = KrillUi.Bordered("Detail" + group, parent, KrillUi.Inset, KrillUi.Line);
			KrillUi.Vertical(panel.gameObject, 6, 3f);

			if (entries.Count == 0)
			{
				KrillUi.Label(panel, Loc("#LOC_KRILL_ui_noAssignments"), 11, KrillUi.Faint, TextAnchor.MiddleCenter);
				return;
			}

			for (int i = 0; i < entries.Count; i++)
			{
				KrillQuery.AssignmentEntry entry = entries[i];
				GameObject line = KrillUi.Go("Entry", panel);
				KrillUi.Horizontal(line, 4, 6f);
				string partTitle = entry.part != null ? entry.part.partInfo.title : "?";
				string actionName = entry.resolved != null ? entry.resolved.guiName : Loc("#LOC_KRILL_ui_unresolved");
				Text label = KrillUi.Label(line.transform, partTitle + " — " + actionName, 11, KrillUi.Text);
				KrillUi.Size(label.gameObject, -1f, 20f, 1f);
				KrillUi.TextButton(line.transform, "✕", () => RemoveAssignmentEntry(entry, group, parts),
					KrillUi.Panel2, KrillUi.Danger, 11, 22f, 20f);
			}
		}

		private void RemoveAssignmentEntry(KrillQuery.AssignmentEntry entry, int group, IList<Part> parts)
		{
			pendingRemoveGroup = -1;
			if (entry.module != null && entry.module.Data.RemoveAssignment(entry.assignment))
			{
				entry.module.MarkDirty();
			}
			// Re-apply highlights for what's left assigned (the removed part may no
			// longer belong in the set at all).
			ClearDetailHighlights();
			ApplyDetailHighlights(group, parts);
			RebuildContent();
		}

		// ------------------------------------------------------------- remove group

		private void OnRemoveClicked(int group)
		{
			if (pendingRemoveGroup == group)
			{
				IList<Part> parts = ActiveParts();
				KrillQuery.RemoveGroupEverywhere(parts, group);
				if (expandedGroup == group)
				{
					CollapseDetail();
				}
				pendingRemoveGroup = -1;
				ScreenMessages.PostScreenMessage(
					Localizer.Format("#LOC_KRILL_ui_groupRemoved", group.ToString()), 4f, ScreenMessageStyle.UPPER_CENTER);
			}
			else
			{
				pendingRemoveGroup = group;
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
