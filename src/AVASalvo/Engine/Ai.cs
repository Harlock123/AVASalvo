namespace AVASalvo.Engine;

/// <summary>
/// The computer's gunner. It picks a target, then finds an angle and a power by trying shots in its head: the same
/// flight as a real shell, wind and all, against the ground as it is. The easier it is set, the more it misjudges.
/// </summary>
public static class Ai
{
    public sealed record Plan(double Angle, double Power, Weapon Weapon, Tank? Target, double Miss);

    /// <summary>Where a shot from <paramref name="t"/> at this angle and power would come down, or null if off the field.</summary>
    public static (double X, double Y)? Landing(World w, Tank t, double angle, double power)
    {
        double a = angle * Math.PI / 180, v = World.Speed(power);
        double mx = t.X + 18 * Math.Cos(a), my = t.Y - 10 - 18 * Math.Sin(a);
        double x = mx, y = my, vx = v * Math.Cos(a), vy = -v * Math.Sin(a), dt = 1 / 60.0;
        for (int i = 0; i < 900; i++)
        {
            vx += w.Wind * World.WindScale * dt;
            vy += World.Gravity * dt;
            x += vx * dt;
            y += vy * dt;
            if (x < -40 || x > World.Width + 40 || y > World.Floor + 60) return null;
            foreach (var o in w.Tanks)
                if (o != t && o.Alive && Math.Abs(x - o.X) < 16 && y > o.Y - 18 && y < o.Y + 2) return (x, y);
            if (i > 3 && x is >= 0 and < World.Width && y >= w.GroundAt(x)) return (x, w.GroundAt(x));
        }

        return null;
    }

    public static Plan Decide(World w, Tank me, Random random)
    {
        var enemies = w.Tanks.Where(o => o != me && o.Alive).ToList();
        if (enemies.Count == 0) return new Plan(me.Angle, me.Power, Weapon.Shell, null, 0);

        // The weakest tank in reach, or failing that the nearest.
        var target = enemies.OrderBy(o => o.Health * 4 + Math.Abs(o.X - me.X)).First();
        (double Angle, double Power, double Miss) best = (me.Angle, me.Power, double.MaxValue);
        bool right = target.X > me.X;
        for (double angle = right ? 15 : 95; angle <= (right ? 85 : 165); angle += 1.5)
        for (double power = 20; power <= 100; power += 1.5)
        {
            if (Landing(w, me, angle, power) is not { } p) continue;
            double miss = Math.Abs(p.X - target.X) + Math.Abs(p.Y - target.Y) * 0.3;
            // Shooting oneself is never wanted.
            if (Math.Abs(p.X - me.X) < 40) miss += 400;
            if (miss < best.Miss) best = (angle, power, miss);
        }

        double spread = me.Seat switch { Seat.Easy => 7, Seat.Normal => 3.2, _ => 1.1 };
        double angleOut = best.Angle + Gauss(random) * spread * 0.6;
        double powerOut = Math.Clamp(best.Power + Gauss(random) * spread, 10, 100);

        var weapon = Weapon.Shell;
        bool has(Weapon k) => me.Ammo[(int)k] != 0;
        if (target.Health <= 45 && has(Weapon.Shell)) weapon = Weapon.Shell;
        else if (has(Weapon.Heavy) && random.NextDouble() < 0.45) weapon = Weapon.Heavy;
        else if (has(Weapon.Cluster) && random.NextDouble() < 0.3) weapon = Weapon.Cluster;
        else if (has(Weapon.Roller) && random.NextDouble() < 0.15 && target.Y > me.Y) weapon = Weapon.Roller;
        return new Plan(Math.Clamp(angleOut, 2, 178), powerOut, weapon, target, best.Miss);
    }

    private static double Gauss(Random r)
    {
        double u = 1 - r.NextDouble(), v = r.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }
}
