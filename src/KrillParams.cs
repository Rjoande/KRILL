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
	}
}
