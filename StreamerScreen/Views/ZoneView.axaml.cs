using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Timer = System.Timers.Timer;

namespace StreamerScreen.Views;

public partial class ZoneView : UserControl
{
    private Animation? _trackAnimation;
    private CancellationTokenSource? _trackAnimationCancellationTokenSource;

    public ZoneView()
    {
        InitializeComponent();
    }

    private async Task Animate(CancellationToken cancellationToken)
    {
        try
        {
            if (_trackAnimation == null)
                return;

            while (!cancellationToken.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    _trackAnimation.RunAsync(TrackScrollViewer, cancellationToken));
            }
            
            Console.WriteLine("Track animation cancelled");
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Track animation cancelled ˆ");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private void TrackTextBlock_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Design.IsDesignMode) return;

        if (_trackAnimationCancellationTokenSource != null)
        {
            _trackAnimationCancellationTokenSource.Cancel();
        }

        var txtBlock = (TextBlock) sender!;
        var scrollViewWidth = Math.Max((int)TrackScrollViewer.DesiredSize.Width, (int)TrackScrollViewer.Width);
        var maxScroll = (int)e.NewSize.Width - scrollViewWidth;
        Console.WriteLine($"lblSize = {e.NewSize.Width} - scroll = {TrackScrollViewer.DesiredSize.Width} - maxScroll = {maxScroll}");
        if (!string.IsNullOrEmpty(txtBlock.Text) && maxScroll > 10)
        {
            _trackAnimationCancellationTokenSource?.Dispose();
            _trackAnimationCancellationTokenSource = new CancellationTokenSource();
            _trackAnimation = CreateTrackAnimation(maxScroll);
            Task.Run(() => Animate(_trackAnimationCancellationTokenSource.Token));
        }
    }

    private static Animation CreateTrackAnimation(double maxScroll)
    {
        var animationLengthInSeconds = (maxScroll / 20) + 2.5d;
        Console.WriteLine($"Track animation length (s) = {animationLengthInSeconds}");
        return new Animation
        {
            Duration = TimeSpan.FromSeconds(animationLengthInSeconds),
            Delay = TimeSpan.FromSeconds(2),
            FillMode = FillMode.Backward,
            IterationCount = new IterationCount(1),
            Children =
            {
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter {Property = ScrollViewer.OffsetProperty, Value = new Vector(0, 0)},
                        new Setter {Property = ScrollViewer.OpacityProperty, Value = 1d},
                    },
                    KeyTime = TimeSpan.Zero
                },
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter {Property = ScrollViewer.OffsetProperty, Value = new Vector(maxScroll, 0)},
                        new Setter {Property = ScrollViewer.OpacityProperty, Value = 1d}
                    },
                    KeyTime = TimeSpan.FromSeconds(animationLengthInSeconds - 2.5d)
                },
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter {Property = ScrollViewer.OffsetProperty, Value = new Vector(maxScroll, 0)},
                        new Setter {Property = ScrollViewer.OpacityProperty, Value = 1d}
                    },
                    KeyTime = TimeSpan.FromSeconds(animationLengthInSeconds - 0.5d)
                },
                new KeyFrame()
                {
                    Setters =
                    {
                        new Setter {Property = ScrollViewer.OffsetProperty, Value = new Vector(maxScroll, 0)},
                        new Setter {Property = ScrollViewer.OpacityProperty, Value = 0d}
                    },
                    KeyTime = TimeSpan.FromSeconds(animationLengthInSeconds)
                }
            }
        };
    }
}