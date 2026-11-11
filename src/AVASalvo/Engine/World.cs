namespace AVASalvo.Engine;

/// <summary>The weapons, in the order the weapon key steps through them.</summary>
public enum Weapon { Shell, Heavy, Cluster, Digger, Roller, Dirt }

/// <summary>What a weapon does, and how many a tank starts a round with (−1: as many as you like).</summary>
public sealed record WeaponInfo(string Name, double Radius, double Damage, int Ammo, string Note);

/// <summary>Who plays a tank: a person at the keyboard, or the computer at one of three strengths.</summary>
public enum Seat { Human, Easy, Normal, Hard }

public sealed class Tank
{
    public required int Id;
    public required string Name;
    public required uint Colour;
    public required Seat Seat;
    public double X, Y;
    /// <summary>The barrel's angle in degrees: 0 points right, 90 straight up, 180 left.</summary>
    public double Angle = 90;
    /// <summary>0 to 100: how hard the shot goes.</summary>
    public double Power = 55;
    public double Health = 100;
    public Weapon Weapon;
    public int[] Ammo = new int[6];
    public bool Alive => Health > 0;
    /// <summary>Points over the whole match.</summary>
    public int Score;
    public int RoundWins;
    /// <summary>How fast it's falling, when the ground under it has gone.</summary>
    public double Fall;
    public double FallFrom;
    public bool IsComputer => Seat != Seat.Human;
}

/// <summary>Something in the air: a shell, a bomblet, a digger burrowing or a roller rolling.</summary>
public sealed class Shot
{
    public double X, Y, Vx, Vy;
    public Weapon Kind;
    public required Tank Owner;
    /// <summary>A cluster splits as it starts to fall; its bomblets don't.</summary>
    public bool Split;
    /// <summary>A digger's or a roller's way along the ground, once it's landed.</summary>
    public bool Grounded;
    public double Travel;
    public int Dir;
    public List<(double X, double Y)> Trail = [];
}

public sealed class Blast
{
    public double X, Y, Radius, Age;
    public bool Dirt;
}

/// <summary>Bits of dirt and fire thrown up by a blast, for show.</summary>
public sealed class Spark
{
    public double X, Y, Vx, Vy, Life, Age;
    public uint Colour;
}

/// <summary>
/// The battlefield: the ground as a height for every column of the screen (dirt with nothing under it falls, so a
/// crater is a dip and a tunnel collapses), the tanks on it, and the shots and blasts in flight. Fixed 60 Hz steps.
/// </summary>
public sealed class World
{
    public const double Width = 1024, Floor = 680, Step = 1 / 60.0;
    public const double Gravity = 230, PowerScale = 7.2, WindScale = 2.2;
    public const int Columns = 1024;

    public static readonly WeaponInfo[] Weapons =
    [
        new("SHELL", 30, 42, -1, "AS MANY AS YOU LIKE"),
        new("HEAVY SHELL", 58, 75, 3, "A BIGGER BANG"),
        new("CLUSTER", 22, 26, 2, "SPLITS IN FIVE AS IT FALLS"),
        new("DIGGER", 16, 30, 2, "BURROWS ON INTO THE GROUND"),
        new("ROLLER", 36, 50, 2, "ROLLS DOWNHILL, THEN GOES OFF"),
        new("DIRT BOMB", 52, 0, 2, "BURIES WHATEVER IT LANDS ON"),
    ];

    /// <summary>The ground's top, in screen y (bigger is lower), for every column.</summary>
    public readonly double[] Ground = new double[Columns];
    public List<Tank> Tanks { get; } = [];
    public List<Shot> Shots { get; } = [];
    public List<Blast> Blasts { get; } = [];
    public List<Spark> Sparks { get; } = [];
    /// <summary>Sounds that happened this step, for the engine to pass on.</summary>
    public List<Sfx> Heard { get; } = [];
    /// <summary>Damage done this turn, by tank, to whom.</summary>
    public List<(Tank By, Tank To, double Amount, bool Killed)> Hits { get; } = [];

    /// <summary>The wind: positive blows to the right. About −40 to 40.</summary>
    public double Wind;
    public int Round;
    public int Seed;
    private Dice _dice = new(1);

    public bool Busy => Shots.Count > 0 || Blasts.Count > 0 || Tanks.Any(t => t.Alive && (t.Fall > 0 || t.Y < GroundAt(t.X) - 0.5)) || _settling;
    private bool _settling;

    public static double Speed(double power) => power * PowerScale;

    public double GroundAt(double x) => Ground[Math.Clamp((int)x, 0, Columns - 1)];

