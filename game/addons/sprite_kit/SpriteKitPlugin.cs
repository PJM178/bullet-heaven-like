// Editor-only code. "TOOLS" is defined when Godot builds the project for the
// editor, but not for an exported game: EditorPlugin and the import classes
// don't exist on Android, so without this #if the export build would fail.
#if TOOLS
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Registers <see cref="SpriteSheetImporter"/> while the plugin is enabled
/// (Project > Project Settings > Plugins > "Sprite Kit").
/// [Tool] makes Godot run this script inside the editor too, not only in the game.
/// </summary>
[Tool]
public partial class SpriteKitPlugin : EditorPlugin
{
    private SpriteSheetImporter _importer;

    public override void _EnterTree()
    {
        _importer = new SpriteSheetImporter();
        AddImportPlugin(_importer);
    }

    public override void _ExitTree()
    {
        RemoveImportPlugin(_importer);
        _importer = null;
    }
}
#endif
