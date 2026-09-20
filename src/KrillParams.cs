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

		// Axis +/- keys (2026-09-18, K1, notes/axes-design.md §11.4). Two global
		// values, like the dead zone — the kind of the axis decides which one
		// applies (KrillAxisDriver.KeyLevel):
		//   Spring: time of a full -1..+1 swing while a key is held, in ms.
		//           0 = snap to ±1 like stock's own custom-axis keys (default).
		//   Fixed:  speed of the persisted level while a key is held, in % of
		//           the full travel per second (50 = ~2 s end to end).
		// Ints with a step, not floats: the float slider's set path rounds with
		// displayFormat (see axisDeadzone), ints have no such trap.
		[GameParameters.CustomIntParameterUI("#LOC_KRILL_settings_axisKeyAttack",
			toolTip = "#LOC_KRILL_settings_axisKeyAttack_tip",
			minValue = 0, maxValue = 1000, stepSize = 50)]
		public int axisKeyAttackMs = 0;

		[GameParameters.CustomIntParameterUI("#LOC_KRILL_settings_axisKeyRate",
			toolTip = "#LOC_KRILL_settings_axisKeyRate_tip",
			minValue = 10, maxValue = 300, stepSize = 10)]
		public int axisKeyRate = 50;

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

		/// <summary>Spring-axis key attack as the duration of a full -1..+1 swing, in seconds; 0 = instant (stock snap).</summary>
		public static float AxisKeyAttackSeconds
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return 0f;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<KrillParams>().axisKeyAttackMs / 1000f;
			}
		}

		/// <summary>Fixed-axis key speed in full-scale units per second. The setting is a percentage of the FULL -1..+1 travel (2 units): 50 %/s -> 1.0 unit/s, end to end in 2 s.</summary>
		public static float AxisKeyRate
		{
			get
			{
				if (HighLogic.CurrentGame == null)
				{
					return 1f;
				}
				return HighLogic.CurrentGame.Parameters.CustomParams<KrillParams>().axisKeyRate / 100f * 2f;
			}
		}
	}
}
