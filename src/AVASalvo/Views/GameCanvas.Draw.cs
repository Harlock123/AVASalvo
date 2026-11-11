using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASalvo.Engine;

namespace AVASalvo.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASalvo/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Shade(uint c, double k)
    {
        byte r = (byte)Math.Clamp(((c >> 16) & 0xFF) * k, 0, 255), g = (byte)Math.Clamp(((c >> 8) & 0xFF) * k, 0, 255), b = (byte)Math.Clamp((c & 0xFF) * k, 0, 255);
        return 0xFF000000 | (uint)r << 16 | (uint)g << 8 | b;
    }

    private static uint Alpha(uint c, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (c & 0xFFFFFF);

    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int s) => (uint)(((a >> s) & 0xFF) + ((((b >> s) & 0xFF) - (double)((a >> s) & 0xFF)) * t));
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    /// <summary>Each round has its own time of day: the sky's two colours, the far hills', and the ground's.</summary>
    private static (uint Top, uint Low, uint Far, uint Dirt, uint Grass) Palette(int round) => (round % 4) switch
    {
        1 => (0xFF0E1A3A, 0xFFE07848, 0xFF3A2A4A, 0xFF6A4A2A, 0xFF5A8A3A),   // dusk
        2 => (0xFF050812, 0xFF1A2448, 0xFF141A30, 0xFF3A3448, 0xFF5A6A8A),   // night
        3 => (0xFF2A6AB8, 0xFFE8D8A0, 0xFF8A7A5A, 0xFFB08850, 0xFFD8B860),   // desert
        _ => (0xFF2A70C8, 0xFFA8D8F0, 0xFF5A8AA8, 0xFF7A5A34, 0xFF4AA03A),   // day
    };

    private void DrawSky(DrawingContext dc, int round)
    {
        var (top, low, far, _, _) = Palette(round);
        for (int i = 0; i < 34; i++)
            dc.FillRectangle(Brush(Mix(top, low, i / 33.0)), new Rect(0, i * 20, World.Width, 21));
        if (round % 4 == 2)
            for (int i = 0; i < 90; i++)
            {
                uint h = Hash(i, round, 7);
                double tw = 0.5 + 0.5 * Math.Sin(_clock * 2 + i);
                dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.3 + 0.5 * tw)), new Rect(h % 1024, h / 1024 % 400, 2, 2));
            }
        else
        {
            // A sun (or a low red one at dusk), and clouds drifting with the wind.
            var sun = round % 4 == 1 ? new Point(760, 470) : new Point(820, 110);
            dc.DrawEllipse(Brush(Alpha(0xFFFFF0B0, 0.25)), null, sun, 70, 70);
            dc.DrawEllipse(Brush(round % 4 == 1 ? 0xFFFF8A50 : 0xFFFFF4C8), null, sun, 40, 40);
            double wind = Engine.State is GameState.Playing or GameState.Attract or GameState.Paused ? Engine.World.Wind : 8;
            for (int i = 0; i < 6; i++)
            {
                uint h = Hash(i, round, 3);
                double x = ((h % 1400) + _clock * (6 + wind * 0.9) * (1 + i % 3 * 0.3)) % 1400 - 200, y = 60 + h / 7 % 220;
                for (int k = 0; k < 5; k++)
                    dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.55)), null, new Point(x + k * 26, y + Math.Sin(k * 1.7) * 8), 34 - Math.Abs(k - 2) * 6, 18);
            }
        }

        // Far hills, for depth.
        var pts = new List<Point> { new(0, World.Floor) };
        for (int x = 0; x <= 1024; x += 16)
            pts.Add(new Point(x, 380 + 50 * Math.Sin(x * 0.006 + round) + 25 * Math.Sin(x * 0.017 + round * 2)));
        pts.Add(new Point(1024, World.Floor));
        Poly(dc, far, null, 0, [.. pts]);
    }

    private void DrawGround(DrawingContext dc, World w)
    {
        var (_, _, _, dirt, grass) = Palette(w.Round);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 768), true);
            for (int x = 0; x < World.Columns; x += 2) c.LineTo(new Point(x, w.Ground[x]));
            c.LineTo(new Point(1024, w.Ground[World.Columns - 1]));
            c.LineTo(new Point(1024, 768));
            c.EndFigure(true);
        }
        dc.DrawGeometry(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.3, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.FromUInt32(dirt), 0), new GradientStop(Color.FromUInt32(Shade(dirt, 0.45)), 1)],
        }, null, g);

        // A grassy edge, and pebbles in the dirt.
        var edge = new StreamGeometry();
        using (var c = edge.Open())
        {
            c.BeginFigure(new Point(0, w.Ground[0]), false);
            for (int x = 2; x < World.Columns; x += 2) c.LineTo(new Point(x, w.Ground[x]));
            c.EndFigure(false);
        }
        dc.DrawGeometry(null, Pen(grass, 5), edge);
        for (int i = 0; i < 160; i++)
        {
            uint h = Hash(i, w.Seed, 11);
            int x = (int)(h % 1024);
            double y = w.Ground[x] + 14 + h / 1024 % 300;
            if (y < 760) dc.FillRectangle(Brush(Alpha(Shade(dirt, 0.6), 0.6)), new Rect(x, y, 3, 2));
        }
    }

    private void DrawTank(DrawingContext dc, Tank t, bool current)
    {
        if (!t.Alive)
        {
            // A wreck, smoking.
            Poly(dc, 0xFF2A2A2A, 0xFF101010, 1.5, new Point(t.X - 15, t.Y), new Point(t.X - 11, t.Y - 7), new Point(t.X + 12, t.Y - 6), new Point(t.X + 15, t.Y));
            for (int k = 0; k < 3; k++)
            {
                double p = (_clock * 0.6 + k / 3.0) % 1;
                dc.DrawEllipse(Brush(Alpha(0xFF606060, 0.5 * (1 - p))), null, new Point(t.X + Math.Sin(p * 6 + t.Id) * 6, t.Y - 10 - p * 40), 4 + p * 10, 4 + p * 10);
            }
            return;
        }

        uint body = t.Colour, dark = Shade(t.Colour, 0.55);
        double a = t.Angle * Math.PI / 180;
        var pivot = new Point(t.X, t.Y - 10);
        dc.DrawLine(Pen(0xFF202020, 5), pivot, new Point(pivot.X + 20 * Math.Cos(a), pivot.Y - 20 * Math.Sin(a)));
        dc.DrawLine(Pen(Shade(body, 0.85), 3), pivot, new Point(pivot.X + 19 * Math.Cos(a), pivot.Y - 19 * Math.Sin(a)));
        // Tracks, the hull and the turret's dome.
        dc.DrawRectangle(Brush(0xFF2A2A30), null, new Rect(t.X - 16, t.Y - 6, 32, 7), 3, 3);
        for (int k = 0; k < 5; k++) dc.DrawEllipse(Brush(0xFF5A5A62), null, new Point(t.X - 12 + k * 6, t.Y - 2.5), 2, 2);
        Poly(dc, body, dark, 1.5, new Point(t.X - 15, t.Y - 6), new Point(t.X - 11, t.Y - 12), new Point(t.X + 11, t.Y - 12), new Point(t.X + 15, t.Y - 6));
        dc.DrawEllipse(Brush(body), Pen(dark, 1.5), pivot, 7, 6);

        // Health above it, and the name of whoever's turn it is.
        double hw = 32;
        dc.FillRectangle(Brush(0xA0000000), new Rect(t.X - hw / 2 - 1, t.Y - 31, hw + 2, 6));
        dc.FillRectangle(Brush(t.Health > 50 ? 0xFF5AE85A : t.Health > 25 ? Yellow : Red), new Rect(t.X - hw / 2, t.Y - 30, hw * t.Health / 100, 4));
        if (current)
        {
            double bob = Math.Sin(_clock * 6) * 3;
            Poly(dc, White, 0xFF000000, 1, new Point(t.X - 7, t.Y - 50 + bob), new Point(t.X + 7, t.Y - 50 + bob), new Point(t.X, t.Y - 40 + bob));
        }
    }

    /// <summary>A dotted line along the first part of the shot's path, for a person aiming (the computer needs no help).</summary>
    private void DrawAimGuide(DrawingContext dc, World w, Tank t)
    {
        double a = t.Angle * Math.PI / 180, v = World.Speed(t.Power);
        var (x, y) = World.Muzzle(t);
        double vx = v * Math.Cos(a), vy = -v * Math.Sin(a), dt = 1 / 60.0;
        for (int i = 0; i < 26; i++)
        {
            vy += World.Gravity * dt;
            x += vx * dt;
            y += vy * dt;
            if (i % 2 == 0) dc.DrawEllipse(Brush(Alpha(White, 0.75 - i * 0.025)), null, new Point(x, y), 2, 2);
        }
    }

    private void DrawShots(DrawingContext dc, World w)
    {
        foreach (var s in w.Shots)
        {
            for (int i = 1; i < s.Trail.Count; i++)
                dc.DrawLine(Pen(Alpha(0xFFFFFFFF, i / (double)s.Trail.Count * 0.5), 2), new Point(s.Trail[i - 1].X, s.Trail[i - 1].Y), new Point(s.Trail[i].X, s.Trail[i].Y));
            uint c = s.Kind switch { Weapon.Heavy => 0xFF303030, Weapon.Dirt => 0xFF8A6A3A, Weapon.Digger => 0xFFC0C0C8, Weapon.Roller => 0xFF505868, _ => 0xFF202020 };
            double r = s.Kind == Weapon.Heavy ? 6 : s.Kind == Weapon.Cluster && s.Split ? 3 : 4.5;
            dc.DrawEllipse(Brush(c), Pen(0xFFFFFFFF, 1), new Point(s.X, s.Y), r, r);
            if (s.Kind == Weapon.Digger && s.Grounded)
                dc.DrawEllipse(Brush(Alpha(Orange, 0.5 + 0.5 * Math.Sin(_clock * 30))), null, new Point(s.X, s.Y), 7, 7);
        }

        foreach (var b in w.Blasts)
        {
            double p = b.Age / 0.6, r = b.Radius * (0.4 + p * 0.8);
            if (b.Dirt)
            {
                dc.DrawEllipse(Brush(Alpha(0xFF8A6A3A, 0.6 * (1 - p))), null, new Point(b.X, b.Y), r, r);
                continue;
            }
            dc.DrawEllipse(Brush(Alpha(0xFFFFF0A0, 0.9 * (1 - p))), null, new Point(b.X, b.Y), r * 0.55, r * 0.55);
            dc.DrawEllipse(Brush(Alpha(0xFFFF8A2A, 0.7 * (1 - p))), null, new Point(b.X, b.Y), r * 0.85, r * 0.85);
            dc.DrawEllipse(null, Pen(Alpha(0xFFFFE0A0, 0.8 * (1 - p)), 3), new Point(b.X, b.Y), r * 1.1, r * 1.1);
        }

        foreach (var s in w.Sparks)
            dc.FillRectangle(Brush(Alpha(s.Colour, 1 - s.Age / s.Life)), new Rect(s.X - 1.5, s.Y - 1.5, 3, 3));
    }

    /// <summary>The wind, top centre: an arrow as long as it is strong.</summary>
    private void DrawWind(DrawingContext dc, World w)
    {
        double cx = 512, y = 34, len = w.Wind * 3;
        dc.FillRectangle(Brush(0x80000000), new Rect(cx - 150, y - 22, 300, 44), 8);
        Text(dc, $"WIND {Math.Abs(w.Wind):0}", new Point(cx, y - 8), 22, White, center: true);
        if (Math.Abs(w.Wind) >= 1)
        {
            double x0 = cx - len / 2, x1 = cx + len / 2, dir = Math.Sign(w.Wind);
            dc.DrawLine(Pen(Cyan, 4), new Point(x0, y + 11), new Point(x1, y + 11));
            Poly(dc, Cyan, null, 0, new Point(x1 + dir * 10, y + 11), new Point(x1, y + 4), new Point(x1, y + 18));
        }
    }

    private void DrawScene(DrawingContext dc, World w)
    {
        DrawSky(dc, w.Round);
        DrawGround(dc, w);
        var g = Engine;
        foreach (var t in w.Tanks) DrawTank(dc, t, t == g.Current && g.Phase == Phase.Aim);
        if (g.Phase == Phase.Aim && g.Current is { IsComputer: false } me && g.State == GameState.Playing) DrawAimGuide(dc, w, me);
        DrawShots(dc, w);
        DrawWind(dc, w);
    }
}
