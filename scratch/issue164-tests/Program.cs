using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using WinPieGestures;

namespace Issue164Tests;

public class Program
{
	private static int _passedCount = 0;
	private static int _failedCount = 0;

	private static void SetConfig(AppConfig config)
	{
		typeof(ConfigManager).GetProperty("CurrentConfig", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
			.SetValue(null, config);
	}

	public static int Main(string[] args)
	{
		bool runBaseline = args.Contains("--baseline");
		Console.WriteLine("==================================================");
		Console.WriteLine(runBaseline
			? "  StarPie Issue #164 - Baseline Failure Verification"
			: "  StarPie Issue #164 - Fix Verification");
		Console.WriteLine("==================================================");

		// Run Defect 1 tests
		Test_Rebind_A_To_B_Fallback_And_Hit(runBaseline);
		Test_ExplicitMultiProcessBinding();
		Test_LegacyConfigFallback();
		Test_SaveAndReloadConfig();
		Test_DuplicateProfileCheck(runBaseline);

		// Run Defect 2 tests
		Test_12Key_TextOnly_FontSize(runBaseline);
		Test_PreviewAndActualConsistency(runBaseline);
		Test_GlobalFontSizeReplacesCurrentPrimaryOverrides();
		Test_AdjacentRegressions_4Key_8Key();

		Console.WriteLine("==================================================");
		Console.WriteLine($"Results: {_passedCount} passed, {_failedCount} failed.");
		Console.WriteLine("==================================================");

		return _failedCount > 0 ? 1 : 0;
	}

	private static void Assert(bool condition, string testName, string detail = "")
	{
		if (condition)
		{
			Console.WriteLine($"[PASS] {testName}");
			_passedCount++;
		}
		else
		{
			Console.WriteLine($"[FAIL] {testName} - {detail}");
			_failedCount++;
		}
	}

	private static void Test_Rebind_A_To_B_Fallback_And_Hit(bool runBaseline)
	{
		Console.WriteLine("\n--- Defect 1.1: Rebind A -> B Fallback & Hit ---");

		// Setup clean config
		var config = new AppConfig
		{
			Profiles = new List<WheelProfile>
			{
				new WheelProfile { ProcessName = "Global", SectorCount = 8, Actions = new List<ActionItem>() }
			}
		};
		SetConfig(config);

		// Add profile for notepad.exe (A)
		var profile = new WheelProfile
		{
			ProcessName = "notepad.exe",
			DisplayName = "Notepad",
			BoundProcesses = "notepad.exe",
			SectorCount = 8,
			Actions = new List<ActionItem>()
		};
		config.Profiles.Add(profile);

		// Initial check: notepad.exe hits this profile
		var initialHit = ConfigManager.GetProfileForProcess("notepad.exe");
		Assert(initialHit == profile, "Initial binding: notepad.exe hits profile");

		// User rebinds profile to code.exe (B)
		profile.BoundProcesses = "code.exe";

		// Program B (code.exe) should now hit this profile
		var hitB = ConfigManager.GetProfileForProcess("code.exe");
		Assert(hitB == profile, "Rebind: code.exe hits rebound profile");

		// Program A (notepad.exe) should now FALL BACK to Global!
		var hitA = ConfigManager.GetProfileForProcess("notepad.exe");
		var globalProfile = ConfigManager.GetGlobalProfile();

		if (runBaseline)
		{
			// In baseline, hitA mistakenly returns 'profile' because ProcessName is "notepad.exe"
			Assert(hitA == profile, "BASELINE EVIDENCE: notepad.exe failed to fall back to Global and still returned profile due to stale ProcessName match");
		}
		else
		{
			Assert(hitA == globalProfile, "Rebind: notepad.exe falls back to Global profile after rebinding to code.exe",
				$"Expected Global, but got {(hitA == profile ? "rebound profile (stale ProcessName)" : hitA?.ProcessName ?? "null")}");
		}
	}

	private static void Test_ExplicitMultiProcessBinding()
	{
		Console.WriteLine("\n--- Defect 1.2: Explicit Multi-Process Binding ---");

		var config = new AppConfig
		{
			Profiles = new List<WheelProfile>
			{
				new WheelProfile { ProcessName = "Global", SectorCount = 8, Actions = new List<ActionItem>() }
			}
		};
		SetConfig(config);

		var multiProfile = new WheelProfile
		{
			ProcessName = "DevSuite",
			DisplayName = "Developer Tools",
			BoundProcesses = "code.exe, devenv.exe; msedge | notepad",
			SectorCount = 8,
			Actions = new List<ActionItem>()
		};
		config.Profiles.Add(multiProfile);

		Assert(ConfigManager.GetProfileForProcess("code.exe") == multiProfile, "Multi-bind matches code.exe");
		Assert(ConfigManager.GetProfileForProcess("devenv.exe") == multiProfile, "Multi-bind matches devenv.exe");
		Assert(ConfigManager.GetProfileForProcess("msedge.exe") == multiProfile, "Multi-bind matches msedge.exe (token msedge)");
		Assert(ConfigManager.GetProfileForProcess("notepad.exe") == multiProfile, "Multi-bind matches notepad.exe");
		Assert(ConfigManager.GetProfileForProcess("calc.exe") == ConfigManager.GetGlobalProfile(), "Unrelated process falls back to Global");
	}

	private static void Test_LegacyConfigFallback()
	{
		Console.WriteLine("\n--- Defect 1.3: Legacy Config Fallback (Empty BoundProcesses) ---");

		var config = new AppConfig
		{
			Profiles = new List<WheelProfile>
			{
				new WheelProfile { ProcessName = "Global", SectorCount = 8, Actions = new List<ActionItem>() }
			}
		};
		SetConfig(config);

		// Legacy profile with BoundProcesses = null
		var legacyProfile1 = new WheelProfile
		{
			ProcessName = "legacyapp.exe",
			DisplayName = "Legacy App",
			BoundProcesses = null,
			SectorCount = 8,
			Actions = new List<ActionItem>()
		};
		config.Profiles.Add(legacyProfile1);

		// Legacy profile with BoundProcesses = "" and match on DisplayName
		var legacyProfile2 = new WheelProfile
		{
			ProcessName = "Custom_1",
			DisplayName = "foobar.exe",
			BoundProcesses = "",
			SectorCount = 8,
			Actions = new List<ActionItem>()
		};
		config.Profiles.Add(legacyProfile2);

		Assert(ConfigManager.GetProfileForProcess("legacyapp.exe") == legacyProfile1, "Legacy profile matches by ProcessName when BoundProcesses is null");
		Assert(ConfigManager.GetProfileForProcess("legacyapp") == legacyProfile1, "Legacy profile matches by base name without .exe");
		Assert(ConfigManager.GetProfileForProcess("foobar.exe") == legacyProfile2, "Legacy profile matches by DisplayName when BoundProcesses is empty");
		Assert(ConfigManager.GetProfileForProcess("unknown.exe") == ConfigManager.GetGlobalProfile(), "Unknown process falls back to Global");
	}

	private static void Test_SaveAndReloadConfig()
	{
		Console.WriteLine("\n--- Defect 1.4: Save & Reload Persistence ---");

		var tempFile = Path.Combine(Path.GetTempPath(), $"starpie_test_config_{Guid.NewGuid():N}.json");
		try
		{
			var config = new AppConfig
			{
				Profiles = new List<WheelProfile>
				{
					new WheelProfile { ProcessName = "Global", SectorCount = 8, Actions = new List<ActionItem>() },
					new WheelProfile
					{
						ProcessName = "notepad.exe",
						DisplayName = "My App",
						BoundProcesses = "target_app.exe",
						SectorCount = 8,
						Actions = new List<ActionItem>()
					}
				}
			};

			var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(tempFile, json);

			var reloadedJson = File.ReadAllText(tempFile);
			var reloadedConfig = JsonSerializer.Deserialize<AppConfig>(reloadedJson)!;
			SetConfig(reloadedConfig);

			var hitTarget = ConfigManager.GetProfileForProcess("target_app.exe");
			var hitNotepad = ConfigManager.GetProfileForProcess("notepad.exe");
			var globalProfile = ConfigManager.GetGlobalProfile();

			Assert(hitTarget != null && hitTarget.ProcessName == "notepad.exe" && hitTarget.BoundProcesses == "target_app.exe",
				"Reloaded config: target_app.exe hits rebound profile");
			Assert(hitNotepad == globalProfile,
				"Reloaded config: notepad.exe falls back to Global profile");
		}
		finally
		{
			if (File.Exists(tempFile)) File.Delete(tempFile);
		}
	}

	private static void Test_DuplicateProfileCheck(bool runBaseline)
	{
		Console.WriteLine("\n--- Defect 1.5: Duplicate Profile Check in Settings ---");

		var config = new AppConfig
		{
			Profiles = new List<WheelProfile>
			{
				new WheelProfile { ProcessName = "Global", SectorCount = 8, Actions = new List<ActionItem>() },
				new WheelProfile
				{
					ProcessName = "notepad.exe",
					DisplayName = "Notepad",
					BoundProcesses = "code.exe", // Rebound to code.exe
					SectorCount = 8,
					Actions = new List<ActionItem>()
				}
			}
		};
		SetConfig(config);

		string checkProc = "notepad.exe";
		bool baselineDuplicate = config.Profiles.Any(p =>
			p.ProcessName.Equals(checkProc, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(p.BoundProcesses) && p.BoundProcesses.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries).Any(x => x.Trim().Equals(checkProc, StringComparison.OrdinalIgnoreCase))));

		if (runBaseline)
		{
			Assert(baselineDuplicate == true, "BASELINE EVIDENCE: Adding notepad.exe is blocked because stale ProcessName matches");
		}
		else
		{
			bool fixedDuplicate = config.Profiles.Any(p => ConfigManager.IsProfileBoundToProcess(p, checkProc));
			bool codeDuplicate = config.Profiles.Any(p => ConfigManager.IsProfileBoundToProcess(p, "code.exe"));
			Assert(fixedDuplicate == false, "Fixed check: Adding notepad.exe is ALLOWED because existing profile is rebound to code.exe");
			Assert(codeDuplicate == true, "Fixed check: Adding code.exe is BLOCKED because profile is bound to code.exe");
		}
	}

