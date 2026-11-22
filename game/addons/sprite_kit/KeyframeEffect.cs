using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Plays a ".str" keyframed effect straight from the file. The effect files
/// live in the git-ignored res://local_assets/effects/, with ".bin" added to
/// every name (e.g. smash.str.bin, its .bmp textures too) so Godot doesn't
/// import them and exports them as-is (export include_filter
/// "local_assets/*.bin").
///
/// An STR file is a list of layers. Each layer has a few textures and a list
/// of keyframes. A keyframe gives a quad's four corners, its position, angle,
/// colour and which texture to show. Playback:
///   - "source" keyframes (type 0) are absolute states;
///   - a "morph" keyframe (type 1) at the same frame holds per-frame deltas
///     that are added until the next source keyframe;
///   - the quad faces the camera; 35 STR pixels are one cell, and
///     the art's (320, 320) sits on the effect's position.
///
/// Put the node where the effect should play and call Play().
/// </summary>
public partial class KeyframeEffect : Node3D
{
    // Effect cells to our metres. A 5x5-cell area effect over our 2 m wide
    // smash gives 0.4 m per cell, keeping it the same size relative to its
    // damage area.
    [Export] public float MetresPerCell = 0.4f;

    // 0 plays smoothly. Above 0, the animation steps at this
    // rate (our PS1-style choppy playback), to compare the two.
    [Export] public float StepFps = 0.0f;

    // Playback speed multiplier.
    [Export] public float Speed = 1.0f;

    public bool Playing { get; private set; }

    // Seconds the effect lasts (its last keyframe).
    public float Duration => _file == null ? 0.0f : _file.MaxKey / (float)_file.Fps;

    private const float PixelsPerCell = 35.0f;
    private const float ArtCentre = 320.0f;
    // Classic STR art sits half a cell low.
    private const float VerticalBaseCells = 0.5f;

    private static readonly StringName TextureParam = "layer_texture";
    private static readonly StringName CornerXParam = "corner_x";
    private static readonly StringName CornerYParam = "corner_y";
    private static readonly StringName TintParam = "tint";

    // Additive (D3D SRC_ALPHA/DEST_ALPHA or ONE) and normal alpha blending
    // (SRC_ALPHA/INV_SRC_ALPHA). Blend modes are fixed per shader in Godot,
    // so the second is the same code with another render_mode.
    private static Shader _addShader;
    private static Shader _mixShader;

    // Parsed files and loaded textures, shared by every instance (pooled
    // casts play the same file). Keyed by absolute path.
    private static readonly Dictionary<string, StrFile> FileCache = new();

    private StrFile _file;
    private MeshInstance3D[] _nodes;
    private ShaderMaterial[] _materials;
    private int[] _shownTexture;
    private float _time;

    // ------------------------------------------------------------ file data

    /// <summary>One keyframe, as stored in the file (124 bytes).</summary>
    private struct StrKey
    {
        public int Frame;
        public uint Type;        // 0 = source, 1 = morph (deltas)
        public float PosX, PosY;
        public float[] Xy;       // corners: x0..x3 then y0..y3
        public float AniFrame;   // which texture
        public uint AniType;     // how AniFrame moves during a morph
        public float Delay;      // AniFrame speed for AniType 2..4
        public float Angle;      // degrees
        public Color Color;      // 0..1
        public uint SrcBlend, DstBlend;
    }

    private sealed class StrLayer
    {
        public Texture2D[] Textures;
        public StrKey[] Keys;
        // Precomputed once: the last frame of any key, and of any source key.
        public int LastFrame;
        public int LastSource;
        // From the first source key: true for normal alpha blending.
        public bool MixBlend;
    }

    private sealed class StrFile
    {
        public int Fps;
        public int MaxKey;
        public StrLayer[] Layers;
    }

    /// <summary>A layer's state at one moment.</summary>
    private struct LayerState
    {
        public float AniFrame;
        public float Angle;
        public float PosX, PosY;
        public Color Color;
        // Corners, filled into the fixed arrays below to avoid allocating.
    }

    // Scratch corner arrays for Evaluate (one instance runs at a time on the
    // main thread, so sharing these is safe).
    private static readonly float[] ScratchXy = new float[8];

    // ------------------------------------------------------------ loading

