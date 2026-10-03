using UnrealBuildTool;

public class LowPolyCharTestEditorTarget : TargetRules
{
	public LowPolyCharTestEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.V7;
		IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_8;
		ExtraModuleNames.Add("LowPolyCharTest");
	}
}
