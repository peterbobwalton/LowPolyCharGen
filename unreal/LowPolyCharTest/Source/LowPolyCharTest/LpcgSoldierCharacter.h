#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "LpcgSoldierCharacter.generated.h"

class UCameraComponent;
class USpringArmComponent;
class UStaticMeshComponent;
class UInputAction;
class UInputMappingContext;
struct FInputActionValue;

/**
 * Playable third-person toon soldier: a LowPolyCharGen character on the Toon Soldiers skeleton, animated
 * by the native ULpcgLocomotionAnimInstance (no Animation Blueprint), with a weapon in the pack's
 * WeaponContainer socket. WASD/mouse/space come from the template's input assets; Shift sprints and
 * C or Ctrl crouches (those two actions are made in code).
 */
UCLASS()
class LOWPOLYCHARTEST_API ALpcgSoldierCharacter : public ACharacter
{
	GENERATED_BODY()

public:
	ALpcgSoldierCharacter();

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Soldier")
	TObjectPtr<USpringArmComponent> CameraBoom;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Soldier")
	TObjectPtr<UCameraComponent> FollowCamera;

	UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Soldier")
	TObjectPtr<UStaticMeshComponent> Weapon;

	/** Socket (a pack bone) the weapon is attached to. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier")
	FName WeaponSocket = TEXT("WeaponContainer");

	/**
	 * Size of the character relative to the pack's (the pack's animations would undo a scale baked into the
	 * mesh, so it is applied to the mesh component). The weapon is counter-scaled to keep its true size.
	 */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier", meta = (ClampMin = "0.5", ClampMax = "2.0"))
	float CharacterScale = 1.2f;

	/** World speeds; the locomotion blend divides by CharacterScale, so strides stay in step. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier")
	float WalkSpeed = 265.f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier")
	float SprintSpeed = 550.f;

	UPROPERTY(EditAnywhere, Category = "Input")
	TObjectPtr<UInputMappingContext> DefaultMappingContext;

	UPROPERTY(EditAnywhere, Category = "Input")
	TObjectPtr<UInputMappingContext> MouseLookMappingContext;

	UPROPERTY(EditAnywhere, Category = "Input")
	TObjectPtr<UInputAction> MoveAction;

	UPROPERTY(EditAnywhere, Category = "Input")
	TObjectPtr<UInputAction> LookAction;

	UPROPERTY(EditAnywhere, Category = "Input")
	TObjectPtr<UInputAction> MouseLookAction;

	UPROPERTY(EditAnywhere, Category = "Input")
	TObjectPtr<UInputAction> JumpAction;

protected:
	virtual void BeginPlay() override;
	virtual void OnConstruction(const FTransform& Transform) override;
	virtual void SetupPlayerInputComponent(UInputComponent* PlayerInputComponent) override;
	virtual void NotifyControllerChanged() override;

private:
	void Move(const FInputActionValue& Value);
	void Look(const FInputActionValue& Value);
	void StartSprint();
	void StopSprint();
	void ToggleCrouch();
	void ApplyCharacterScale();

	/** Sprint and crouch, which the template has no assets for (built in the constructor). */
	UPROPERTY()
	TObjectPtr<UInputMappingContext> CodeMappingContext;

	UPROPERTY()
	TObjectPtr<UInputAction> SprintAction;

	UPROPERTY()
	TObjectPtr<UInputAction> CrouchAction;
};
