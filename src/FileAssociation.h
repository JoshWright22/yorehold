#pragma once

// Makes double-clicking a .yore file open it with this copy of the game (for the current user
// only; nothing needs administrator rights). Does nothing on platforms without such a registry.
void registerYoreFiles();
