#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
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

	/** Ground speed fed to the locomotion blend when Animation is empty (0 idle, ~170 walk, ~420 run). */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	float LocomotionSpeed = 0.f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	bool bCrouched = false;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	TObjectPtr<UStaticMesh> WeaponMesh;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Display")
	FName WeaponSocket = TEXT("WeaponContainer");

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
