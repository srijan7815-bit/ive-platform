using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Ive.App.UI;

/// <summary>Damped-spring easing for storyboard animations (overshoots, then settles).</summary>
public sealed class SpringEase : EasingFunctionBase
{
    public SpringEase() => EasingMode = EasingMode.EaseIn; // EaseIn == use EaseInCore as-is

    protected override double EaseInCore(double t)
    {
        if (t >= 1) return 1;
        const double zeta = 0.42, omega = 11.0;
        var wd = omega * Math.Sqrt(1 - zeta * zeta);
        return 1 - Math.Exp(-zeta * omega * t) * (Math.Cos(wd * t) + zeta * omega / wd * Math.Sin(wd * t));
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();
}

/// <summary>
/// Real spring physics (semi-implicit Euler, sub-stepped). Interruptible: change <see cref="Target"/> at any
/// moment and velocity carries over — this is what makes the notch feel liquid instead of keyframed.
/// </summary>
public sealed class SpringValue
{
    private readonly double _k, _c;
    public double Value, Target, Velocity;

    public SpringValue(double value, double stiffness = 170, double damping = 15)
    {
        Value = Target = value;
        _k = stiffness;
        _c = damping;
    }

    public bool Settled => Math.Abs(Target - Value) < 0.02 && Math.Abs(Velocity) < 0.05;

    public void Step(double dt)
    {
        while (dt > 0)
        {
            var h = Math.Min(dt, 1.0 / 240);
            Velocity += (-_k * (Value - Target) - _c * Velocity) * h;
            Value += Velocity * h;
            dt -= h;
        }
    }

    public void Snap()
    {
        Value = Target;
        Velocity = 0;
    }
}