    /// <summary>A new field: rolling hills from a seed, a little different every round, and the tanks spaced along it.</summary>
    public void Build(int seed, IReadOnlyList<Tank> tanks)
    {
        Seed = seed;
        _dice = new Dice(seed);
        var d = _dice;
        double baseY = 470 + d.Range(-50, 50);
        double a1 = d.Range(40, 110), a2 = d.Range(15, 50), a3 = d.Range(5, 18);
        double f1 = d.Range(0.004, 0.009), f2 = d.Range(0.012, 0.025), f3 = d.Range(0.04, 0.07);
        double p1 = d.Range(0, 6.3), p2 = d.Range(0, 6.3), p3 = d.Range(0, 6.3);
        int style = d.Next(3);
        for (int x = 0; x < Columns; x++)
        {
            double y = baseY + a1 * Math.Sin(x * f1 + p1) + a2 * Math.Sin(x * f2 + p2) + a3 * Math.Sin(x * f3 + p3);
            // A round in three has a mesa or a valley in the middle.
            double mid = Math.Exp(-Math.Pow((x - 512) / 140.0, 2));
            if (style == 1) y -= 120 * mid;
            if (style == 2) y += 110 * mid;
            Ground[x] = Math.Clamp(y, 230, Floor - 30);
        }

        Tanks.Clear();
        Tanks.AddRange(tanks);
        int n = tanks.Count;
        // Spread the tanks out, in an order shuffled each round, with a little jitter.
        var order = Enumerable.Range(0, n).OrderBy(_ => d.Next(1000)).ToList();
        for (int i = 0; i < n; i++)
        {
            var t = tanks[order[i]];
            double x = 90 + (Width - 180) * (n == 1 ? 0.5 : i / (double)(n - 1)) + d.Range(-30, 30);
            t.X = Math.Clamp(x, 60, Width - 60);
            Flatten(t.X, 22);
            t.Y = GroundAt(t.X);
            t.Health = 100;
            t.Fall = 0;
            t.Angle = t.X < Width / 2 ? 60 : 120;
            t.Power = 55;
            t.Weapon = Weapon.Shell;
            for (int w = 0; w < Weapons.Length; w++) t.Ammo[w] = Weapons[w].Ammo;
        }

        Shots.Clear();
        Blasts.Clear();
        Sparks.Clear();
        Wind = d.Range(-25, 25);
    }

    /// <summary>A little pad of level ground for a tank to sit on, eased into the slope either side.</summary>
    private void Flatten(double x, int half)
    {
        int c = (int)x;
        double y = 0;
        int n = 0;
        for (int i = c - half; i <= c + half; i++)
            if (i is >= 0 and < Columns) { y += Ground[i]; n++; }
        y /= Math.Max(1, n);
        const int ramp = 40;
        for (int i = c - half - ramp; i <= c + half + ramp; i++)
        {
            if (i is < 0 or >= Columns) continue;
            int off = Math.Abs(i - c) - half;
            double k = off <= 0 ? 1 : 0.5 + 0.5 * Math.Cos(Math.PI * off / ramp);
            Ground[i] += (y - Ground[i]) * k;
        }
    }

    /// <summary>The wind changes a little between turns, and now and then a lot.</summary>
    public void ShiftWind()
    {
        Wind = _dice.Next(6) == 0 ? _dice.Range(-40, 40) : Math.Clamp(Wind + _dice.Range(-10, 10), -40, 40);
    }

    public double Rand(double lo, double hi) => _dice.Range(lo, hi);

    /// <summary>Fires the tank's weapon from the end of its barrel.</summary>
    public void Fire(Tank t)
    {
        double a = t.Angle * Math.PI / 180, v = Speed(t.Power);
        var (mx, my) = Muzzle(t);
        Shots.Add(new Shot { X = mx, Y = my, Vx = v * Math.Cos(a), Vy = -v * Math.Sin(a), Kind = t.Weapon, Owner = t });
        if (t.Ammo[(int)t.Weapon] > 0) t.Ammo[(int)t.Weapon]--;
        if (t.Ammo[(int)t.Weapon] == 0) t.Weapon = Weapon.Shell;
        Hits.Clear();
        Heard.Add(t.Weapon == Weapon.Heavy ? Sfx.FireHeavy : Sfx.Fire);
    }

    public static (double X, double Y) Muzzle(Tank t)
    {
        double a = t.Angle * Math.PI / 180;
        return (t.X + 18 * Math.Cos(a), t.Y - 10 - 18 * Math.Sin(a));
    }

    public void Tick()
    {
        double dt = Step;
        for (int i = Shots.Count - 1; i >= 0; i--) MoveShot(Shots[i], dt);
        foreach (var b in Blasts) b.Age += dt;
        Blasts.RemoveAll(b => b.Age > 0.6);
        foreach (var s in Sparks)
        {
            s.Age += dt;
            s.Vy += Gravity * dt;
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
        }
        Sparks.RemoveAll(s => s.Age > s.Life || s.Y > Floor + 40);
        Settle(dt);
    }

