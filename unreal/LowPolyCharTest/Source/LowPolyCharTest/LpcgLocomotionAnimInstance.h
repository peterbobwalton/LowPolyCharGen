#pragma once

#include "CoreMinimal.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "LpcgLocomotionAnimInstance.generated.h"

class UAnimSequence;

/** Idle, walk and run loops for one stance, and the ground speeds the walk and run clips were made for. */
USTRUCT(BlueprintType)
struct FLpcgLocomotionSet
{
	GENERATED_BODY()

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	TObjectPtr<UAnimSequence> Idle = nullptr;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	TObjectPtr<UAnimSequence> Walk = nullptr;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	TObjectPtr<UAnimSequence> Run = nullptr;

	/** Ground speed (cm/s) at which the walk clip is fully weighted. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	float WalkSpeed = 170.f;

	/** Ground speed (cm/s) at which the run clip is fully weighted. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	float RunSpeed = 420.f;

	bool IsValid() const { return Idle != nullptr; }
};

/**
 * Native locomotion: blends idle -> walk -> run by ground speed, and standing -> crouched, entirely in
 * C++ (the pose is evaluated in the proxy, no Animation Blueprint or anim graph). Speed and stance come
 * from the owning pawn's movement, or from SpeedOverride / bCrouchOverride for display actors.
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

	/** When >= 0, used instead of the pawn's speed (for characters that are not moving, e.g. a lineup). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	float SpeedOverride = -1.f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	bool bCrouchOverride = false;

	/** Seconds to blend between standing and crouched. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Locomotion")
	float CrouchBlendTime = 0.2f;

	/** Current values, read by the proxy each update. */
	UPROPERTY(BlueprintReadOnly, Category = "Locomotion")
	float Speed = 0.f;

	UPROPERTY(BlueprintReadOnly, Category = "Locomotion")
	float CrouchAlpha = 0.f;

	/** Loads the pack's infantry idle/walk/run and crouch loops into any set that is still empty. */
	void UseDefaultInfantrySets();

protected:
	virtual void NativeInitializeAnimation() override;
	virtual void NativeUpdateAnimation(float DeltaSeconds) override;
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
	/** Writes the speed-blended pose of one stance into Output. */
	void EvaluateSet(const FLpcgLocomotionSet& Set, FPoseContext& Output) const;
	static void Sample(const UAnimSequence* Sequence, double Time, FPoseContext& Output);

	FLpcgLocomotionSet Standing;
	FLpcgLocomotionSet Crouching;
	float Speed = 0.f;
	float CrouchAlpha = 0.f;
	double IdleTime = 0.0;
	/** Gait cycle position, 0..1, shared by the walk and run clips so the feet stay in step while blending. */
	float Phase = 0.f;
};