    /// <summary>
    /// Loads an STR file (a res:// or absolute path) and builds a quad for
    /// each layer. Returns false if the file can't be read.
    /// </summary>
    public bool Load(string path)
    {
        if (!FileCache.TryGetValue(path, out _file))
        {
            _file = ParseFile(path);
            if (_file == null)
            {
                return false;
            }
            FileCache[path] = _file;
        }

        if (_addShader == null)
        {
            _addShader = GD.Load<Shader>("res://addons/sprite_kit/keyframe_layer.gdshader");
            _mixShader = new Shader { Code = _addShader.Code.Replace("blend_add", "blend_mix") };
        }

        int count = _file.Layers.Length;
        _nodes = new MeshInstance3D[count];
        _materials = new ShaderMaterial[count];
        _shownTexture = new int[count];
        for (int i = 0; i < count; i++)
        {
            ShaderMaterial material = new()
            {
                Shader = _file.Layers[i].MixBlend ? _mixShader : _addShader,
                RenderPriority = i,
            };
            QuadMesh mesh = new()
            {
                Size = Vector2.One,
                // The shader moves the corners far from the quad's own 1x1
                // size, so give it a big box or Godot culls it off screen.
                CustomAabb = new Aabb(new Vector3(-20, -20, -20), new Vector3(40, 40, 40)),
            };
            MeshInstance3D node = new()
            {
                Mesh = mesh,
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(node);
            _nodes[i] = node;
            _materials[i] = material;
            _shownTexture[i] = -1;
        }
        return true;
    }

    private static StrFile ParseFile(string path)
    {
        // Godot's FileAccess, not System.IO: on Android res:// files live
        // inside the APK, which System.IO can't see.
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PushWarning($"KeyframeEffect: no file at {path}");
            return null;
        }

        // BinaryReader reads little-endian numbers in order, as the file
        // stores them. MemoryStream lets it read from the bytes in memory.
        using BinaryReader reader = new(new MemoryStream(Godot.FileAccess.GetFileAsBytes(path)));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "STRM")
        {
            GD.PushWarning($"KeyframeEffect: {path} is not an STR file");
            return null;
        }
        reader.ReadUInt32(); // version
        StrFile file = new()
        {
            Fps = (int)reader.ReadUInt32(),
            MaxKey = (int)reader.ReadUInt32(),
        };
        int layerCount = (int)reader.ReadUInt32();
        reader.ReadBytes(16); // reserved

        if (file.Fps < 1 || file.Fps > 240)
        {
            file.Fps = 60;
        }

