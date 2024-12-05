using System;
using System.Linq;
using System.Timers;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;

namespace StreamerScreen.Views;

public partial class ZoneView : UserControl
{
    private readonly Animation _trackAnimation;
    private readonly Timer _animationTimer;
    
    public ZoneView()
    {
        InitializeComponent();
        
        _trackAnimation = (Animation)Resources["TrackAnimation"]!;
        _animationTimer = new Timer();
        _animationTimer.Elapsed += Animate;
        _animationTimer.AutoReset = true;
    }
    
    private void Animate(object? sender, ElapsedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => _trackAnimation.RunAsync(TrackScrollViewer));
    }
    
    private async void TrackTextBlock_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Design.IsDesignMode) return;

        Console.WriteLine($"Animation timer stopped");
        _animationTimer.Stop();

        var txtBlock = (TextBlock) sender!;
        var maxSize = e.NewSize.Width - TrackScrollViewer.Bounds.Width;
        if (!string.IsNullOrEmpty(txtBlock.Text) && maxSize > 10)
        {
            var keyframe = _trackAnimation.Children.Last();
            var setter = (Setter) keyframe.Setters.First();
            setter.Value = new Vector(maxSize, 0);

            var animationLengthInSeconds = maxSize / 20;
            _trackAnimation.Duration = TimeSpan.FromSeconds(animationLengthInSeconds);
            Console.WriteLine($"Animation timer interval = {animationLengthInSeconds}");

            Dispatcher.UIThread.Post(() => _trackAnimation.RunAsync(TrackScrollViewer));

            _animationTimer.Interval = 1000 * (animationLengthInSeconds + 2);
            _animationTimer.Start();
            Console.WriteLine($"Animation timer started");
        }
    }
}