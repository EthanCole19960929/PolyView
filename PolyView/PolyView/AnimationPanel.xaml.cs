using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PolyView;

public partial class AnimationPanel : UserControl
{
    ModelAnimationSource? _model;
    CancellationTokenSource? _scan;
    AnimationScanResult? _result;
    bool _busy;
    int _checkedFiles;
    string? _failure;
    ModelViewport? _viewport;
    bool _updatingPlayback;
    bool _bindingClips;

    public void Attach(ModelViewport viewport)
    {
        _viewport = viewport;
        viewport.AnimationPlaybackChanged += (_, _) => UpdatePlayback();
        UpdatePlayback();
    }

    public ModelAnimationSource? Model => _model;

    public object Describe() => new
    {
        ok = true, object_id = _model?.ObjectId, model = _model?.ModelName,
        folder = _model == null ? null : Path.GetDirectoryName(_model.ModelPath), scanning = _busy,
        clips = _result?.Clips.Select(c => new { name = c.Name, clip_name = c.ClipName, file = c.FilePath, clip_index = c.ClipIndex, relative_path = c.RelativePath, duration_seconds = c.DurationSeconds }).ToArray(),
        playback = _viewport?.PlaybackState,
        errors = _result?.Errors, error = _failure,
    };

    public AnimationPanel()
    {
        InitializeComponent();
        ApplyLanguage();
    }

    public void SetModel(ModelAnimationSource? model)
    {
        if (_model == model) return;
        _model = model;

        _ = RefreshAsync();
    }

    public void CancelScan()
    {
        _scan?.Cancel();
    }

