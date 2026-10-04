#include "LpcgSoldierCharacter.h"

#include "Camera/CameraComponent.h"
#include "Components/CapsuleComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/StaticMeshComponent.h"
#include "EnhancedInputComponent.h"
#include "EnhancedInputSubsystems.h"
#include "Engine/LocalPlayer.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/StaticMesh.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/PlayerController.h"
#include "GameFramework/SpringArmComponent.h"
#include "InputAction.h"
#include "InputActionValue.h"
#include "InputMappingContext.h"
#include "LpcgLocomotionAnimInstance.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "UObject/ConstructorHelpers.h"

ALpcgSoldierCharacter::ALpcgSoldierCharacter()
{
	bUseControllerRotationPitch = false;
	bUseControllerRotationYaw = false;
	bUseControllerRotationRoll = false;

	UCharacterMovementComponent* Movement = GetCharacterMovement();
	Movement->bOrientRotationToMovement = true;
	Movement->RotationRate = FRotator(0.f, 540.f, 0.f);
	Movement->JumpZVelocity = 560.f;
	Movement->AirControl = 0.35f;
	Movement->GetNavAgentPropertiesRef().bCanCrouch = true;

	// The pack's characters face +Y with their feet at the origin.
	static ConstructorHelpers::FObjectFinder<USkeletalMesh> MeshAsset(TEXT("/Game/LowPolyCharGen/Characters/SK_LPCG_Desert_Rifleman.SK_LPCG_Desert_Rifleman"));
	if (MeshAsset.Succeeded())
	{
		GetMesh()->SetSkeletalMesh(MeshAsset.Object);
	}
	GetMesh()->SetRelativeRotation(FRotator(0.f, -90.f, 0.f));
	GetMesh()->SetAnimationMode(EAnimationMode::AnimationBlueprint);
	GetMesh()->SetAnimInstanceClass(ULpcgLocomotionAnimInstance::StaticClass());
	// Crowds: distant soldiers animate at a lower rate and off-screen ones don't evaluate a pose at all.
	GetMesh()->bEnableUpdateRateOptimizations = true;
	GetMesh()->VisibilityBasedAnimTickOption = EVisibilityBasedAnimTickOption::OnlyTickPoseWhenRendered;

	Weapon = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Weapon"));
	Weapon->SetupAttachment(GetMesh(), WeaponSocket);
	Weapon->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	static ConstructorHelpers::FObjectFinder<UStaticMesh> WeaponAsset(TEXT("/Game/Toon_Soldiers_Armies/Meshes/Weapons/SM_m4.SM_m4"));
	if (WeaponAsset.Succeeded())
	{
		Weapon->SetStaticMesh(WeaponAsset.Object);
	}

	CameraBoom = CreateDefaultSubobject<USpringArmComponent>(TEXT("CameraBoom"));
	CameraBoom->SetupAttachment(RootComponent);
	CameraBoom->TargetArmLength = 440.f;
	CameraBoom->SocketOffset = FVector(0.f, 45.f, 75.f);
	CameraBoom->bUsePawnControlRotation = true;

	FollowCamera = CreateDefaultSubobject<UCameraComponent>(TEXT("FollowCamera"));
	FollowCamera->SetupAttachment(CameraBoom, USpringArmComponent::SocketName);
	FollowCamera->bUsePawnControlRotation = false;

	// The Third Person template's input assets.
	static ConstructorHelpers::FObjectFinder<UInputMappingContext> Imc(TEXT("/Game/Input/IMC_Default.IMC_Default"));
	static ConstructorHelpers::FObjectFinder<UInputMappingContext> ImcMouse(TEXT("/Game/Input/IMC_MouseLook.IMC_MouseLook"));
	static ConstructorHelpers::FObjectFinder<UInputAction> IaMove(TEXT("/Game/Input/Actions/IA_Move.IA_Move"));
	static ConstructorHelpers::FObjectFinder<UInputAction> IaLook(TEXT("/Game/Input/Actions/IA_Look.IA_Look"));
	static ConstructorHelpers::FObjectFinder<UInputAction> IaMouseLook(TEXT("/Game/Input/Actions/IA_MouseLook.IA_MouseLook"));
	static ConstructorHelpers::FObjectFinder<UInputAction> IaJump(TEXT("/Game/Input/Actions/IA_Jump.IA_Jump"));
	DefaultMappingContext = Imc.Object;
	MouseLookMappingContext = ImcMouse.Object;
	MoveAction = IaMove.Object;
	LookAction = IaLook.Object;
	MouseLookAction = IaMouseLook.Object;
	JumpAction = IaJump.Object;

	ULpcgLocomotionAnimInstance::FindDefaultInfantrySets(Standing, Crouching);

	ApplyCharacterScale();
}

