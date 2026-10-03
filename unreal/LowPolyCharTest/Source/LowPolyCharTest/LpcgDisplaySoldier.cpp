#include "LpcgDisplaySoldier.h"

#include "Animation/AnimSequence.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Components/TextRenderComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/StaticMesh.h"
#include "LpcgLocomotionAnimInstance.h"

ALpcgDisplaySoldier::ALpcgDisplaySoldier()
{
	RootComponent = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));

	// The pack's characters face +Y; turn them so the actor's forward (+X) is where they look.
	Body = CreateDefaultSubobject<USkeletalMeshComponent>(TEXT("Body"));
	Body->SetupAttachment(RootComponent);
	Body->SetRelativeRotation(FRotator(0.f, -90.f, 0.f));
	Body->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
	Body->SetUpdateAnimationInEditor(true);

	Weapon = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Weapon"));
	Weapon->SetupAttachment(Body, WeaponSocket);
	Weapon->SetCollisionEnabled(ECollisionEnabled::NoCollision);

	NameText = CreateDefaultSubobject<UTextRenderComponent>(TEXT("Label"));
	NameText->SetupAttachment(RootComponent);
	NameText->SetHorizontalAlignment(EHTA_Center);
	NameText->SetWorldSize(14.f);
}

void ALpcgDisplaySoldier::Refresh()
{
	if (Body->GetSkeletalMeshAsset() != Mesh)
	{
		Body->SetSkeletalMesh(Mesh);
	}

	if (Animation)
	{
		Body->SetAnimationMode(EAnimationMode::AnimationSingleNode);
		Body->AnimationData.AnimToPlay = Animation;
		Body->AnimationData.bSavedLooping = true;
		Body->AnimationData.bSavedPlaying = true;
		Body->AnimationData.SavedPlayRate = 1.f;
		Body->PlayAnimation(Animation, true);
	}
	else
	{
		Body->SetAnimationMode(EAnimationMode::AnimationBlueprint);
		Body->SetAnimInstanceClass(ULpcgLocomotionAnimInstance::StaticClass());
		if (ULpcgLocomotionAnimInstance* Anim = Cast<ULpcgLocomotionAnimInstance>(Body->GetAnimInstance()))
		{
			Anim->UseDefaultInfantrySets();
			Anim->SpeedOverride = LocomotionSpeed;
			Anim->bCrouchOverride = bCrouched;
		}
	}

	Body->SetRelativeScale3D(FVector(CharacterScale));
	Weapon->SetRelativeScale3D(FVector(1.f / CharacterScale));
	NameText->SetRelativeLocation(FVector(0.f, 0.f, 215.f * CharacterScale));

	Weapon->SetStaticMesh(WeaponMesh);
	if (Weapon->GetAttachSocketName() != WeaponSocket)
	{
		Weapon->AttachToComponent(Body, FAttachmentTransformRules::SnapToTargetNotIncludingScale, WeaponSocket);
	}
	NameText->SetText(Label);
}

void ALpcgDisplaySoldier::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	Refresh();
}

void ALpcgDisplaySoldier::BeginPlay()
{
	Super::BeginPlay();
	Refresh();
}
