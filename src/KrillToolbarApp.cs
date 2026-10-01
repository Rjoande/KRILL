using KSP.UI.Screens;
using ToolbarControl_NS;
using UnityEngine;

namespace KRILL
{
	[KSPAddon(KSPAddon.Startup.MainMenu, true)]
	public class KrillToolbarRegistration : MonoBehaviour
	{
		public void Start()
		{
			ToolbarControl.RegisterMod(KrillToolbarApp.MODID, KrillToolbarApp.MODNAME);
		}
	}

	/// <summary>
	/// Toolbar button opening/closing the single KrillWindow. [KSPAddon] is not
	/// AllowMultiple, so the shared logic lives here and one near-empty subclass
	/// per scene picks it up.
	/// </summary>
	public class KrillToolbarApp : MonoBehaviour
	{
		internal const string MODID = "KRILL_NS";
		internal const string MODNAME = "KRILL";

		private ToolbarControl toolbarControl;

		public void Start()
		{
			Refresh();
		}

		/// <summary>
		/// Like stock's custom action groups, KRILL does not exist until the facility
		/// tier unlocks them: no button, hence no window. Adds or removes the button
		/// to match the gate; the window closes first, while its callback still has
		/// a button to reset.
		/// </summary>
		protected void Refresh()
		{
			bool unlocked = KrillQuery.ExtendedGroupsUnlockedHere();
			if (unlocked && toolbarControl == null)
			{
				AddButton();
			}
			else if (!unlocked)
			{
				Debug.Log("[KRILL] toolbar button not shown: extended groups locked by the career facility tier");
				if (toolbarControl != null)
				{
					UI.KrillWindow.CloseCurrent();
					RemoveButton();
				}
			}
		}

		private void AddButton()
		{
			toolbarControl = gameObject.AddComponent<ToolbarControl>();
			toolbarControl.AddToAllToolbars(
				UI.KrillWindow.Open, UI.KrillWindow.CloseCurrent,
				ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH
					| ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
				MODID, "KrillButton",
				"KRILL/Textures/KRILL_38",
				"KRILL/Textures/KRILL_24",
				MODNAME);
			UI.KrillWindow.OnClosed = () => toolbarControl.SetFalse(false);

#if KRILL_CONSOLE_PREVIEW
			// Right-click toggles the flight-only Console (no-op outside flight);
			// left-click keeps the window toggle wired by AddToAllToolbars.
			toolbarControl.AddLeftRightClickCallbacks(() => { }, UI.KrillConsole.ToggleVisible);
#endif
		}

		private void RemoveButton()
		{
			UI.KrillWindow.OnClosed = null;
			if (toolbarControl != null)
			{
				toolbarControl.OnDestroy();
				Destroy(toolbarControl);
				toolbarControl = null;
			}
		}

		protected virtual void OnDestroy()
		{
			RemoveButton();
		}
	}

	/// <summary>
	/// Switching VAB <-> SPH from inside the editor does not reload the scene:
	/// EditorDriver.SwitchEditor swaps the scenery, updates editorFacility, then
	/// EditorLogic.StartEditor fires onEditorRestart — the hook stock's own panels
	/// use, with the new facility already in place — so the gate is re-evaluated
	/// there, the two facilities being allowed to sit at different tiers.
	/// </summary>
	[KSPAddon(KSPAddon.Startup.EditorAny, false)]
	public class KrillToolbarAppEditor : KrillToolbarApp
	{
		public void Awake()
		{
			GameEvents.onEditorRestart.Add(Refresh);
		}

		protected override void OnDestroy()
		{
			GameEvents.onEditorRestart.Remove(Refresh);
			base.OnDestroy();
		}
	}

	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class KrillToolbarAppFlight : KrillToolbarApp
	{
	}
}