void ALpcgSoldierCharacter::ApplyCharacterScale()
{
	// The pack's characters are about 180 cm tall with their feet at the mesh origin. The scale goes on the
	// capsule (the root), not the mesh: crouching restores the class default's capsule and mesh offset, which
	// would lose a per-instance size, but it does respect the capsule's scale. The mesh inherits it, so the
	// anim instance still sees it in the mesh's world scale.
	GetCapsuleComponent()->SetCapsuleSize(38.f, 90.f);
	GetCapsuleComponent()->SetRelativeScale3D(FVector(CharacterScale));
	GetCharacterMovement()->SetCrouchedHalfHeight(58.f);
	GetMesh()->SetRelativeLocation(FVector(0.f, 0.f, -90.f));
	GetMesh()->SetRelativeScale3D(FVector::OneVector);
}

void ALpcgSoldierCharacter::PostInitializeComponents()
{
	Super::PostInitializeComponents();
	if (!CodeMappingContext)
	{
		SprintAction = NewObject<UInputAction>(this, TEXT("IA_Sprint"));
		CrouchAction = NewObject<UInputAction>(this, TEXT("IA_Crouch"));
		CodeMappingContext = NewObject<UInputMappingContext>(this, TEXT("IMC_LpcgSoldier"));
		CodeMappingContext->MapKey(SprintAction, EKeys::LeftShift);
		CodeMappingContext->MapKey(SprintAction, EKeys::Gamepad_LeftThumbstick);
		CodeMappingContext->MapKey(CrouchAction, EKeys::C);
		CodeMappingContext->MapKey(CrouchAction, EKeys::LeftControl);
		CodeMappingContext->MapKey(CrouchAction, EKeys::Gamepad_FaceButton_Right);
	}
}

void ALpcgSoldierCharacter::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	ApplyCharacterScale();
}

void ALpcgSoldierCharacter::SetGrime(float Amount)
{
	Grime = FMath::Clamp(Amount, 0.f, 1.f);
	if (UMaterialInstanceDynamic* Material = GetMesh()->CreateDynamicMaterialInstance(0))
	{
		Material->SetScalarParameterValue(TEXT("GrimeAmount"), Grime);
	}
}

void ALpcgSoldierCharacter::BeginPlay()
{
	Super::BeginPlay();
	if (Grime >= 0.f) SetGrime(Grime);
	Weapon->SetRelativeScale3D(FVector::OneVector);   // the socket carries the scale
	if (Weapon->GetAttachSocketName() != WeaponSocket)
	{
		Weapon->AttachToComponent(GetMesh(), FAttachmentTransformRules(EAttachmentRule::SnapToTarget, EAttachmentRule::SnapToTarget, EAttachmentRule::KeepRelative, false), WeaponSocket);
	}
	// Move at the speeds the clips were authored for, so the planted foot keeps pace with the ground.
	UCharacterMovementComponent* Movement = GetCharacterMovement();
	const TArray<FLpcgGait>& Gaits = Standing.Gaits;
	if (MoveSpeed <= 0.f && Gaits.Num()) MoveSpeed = Gaits[FMath::Min(1, Gaits.Num() - 1)].Speed * CharacterScale;
	if (SprintSpeed <= 0.f) SprintSpeed = Standing.FastestGait() * CharacterScale;
	if (Crouching.Gaits.Num()) Movement->MaxWalkSpeedCrouched = Crouching.SlowestGait() * CharacterScale;
	MoveSpeed = MoveSpeed > 0.f ? MoveSpeed : 360.f;
	SprintSpeed = FMath::Max(SprintSpeed, MoveSpeed);
	Movement->MaxWalkSpeed = MoveSpeed;
}