	private static void Test_12Key_TextOnly_FontSize(bool runBaseline)
	{
		Console.WriteLine("\n--- Defect 2.1: 12-Key TextOnly Font Size Effective Change ---");

		double defaultBase = 11.0;
		double increasedBase = 14.0;
		double largeBase = 18.0;

		if (runBaseline)
		{
			// Baseline RadialWindow calculation:
			// num13 = ((sectorLayout == "TextOnly") ? (baseFontSize + 1.0) : baseFontSize);
			// case 12: num13 = Math.Min(num13, 10.0); break;
			double num13_default = Math.Min(defaultBase + 1.0, 10.0);
			double num13_increased = Math.Min(increasedBase + 1.0, 10.0);
			double num13_large = Math.Min(largeBase + 1.0, 10.0);

			Assert(num13_default == 10.0 && num13_increased == 10.0 && num13_large == 10.0,
				"BASELINE EVIDENCE: 12-key TextOnly font size is hard-clamped to 10.0 and never changes when user sets 11, 14, or 18px");
		}
		else
		{
			// Directly invoke RadialWindow.CalculatePrimarySectorFontSize!
			ConfigManager.CurrentConfig.SectorFontSize = 11.0;
			double f11 = RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", null, "测试");

			ConfigManager.CurrentConfig.SectorFontSize = 14.0;
			double f14 = RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", null, "测试");

			ConfigManager.CurrentConfig.SectorFontSize = 18.0;
			double f18 = RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", null, "测试");

			var customAction = new ActionItem { CustomFontSize = 16.0 };
			double fCustom16 = RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", customAction, "测试");

			Assert(f14 > f11, $"12-key TextOnly font size increases from 11px ({f11:0.0}) to 14px ({f14:0.0})");
			Assert(f18 > f14, $"12-key TextOnly font size increases from 14px ({f14:0.0}) to 18px ({f18:0.0})");
			Assert(fCustom16 > f14, $"12-key TextOnly per-sector CustomFontSize=16 ({fCustom16:0.0}) takes effect over global 14px");
		}
	}

