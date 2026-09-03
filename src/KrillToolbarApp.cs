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
	/// Toolbar button opening/closing the single KrillWindow (design doc §6: one
	/// window, no per-scene UI split beyond what the window itself branches on).
	/// A single class can't carry two [KSPAddon] attributes (AllowMultiple=false),
	/// so the shared logic lives here and two near-empty subclasses below each
	/// pick up one scene — a fresh instance (and a fresh ToolbarControl) per scene
	/// load, a standard ToolbarControl pattern.
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
			// Right-click toggles the flight-only Console (2026-08-30) — kept as
			// a permanent shortcut, not just a debug aid for the current
			// geometry test. No-op outside flight (UI.KrillConsole.current is
			// null there); left-click keeps its existing onTrue/onFalse window
			// toggle untouched, so a no-op is passed for onLeftClick here.
			// Preview builds only (see KRILL.csproj): the console is not
			// finished, a release build has no right-click behavior at all.
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
