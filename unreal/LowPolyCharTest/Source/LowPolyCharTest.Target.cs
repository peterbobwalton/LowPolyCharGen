using UnrealBuildTool;

public class LowPolyCharTestTarget : TargetRules
{
	public LowPolyCharTestTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.V7;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_8;
		ExtraModuleNames.Add("LowPolyCharTest");
	}
}