    private void MoveShot(Shot s, double dt)
    {
        if (s.Grounded)
        {
            Burrow(s, dt);
            return;
        }

        s.Vx += Wind * WindScale * dt;
        s.Vy += Gravity * dt;
        s.X += s.Vx * dt;
        s.Y += s.Vy * dt;
        if (s.Trail.Count == 0 || Math.Abs(s.Trail[^1].X - s.X) + Math.Abs(s.Trail[^1].Y - s.Y) > 6) s.Trail.Add((s.X, s.Y));
        if (s.Trail.Count > 60) s.Trail.RemoveAt(0);

        // A cluster splits into five as it passes the top of its flight.
        if (s.Kind == Weapon.Cluster && !s.Split && s.Vy > 0)
        {
            Shots.Remove(s);
            for (int k = -2; k <= 2; k++)
                Shots.Add(new Shot { X = s.X, Y = s.Y, Vx = s.Vx + k * 45, Vy = s.Vy - 20, Kind = Weapon.Cluster, Owner = s.Owner, Split = true });
            Heard.Add(Sfx.Split);
            return;
        }

        if (s.X < -40 || s.X > Width + 40 || s.Y > Floor + 60)
        {
            Shots.Remove(s);
            return;
        }

        // Straight into a tank goes off at once.
        foreach (var t in Tanks)
            if (t.Alive && Math.Abs(s.X - t.X) < 16 && s.Y > t.Y - 18 && s.Y < t.Y + 2)
            {
                Detonate(s, s.X, s.Y);
                return;
            }

        if (s.Y >= GroundAt(s.X) && s.X is >= 0 and < Width)
        {
            double y = GroundAt(s.X);
            if (s.Kind is Weapon.Digger or Weapon.Roller)
            {
                s.Grounded = true;
                s.Y = y;
                s.Dir = s.Kind == Weapon.Roller ? (Slope(s.X) > 0 ? 1 : Slope(s.X) < 0 ? -1 : Math.Sign(s.Vx) == 0 ? 1 : Math.Sign(s.Vx)) : Math.Sign(s.Vx) == 0 ? 1 : Math.Sign(s.Vx);
                Heard.Add(s.Kind == Weapon.Digger ? Sfx.Dig : Sfx.Thud);
                return;
            }

            Detonate(s, s.X, y);
        }
    }

    /// <summary>Which way the ground falls away at x: +1 down to the right.</summary>
    private double Slope(double x) => GroundAt(x + 6) - GroundAt(x - 6);

    /// <summary>A digger tunnels on and down for a while; a roller rolls down until the ground turns up, then goes off.</summary>
    private void Burrow(Shot s, double dt)
    {
        if (s.Kind == Weapon.Digger)
        {
            s.Travel += 180 * dt;
            s.X += s.Dir * 150 * dt;
            s.Y += 70 * dt;
            Carve(s.X, s.Y, 9, collapse: false);
            foreach (var t in Tanks)
                if (t.Alive && Math.Abs(s.X - t.X) < 14 && Math.Abs(s.Y - t.Y) < 18)
                {
                    Detonate(s, s.X, s.Y);
                    return;
                }
            if (s.Travel > 150 || s.X < 0 || s.X >= Width || s.Y > Floor) Detonate(s, s.X, s.Y);
            return;
        }

        // The roller.
        s.Travel += dt;
        double next = s.X + s.Dir * 110 * dt;
        bool uphill = GroundAt(next) < GroundAt(s.X) - 1.2;
        bool tank = Tanks.Any(t => t.Alive && t != s.Owner && Math.Abs(next - t.X) < 16 && Math.Abs(GroundAt(next) - t.Y) < 20);
        if (uphill || tank || next < 0 || next >= Width || s.Travel > 5)
        {
            Detonate(s, s.X, GroundAt(s.X) - 4);
            return;
        }
        s.X = next;
        s.Y = GroundAt(s.X);
    }

    private void Detonate(Shot s, double x, double y)
    {
        Shots.Remove(s);
        var info = Weapons[(int)s.Kind];
        if (s.Kind == Weapon.Dirt)
        {
            Pile(x, y, info.Radius);
            Blasts.Add(new Blast { X = x, Y = y, Radius = info.Radius, Dirt = true });
            Heard.Add(Sfx.Dirt);
            Throw(x, y, 18, 0xFF8A6A3A);
            return;
        }

        Explode(x, y, info.Radius, info.Damage, s.Owner);
    }

