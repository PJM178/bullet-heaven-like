#if TOOLS
using Godot;
using Godot.Collections;

namespace BulletHeavenLike;

/// <summary>
/// Turns a baked sprite sheet (<c>name.spritesheet</c> JSON + <c>name.png</c> atlas next
/// to it) into a <see cref="SpriteFrames"/> resource. Godot runs this automatically
/// whenever the .spritesheet file changes; you then load the .spritesheet path like any
/// resource, e.g. <c>GD.Load&lt;SpriteFrames&gt;("res://local_assets/monsters/poring.spritesheet")</c>,
/// or drag it into an AnimatedSprite3D's "Sprite Frames" slot.
///
/// Every frame becomes an <see cref="AtlasTexture"/> padded with a margin to one shared
/// canvas (the sprite's "bounds"), so all frames are the same size and the
/// sprite's origin (its feet) sits at the same pixel in each. <see cref="SpriteDirections.Setup"/>
/// then sets the node's offset so that origin lands on the node's position.
///
/// Animations are named "&lt;anim&gt;_&lt;dir&gt;", e.g. "walk_SW". See
/// the baking tool's README ("Baked sprites") for the file format.
/// </summary>
[Tool]
public partial class SpriteSheetImporter : EditorImportPlugin
{
    // Animations that repeat. Everything else (attack, hurt, die, ...) plays once.
    private static readonly string[] LoopingAnims = { "idle", "walk", "ready", "anim5", "anim6" };

    public override string _GetImporterName() => "pjm.sprite_sheet";
    public override string _GetVisibleName() => "Sprite Sheet (SpriteFrames)";
    public override string[] _GetRecognizedExtensions() => new[] { "spritesheet" };
    public override string _GetSaveExtension() => "res";
    public override string _GetResourceType() => "SpriteFrames";
    public override float _GetPriority() => 1.0f;
    public override int _GetPresetCount() => 1;
    public override string _GetPresetName(int presetIndex) => "Default";

    // Higher numbers import later. The atlas .png must already be imported as a
    // texture when we run, and Godot's texture importer uses order 0.
    public override int _GetImportOrder() => 100;

    // C# import plugins are safest on the main thread.
    public override bool _CanImportThreaded() => false;

    public override Array<Dictionary> _GetImportOptions(string path, int presetIndex) => new();

    public override bool _GetOptionVisibility(string path, StringName optionName, Dictionary options) => true;

    public override Error _Import(string sourceFile, string savePath, Dictionary options,
        Array<string> platformVariants, Array<string> genFiles)
    {
        string text = FileAccess.GetFileAsString(sourceFile);
        if (Json.ParseString(text).Obj is not Dictionary data)
        {
            GD.PushError($"Sprite sheet: {sourceFile} is not valid JSON.");
            return Error.ParseError;
        }

        // The atlas sits next to the .spritesheet file.
        string atlasPath = sourceFile.GetBaseDir().PathJoin((string)data["atlas"]);
        var atlas = ResourceLoader.Load<Texture2D>(atlasPath);
        if (atlas == null)
        {
            GD.PushError($"Sprite sheet: atlas {atlasPath} not found or not imported.");
            return Error.FileNotFound;
        }

        // bounds = [x, y, w, h]: union of all frames, relative to the origin.
        var b = (Array)data["bounds"];
        var bounds = new Rect2((float)b[0], (float)b[1], (float)b[2], (float)b[3]);
        string kind = (string)data["kind"];

        var frames = new SpriteFrames();
        frames.RemoveAnimation("default"); // SpriteFrames starts with an empty "default".

        // Per-frame sounds, kept as metadata: { "walk_S": { 2: "effect/xxx.wav" } }.
        var sounds = new Dictionary();

        foreach (Dictionary action in (Array)data["actions"])
        {
            string anim = (string)action["anim"];
            Variant dir = action["dir"];
            string name = dir.VariantType == Variant.Type.Nil ? anim : $"{anim}_{(string)dir}";
            if (frames.HasAnimation(name))
                name += $"_{(int)action["index"]}";

            float delayMs = (float)action["delay_ms"];
            frames.AddAnimation(name);
            frames.SetAnimationSpeed(name, delayMs > 0 ? 1000.0 / delayMs : 10.0);
            frames.SetAnimationLoop(name, System.Array.IndexOf(LoopingAnims, anim) >= 0);

            // A player's idle and sit "frames" are head turns (straight, left, right),
            // not an animation: speed 0 so it stays on whichever frame you set.
            if (kind == "player" && (anim == "idle" || anim == "sit"))
            {
                frames.SetAnimationSpeed(name, 0);
                frames.SetAnimationLoop(name, false);
            }

            var frameSounds = new Dictionary();
            int frameIndex = 0;
            foreach (Dictionary frame in (Array)action["frames"])
            {
                frames.AddFrame(name, MakeFrame(atlas, frame, bounds));
                if (frame.TryGetValue("sound", out Variant sound))
                    frameSounds[frameIndex] = sound;
                frameIndex++;
            }
            if (frameSounds.Count > 0)
                sounds[name] = frameSounds;
        }

        // Metadata travels with the resource; SpriteDirections.Setup reads "sheet_bounds".
        frames.SetMeta("sheet_bounds", bounds);
        frames.SetMeta("sheet_kind", kind);
        if (sounds.Count > 0)
            frames.SetMeta("sheet_sounds", sounds);

        return ResourceSaver.Save(frames, $"{savePath}.{_GetSaveExtension()}");
    }

    /// <summary>
    /// One frame: the frame's rectangle in the atlas, padded with a margin so the
    /// texture covers the whole <paramref name="bounds"/> canvas.
    /// </summary>
    private static AtlasTexture MakeFrame(Texture2D atlas, Dictionary frame, Rect2 bounds)
    {
        var tex = new AtlasTexture { Atlas = atlas, FilterClip = true };
        Variant rectVar = frame["rect"];
        if (rectVar.VariantType == Variant.Type.Nil)
        {
            // Empty frame: one transparent pixel (the atlas keeps a 1 px transparent
            // border around every frame, so pixel 0,0 is always empty).
            tex.Region = new Rect2(0, 0, 1, 1);
            tex.Margin = new Rect2(0, 0, bounds.Size.X - 1, bounds.Size.Y - 1);
            return tex;
        }

        var r = (Array)rectVar;
        var o = (Array)frame["origin"];
        var region = new Rect2((float)r[0], (float)r[1], (float)r[2], (float)r[3]);
        // origin = where the sprite's origin lies inside the cropped frame.
        // The frame's top-left is therefore at -origin relative to the origin, and
        // the bounds' top-left is at bounds.Position: the difference is the left/top pad.
        float left = -(float)o[0] - bounds.Position.X;
        float top = -(float)o[1] - bounds.Position.Y;
        tex.Region = region;
        // Margin: Position = left/top padding, Size = total extra width/height.
        tex.Margin = new Rect2(left, top, bounds.Size.X - region.Size.X, bounds.Size.Y - region.Size.Y);
        return tex;
    }
}
#endif
