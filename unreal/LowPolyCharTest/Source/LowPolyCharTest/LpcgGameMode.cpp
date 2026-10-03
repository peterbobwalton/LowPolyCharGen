#include "LpcgGameMode.h"

#include "LpcgSoldierCharacter.h"

ALpcgGameMode::ALpcgGameMode()
{
	DefaultPawnClass = ALpcgSoldierCharacter::StaticClass();
}
