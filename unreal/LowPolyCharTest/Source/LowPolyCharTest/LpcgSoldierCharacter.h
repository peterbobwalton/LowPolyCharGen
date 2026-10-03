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

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier")
	float WalkSpeed = 220.f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier")
	float SprintSpeed = 460.f;

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
	virtual void SetupPlayerInputComponent(UInputComponent* PlayerInputComponent) override;
	virtual void NotifyControllerChanged() override;

private:
	void Move(const FInputActionValue& Value);
	void Look(const FInputActionValue& Value);
	void StartSprint();
	void StopSprint();
	void ToggleCrouch();

	/** Sprint and crouch, which the template has no assets for (built in the constructor). */
	UPROPERTY()
	TObjectPtr<UInputMappingContext> CodeMappingContext;

	UPROPERTY()
	TObjectPtr<UInputAction> SprintAction;

	UPROPERTY()
	TObjectPtr<UInputAction> CrouchAction;
};
