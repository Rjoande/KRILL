#if KRILL_SELFTEST
using UnityEngine;

namespace KRILL
{
	/// <summary>
	/// In-game data self-tests, compiled only with -p:KrillSelfTest=true (see
	/// KRILL.csproj). KSP's ConfigNode can't be exercised outside the game, so
	/// the round-trip checks run once at the main menu and report to KSP.log as
	/// "[KRILL][selftest] ..." lines — grep for FAIL. Pure data, no scene, no
	/// vessel, no UI: this replaces the PAW debug events removed in 2026-07-20
	/// for the one job they did that a window can't (exercising the loader on
	/// deliberately malformed input).
	///
	/// Currently covers the extended-axis payload (2026-09-07, A1); group data
	/// was validated the same way back in M1 and has stayed shape-stable since.
	/// </summary>
	[KSPAddon(KSPAddon.Startup.MainMenu, true)]
	public class KrillSelfTest : MonoBehaviour
	{
		private int failures;

		private void Start()
		{
			failures = 0;
			try
			{
				AxisRoundTrip();
				AxisTolerantLoad();
				AxisHelpers();
			}
			catch (System.Exception e)
			{
				failures++;
				Debug.LogError("[KRILL][selftest] EXCEPTION: " + e);
			}
			Debug.Log(failures == 0
				? "[KRILL][selftest] axes: ALL PASS"
				: "[KRILL][selftest] axes: " + failures + " FAILURE(S), see lines above");
		}

		private void Check(bool ok, string what)
		{
			if (ok)
			{
				Debug.Log("[KRILL][selftest] PASS " + what);
			}
			else
			{
				failures++;
				Debug.LogError("[KRILL][selftest] FAIL " + what);
			}
		}

		private static KrillPartData Sample()
		{
			KrillPartData d = new KrillPartData();
			KrillFieldRef hinge = new KrillFieldRef { module = "ModuleRoboticServoHinge", occurrence = 0, field = "targetAngle" };
			KrillFieldRef light = new KrillFieldRef { module = "ModuleLight", occurrence = 1, field = "lightR" };
			d.AddAxisAssignment(0, 5, hinge, false, false, 0.2f);
			d.AddAxisAssignment(2, 5, hinge, true, true, 3f);
			d.AddAxisAssignment(0, 7, light, false, true, 1f);
			d.SetAxisName(0, 5, "Boom");
			d.SetAxisName(3, 7, "Cabin light");
			d.SetAxisKind(0, 5, KrillAxisKind.Fixed);
			d.SetAxisValue(0, 5, -0.25f);
			d.SetAxisKind(2, 5, KrillAxisKind.Spring);
			d.SetAxisRest(2, 5, -1);
			d.SetAxisIndicatorType(0, 7, KrillIndicatorType.Warning);
			return d;
		}

		private void AxisRoundTrip()
		{
			KrillPartData a = Sample();
			string s1 = a.SaveToString();
			KrillPartData b = new KrillPartData();
			b.LoadFromString(s1);
			string s2 = b.SaveToString();
			Check(s1 == s2, "axis payload string round-trip is stable");
			Check(b.axisAssignments.Count == 3, "3 axis assignments survive (" + b.axisAssignments.Count + ")");
			Check(b.axisNames.Count == 2, "2 axis names survive (" + b.axisNames.Count + ")");
			Check(b.axisSettings.Count == 3, "3 axis settings survive (" + b.axisSettings.Count + ")");
			KrillAxisAssignment inv = b.FindAxisAssignment(2, 5, new KrillFieldRef { module = "ModuleRoboticServoHinge", field = "targetAngle" });
			Check(inv != null && inv.inverted && inv.incremental && Mathf.Approximately(inv.speed, 3f), "options (inverted/incremental/speed 3) survive");
			Check(b.GetAxisKind(0, 5) == KrillAxisKind.Fixed && Mathf.Approximately(b.GetAxisValue(0, 5), -0.25f), "Fixed kind + value survive");
			Check(b.GetAxisKind(2, 5) == KrillAxisKind.Spring && b.GetAxisRest(2, 5) == -1, "Spring kind + rest -1 survive");
			Check(b.GetAxisIndicatorType(0, 7) == KrillIndicatorType.Warning, "silent indicator slot survives");
			Check(b.GetAxisName(0, 5) == "Boom" && b.GetAxisName(4, 5) == null, "axis name stays in its own set (no set-0 inheritance, 2026-09-14)");
			Check(b.GetAxisName(3, 7) == "Cabin light" && b.GetAxisName(0, 7) == null, "exact-set axis name does not leak to other sets");
			Check(b.GetName(0, 5) == null && b.names.Count == 0, "axis names never land in the GROUP name list");
			// Group data must be untouched by the new lists: an empty group side stays empty.
			Check(b.assignments.Count == 0 && b.kinds.Count == 0 && b.signals.Count == 0, "group lists untouched by axis payload");
		}

