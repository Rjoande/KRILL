using System.Collections;
using System.Reflection;
using KSP.Localization;

namespace KRILL
{
	/// <summary>
	/// KRILL section in the stock settings page (Difficulty -> KRILL). This is the
	/// "stock-looking" home for player-global options, per design doc §3 — never a
	/// cfg file the player has to edit by hand.
	///
	/// maxVisibleGroup is a pure UI cap: it bounds which group numbers the KRILL
	/// window offers, but data above the cap is never touched or dropped (lowering
	/// the cap after assigning group 40 just hides it until raised again).
	/// </summary>
	public class KrillParams : GameParameters.CustomParameterNode
	{
		public override string Title => Localizer.Format("#LOC_KRILL_settings_title");
		public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
		public override string Section => "KRILL";
		public override string DisplaySection => "KRILL";
		public override int SectionOrder => 1;
		public override bool HasPresets => false;

		[GameParameters.CustomIntParameterUI("#LOC_KRILL_settings_maxGroup",
			toolTip = "#LOC_KRILL_settings_maxGroup_tip",
			minValue = 20, maxValue = 99)]
		public int maxVisibleGroup = 20;

		// Same pure-UI cap for extended axes (2026-09-07): A1-A4 are the stock
		// custom axes shown as mirror rows, so the minimum of 5 always leaves at
		// least one extended axis visible; 12 by default = 8 extended.
		[GameParameters.CustomIntParameterUI("#LOC_KRILL_settings_maxAxis",
			toolTip = "#LOC_KRILL_settings_maxAxis_tip",
			minValue = 5, maxValue = 40)]
		public int maxVisibleAxis = 12;

		// One dead zone for every extended axis (2026-09-08, user request): the
		// bindings themselves are stored with deadzone 0 and this is applied at
		// read time (KrillAxisKeymap.TryRead), so changing it here takes effect on
		// every axis at once, no recapture. Default = stock's own 0.05.
		//
		// Format matters (2026-09-11, found on decompiled DifficultyOptionsMenu):
		// the slider's set path ROUNDS the raw 0..0.5 value with displayFormat
		// before storing it, so "N0" stored 0 for every position. A "P" format
		// takes the other branch (rounds to whole percent, stores the fraction)
		// and the label formats the fraction as a percentage by itself — so
		// asPercentage must stay false, or the label would multiply twice.
		[GameParameters.CustomFloatParameterUI("#LOC_KRILL_settings_axisDeadzone",
			toolTip = "#LOC_KRILL_settings_axisDeadzone_tip",
			minValue = 0f, maxValue = 0.5f, stepCount = 51, displayFormat = "P0", asPercentage = false)]
		public float axisDeadzone = 0.05f;

		public override void SetDifficultyPreset(GameParameters.Preset preset)
		{
		}

		public override bool Enabled(MemberInfo member, GameParameters parameters)
		{
			return true;
		}

		public override bool Interactible(MemberInfo member, GameParameters parameters)
		{
			return true;
		}

		public override IList ValidValues(MemberInfo member)
		{
			return null;
		}

		public static int MaxVisibleGroup
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return 20;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<KrillParams>().maxVisibleGroup;
			}
		}

		public static int MaxVisibleAxis
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return 12;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<KrillParams>().maxVisibleAxis;
			}
		}

		public static float AxisDeadzone
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return 0.05f;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<KrillParams>().axisDeadzone;
			}
		}
	}
}
