#include "LpcgLocomotionAnimInstance.h"

#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "AnimationRuntime.h"
#include "GameFramework/Character.h"
#include "GameFramework/Pawn.h"
#include "LpcgDisplaySoldier.h"
#include "LpcgSoldierCharacter.h"
#include "UObject/ConstructorHelpers.h"

namespace
{
	/** Below this fraction of the slowest gait the clip slows down no further; its weight fades out instead. */
	constexpr float MinPlayRate = 0.5f;
	/** Above the fastest gait the clip speeds up, but no further than this. */
	constexpr float MaxPlayRate = 1.4f;

	UAnimSequence* FindClip(const TCHAR* Path)
	{
		ConstructorHelpers::FObjectFinder<UAnimSequence> Finder(Path);
		return Finder.Object;
	}

	/** Drops gaits without a clip or speed and sorts the rest slowest first (the blend relies on both). */
	void Sanitize(FLpcgLocomotionSet& Set)
	{
		Set.Gaits.RemoveAll([](const FLpcgGait& G) { return G.Clip == nullptr || G.Speed <= 0.f; });
		Set.Gaits.StableSort([](const FLpcgGait& A, const FLpcgGait& B) { return A.Speed < B.Speed; });
	}
}

void ULpcgLocomotionAnimInstance::FindDefaultInfantrySets(FLpcgLocomotionSet& OutStanding, FLpcgLocomotionSet& OutCrouching)
{
	// Speeds are how fast each in-place clip's planted foot travels backwards, measured in the editor
	// (the pack's Root_Motion versions under-travel and slide by the difference: run 302 vs 369).
	// Looked up per call (constructors only), not cached: a cached pointer would outlive a re-imported clip.
	UAnimSequence* const Idle = FindClip(TEXT("/Game/Toon_Soldiers_Armies/Animations/Infantry/infantry_combat_idle.infantry_combat_idle"));
	UAnimSequence* const Walk = FindClip(TEXT("/Game/Toon_Soldiers_Armies/Animations/Infantry/Movement/infantry_combat_walk.infantry_combat_walk"));
	UAnimSequence* const Run = FindClip(TEXT("/Game/Toon_Soldiers_Armies/Animations/Infantry/Movement/infantry_combat_run.infantry_combat_run"));
	UAnimSequence* const Sprint = FindClip(TEXT("/Game/Toon_Soldiers_Armies/Animations/Infantry/Movement/infantry_sprint.infantry_sprint"));
	UAnimSequence* const CrouchIdle = FindClip(TEXT("/Game/Toon_Soldiers_Armies/Animations/Infantry/Crouch/infantry_crouch_idle.infantry_crouch_idle"));
	UAnimSequence* const CrouchWalk = FindClip(TEXT("/Game/Toon_Soldiers_Armies/Animations/Infantry/Crouch/infantry_crouch_walk.infantry_crouch_walk"));

	OutStanding.Idle = Idle;
	OutStanding.Gaits = { { Walk, 111.5f }, { Run, 369.f }, { Sprint, 478.f } };
	OutCrouching.Idle = CrouchIdle;
	OutCrouching.Gaits = { { CrouchWalk, 107.f } };
	Sanitize(OutStanding);
	Sanitize(OutCrouching);
}

void ULpcgLocomotionAnimInstance::SetSets(const FLpcgLocomotionSet& InStanding, const FLpcgLocomotionSet& InCrouching)
{
	Standing = InStanding;
	Crouching = InCrouching;
	Sanitize(Standing);
	Sanitize(Crouching);
	++SetsVersion;
}

