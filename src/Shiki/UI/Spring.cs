using System;
using System.Windows;
using System.Windows.Media;

namespace Shiki.UI;

/// <summary>
/// A spring from 0 to 1, stepped every frame: quick to start, a touch of overshoot, then it
/// settles (like Apple's "duration 0.5, bounce 0.2"). Reduced motion: straight to 1.
/// </summary>
public sealed class Spring
{
    readonly Action<double> _step;
    readonly Action? _done;
    readonly double _stiffness, _damping;
    double _x, _v;
    TimeSpan _last;
    bool _running;

    /// <param name="response">Roughly how long it takes, in seconds.</param>
    /// <param name="damping">1 settles without overshoot; lower bounces a little.</param>
    public Spring(Action<double> step, Action? done = null, double response = 0.45, double damping = 0.78)
    {
        _step = step;
        _done = done;
        _stiffness = Math.Pow(2 * Math.PI / response, 2);
        _damping = 2 * damping * Math.Sqrt(_stiffness);
    }

    public Spring Start()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _step(1);
            _done?.Invoke();
            return this;
        }
        _running = true;
        _step(0);
        CompositionTarget.Rendering += Tick;
        return this;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= Tick;
    }

    void Tick(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;
        double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _last).TotalSeconds, 0, 0.05);
        _last = now;
        // Small steps, so it behaves the same at any frame rate.
        for (double left = dt; left > 0; left -= 1 / 480.0)
        {
            double h = Math.Min(left, 1 / 480.0);
            _v += (-_stiffness * (_x - 1) - _damping * _v) * h;
            _x += _v * h;
        }
        bool settled = Math.Abs(_x - 1) < 0.002 && Math.Abs(_v) < 0.02;
        _step(settled ? 1 : _x);
        if (!settled) return;
        Stop();
        _done?.Invoke();
    }
}
