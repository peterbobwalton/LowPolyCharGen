#include "LpcgLocomotionAnimInstance.h"

#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "AnimationRuntime.h"
#include "GameFramework/Character.h"
#include "GameFramework/Pawn.h"

namespace
{
	const TCHAR* const AnimRoot = TEXT("/Game/Toon_Soldiers_Armies/Animations/Infantry/");

	UAnimSequence* LoadClip(const FString& RelativePath)
	{
		const FString Name = FPaths::GetBaseFilename(RelativePath);
		return LoadObject<UAnimSequence>(nullptr, *FString::Printf(TEXT("%s%s.%s"), AnimRoot, *RelativePath, *Name));
	}

	/** Below this fraction of the slowest gait the clip slows down no further; its weight fades out instead. */
	constexpr float MinPlayRate = 0.5f;
	/** Above the fastest gait the clip speeds up, but no further than this. */
	constexpr float MaxPlayRate = 1.4f;
}

void ULpcgLocomotionAnimInstance::UseDefaultInfantrySets()
{
	// Speeds are how fast each in-place clip's planted foot travels backwards, measured in the editor
	// (the pack's Root_Motion versions under-travel and slide by the difference: run 302 vs 369).
	if (!Standing.IsValid())
	{
		Standing.Idle = LoadClip(TEXT("infantry_combat_idle"));
		Standing.Gaits = {
			{ LoadClip(TEXT("Movement/infantry_combat_walk")), 111.5f },
			{ LoadClip(TEXT("Movement/infantry_combat_run")), 369.f },
			{ LoadClip(TEXT("Movement/infantry_sprint")), 478.f },
		};
	}
	if (!Crouching.IsValid())
	{
		Crouching.Idle = LoadClip(TEXT("Crouch/infantry_crouch_idle"));
		Crouching.Gaits = { { LoadClip(TEXT("Crouch/infantry_crouch_walk")), 107.f } };
	}
	Standing.Gaits.RemoveAll([](const FLpcgGait& G) { return G.Clip == nullptr || G.Speed <= 0.f; });
	Crouching.Gaits.RemoveAll([](const FLpcgGait& G) { return G.Clip == nullptr || G.Speed <= 0.f; });
}

void ULpcgLocomotionAnimInstance::NativeInitializeAnimation()
{
	Super::NativeInitializeAnimation();
	UseDefaultInfantrySets();
}

void ULpcgLocomotionAnimInstance::NativeUpdateAnimation(float DeltaSeconds)
{
	Super::NativeUpdateAnimation(DeltaSeconds);

	bool bCrouched = bCrouchOverride;
	if (SpeedOverride >= 0.f)
	{
		Speed = SpeedOverride;
	}
	else if (const APawn* Pawn = TryGetPawnOwner())
	{
		// The clips' speeds are for an unscaled character; a bigger one covers more ground per stride.
		const float Scale = FMath::Max(GetSkelMeshComponent()->GetComponentScale().Z, UE_KINDA_SMALL_NUMBER);
		Speed = Pawn->GetVelocity().Size2D() / Scale;
		if (const ACharacter* Character = Cast<ACharacter>(Pawn))
		{
			bCrouched |= Character->bIsCrouched;
		}
	}
	else
	{
		Speed = 0.f;
	}

	const float Target = bCrouched ? 1.f : 0.f;
	const float Step = CrouchBlendTime > 0.f ? DeltaSeconds / CrouchBlendTime : 1.f;
	CrouchAlpha = FMath::Clamp(CrouchAlpha + FMath::Clamp(Target - CrouchAlpha, -Step, Step), 0.f, 1.f);
}

FAnimInstanceProxy* ULpcgLocomotionAnimInstance::CreateAnimInstanceProxy()
{
	return new FLpcgLocomotionProxy(this);
}

void ULpcgLocomotionAnimInstance::DestroyAnimInstanceProxy(FAnimInstanceProxy* InProxy)
{
	delete InProxy;
}

// ---- proxy ------------------------------------------------------------------------------------

void FLpcgLocomotionProxy::PreUpdate(UAnimInstance* InAnimInstance, float DeltaSeconds)
{
	FAnimInstanceProxy::PreUpdate(InAnimInstance, DeltaSeconds);
	const ULpcgLocomotionAnimInstance* Instance = CastChecked<ULpcgLocomotionAnimInstance>(InAnimInstance);
	Standing = Instance->Standing;
	Crouching = Instance->Crouching;
	Speed = Instance->Speed;
	CrouchAlpha = Instance->CrouchAlpha;
}