void ULpcgLocomotionAnimInstance::NativeInitializeAnimation()
{
	Super::NativeInitializeAnimation();

	// Re-read from the owner whenever the instance is (re)created, so nothing set on it earlier is lost.
	if (const ALpcgSoldierCharacter* Soldier = Cast<ALpcgSoldierCharacter>(GetOwningActor()))
	{
		SetSets(Soldier->Standing, Soldier->Crouching);
	}
	else if (const ALpcgDisplaySoldier* Display = Cast<ALpcgDisplaySoldier>(GetOwningActor()))
	{
		SetSets(Display->Standing, Display->Crouching);
		SpeedOverride = Display->LocomotionSpeed;
		bCrouchOverride = Display->bCrouched;
	}
}

void ULpcgLocomotionAnimInstance::UpdateLocomotionState(float DeltaSeconds)
{
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
	// Game thread: refresh this frame's speed and stance before copying (NativeUpdateAnimation runs after PreUpdate).
	ULpcgLocomotionAnimInstance* Instance = CastChecked<ULpcgLocomotionAnimInstance>(InAnimInstance);
	Instance->UpdateLocomotionState(DeltaSeconds);
	if (SetsVersion != Instance->SetsVersion)
	{
		// Only when they change: copying the arrays every frame would allocate for every soldier on screen.
		Standing = Instance->Standing;
		Crouching = Instance->Crouching;
		SetsVersion = Instance->SetsVersion;
	}
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
		Blend.Cycle = Blend.B->GetPlayLength();
		return Blend;
	}

	// Between two gaits: cross-fade, with one cycle taking as long as the blended stride (each clip's
	// speed x length) needs at this speed, so the blended foot moves exactly with the ground.
	Blend.bAIsIdle = false;
	for (int32 i = 0; i + 1 < Gaits.Num(); ++i)
	{
		if (Speed < Gaits[i + 1].Speed)
		{
			const FLpcgGait& Lo = Gaits[i];
			const FLpcgGait& Hi = Gaits[i + 1];
			Blend.A = Lo.Clip;
			Blend.B = Hi.Clip;
			Blend.Alpha = (Speed - Lo.Speed) / (Hi.Speed - Lo.Speed);
			const float Stride = FMath::Lerp(Lo.Speed * Lo.Clip->GetPlayLength(), Hi.Speed * Hi.Clip->GetPlayLength(), Blend.Alpha);
			Blend.Cycle = Stride / Speed;   // Speed >= the slowest gait's here, so > 0
			return Blend;
		}
	}

	// Faster than the fastest gait: speed its clip up.
	Blend.A = Gaits.Last().Clip;
	Blend.PlayRate = FMath::Min(Speed / Gaits.Last().Speed, MaxPlayRate);
	Blend.Cycle = Blend.A->GetPlayLength();
	return Blend;
}

void FLpcgLocomotionProxy::Update(float DeltaSeconds)
{
	IdleTime += DeltaSeconds;

	// Advance the shared gait cycle by the dominant stance's clips.
	const FGaitBlend Blend = Resolve(CrouchAlpha > 0.5f ? Crouching : Standing);
	if (Blend.Cycle > UE_SMALL_NUMBER)
	{
		Phase = FMath::Fmod(Phase + DeltaSeconds * Blend.PlayRate / Blend.Cycle, 1.f);
	}
}

void FLpcgLocomotionProxy::Sample(const UAnimSequence* Sequence, double Time, FPoseContext& Output)
{
	if (!Sequence)
	{
		Output.ResetToRefPose();
		return;
	}
	const double Length = Sequence->GetPlayLength();
	FAnimationPoseData PoseData(Output);
	Sequence->GetAnimationPose(PoseData, FAnimExtractContext(Length > 0.0 ? FMath::Fmod(Time, Length) : 0.0, false, {}, true));
}

void FLpcgLocomotionProxy::EvaluateSet(const FLpcgLocomotionSet& Set, FPoseContext& Output) const
{
	const FGaitBlend Blend = Resolve(Set);
	auto TimeOf = [this](const UAnimSequence* Clip, bool bIdle) { return bIdle || !Clip ? IdleTime : double(Phase) * Clip->GetPlayLength(); };

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