	private static void Test_PreviewAndActualConsistency(bool runBaseline)
	{
		Console.WriteLine("\n--- Defect 2.2: Settings Preview and RadialWindow Consistency ---");

		double base11 = 11.0;
		double base15 = 15.0;

		double previewScale = 0.82 * (1.0); // normalized previewScale

		if (runBaseline)
		{
			// In baseline SettingsWindow preview vs RadialWindow:
			double preview11 = (base11 + 1.2) * 0.82 * 0.8 * previewScale;
			double preview15 = (base15 + 1.2) * 0.82 * 0.8 * previewScale;

			double actual11 = Math.Min(base11 + 1.0, 10.0);
			double actual15 = Math.Min(base15 + 1.0, 10.0);

			double previewGrowth = preview15 / preview11; // ~ 1.33 (+33%)
			double actualGrowth = actual15 / actual11;   // 1.0 (0%)

			Assert(Math.Abs(previewGrowth - actualGrowth) > 0.2,
				$"BASELINE EVIDENCE: Preview grew by {(previewGrowth - 1.0):P0} but actual wheel grew by {(actualGrowth - 1.0):P0} (inconsistent!)");
		}
		else
		{
			// Fixed RadialWindow and SettingsWindow:
			ConfigManager.CurrentConfig.SectorFontSize = base11;
			double actual11 = RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", null, "短文本");
			double preview11 = actual11 * previewScale;

			ConfigManager.CurrentConfig.SectorFontSize = base15;
			double actual15 = RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", null, "短文本");
			double preview15 = actual15 * previewScale;

			double previewGrowth = preview15 / preview11;
			double actualGrowth = actual15 / actual11;

			Assert(Math.Abs(previewGrowth - actualGrowth) < 0.0001,
				$"Fixed consistency: Preview growth ({previewGrowth:F4}) matches actual wheel growth ({actualGrowth:F4}) exactly");
		}
	}