		private void AxisTolerantLoad()
		{
			ConfigNode root = new ConfigNode(KrillPartData.BackupNodeName);
			// Valid entry with every optional missing -> defaults.
			ConfigNode ok = root.AddNode(KrillAxisAssignment.NodeName);
			ok.AddValue("set", 1);
			ok.AddValue("axis", 6);
			ok.AddValue("module", "ModuleEngines");
			ok.AddValue("field", "thrustPercentage");
			// Missing axis -> dropped.
			ConfigNode noAxis = root.AddNode(KrillAxisAssignment.NodeName);
			noAxis.AddValue("set", 0);
			noAxis.AddValue("module", "ModuleEngines");
			noAxis.AddValue("field", "thrustPercentage");
			// Stock-range axis number (mirror rows are never persisted by KRILL) -> dropped.
			ConfigNode stockAxis = root.AddNode(KrillAxisAssignment.NodeName);
			stockAxis.AddValue("set", 0);
			stockAxis.AddValue("axis", 3);
			stockAxis.AddValue("module", "ModuleEngines");
			stockAxis.AddValue("field", "thrustPercentage");
			// Bad speed -> default; unknown kind -> Spring; rest out of range -> clamped; value out of range -> clamped.
			ConfigNode badSpeed = root.AddNode(KrillAxisAssignment.NodeName);
			badSpeed.AddValue("set", 0);
			badSpeed.AddValue("axis", 9);
			badSpeed.AddValue("module", "ModuleLight");
			badSpeed.AddValue("field", "lightR");
			badSpeed.AddValue("speed", -4f);
			ConfigNode weird = root.AddNode(KrillAxisSetting.NodeName);
			weird.AddValue("set", 0);
			weird.AddValue("axis", 9);
			weird.AddValue("kind", "Bouncy");
			weird.AddValue("rest", 7);
			weird.AddValue("value", 42f);
			ConfigNode badSetting = root.AddNode(KrillAxisSetting.NodeName);
			badSetting.AddValue("axis", 9); // no set -> dropped

			KrillPartData d = new KrillPartData();
			d.Load(root);
			Check(d.axisAssignments.Count == 2, "malformed/out-of-range axis assignments dropped, valid ones kept (" + d.axisAssignments.Count + ")");
			KrillAxisAssignment first = d.FindAxisAssignment(1, 6, new KrillFieldRef { module = "ModuleEngines", field = "thrustPercentage" });
			Check(first != null && !first.inverted && !first.incremental && Mathf.Approximately(first.speed, KrillAxes.DefaultSpeed), "missing options default (direct, absolute, 20%/s)");
			KrillAxisAssignment slow = d.FindAxisAssignment(0, 9, new KrillFieldRef { module = "ModuleLight", field = "lightR" });
			Check(slow != null && Mathf.Approximately(slow.speed, KrillAxes.DefaultSpeed), "non-positive speed falls back to default");
			Check(d.axisSettings.Count == 1, "setting without set dropped, weird one kept (" + d.axisSettings.Count + ")");
			Check(d.GetAxisKind(0, 9) == KrillAxisKind.Spring, "unknown kind name falls back to Spring");
			Check(d.GetAxisRest(0, 9) == KrillAxes.RestMax, "rest clamped to +1");
			Check(Mathf.Approximately(d.GetAxisValue(0, 9), 1f), "value clamped to 1");
		}

		private void AxisHelpers()
		{
			Check(Mathf.Approximately(KrillAxes.NextSpeed(0.2f), 0.5f) && Mathf.Approximately(KrillAxes.NextSpeed(3f), 0.2f)
				&& Mathf.Approximately(KrillAxes.NextSpeed(0.123f), 0.2f), "speed cycle wraps and recovers from a foreign value");
			Check(KrillAxes.NextRest(-1) == 0 && KrillAxes.NextRest(0) == 1 && KrillAxes.NextRest(1) == -1, "rest cycle -1 -> 0 -> +1 -> -1");
			KrillPartData d = Sample();
			Check(d.RemoveAxisInSet(0, 5) && d.GetAxisName(0, 5) == null && d.FindAxisSetting(0, 5) == null
				&& d.FindAxisAssignment(2, 5, new KrillFieldRef { module = "ModuleRoboticServoHinge", field = "targetAngle" }) != null,
				"RemoveAxisInSet clears only that set (set 2 of the same axis survives)");
			Check(!d.RemoveAxisInSet(0, 5), "RemoveAxisInSet reports nothing to remove the second time");
			Check(d.RemoveAxisAssignmentMatching(0, 7, new KrillFieldRef { module = "ModuleLight", occurrence = 1, field = "lightR" })
				&& !d.RemoveAxisAssignmentMatching(0, 7, new KrillFieldRef { module = "ModuleLight", occurrence = 0, field = "lightR" }),
				"assignment match is by value including occurrence");
			KrillPartData e = new KrillPartData();
			Check(e.IsEmpty, "fresh payload is empty");
			e.SetAxisKind(1, 5, KrillAxisKind.Fixed);
			Check(!e.IsEmpty && e.SaveToString().Contains(KrillAxisSetting.NodeName), "an axis setting alone makes the payload non-empty and serializes");
		}
	}
}
#endif
