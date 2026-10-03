#include "LpcgLocomotionAnimInstance.h"

#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "AnimationRuntime.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/Pawn.h"

namespace
{
	const TCHAR* const AnimRoot = TEXT("/Game/Toon_Soldiers_Armies/Animations/Infantry/");

	UAnimSequence* LoadClip(const FString& RelativePath)
	{
		const FString Name = FPaths::GetBaseFilename(RelativePath);
		return LoadObject<UAnimSequence>(nullptr, *FString::Printf(TEXT("%s%s.%s"), AnimRoot, *RelativePath, *Name));
	}
}

void ULpcgLocomotionAnimInstance::UseDefaultInfantrySets()
{
	if (!Standing.IsValid())
	{
		Standing.Idle = LoadClip(TEXT("infantry_combat_idle"));
		Standing.Walk = LoadClip(TEXT("Movement/infantry_combat_walk"));
		Standing.Run = LoadClip(TEXT("Movement/infantry_combat_run"));
	}
	if (!Crouching.IsValid())
	{
		Crouching.Idle = LoadClip(TEXT("Crouch/infantry_crouch_idle"));
		Crouching.Walk = LoadClip(TEXT("Crouch/infantry_crouch_walk"));
		Crouching.Run = Crouching.Walk;
		Crouching.WalkSpeed = 120.f;
		Crouching.RunSpeed = 240.f;
	}
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

void FLpcgLocomotionProxy::Update(float DeltaSeconds)
{
	IdleTime += DeltaSeconds;

	// Advance the gait cycle at the rate of whichever clip dominates at this speed.
	const FLpcgLocomotionSet& Set = CrouchAlpha > 0.5f ? Crouching : Standing;
	if (Set.Walk && Set.Run)
	{
		const float RunWeight = FMath::Clamp((Speed - Set.WalkSpeed) / FMath::Max(Set.RunSpeed - Set.WalkSpeed, 1.f), 0.f, 1.f);
		const float Cycle = FMath::Lerp(Set.Walk->GetPlayLength(), Set.Run->GetPlayLength(), RunWeight);
		if (Cycle > UE_SMALL_NUMBER)
		{
			Phase = FMath::Fmod(Phase + DeltaSeconds / Cycle, 1.f);
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
	// Pick the two neighbouring clips for this speed and how far we are between them.
	const UAnimSequence* A = Set.Idle;
	const UAnimSequence* B = Set.Walk;
	float Alpha = Set.WalkSpeed > 0.f ? Speed / Set.WalkSpeed : 0.f;
	bool bBothGait = false;
	if (Alpha >= 1.f && Set.Run)
	{
		A = Set.Walk;
		B = Set.Run;
		Alpha = (Speed - Set.WalkSpeed) / FMath::Max(Set.RunSpeed - Set.WalkSpeed, 1.f);
		bBothGait = true;
	}
	Alpha = FMath::Clamp(Alpha, 0.f, 1.f);
	if (!B) Alpha = 0.f;

	auto TimeFor = [&](const UAnimSequence* Clip, bool bGait) { return bGait ? double(Phase) * Clip->GetPlayLength() : IdleTime; };

	if (Alpha <= KINDA_SMALL_NUMBER)
	{
		Sample(A, TimeFor(A, bBothGait), Output);
		return;
	}
	if (Alpha >= 1.f - KINDA_SMALL_NUMBER)
	{
		Sample(B, TimeFor(B, true), Output);
		return;
	}

	// Sample A straight into the output and blend B over it: one temporary pose instead of two.
	Sample(A, TimeFor(A, bBothGait), Output);
	FPoseContext PoseB(Output);
	Sample(B, TimeFor(B, true), PoseB);
	FAnimationPoseData Out(Output);
	FAnimationRuntime::BlendTwoPosesTogetherInPlace(Out, FAnimationPoseData(PoseB), 1.f - Alpha);
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