    public async Task RefreshAsync()
    {
        _scan?.Cancel();
        _scan?.Dispose();
        var scan = _scan = new CancellationTokenSource();
        var token = scan.Token;
        var model = _model;
        _result = null;
        _failure = null;
        _checkedFiles = 0;
        var previous = ClipList.SelectedItem as AnimationClipInfo;
        _bindingClips = true;
        ClipList.ItemsSource = null;
        _bindingClips = false;
        _busy = model != null;
        ApplyLanguage();
        if (model == null) return;

        try
        {
            var progress = new Progress<int>(count =>
            {
                if (token.IsCancellationRequested || _scan != scan || !_busy) return;
                _checkedFiles = count;
                UpdateStatus();
            });
            var result = await Task.Run(() => AnimationCatalog.Scan(model.ModelPath, token, progress), token);
            if (token.IsCancellationRequested || _scan != scan) return;
            _result = result;
            _bindingClips = true;
            ClipList.ItemsSource = result.Clips;
            ClipList.SelectedItem = result.Clips.FirstOrDefault(c => c.FilePath == previous?.FilePath && c.ClipIndex == previous.ClipIndex)
                ?? result.Clips.FirstOrDefault();
            _bindingClips = false;
            FilterClips();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_scan != scan || token.IsCancellationRequested) return;
            _failure = ex.Message;
            App.LogException("AnimationScan", ex);
        }
        finally
        {
            if (_scan == scan)
            {
                _busy = false;
                ApplyLanguage();
            }
        }
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint != null) SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ClipList != null) FilterClips();
    }

    void FilterClips()
    {
        if (ClipList.ItemsSource != null)
        {
            string filter = SearchBox.Text.Trim();
            CollectionViewSource.GetDefaultView(ClipList.ItemsSource).Filter = item => item is AnimationClipInfo clip
                && (clip.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || clip.ClipName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || clip.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        UpdateStatus();
    }

    public void ApplyLanguage()
    {
        Heading.Text = _model == null ? UiText.AnimationTitle : $"{UiText.AnimationTitle} · {_model.ModelName}";
        RefreshButton.Content = UiText.AnimationRefresh;
        RefreshButton.ToolTip = UiText.AnimationRefreshHint;
        SearchBox.ToolTip = UiText.AnimationSearch;
        SearchHint.Text = UiText.AnimationSearch;
        NameColumn.Header = UiText.AnimationName;
        DurationColumn.Header = UiText.AnimationDuration;
        StopButton.Content = UiText.AnimationStop;
        LoopCheck.Content = UiText.AnimationLoop;
        Timeline.ToolTip = UiText.AnimationSeek;
        UpdatePlayback();
        UpdateStatus();
    }

    void UpdateStatus()
    {
        if (EmptyText == null) return;
        EmptyText.Text = _busy ? UiText.AnimationScanning : _failure != null ? UiText.AnimationScanFailed : UiText.AnimationEmpty;
        EmptyText.Visibility = ClipList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScanStatus.Text = _viewport?.PlaybackState.Error ?? (_viewport?.PlaybackState.IsLoading == true ? UiText.AnimationLoading : null)
            ?? (_busy ? UiText.AnimationProgress(_checkedFiles)
            : _failure ?? (_result == null ? "" : UiText.AnimationFound(_result.Clips.Count, _result.Errors.Count)));
        ScanStatus.ToolTip = _viewport?.PlaybackState.Error ?? _failure ?? (_result == null ? null : string.Join("\n", _result.Errors));
    }

    async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_viewport?.PlaybackState.IsPlaying == true) _viewport.PauseAnimation();
        else await PlaySelected();
    }

    void Stop_Click(object sender, RoutedEventArgs e) => _viewport?.StopAnimation();
    void Loop_Changed(object sender, RoutedEventArgs e) { if (!_updatingPlayback) _viewport?.SetAnimationLoop(LoopCheck.IsChecked == true); }
    void Speed_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingPlayback && SpeedBox.SelectedIndex >= 0) _viewport?.SetAnimationSpeed(new[] { 0.25, 0.5, 1, 1.5, 2, 4 }[SpeedBox.SelectedIndex]);
    }
    void Timeline_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingPlayback) _viewport?.SeekAnimation(e.NewValue);
    }
    async void Clip_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_bindingClips) return;
        if ((_viewport?.PlaybackState.IsPlaying == true || _viewport?.PlaybackState.IsLoading == true) && ClipList.SelectedItem != null) await PlaySelected();
        UpdatePlayback();
    }
    async void Clip_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ClipList, e.OriginalSource as DependencyObject) is DataGridRow) await PlaySelected();
    }
    async Task PlaySelected()
    {
        if (_viewport != null && ClipList.SelectedItem is AnimationClipInfo clip) await _viewport.PlayAnimationAsync(clip);
        else _viewport?.ResumeAnimation();
    }

    public object StartPlayback(string? name = null)
    {
        var clip = name == null ? ClipList.SelectedItem as AnimationClipInfo
            : _result?.Clips.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (clip == null) return new { ok = false, error = "Select an animation or provide a valid animation name." };
        _bindingClips = true;
        ClipList.SelectedItem = clip;
        _bindingClips = false;
        _ = _viewport?.PlayAnimationAsync(clip);
        return Describe();
    }

    void UpdatePlayback()
    {
        if (PlayButton == null) return;
        var state = _viewport?.PlaybackState;
        _updatingPlayback = true;
        try
        {
            PlayButton.Content = state?.IsPlaying == true ? UiText.AnimationPause : UiText.AnimationPlay;
            PlayButton.IsEnabled = _viewport != null && (ClipList.SelectedItem != null || state?.ObjectId != null) && state?.IsLoading != true;
            StopButton.IsEnabled = state?.ObjectId != null || state?.IsLoading == true;
            Timeline.IsEnabled = state?.ObjectId != null;
            Timeline.Maximum = Math.Max(state?.Duration ?? 0, 0.001);
            Timeline.Value = state?.Time ?? 0;
            LoopCheck.IsChecked = state?.Loop ?? true;
            PlaybackTime.Text = $"{state?.Time ?? 0:0.00} / {state?.Duration ?? 0:0.00} s";
        }
        finally { _updatingPlayback = false; }
        UpdateStatus();
    }
}
