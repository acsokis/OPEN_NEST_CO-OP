using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenNestCoop.GameSync;

internal sealed class ScoutFrame
{
    public string Id = "", Parent = "";
    public float[] Position, Rotation, Scale;
    public bool Active;
    public ScoutAnimation[] Animations = Array.Empty<ScoutAnimation>();
    public ScoutAudio[] Audio = Array.Empty<ScoutAudio>();
    public ScoutPart[] Parts = Array.Empty<ScoutPart>();
}
internal sealed class ScoutPart
{
    public string Key = "";
    public float[] Position, Rotation, Scale;
    public bool Active;
}
internal sealed class ScoutAnimation
{
    public string Key = "";
    public int State;
    public float Time, Speed;
    public bool Active;
}
internal sealed class ScoutAudio
{
    public string Key = "";
    public long Sequence;
    public bool Playing;
    public int Timeline;
}
internal static class ScoutStateCodec
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, MaxDepth = 8 };
    internal static string Encode(ScoutFrame[] frames) { Validate(frames); return JsonSerializer.Serialize(frames, Json); }
    internal static ScoutFrame[] Decode(string json)
    {
        if (json == null || json.Length > 1000000) throw new InvalidOperationException("Scout state too large");
        var frames = JsonSerializer.Deserialize<ScoutFrame[]>(json, Json); Validate(frames); return frames;
    }
    private static bool Key(string key, int max) => !string.IsNullOrEmpty(key) && key.Length <= max;
    private static bool Vector(float[] values, int length) => values != null && values.Length == length && values.All(v => float.IsFinite(v) && Math.Abs(v) < 10000000);
    private static void Validate(ScoutFrame[] frames)
    {
        if (frames == null || frames.Length > 32) throw new InvalidOperationException("Invalid scout count");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var frame in frames)
        {
            if (frame == null || !Key(frame.Id, 64) || !ids.Add(frame.Id) || !Key(frame.Parent, 2048)
                || !Vector(frame.Position, 3) || !Vector(frame.Rotation, 4) || !Vector(frame.Scale, 3)
                || frame.Animations == null || frame.Animations.Length > 32 || frame.Audio == null || frame.Audio.Length > 32
                || frame.Parts == null || frame.Parts.Length > 128)
                throw new InvalidOperationException("Invalid scout transform");
            var parts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var animation in frame.Animations)
                if (animation == null || !Key(animation.Key, 2048) || !parts.Add(animation.Key)
                    || !float.IsFinite(animation.Time) || !float.IsFinite(animation.Speed) || Math.Abs(animation.Time) > 1000000 || Math.Abs(animation.Speed) > 100)
                    throw new InvalidOperationException("Invalid scout animation");
            parts.Clear();
            foreach (var part in frame.Parts)
                if (part == null || !Key(part.Key, 2048) || !parts.Add(part.Key)
                    || !Vector(part.Position, 3) || !Vector(part.Rotation, 4) || !Vector(part.Scale, 3))
                    throw new InvalidOperationException("Invalid scout child transform");
            parts.Clear();
            foreach (var sound in frame.Audio)
                if (sound == null || !Key(sound.Key, 2048) || !parts.Add(sound.Key) || sound.Sequence < 0 || sound.Timeline < 0 || sound.Timeline > 86400000)
                    throw new InvalidOperationException("Invalid scout audio");
        }
    }
    internal static bool PlaySound(bool newObject, long previousSequence, ScoutAudio audio, bool localPlaying) =>
        audio.Sequence > previousSequence ? (audio.Playing || !newObject) : audio.Sequence == previousSequence && audio.Playing && !localPlaying;
}
