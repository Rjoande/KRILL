using System.Collections;
using System.Reflection;
using KSP.Localization;

namespace KRILL
{
	/// <summary>
	/// KRILL section in the stock settings page (Difficulty -> KRILL), so global
	/// options never need a hand-edited cfg. The caps are pure UI bounds: data
	/// above a cap is hidden, never touched or dropped.
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

		// Same pure-UI cap for extended axes. A1-A4 are the stock custom axes shown
		// as mirror rows, so a minimum of 5 always leaves one extended axis visible.
		[GameParameters.CustomIntParameterUI("#LOC_KRILL_settings_maxAxis",
			toolTip = "#LOC_KRILL_settings_maxAxis_tip",
			minValue = 5, maxValue = 40)]
		public int maxVisibleAxis = 12;

		// One dead zone for every extended axis, applied at read time so changing it
		// needs no recapture (the binds themselves keep deadzone 0). Keep "P0": the
		// slider rounds with displayFormat before storing, and "N0" would store 0.
		[GameParameters.CustomFloatParameterUI("#LOC_KRILL_settings_axisDeadzone",
			toolTip = "#LOC_KRILL_settings_axisDeadzone_tip",
			minValue = 0f, maxValue = 0.5f, stepCount = 51, displayFormat = "P0", asPercentage = false)]
		public float axisDeadzone = 0.05f;

		// Axis +/- keys: two global values, the axis kind picks which one applies —
		// Spring uses the attack (ms for a full -1..+1 swing, 0 = stock snap), Fixed
		// the rate (% of the full travel per second). Ints avoid the format trap.
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

		/// <summary>Fixed-axis key speed in units per second. The setting is a percentage of the FULL -1..+1 travel: 50 %/s -> 1.0 unit/s.</summary>
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
