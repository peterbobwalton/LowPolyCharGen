#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "LpcgGameMode.generated.h"

/** Spawns the player as an ALpcgSoldierCharacter. */
UCLASS()
class LOWPOLYCHARTEST_API ALpcgGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	ALpcgGameMode();
};
