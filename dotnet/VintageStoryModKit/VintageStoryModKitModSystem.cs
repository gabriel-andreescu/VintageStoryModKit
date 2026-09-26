using Vintagestory.API.Common;

namespace VintageStoryModKit;

// The game rejects a code mod unless an assembly declares a mod system or a ModInfo attribute.
// modinfo.json already holds the metadata, so an empty mod system avoids duplicating it.
internal sealed class VintageStoryModKitModSystem : ModSystem;
