#include "LpcgDisplaySoldier.h"

#include "Animation/AnimSequence.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Components/TextRenderComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/StaticMesh.h"
#include "LpcgLocomotionAnimInstance.h"
#include "Materials/MaterialInstanceDynamic.h"

ALpcgDisplaySoldier::ALpcgDisplaySoldier()
{
	RootComponent = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));

	// The pack's characters face +Y; turn them so the actor's forward (+X) is where they look.
	Body = CreateDefaultSubobject<USkeletalMeshComponent>(TEXT("Body"));
	Body->SetupAttachment(RootComponent);
	Body->SetRelativeRotation(FRotator(0.f, -90.f, 0.f));
	Body->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
	Body->SetUpdateAnimationInEditor(true);
	// Crowds: distant soldiers animate at a lower rate and off-screen ones don't evaluate a pose at all.
	Body->bEnableUpdateRateOptimizations = true;
	Body->VisibilityBasedAnimTickOption = EVisibilityBasedAnimTickOption::OnlyTickPoseWhenRendered;

	Weapon = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Weapon"));
	Weapon->SetupAttachment(Body, WeaponSocket);
	Weapon->SetCollisionEnabled(ECollisionEnabled::NoCollision);

	NameText = CreateDefaultSubobject<UTextRenderComponent>(TEXT("Label"));
	NameText->SetupAttachment(RootComponent);
	NameText->SetHorizontalAlignment(EHTA_Center);
	NameText->SetWorldSize(14.f);

	ULpcgLocomotionAnimInstance::FindDefaultInfantrySets(Standing, Crouching);
}

void ALpcgDisplaySoldier::Refresh()
{
	if (Body->GetSkeletalMeshAsset() != Mesh)
	{
		Body->EmptyOverrideMaterials();   // a grime instance made for the previous mesh would carry its material
		Body->SetSkeletalMesh(Mesh);
	}

	if (Animation)
	{
		// Only (re)start when the clip changes, so editing other properties doesn't reset every soldier.
		if (Body->GetAnimationMode() != EAnimationMode::AnimationSingleNode || Body->AnimationData.AnimToPlay != Animation)
		{
			Body->SetAnimationMode(EAnimationMode::AnimationSingleNode);
			Body->AnimationData.AnimToPlay = Animation;
			Body->AnimationData.bSavedLooping = true;
			Body->AnimationData.bSavedPlaying = true;
			Body->AnimationData.SavedPlayRate = 1.f;
			Body->PlayAnimation(Animation, true);
		}
	}
	else
	{
		Body->SetAnimationMode(EAnimationMode::AnimationBlueprint);
		Body->SetAnimInstanceClass(ULpcgLocomotionAnimInstance::StaticClass());
		// The instance reads speed, stance and clips from this actor when it initialises; push them for the live one too.
		if (ULpcgLocomotionAnimInstance* Anim = Cast<ULpcgLocomotionAnimInstance>(Body->GetAnimInstance()))
		{
			Anim->SetSets(Standing, Crouching);
			Anim->SpeedOverride = LocomotionSpeed;
			Anim->bCrouchOverride = bCrouched;
		}
	}

	Body->SetRelativeScale3D(FVector(CharacterScale));
	NameText->SetRelativeLocation(FVector(0.f, 0.f, 215.f * CharacterScale));

	Weapon->SetStaticMesh(WeaponMesh);
	Weapon->SetRelativeScale3D(FVector::OneVector);   // the socket carries the scale
	if (Weapon->GetAttachSocketName() != WeaponSocket)
	{
		Weapon->AttachToComponent(Body, FAttachmentTransformRules(EAttachmentRule::SnapToTarget, EAttachmentRule::SnapToTarget, EAttachmentRule::KeepRelative, false), WeaponSocket);
	}
	NameText->SetText(Label);

	if (Grime >= 0.f)
	{
		if (UMaterialInstanceDynamic* Material = Body->CreateDynamicMaterialInstance(0))
		{
			Material->SetScalarParameterValue(TEXT("GrimeAmount"), Grime);
		}
	}
	else if (Body->GetNumOverrideMaterials() > 0)
	{
		Body->EmptyOverrideMaterials();   // back to the material instance's own grime amount
	}
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
