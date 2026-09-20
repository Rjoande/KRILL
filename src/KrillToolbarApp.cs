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

		public void OnDestroy()
		{
			UI.KrillWindow.OnClosed = null;
			if (toolbarControl != null)
			{
				toolbarControl.OnDestroy();
				Destroy(toolbarControl);
			}
		}
	}

	[KSPAddon(KSPAddon.Startup.EditorAny, false)]
	public class KrillToolbarAppEditor : KrillToolbarApp
	{
	}

	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class KrillToolbarAppFlight : KrillToolbarApp
	{
	}
}
