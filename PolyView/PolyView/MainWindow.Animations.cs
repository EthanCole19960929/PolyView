using System.Windows.Threading;

namespace PolyView;

public partial class MainWindow
{
    bool _animationUpdateQueued;

    void InitAnimations()
    {
        Animations.Attach(Viewport);
        Viewport.SelectionChanged += (_, _) => QueueAnimationUpdate();
        Viewport.SceneChanged += (_, _) => QueueAnimationUpdate();
        Closed += (_, _) => { Animations.CancelScan(); Viewport.StopAnimation(); };
        QueueAnimationUpdate();
    }

    void QueueAnimationUpdate()
    {
        if (_animationUpdateQueued) return;
        _animationUpdateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _animationUpdateQueued = false;
            var source = Viewport.GetAnimationSource();
            bool changedModel = source?.ObjectId != Animations.Model?.ObjectId;
            AnimationTab.IsEnabled = source != null;
            if (changedModel)
            {
                Viewport.StopAnimation();
                if (source != null) SideTabs.SelectedItem = AnimationTab;
            }
            Animations.SetModel(source);
        }, DispatcherPriority.Background);
    }
}
