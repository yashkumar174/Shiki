using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shiki.UI;

/// <summary>
/// Four slim, rounded bars that move with the voice: each eases toward its own share of the
/// loudness, with a slow breathing drift so it's alive even in a pause. Thinking: a soft wave
/// travels across. Still: four small dots. Reduced motion: steady bars, no animation.
/// </summary>
public sealed class Waveform : Canvas
{
    public enum Modes { Still, Listening, Thinking }

    readonly Rectangle[] _bars = new Rectangle[4];
    readonly double[] _height = new double[4];
    // Each bar's share of the voice, so they don't move as one block.
    static readonly double[] Share = [0.62, 1.0, 0.84, 0.55];
    readonly double _barWidth, _gap, _tall;
    double _level, _clock;
    TimeSpan _last;
    bool _running;

    public Modes Mode { get; set; } = Modes.Still;

    /// <summary>Loudness now, 0 to 1 (the microphone's).</summary>
    public double Level { get => _level; set => _level = Math.Clamp(value, 0, 1); }

    public Waveform(Brush colour, double height = 18, double barWidth = 3, double gap = 3)
    {
        _barWidth = barWidth;
        _gap = gap;
        _tall = height;
        Width = barWidth * 4 + gap * 3;
        Height = height;
        for (int i = 0; i < 4; i++)
        {
            _bars[i] = new Rectangle { Width = barWidth, Height = barWidth, RadiusX = barWidth / 2, RadiusY = barWidth / 2, Fill = colour };
            SetLeft(_bars[i], i * (barWidth + gap));
            Children.Add(_bars[i]);
            _height[i] = barWidth;
        }
        Place();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    public Brush Colour
    {
        set { foreach (var bar in _bars) bar.Fill = value; }
    }

    void Start()
    {
        if (_running) return;
        _running = true;
        CompositionTarget.Rendering += Tick;
    }

    void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= Tick;
    }

    void Tick(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _last).TotalSeconds, 0, 0.1);
        _last = now;
        _clock += dt;
        bool motion = SystemParameters.ClientAreaAnimation;
        double min = _barWidth, room = _tall - min;
        for (int i = 0; i < 4; i++)
        {
            double target = Mode switch
            {
                // The voice, shaped per bar, plus a gentle breathing so a pause still looks alive.
                Modes.Listening => min + room * Math.Clamp(Math.Pow(_level, 0.7) * Share[i] * 1.15 + (motion ? 0.10 + 0.07 * Math.Sin(_clock * 3.1 + i * 1.3) : 0.25), 0, 1),
                // A soft wave travelling across while the answer is on its way.
                Modes.Thinking => min + room * (motion ? 0.18 + 0.32 * Math.Max(0, Math.Sin(_clock * 5.2 - i * 0.9)) : 0.35),
                _ => min,
            };
            // Ease toward it: quick to rise, a little slower to fall, so it never jitters.
            double ease = target > _height[i] ? 0.35 : 0.16;
            _height[i] += (target - _height[i]) * (motion ? Math.Min(1, ease * dt * 60) : 1);
        }
        Place();
    }

    /// <summary>Jumps straight to where it's heading (for a still picture of it).</summary>
    public void Snap()
    {
        _clock = 0.4;
        for (int i = 0; i < 4; i++) _height[i] = _barWidth + (_tall - _barWidth) * Math.Clamp(Math.Pow(_level, 0.7) * Share[i] * 1.15 + 0.10 + 0.07 * Math.Sin(_clock * 3.1 + i * 1.3), 0, 1);
        Place();
    }

    void Place()
    {
        for (int i = 0; i < 4; i++)
        {
            var h = Math.Max(_barWidth, _height[i]);
            _bars[i].Height = h;
            SetTop(_bars[i], (_tall - h) / 2);
        }
    }
}
