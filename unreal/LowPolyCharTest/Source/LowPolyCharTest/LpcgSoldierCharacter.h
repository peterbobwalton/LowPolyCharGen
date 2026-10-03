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

	/**
	 * The packs' weapon socket on the right hand. It carries the 0.6 scale that, with the Biped's 1.8 bone
	 * scale, brings the pack's guns to their intended size (the bare WeaponContainer bone would make them 1.8x).
	 */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier")
	FName WeaponSocket = TEXT("WeaponSocket_R");

	/**
	 * Size of the character relative to the pack's (the pack's animations would undo a scale baked into the
	 * mesh, so it is applied to the mesh component). The weapon scales with it, keeping the packs' proportions.
	 */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier", meta = (ClampMin = "0.5", ClampMax = "2.0"))
	float CharacterScale = 1.2f;

	/**
	 * Normal and sprint world speeds. 0 = take them from the clips (run and sprint speed x CharacterScale),
	 * which is what keeps the feet planted; the locomotion blend copes with any other value too.
	 */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier")
	float MoveSpeed = 0.f;

	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier")
	float SprintSpeed = 0.f;

	/** Grime layer amount (0 clean .. 1 filthy); below 0 keeps the material instance's own value. */
	UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Soldier", meta = (ClampMin = "-1.0", ClampMax = "1.0"))
	float Grime = -1.f;

	/** Blends the grime layer in or out at runtime (e.g. dirtier the longer the soldier is in the field). */
	UFUNCTION(BlueprintCallable, Category = "Soldier")
	void SetGrime(float Amount);

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
