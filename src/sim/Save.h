#pragma once

#include <yorehold/framework/save/SaveFile.h>

// The autosave file: World::stateJson() wrapped in a versioned envelope. Older versions are
// brought up to date as they are read.
namespace Save
{

const yh::SaveFormat& format();

}
