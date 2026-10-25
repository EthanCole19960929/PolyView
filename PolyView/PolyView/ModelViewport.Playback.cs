using System.Diagnostics;

namespace PolyView;

public sealed record AnimationPlaybackState(int? ObjectId, string? Name, bool IsPlaying, bool IsLoading, double Time, double Duration, bool Loop, double Speed, string? Error);

public sealed partial class ModelViewport
{
    sealed class PlaybackSession(int id, MeshData original, SkeletalAnimation animation, AnimationClipInfo clip)
    {
        public readonly int Id = id;
        public readonly MeshData Original = original;
        public readonly SkeletalAnimation Animation = animation;
        public readonly AnimationClipInfo Clip = clip;
        public double Time;
        public bool Playing;
    }

    PlaybackSession? _playback;
    int _playbackLoadVersion;
    bool _playbackLoading;
    string? _playbackError;
    bool _playbackLoop = true;
    double _playbackSpeed = 1;
    long _playbackTick, _playbackUiTick;
    public event EventHandler? AnimationPlaybackChanged;
    public AnimationPlaybackState PlaybackState => new(_playback?.Id, _playback?.Clip.Name, _playback?.Playing ?? false,
        _playbackLoading, _playback?.Time ?? 0, _playback?.Animation.Duration ?? 0, _playbackLoop, _playbackSpeed, _playbackError);

    public async Task<bool> PlayAnimationAsync(AnimationClipInfo clip)
    {
        var source = GetAnimationSource();
        if (source == null || !TryGetObject(source.ObjectId, out var obj)) return false;
        if (_playback is { } current && current.Id == obj.Id && current.Clip.FilePath == clip.FilePath && current.Clip.ClipIndex == clip.ClipIndex)
        {
            current.Playing = true;
            if (current.Time >= current.Animation.Duration) SeekAnimation(0);
            _playbackTick = Stopwatch.GetTimestamp();
            RaisePlaybackChanged();
            return true;
        }
        StopAnimation();
        int version = ++_playbackLoadVersion;
        var original = obj.Mesh;
        _playbackLoading = true;
        RaisePlaybackChanged();
        try
        {
            var animation = await Task.Run(() => AssimpImporter.LoadAnimation(obj.SourcePath!, original, clip.FilePath, clip.ClipIndex));
            if (version != _playbackLoadVersion || !TryGetObject(obj.Id, out var live) || !ReferenceEquals(live.Mesh, original)) return false;
            animation.Evaluate(0);
            _playback = new PlaybackSession(obj.Id, original, animation, clip) { Playing = true };
            obj.Mesh = animation.Mesh;
            UploadPlaybackMesh(obj);
            _playbackTick = Stopwatch.GetTimestamp();
            Invalidate();
            return true;
        }
        catch (Exception ex)
        {
            if (version == _playbackLoadVersion)
            {
                _playbackError = ex.Message;
                App.LogException("AnimationPlayback", ex);
            }
            return false;
        }
        finally
        {
            if (version == _playbackLoadVersion)
            {
                _playbackLoading = false;
                RaisePlaybackChanged();
            }
        }
    }

    public void PauseAnimation()
    {
        if (_playback != null) _playback.Playing = false;
        RaisePlaybackChanged();
    }

    public void ResumeAnimation()
    {
        if (_playback == null) return;
        if (_playback.Time >= _playback.Animation.Duration) SeekAnimation(0);
        _playback.Playing = true;
        _playbackTick = Stopwatch.GetTimestamp();
        RaisePlaybackChanged();
    }

    public void StopAnimation()
    {
        _playbackLoadVersion++;
        _playbackLoading = false;
        _playbackError = null;
        if (_playback is { } session)
        {
            if (TryGetObject(session.Id, out var obj) && ReferenceEquals(obj.Mesh, session.Animation.Mesh))
            {
                obj.Mesh = session.Original;
                UploadPlaybackMesh(obj);
            }
            _partSlots.Remove(session.Animation.Mesh);
        }
        _playback = null;
        Invalidate();
        RaisePlaybackChanged();
    }

    public void SeekAnimation(double seconds)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentException("Invalid animation time.");
        if (_playback is not { } session) return;
        session.Time = Math.Clamp(seconds, 0, session.Animation.Duration);
        ApplyAnimationPose(session);
        _playbackTick = Stopwatch.GetTimestamp();
        RaisePlaybackChanged();
    }

    public void SetAnimationLoop(bool loop) { _playbackLoop = loop; RaisePlaybackChanged(); }
    public void SetAnimationSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed < 0.1 || speed > 4) throw new ArgumentException("Animation speed must be between 0.1 and 4.");
        _playbackSpeed = speed; RaisePlaybackChanged();
    }

    void AdvanceAnimation()
    {
        if (_playback is not { } session) return;
        if (!TryGetObject(session.Id, out var obj) || !ReferenceEquals(obj.Mesh, session.Animation.Mesh)) { StopAnimation(); return; }
        if (!session.Playing) return;
        long now = Stopwatch.GetTimestamp();
        double elapsed = Math.Clamp((now - _playbackTick) / (double)Stopwatch.Frequency, 0, 0.1);
        _playbackTick = now;
        session.Time += elapsed * _playbackSpeed;
        if (session.Time >= session.Animation.Duration)
        {
            if (_playbackLoop) session.Time %= session.Animation.Duration;
            else { session.Time = session.Animation.Duration; session.Playing = false; }
        }
        ApplyAnimationPose(session);
        if (!session.Playing || (now - _playbackUiTick) / (double)Stopwatch.Frequency >= 0.1)
        {
            _playbackUiTick = now;
            RaisePlaybackChanged();
        }
    }

    void ApplyAnimationPose(PlaybackSession session)
    {
        session.Animation.Evaluate(session.Time);
        _renderer?.UpdateVertices(session.Id, session.Animation.Mesh.Vertices);
        Invalidate();
    }

    void UploadPlaybackMesh(SceneObject obj)
    {
        if (_renderer == null) return;
        _renderer.AddMesh(obj.Id, obj.Mesh);
        _renderer.SetWorld(obj.Id, obj.World);
        _renderer.SetVisible(obj.Id, obj.Visible);
        bool[]? hidden = null;
        foreach (int node in obj.HiddenNodes)
        {
            var mask = PartMask(obj, node);
            hidden ??= new bool[mask.Length];
            for (int i = 0; i < mask.Length; i++) hidden[i] |= mask[i];
        }
        _renderer.SetPartHidden(obj.Id, hidden);
        if (obj.Id == _selectedId && _selectedNode >= 0) _renderer.SetPartHighlight(obj.Id, PartMask(obj, _selectedNode));
    }

    void RaisePlaybackChanged() => AnimationPlaybackChanged?.Invoke(this, EventArgs.Empty);
}