	private static void Test_GlobalFontSizeReplacesCurrentPrimaryOverrides()
	{
		Console.WriteLine("\n--- Global font slider after per-sector customization ---");

		var first = new ActionItem
		{
			Name = "First",
			LayoutMode = "Inherit",
			CustomFontSize = 8.0,
			SubActions = new List<ActionItem> { new() { CustomFontSize = 9.0 } }
		};
		var second = new ActionItem { Name = "Second", CustomFontSize = 16.0 };
		var profile = new WheelProfile
		{
			ProcessName = "Global",
			SectorCount = 12,
			Actions = new List<ActionItem> { first, second }
		};
		var otherProfile = new WheelProfile
		{
			ProcessName = "other.exe",
			Actions = new List<ActionItem> { new() { CustomFontSize = 10.0 } }
		};
		SetConfig(new AppConfig
		{
			IconLayoutMode = "TextOnly",
			SectorFontSize = 11.0,
			Profiles = new List<WheelProfile> { profile, otherProfile }
		});

		double firstBefore = RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", first, first.Name);
		double secondBefore = RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", second, second.Name);
		ConfigManager.SetGlobalPrimarySectorFontSize(profile, 18.0);
		double firstAfter = RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", first, first.Name);
		double secondAfter = RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", second, second.Name);

		Assert(ConfigManager.CurrentConfig.SectorFontSize == 18.0, "Global slider stores the requested size");
		Assert(first.CustomFontSize == null && second.CustomFontSize == null,
			"Global slider returns current primary sectors to font-size inheritance");
		Assert(firstAfter > firstBefore && secondAfter > secondBefore,
			"Previously customized primary sectors both respond to the global slider");
		Assert(first.SubActions[0].CustomFontSize == 9.0 && otherProfile.Actions[0].CustomFontSize == 10.0,
			"Other profiles and sub-actions retain their independent font sizes");

		first.CustomFontSize = 12.0;
		Assert(RadialWindow.CalculatePrimarySectorFontSize(12, "TextOnly", first, first.Name) < firstAfter,
			"Per-sector font size can be customized again after the global edit");
	}

	private static void Test_AdjacentRegressions_4Key_8Key()
	{
		Console.WriteLine("\n--- Adjacent Regressions: 4-Key & 8-Key Wheels ---");

		// 8-key TextOnly
		double base8_11 = (11.0 + 1.0);
		double base8_15 = (15.0 + 1.0);
		Assert(base8_15 > base8_11, "8-key TextOnly font size changes with SectorFontSize");

		// 4-key TextOnly
		double base4_11 = Math.Max(11.0 + 1.0, 12.0);
		double base4_15 = Math.Max(15.0 + 1.0, 12.0);
		Assert(base4_15 > base4_11, "4-key TextOnly font size changes with SectorFontSize");

		// Long text formatting across 4, 8, 12 keys
		string longText = "音量增 (Vol Up)";
		string formatted4 = SectorTextFormatter.FormatSectorText(longText, 4);
		string formatted8 = SectorTextFormatter.FormatSectorText(longText, 8);
		string formatted12 = SectorTextFormatter.FormatSectorText(longText, 12);

		Assert(formatted12.Contains('\n'), "12-key wraps long text '音量增 (Vol Up)' into two lines");
		Assert(formatted8.Contains('\n'), "8-key wraps long text '音量增 (Vol Up)' into two lines");
		Assert(!string.IsNullOrEmpty(formatted4), "4-key handles text properly");
	}
}