FLpcgLocomotionProxy::FGaitBlend FLpcgLocomotionProxy::Resolve(const FLpcgLocomotionSet& Set) const
{
	FGaitBlend Blend;
	Blend.A = Set.Idle;
	const TArray<FLpcgGait>& Gaits = Set.Gaits;
	if (Gaits.IsEmpty())
	{
		return Blend;
	}

	// Slower than the slowest gait: slow its clip down to half speed, then fade it into the idle.
	if (Speed < Gaits[0].Speed)
	{
		Blend.B = Gaits[0].Clip;
		Blend.PlayRate = FMath::Max(Speed / Gaits[0].Speed, MinPlayRate);
		Blend.Alpha = FMath::Clamp(Speed / (Gaits[0].Speed * MinPlayRate), 0.f, 1.f);
		return Blend;
	}

	// Between two gaits: cross-fade at normal rate; the blended stride covers the blended speed.
	Blend.bAIsIdle = false;
	for (int32 i = 0; i + 1 < Gaits.Num(); ++i)
	{
		if (Speed < Gaits[i + 1].Speed)
		{
			Blend.A = Gaits[i].Clip;
			Blend.B = Gaits[i + 1].Clip;
			Blend.Alpha = (Speed - Gaits[i].Speed) / (Gaits[i + 1].Speed - Gaits[i].Speed);
			return Blend;
		}
	}

	// Faster than the fastest gait: speed its clip up.
	Blend.A = Gaits.Last().Clip;
	Blend.PlayRate = FMath::Min(Speed / Gaits.Last().Speed, MaxPlayRate);
	return Blend;
}

void FLpcgLocomotionProxy::Update(float DeltaSeconds)
{
	IdleTime += DeltaSeconds;

	// Advance the shared gait cycle by the dominant stance's clips.
	const FGaitBlend Blend = Resolve(CrouchAlpha > 0.5f ? Crouching : Standing);
	const UAnimSequence* From = Blend.bAIsIdle ? Blend.B : Blend.A;
	const UAnimSequence* To = Blend.B ? Blend.B : Blend.A;
	if (From && To)
	{
		const float Cycle = FMath::Lerp(From->GetPlayLength(), To->GetPlayLength(), Blend.bAIsIdle ? 1.f : Blend.Alpha);
		if (Cycle > UE_SMALL_NUMBER)
		{
			Phase = FMath::Fmod(Phase + DeltaSeconds * Blend.PlayRate / Cycle, 1.f);
		}
	}
}

void FLpcgLocomotionProxy::Sample(const UAnimSequence* Sequence, double Time, FPoseContext& Output)
{
	const double Length = Sequence->GetPlayLength();
	FAnimationPoseData PoseData(Output);
	Sequence->GetAnimationPose(PoseData, FAnimExtractContext(Length > 0.0 ? FMath::Fmod(Time, Length) : 0.0, false, {}, true));
}

void FLpcgLocomotionProxy::EvaluateSet(const FLpcgLocomotionSet& Set, FPoseContext& Output) const
{
	const FGaitBlend Blend = Resolve(Set);
	auto TimeOf = [this](const UAnimSequence* Clip, bool bIdle) { return bIdle ? IdleTime : double(Phase) * Clip->GetPlayLength(); };

	if (!Blend.B || Blend.Alpha <= KINDA_SMALL_NUMBER)
	{
		Sample(Blend.A, TimeOf(Blend.A, Blend.bAIsIdle), Output);
		return;
	}
	if (Blend.Alpha >= 1.f - KINDA_SMALL_NUMBER)
	{
		Sample(Blend.B, TimeOf(Blend.B, false), Output);
		return;
	}

	// Sample A straight into the output and blend B over it: one temporary pose instead of two.
	Sample(Blend.A, TimeOf(Blend.A, Blend.bAIsIdle), Output);
	FPoseContext PoseB(Output);
	Sample(Blend.B, TimeOf(Blend.B, false), PoseB);
	FAnimationPoseData Out(Output);
	FAnimationRuntime::BlendTwoPosesTogetherInPlace(Out, FAnimationPoseData(PoseB), 1.f - Blend.Alpha);
}

bool FLpcgLocomotionProxy::Evaluate(FPoseContext& Output)
{
	if (!Standing.IsValid())
	{
		Output.ResetToRefPose();
		return true;
	}

	if (CrouchAlpha <= KINDA_SMALL_NUMBER || !Crouching.IsValid())
	{
		EvaluateSet(Standing, Output);
		return true;
	}
	if (CrouchAlpha >= 1.f - KINDA_SMALL_NUMBER)
	{
		EvaluateSet(Crouching, Output);
		return true;
	}

	EvaluateSet(Standing, Output);
	FPoseContext Crouch(Output);
	EvaluateSet(Crouching, Crouch);
	FAnimationPoseData Out(Output);
	FAnimationRuntime::BlendTwoPosesTogetherInPlace(Out, FAnimationPoseData(Crouch), 1.f - CrouchAlpha);
	return true;
}
