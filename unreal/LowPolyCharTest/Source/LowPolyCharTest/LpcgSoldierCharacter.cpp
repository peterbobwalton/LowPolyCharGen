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
#include "UObject/ConstructorHelpers.h"

ALpcgSoldierCharacter::ALpcgSoldierCharacter()
{
	bUseControllerRotationPitch = false;
	bUseControllerRotationYaw = false;
	bUseControllerRotationRoll = false;

	UCharacterMovementComponent* Movement = GetCharacterMovement();
	Movement->bOrientRotationToMovement = true;
	Movement->RotationRate = FRotator(0.f, 540.f, 0.f);
	Movement->MaxWalkSpeed = WalkSpeed;
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

	// Sprint and crouch have no template assets, so they are built here once, as subobjects.
	SprintAction = CreateDefaultSubobject<UInputAction>(TEXT("IA_Sprint"));
	CrouchAction = CreateDefaultSubobject<UInputAction>(TEXT("IA_Crouch"));
	CodeMappingContext = CreateDefaultSubobject<UInputMappingContext>(TEXT("IMC_LpcgSoldier"));
	CodeMappingContext->MapKey(SprintAction, EKeys::LeftShift);
	CodeMappingContext->MapKey(SprintAction, EKeys::Gamepad_LeftThumbstick);
	CodeMappingContext->MapKey(CrouchAction, EKeys::C);
	CodeMappingContext->MapKey(CrouchAction, EKeys::LeftControl);
	CodeMappingContext->MapKey(CrouchAction, EKeys::Gamepad_FaceButton_Right);

	ApplyCharacterScale();
}

void ALpcgSoldierCharacter::ApplyCharacterScale()
{
	// The pack's characters are about 180 cm tall with their feet at the mesh origin.
	const float S = CharacterScale;
	GetCapsuleComponent()->SetCapsuleSize(38.f * S, 90.f * S);
	GetCharacterMovement()->SetCrouchedHalfHeight(58.f * S);
	GetCharacterMovement()->MaxWalkSpeedCrouched = 160.f * S;
	GetMesh()->SetRelativeLocation(FVector(0.f, 0.f, -90.f * S));
	GetMesh()->SetRelativeScale3D(FVector(S));
	Weapon->SetRelativeScale3D(FVector(1.f / S));
}

void ALpcgSoldierCharacter::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	ApplyCharacterScale();
}

void ALpcgSoldierCharacter::BeginPlay()
{
	Super::BeginPlay();
	GetCharacterMovement()->MaxWalkSpeed = WalkSpeed;
	if (Weapon && Weapon->GetAttachSocketName() != WeaponSocket)
	{
		Weapon->AttachToComponent(GetMesh(), FAttachmentTransformRules::SnapToTargetNotIncludingScale, WeaponSocket);
	}
	if (ULpcgLocomotionAnimInstance* Anim = Cast<ULpcgLocomotionAnimInstance>(GetMesh()->GetAnimInstance()))
	{
		Anim->UseDefaultInfantrySets();
	}
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
	if (!bIsCrouched) GetCharacterMovement()->MaxWalkSpeed = SprintSpeed;
}

void ALpcgSoldierCharacter::StopSprint()
{
	GetCharacterMovement()->MaxWalkSpeed = WalkSpeed;
}

void ALpcgSoldierCharacter::ToggleCrouch()
{
	if (bIsCrouched) UnCrouch(); else Crouch();
}