    /// <summary>A blast: a crater, damage to every tank in reach (less the further off), and maybe more blasts.</summary>
    public void Explode(double x, double y, double radius, double damage, Tank by)
    {
        Carve(x, y, radius, collapse: true);
        Blasts.Add(new Blast { X = x, Y = y, Radius = radius });
        Heard.Add(radius > 40 ? Sfx.BigBoom : Sfx.Boom);
        Throw(x, y, (int)(radius * 0.8), 0xFFFFB040);
        foreach (var t in Tanks.ToList())
        {
            if (!t.Alive) continue;
            double d = Math.Sqrt((t.X - x) * (t.X - x) + (t.Y - 8 - y) * (t.Y - 8 - y));
            if (d > radius + 10) continue;
            double amount = damage * Math.Clamp(1 - (d - 10) / radius, 0.15, 1);
            Hurt(t, amount, by);
        }
    }

    public void Hurt(Tank t, double amount, Tank by)
    {
        if (!t.Alive || amount <= 0) return;
        amount = Math.Min(amount, t.Health);
        t.Health -= amount;
        bool killed = t.Health <= 0.5;
        if (killed) t.Health = 0;
        Hits.Add((by, t, amount, killed));
        if (by != t) by.Score += (int)Math.Round(amount);
        Heard.Add(Sfx.Hit);
        if (killed)
        {
            if (by != t) by.Score += 100;
            Heard.Add(Sfx.TankDeath);
            // A tank goes up with a blast of its own, which can catch a neighbour.
            Explode(t.X, t.Y - 6, 34, 22, by);
        }
    }

    /// <summary>Takes a circle out of the ground. With <paramref name="collapse"/>, the dirt above the hole falls into it.</summary>
    public void Carve(double x, double y, double r, bool collapse)
    {
        for (int c = (int)(x - r); c <= (int)(x + r); c++)
        {
            if (c < 0 || c >= Columns) continue;
            double dx = c - x, h = Math.Sqrt(Math.Max(0, r * r - dx * dx));
            double top = y - h, bottom = y + h;
            double g = Ground[c];
            if (bottom <= g) continue;
            // The part of the hole that was dirt; everything above it drops by that much.
            double removed = bottom - Math.Max(top, g);
            Ground[c] = Math.Min(Floor + 20, g + removed);
        }
        _ = collapse;
        _settling = true;
    }

    /// <summary>Puts a mound of dirt down, a circle's worth round where it lands.</summary>
    public void Pile(double x, double y, double r)
    {
        for (int c = (int)(x - r); c <= (int)(x + r); c++)
        {
            if (c < 0 || c >= Columns) continue;
            double dx = c - x, h = Math.Sqrt(Math.Max(0, r * r - dx * dx));
            Ground[c] = Math.Max(120, Ground[c] - h * 1.4);
        }
        _settling = true;
    }

    private void Throw(double x, double y, int n, uint colour)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _dice.Range(Math.PI * 0.1, Math.PI * 0.9), v = _dice.Range(80, 320);
            Sparks.Add(new Spark { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = -Math.Sin(a) * v, Life = _dice.Range(0.4, 1.1), Colour = i % 3 == 0 ? 0xFF8A6A3A : colour });
        }
    }

    /// <summary>Tanks sitting on air fall, and a long fall hurts. A buried tank stays buried until the dirt is shot away.</summary>
    private void Settle(double dt)
    {
        _settling = false;
        foreach (var t in Tanks)
        {
            if (!t.Alive) continue;
            double ground = GroundAt(t.X);
            if (t.Y < ground - 0.5)
            {
                if (t.Fall == 0) t.FallFrom = t.Y;
                t.Fall += Gravity * 1.4 * dt;
                t.Y = Math.Min(ground, t.Y + t.Fall * dt);
                if (t.Y >= ground - 0.5) Land(t, ground);
            }
            else if (t.Fall > 0) Land(t, ground);
            else if (t.Y > ground + 0.5)
            {
                // Buried: it rides up on the new dirt, as if dug out.
                t.Y = ground;
            }
        }
    }

    private void Land(Tank t, double ground)
    {
        t.Y = ground;
        double drop = t.Y - t.FallFrom;
        t.Fall = 0;
        if (drop > 24)
        {
            Heard.Add(Sfx.Thud);
            var last = Hits.Count > 0 ? Hits[^1].By : t;
            Hurt(t, (drop - 24) * 0.45, last);
        }
    }
}

/// <summary>A small seeded random number generator (xorshift), so a field and its winds come out the same from a seed.</summary>
public sealed class Dice(int seed)
{
    private ulong _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL;

    public ulong NextULong()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return _s;
    }

    public int Next(int n) => (int)(NextULong() % (ulong)Math.Max(1, n));

    public double Unit() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public double Range(double lo, double hi) => lo + (hi - lo) * Unit();
}
