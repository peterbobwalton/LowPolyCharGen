#pragma once

#include "CoreMinimal.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "LpcgLocomotionAnimInstance.generated.h"

class UAnimSequence;

/** One looping gait clip and the ground speed (cm/s, unscaled character) its feet were authored for. */
USTRUCT(BlueprintType)
struct FLpcgGait
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	TObjectPtr<UAnimSequence> Clip = nullptr;

	/** How fast the clip's planted foot moves over the ground. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	float Speed = 0.f;
};

/** Idle plus gaits in increasing speed (walk, run, sprint ...) for one stance. */
USTRUCT(BlueprintType)
struct FLpcgLocomotionSet
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	TObjectPtr<UAnimSequence> Idle = nullptr;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	TArray<FLpcgGait> Gaits;

	bool IsValid() const { return Idle != nullptr; }
	float SlowestGait() const { return Gaits.Num() ? Gaits[0].Speed : 0.f; }
	float FastestGait() const { return Gaits.Num() ? Gaits.Last().Speed : 0.f; }
};

/**
 * Native locomotion: blends idle -> walk -> run -> sprint by ground speed, and standing -> crouched,
 * entirely in C++ (the pose is evaluated in the proxy, no Animation Blueprint or anim graph).
 * Playback rate follows the speed so the planted foot moves exactly with the ground (no sliding).
 * Speed and stance come from the owning pawn, or from SpeedOverride / bCrouchOverride for display actors.
 */
UCLASS(Transient, NotBlueprintable)
class LOWPOLYCHARTEST_API ULpcgLocomotionAnimInstance : public UAnimInstance
{
	GENERATED_BODY()

public:
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	FLpcgLocomotionSet Standing;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	FLpcgLocomotionSet Crouching;

	/** When >= 0, used instead of the pawn's speed (unscaled cm/s; for characters that are not moving). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	float SpeedOverride = -1.f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	bool bCrouchOverride = false;

	/** Seconds to blend between standing and crouched. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	float CrouchBlendTime = 0.2f;

	/** Ground speed in the clips' space (world speed divided by the mesh scale). */
	UPROPERTY(BlueprintReadOnly, Category = "Locomotion")
	float Speed = 0.f;

	UPROPERTY(BlueprintReadOnly, Category = "Locomotion")
	float CrouchAlpha = 0.f;

	/**
	 * Fills the sets with the pack's infantry idle/walk/run/sprint and crouch loops. Call it from an actor's
	 * constructor: the clips are found with ConstructorHelpers, so the owner's CDO references them and
	 * they get cooked (a runtime LoadObject by path would leave them out of packaged builds).
	 */
	static void FindDefaultInfantrySets(FLpcgLocomotionSet& OutStanding, FLpcgLocomotionSet& OutCrouching);

	/** Reads the pawn's ground speed and stance (game thread; the proxy calls it before each update). */
	void UpdateLocomotionState(float DeltaSeconds);

protected:
	/** Takes the sets (and, for display actors, the fixed speed and stance) from the owning actor. */
	virtual void NativeInitializeAnimation() override;
	virtual FAnimInstanceProxy* CreateAnimInstanceProxy() override;
	virtual void DestroyAnimInstanceProxy(FAnimInstanceProxy* InProxy) override;
};

/** Evaluates the locomotion blend on the animation worker thread. */
struct FLpcgLocomotionProxy : public FAnimInstanceProxy
{
	FLpcgLocomotionProxy() = default;
	explicit FLpcgLocomotionProxy(UAnimInstance* Instance) : FAnimInstanceProxy(Instance) {}

	virtual void PreUpdate(UAnimInstance* InAnimInstance, float DeltaSeconds) override;
	virtual void Update(float DeltaSeconds) override;
	virtual bool Evaluate(FPoseContext& Output) override;

private:
	/** Where a speed falls in a set: blend from clip A to clip B by Alpha, at PlayRate. A may be the idle. */
	struct FGaitBlend
	{
		const UAnimSequence* A = nullptr;
		const UAnimSequence* B = nullptr;
		float Alpha = 0.f;
		float PlayRate = 1.f;
		bool bAIsIdle = true;
	};
	FGaitBlend Resolve(const FLpcgLocomotionSet& Set) const;
	void EvaluateSet(const FLpcgLocomotionSet& Set, FPoseContext& Output) const;
	static void Sample(const UAnimSequence* Sequence, double Time, FPoseContext& Output);

	FLpcgLocomotionSet Standing;
	FLpcgLocomotionSet Crouching;
	float Speed = 0.f;
	float CrouchAlpha = 0.f;
	double IdleTime = 0.0;
	/** Gait cycle position, 0..1, shared by all gait clips so the feet stay in step while blending. */
	float Phase = 0.f;
};
