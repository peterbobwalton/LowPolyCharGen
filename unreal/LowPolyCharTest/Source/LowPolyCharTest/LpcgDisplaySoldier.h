#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "LpcgLocomotionAnimInstance.h"
#include "LpcgDisplaySoldier.generated.h"

class UAnimSequence;
class USkeletalMesh;
class UStaticMesh;
class USkeletalMeshComponent;
class UStaticMeshComponent;
class UTextRenderComponent;

/**
 * A character standing in a lineup: a skeletal mesh looping one animation (or the native locomotion
 * blend at a fixed speed), a weapon in the pack's WeaponContainer socket and a name label. Set up in
 * C++ in OnConstruction, so it animates in the editor viewport as well as in play.
 */
UCLASS()
class LOWPOLYCHARTEST_API ALpcgDisplaySoldier : public AActor
{
	GENERATED_BODY()

public:
	ALpcgDisplaySoldier();

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	TObjectPtr<USkeletalMesh> Mesh;

	/** Loop this clip. Leave empty to use the locomotion blend at LocomotionSpeed instead. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	TObjectPtr<UAnimSequence> Animation;

	/** Ground speed fed to the locomotion blend when Animation is empty (0 idle, 111 walk, 369 run, 478 sprint). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	float LocomotionSpeed = 0.f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	bool bCrouched = false;

	/** Locomotion clips for the native blend (defaults: the pack's infantry set). */
	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Display")
	FLpcgLocomotionSet Standing;

	UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Display")
	FLpcgLocomotionSet Crouching;

	/** Size relative to the pack's characters; the weapon scales with it. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display", meta = (ClampMin = "0.5", ClampMax = "2.0"))
	float CharacterScale = 1.0f;

	/** Grime layer amount (0 clean .. 1 filthy); below 0 keeps the material instance's own value. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display", meta = (ClampMin = "-1.0", ClampMax = "1.0"))
	float Grime = -1.f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	TObjectPtr<UStaticMesh> WeaponMesh;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	FName WeaponSocket = TEXT("WeaponSocket_R");

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	FText Label;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Display")
	TObjectPtr<USkeletalMeshComponent> Body;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Display")
	TObjectPtr<UStaticMeshComponent> Weapon;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Display")
	TObjectPtr<UTextRenderComponent> NameText;

	/** Applies the properties to the components (called from OnConstruction and BeginPlay). */
	UFUNCTION(BlueprintCallable, Category = "Display")
	void Refresh();

protected:
	virtual void OnConstruction(const FTransform& Transform) override;
	virtual void BeginPlay() override;
};