void ALpcgSoldierCharacter::NotifyControllerChanged()
{
	Super::NotifyControllerChanged();

	const APlayerController* PC = Cast<APlayerController>(Controller);
	UEnhancedInputLocalPlayerSubsystem* Subsystem = PC ? ULocalPlayer::GetSubsystem<UEnhancedInputLocalPlayerSubsystem>(PC->GetLocalPlayer()) : nullptr;
	if (!Subsystem)
	{
		return;
	}

	if (DefaultMappingContext) Subsystem->AddMappingContext(DefaultMappingContext, 0);
	if (MouseLookMappingContext) Subsystem->AddMappingContext(MouseLookMappingContext, 0);
	Subsystem->AddMappingContext(CodeMappingContext, 1);
}

void ALpcgSoldierCharacter::SetupPlayerInputComponent(UInputComponent* PlayerInputComponent)
{
	UEnhancedInputComponent* Input = Cast<UEnhancedInputComponent>(PlayerInputComponent);
	if (!Input)
	{
		return;
	}
	if (JumpAction)
	{
		Input->BindAction(JumpAction, ETriggerEvent::Started, this, &ACharacter::Jump);
		Input->BindAction(JumpAction, ETriggerEvent::Completed, this, &ACharacter::StopJumping);
	}
	if (MoveAction) Input->BindAction(MoveAction, ETriggerEvent::Triggered, this, &ALpcgSoldierCharacter::Move);
	if (LookAction) Input->BindAction(LookAction, ETriggerEvent::Triggered, this, &ALpcgSoldierCharacter::Look);
	if (MouseLookAction) Input->BindAction(MouseLookAction, ETriggerEvent::Triggered, this, &ALpcgSoldierCharacter::Look);

	Input->BindAction(SprintAction, ETriggerEvent::Started, this, &ALpcgSoldierCharacter::StartSprint);
	Input->BindAction(SprintAction, ETriggerEvent::Completed, this, &ALpcgSoldierCharacter::StopSprint);
	Input->BindAction(CrouchAction, ETriggerEvent::Started, this, &ALpcgSoldierCharacter::ToggleCrouch);
}

void ALpcgSoldierCharacter::Move(const FInputActionValue& Value)
{
	const FVector2D Axis = Value.Get<FVector2D>();
	if (!Controller)
	{
		return;
	}
	const FRotator Yaw(0.f, Controller->GetControlRotation().Yaw, 0.f);
	AddMovementInput(FRotationMatrix(Yaw).GetUnitAxis(EAxis::X), Axis.Y);
	AddMovementInput(FRotationMatrix(Yaw).GetUnitAxis(EAxis::Y), Axis.X);
}

void ALpcgSoldierCharacter::Look(const FInputActionValue& Value)
{
	const FVector2D Axis = Value.Get<FVector2D>();
	AddControllerYawInput(Axis.X);
	AddControllerPitchInput(Axis.Y);
}

void ALpcgSoldierCharacter::StartSprint()
{
	bWantsSprint = true;
	ApplyMoveSpeed();
}

void ALpcgSoldierCharacter::StopSprint()
{
	bWantsSprint = false;
	ApplyMoveSpeed();
}

void ALpcgSoldierCharacter::ApplyMoveSpeed()
{
	GetCharacterMovement()->MaxWalkSpeed = bWantsSprint && !bIsCrouched ? SprintSpeed : MoveSpeed;
}

void ALpcgSoldierCharacter::OnStartCrouch(float HalfHeightAdjust, float ScaledHalfHeightAdjust)
{
	Super::OnStartCrouch(HalfHeightAdjust, ScaledHalfHeightAdjust);
	ApplyMoveSpeed();
}

void ALpcgSoldierCharacter::OnEndCrouch(float HalfHeightAdjust, float ScaledHalfHeightAdjust)
{
	Super::OnEndCrouch(HalfHeightAdjust, ScaledHalfHeightAdjust);
	ApplyMoveSpeed();   // still holding Shift: back to sprinting
}

void ALpcgSoldierCharacter::ToggleCrouch()
{
	if (bIsCrouched) UnCrouch(); else Crouch();
}