        // Path.GetDirectoryName would mangle "res://", so cut at the last '/'.
        string directory = path.Substring(0, path.LastIndexOf('/'));
        // Copies made for the phone end in ".bin"
        // so Godot exports them untouched instead of importing them.
        bool binCopies = path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);
        file.Layers = new StrLayer[layerCount];
        for (int l = 0; l < layerCount; l++)
        {
            StrLayer layer = new();
            int textureCount = reader.ReadInt32();
            layer.Textures = new Texture2D[textureCount];
            for (int t = 0; t < textureCount; t++)
            {
                string name = ReadFixedString(reader, 128);
                layer.Textures[t] = LoadTexture(directory, name, binCopies);
            }

            int keyCount = reader.ReadInt32();
            layer.Keys = new StrKey[keyCount];
            bool sawSource = false;
            for (int k = 0; k < keyCount; k++)
            {
                StrKey key = new()
                {
                    Frame = reader.ReadInt32(),
                    Type = reader.ReadUInt32(),
                    PosX = reader.ReadSingle(),
                    PosY = reader.ReadSingle(),
                };
                for (int i = 0; i < 8; i++)
                {
                    reader.ReadSingle(); // texture UVs: unused
                }
                key.Xy = new float[8];
                for (int i = 0; i < 8; i++)
                {
                    key.Xy[i] = reader.ReadSingle();
                }
                key.AniFrame = reader.ReadSingle();
                key.AniType = reader.ReadUInt32();
                key.Delay = reader.ReadSingle();
                key.Angle = reader.ReadSingle() / (1024.0f / 360.0f);
                key.Color = new Color(reader.ReadSingle() / 255.0f, reader.ReadSingle() / 255.0f,
                    reader.ReadSingle() / 255.0f, reader.ReadSingle() / 255.0f);
                key.SrcBlend = reader.ReadUInt32();
                key.DstBlend = reader.ReadUInt32();
                reader.ReadUInt32(); // mtpreset

                if (key.Type == 0 && !sawSource)
                {
                    sawSource = true;
                    layer.MixBlend = key.DstBlend == 6;
                    if (key.SrcBlend != 5 || (key.DstBlend != 6 && key.DstBlend != 7 && key.DstBlend != 2))
                    {
                        GD.PushWarning($"KeyframeEffect: layer {l} uses blend {key.SrcBlend}/{key.DstBlend}; drawn additive.");
                    }
                }

                layer.Keys[k] = key;
                layer.LastFrame = Math.Max(layer.LastFrame, key.Frame);
                if (key.Type == 0)
                {
                    layer.LastSource = Math.Max(layer.LastSource, key.Frame);
                }
            }
            file.Layers[l] = layer;
        }
        return file;
    }

    private static string ReadFixedString(BinaryReader reader, int length)
    {
        byte[] bytes = reader.ReadBytes(length);
        int end = Array.IndexOf(bytes, (byte)0);
        // Names are cp949 (Korean); effect texture names are plain ASCII.
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? length : end).Replace('\\', '/');
    }

    /// <summary>
    /// Loads a .bmp/.tga texture named by the STR file, looking next to it
    /// first. The file names inside don't always match case, so look case-insensitively.
    /// BMP magenta (255, 0, 255) is the "transparent" colour.
    /// </summary>
    private static Texture2D LoadTexture(string strDirectory, string name, bool binCopies)
    {
        string found;
        if (binCopies)
        {
            // The copies are stored lower-case, flat, with ".bin" added.
            found = $"{strDirectory}/{Path.GetFileName(name).ToLowerInvariant()}.bin";
            if (!Godot.FileAccess.FileExists(found))
            {
                found = null;
            }
        }
        else
        {
            string disk = ProjectSettings.GlobalizePath(strDirectory);
            found = FindCaseInsensitive(disk, name) ?? FindCaseInsensitive(disk, Path.GetFileName(name));
        }
        if (found == null)
        {
            GD.PushWarning($"KeyframeEffect: texture {name} not found");
            return null;
        }

        byte[] data = Godot.FileAccess.GetFileAsBytes(found);
        // Strip ".bin" so the extension checks below see .bmp / .tga.
        if (binCopies)
        {
            found = found.Substring(0, found.Length - 4);
        }
        Image image = new();
        Error error = found.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)
            ? image.LoadTgaFromBuffer(data)
            : image.LoadBmpFromBuffer(data);
        if (error != Error.Ok)
        {
            GD.PushWarning($"KeyframeEffect: could not read {found}: {error}");
            return null;
        }

        image.Convert(Image.Format.Rgba8);
        if (found.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            // Raw RGBA bytes, 4 per pixel: clear the magenta ones.
            byte[] pixels = image.GetData();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 250 && pixels[i + 1] < 5 && pixels[i + 2] > 250)
                {
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;
                }
            }
            image.SetData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, pixels);
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static string FindCaseInsensitive(string directory, string relative)
    {
        string current = directory;
        foreach (string part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current))
            {
                return null;
            }
            string match = null;
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (string.Equals(Path.GetFileName(entry), part, StringComparison.OrdinalIgnoreCase))
                {
                    match = entry;
                    break;
                }
            }
            if (match == null)
            {
                return null;
            }
            current = match;
        }
        return File.Exists(current) ? current : null;
    }

    // ------------------------------------------------------------ playback

    public void Play()
    {
        if (_file == null)
        {
            return;
        }
        _time = 0.0f;
        Playing = true;
        UpdateLayers();
    }

    public void Stop()
    {
        Playing = false;
        if (_nodes == null)
        {
            return;
        }
        foreach (MeshInstance3D node in _nodes)
        {
            node.Visible = false;
        }
    }

    public override void _Process(double delta)
    {
        if (!Playing)
        {
            return;
        }
        _time += (float)delta * Speed;
        if (_time >= Duration)
        {
            Stop();
            return;
        }
        UpdateLayers();
    }

    private void UpdateLayers()
    {
        float time = StepFps > 0.0f ? Mathf.Floor(_time * StepFps) / StepFps : _time;
        // keyIndex is fractional: morphs add their deltas smoothly.
        float keyIndex = time * _file.Fps;
        float scale = MetresPerCell / PixelsPerCell;

        for (int i = 0; i < _file.Layers.Length; i++)
        {
            StrLayer layer = _file.Layers[i];
            MeshInstance3D node = _nodes[i];
            if (layer.Textures.Length == 0 || !Evaluate(layer, keyIndex, out LayerState state))
            {
                node.Visible = false;
                continue;
            }
            int texture = (int)state.AniFrame;
            if (texture < 0 || texture >= layer.Textures.Length || layer.Textures[texture] == null
                || float.IsNaN(state.Angle) || float.IsNaN(state.PosX) || float.IsNaN(state.PosY))
            {
                node.Visible = false;
                continue;
            }

            // Corners in STR pixels (y down) -> flip y, turn by
            // -angle, add the offset from the art centre, lower half a cell.
            float r = Mathf.DegToRad(-state.Angle);
            float cos = Mathf.Cos(r);
            float sin = Mathf.Sin(r);
            float offsetX = (state.PosX - ArtCentre) * scale;
            float offsetY = -(state.PosY - ArtCentre) * scale - VerticalBaseCells * MetresPerCell;
            // Texture corner order: TL=(xy0,xy4) TR=(xy1,xy5) BL=(xy3,xy7) BR=(xy2,xy6).
            Vector4 cornerX = default;
            Vector4 cornerY = default;
            for (int c = 0; c < 4; c++)
            {
                int src = c == 2 ? 3 : c == 3 ? 2 : c;
                float px = ScratchXy[src] * scale;
                float py = -ScratchXy[src + 4] * scale;
                cornerX[c] = px * cos - py * sin + offsetX;
                cornerY[c] = px * sin + py * cos + offsetY;
            }

            ShaderMaterial material = _materials[i];
            if (texture != _shownTexture[i])
            {
                material.SetShaderParameter(TextureParam, layer.Textures[texture]);
                _shownTexture[i] = texture;
            }
            material.SetShaderParameter(CornerXParam, cornerX);
            material.SetShaderParameter(CornerYParam, cornerY);
            material.SetShaderParameter(TintParam, state.Color);
            node.Visible = true;
        }
    }

    /// <summary>
    /// Half the width of layer layerIndex's quad, in metres, at
    /// secondsSincePlay into the effect: for a ring texture, its radius out
    /// to the texture's edge. 0 before the layer first shows, -1 once it
    /// has finished. Follows StepFps and Speed, so it matches what is drawn.
    /// </summary>
    public float LayerHalfWidth(int layerIndex, float secondsSincePlay)
    {
        if (_file == null || layerIndex < 0 || layerIndex >= _file.Layers.Length)
        {
            return -1.0f;
        }
        StrLayer layer = _file.Layers[layerIndex];
        if (layer.Keys.Length == 0)
        {
            return -1.0f;
        }

        float time = secondsSincePlay * Speed;
        if (StepFps > 0.0f)
        {
            time = Mathf.Floor(time * StepFps) / StepFps;
        }
        float keyIndex = time * _file.Fps;
        if (keyIndex < layer.Keys[0].Frame)
        {
            return 0.0f; // not shown yet
        }
        if (!Evaluate(layer, keyIndex, out _))
        {
            return -1.0f; // finished
        }

        // Evaluate left the corners' x in ScratchXy[0..3], in STR pixels.
        float minX = Mathf.Min(Mathf.Min(ScratchXy[0], ScratchXy[1]), Mathf.Min(ScratchXy[2], ScratchXy[3]));
        float maxX = Mathf.Max(Mathf.Max(ScratchXy[0], ScratchXy[1]), Mathf.Max(ScratchXy[2], ScratchXy[3]));
        return (maxX - minX) * 0.5f * MetresPerCell / PixelsPerCell;
    }

    /// <summary>
    /// The layer's state at
    /// keyIndex, or false if the layer shows nothing then. Corners go into
    /// ScratchXy.
    /// </summary>
    private static bool Evaluate(StrLayer layer, float keyIndex, out LayerState state)
    {
        state = default;
        int fromId = -1;
        int toId = -1;
        StrKey[] keys = layer.Keys;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i].Frame <= keyIndex)
            {
                if (keys[i].Type == 0)
                {
                    fromId = i;
                }
                if (keys[i].Type == 1)
                {
                    toId = i;
                }
            }
        }
        if (fromId < 0 || (toId < 0 && layer.LastFrame < keyIndex))
        {
            return false;
        }

        StrKey from = keys[fromId];
        bool morphing = toId == fromId + 1 && keys[toId].Frame == from.Frame;
        if (!morphing)
        {
            // A source key with no morph after it: hold it, unless the
            // layer's last source key has passed and an old morph is left.
            if (toId >= 0 && layer.LastSource <= from.Frame)
            {
                return false;
            }
            state.AniFrame = from.AniFrame;
            state.Angle = from.Angle;
            state.PosX = from.PosX;
            state.PosY = from.PosY;
            state.Color = from.Color;
            Array.Copy(from.Xy, ScratchXy, 8);
            return true;
        }

        StrKey to = keys[toId];
        float dl = keyIndex - from.Frame;
        state.Color = new Color(from.Color.R + to.Color.R * dl, from.Color.G + to.Color.G * dl,
            from.Color.B + to.Color.B * dl, from.Color.A + to.Color.A * dl);
        for (int i = 0; i < 8; i++)
        {
            ScratchXy[i] = from.Xy[i] + to.Xy[i] * dl;
        }
        state.Angle = from.Angle + to.Angle * dl;
        state.PosX = from.PosX + to.PosX * dl;
        state.PosY = from.PosY + to.PosY * dl;

        int textureCount = Math.Max(layer.Textures.Length, 1);
        state.AniFrame = to.AniType switch
        {
            1 => from.AniFrame + to.AniFrame * dl,
            2 => Math.Min(from.AniFrame + to.Delay * dl, layer.Textures.Length - 1),
            3 => (from.AniFrame + to.Delay * dl) % textureCount,
            4 => (from.AniFrame - to.Delay * dl) % textureCount,
            _ => 0.0f,
        };
        return true;
    }
}
