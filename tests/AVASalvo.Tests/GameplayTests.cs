using Avalonia.Input;
using AVASalvo.Engine;

namespace AVASalvo.Tests;

public sealed class GameplayTests
{
    private static HighScoreTable TempTable() => HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avasalvo-" + Guid.NewGuid().ToString("N")));

    private static GameEngine Engine(int seed = 7) => new(TempTable(), seed) { Silent = true };

    /// <summary>A flat field at y = 500 with tanks at the given x positions.</summary>
    private static (World W, List<Tank> T) Flat(params double[] xs)
    {
        var tanks = xs.Select((x, i) => new Tank { Id = i, Name = $"T{i}", Colour = 0xFFFFFFFF, Seat = Seat.Hard }).ToList();
        var w = new World();
        w.Build(1, tanks);
        for (int c = 0; c < World.Columns; c++) w.Ground[c] = 500;
        for (int i = 0; i < xs.Length; i++) { tanks[i].X = xs[i]; tanks[i].Y = 500; }
        w.Wind = 0;
        return (w, tanks);
    }

    private static void RunOut(World w, int max = 60 * 20)
    {
        for (int i = 0; i < max && w.Busy; i++) w.Tick();
        Assert.False(w.Busy);
    }

    [Fact]
    public void A_shell_into_the_ground_digs_a_crater_and_the_rim_stays()
    {
        var (w, _) = Flat(100, 900);
        w.Explode(500, 500, 30, 40, w.Tanks[0]);
        Assert.Equal(530, w.GroundAt(500), 0);      // the full depth at the middle
        Assert.True(w.GroundAt(520) > 500 && w.GroundAt(520) < 530);
        Assert.Equal(500, w.GroundAt(560), 0);
    }

    [Fact]
    public void A_blast_under_a_hill_drops_the_dirt_above_it_into_the_hole()
    {
        var (w, _) = Flat(100, 900);
        // A tunnel 40 deep under the surface: the 60 pixels it takes out of the column all come off the top.
        w.Carve(500, 560, 30, collapse: true);
        Assert.Equal(560, w.GroundAt(500), 0);
    }

    [Fact]
    public void A_tank_left_on_air_falls_and_a_long_fall_hurts()
    {
        var (w, t) = Flat(300, 700);
        for (int c = 260; c < 340; c++) w.Ground[c] = 600;     // the ground drops away a hundred pixels
        w.Hits.Add((t[1], t[1], 0, false));
        RunOut(w);
        Assert.Equal(600, t[0].Y, 0);
        Assert.InRange(t[0].Health, 100 - (100 - 24) * 0.45 - 1, 100 - (100 - 24) * 0.45 + 1);
    }

    [Fact]
    public void Damage_falls_off_with_distance_and_a_kill_scores_for_the_shooter()
    {
        var (w, t) = Flat(300, 328, 900);
        w.Explode(300, 495, 30, 40, t[2]);
        Assert.True(t[0].Health < t[1].Health && t[1].Health < 100, $"{t[0].Health} {t[1].Health}");
        int before = t[2].Score;
        t[0].Health = 5;
        w.Explode(300, 495, 30, 40, t[2]);
        Assert.False(t[0].Alive);
        Assert.True(t[2].Score >= before + 100 + 5);
    }

    [Fact]
    public void The_wind_carries_a_shell()
    {
        var (w, t) = Flat(200, 900);
        t[0].Angle = 60;
        t[0].Power = 55;
        var still = Ai.Landing(w, t[0], 60, 42)!.Value.X;
        w.Wind = 30;
        var blown = Ai.Landing(w, t[0], 60, 42)!.Value.X;
        w.Wind = -30;
        var against = Ai.Landing(w, t[0], 60, 42)!.Value.X;
        Assert.True(blown > still + 40 && against < still - 40, $"{against} {still} {blown}");
    }

    [Fact]
    public void A_shell_flies_where_the_computer_says_it_will()
    {
        var (w, t) = Flat(200, 900);
        w.Wind = 12;
        var expected = Ai.Landing(w, t[0], 55, 45)!.Value;
        t[0].Angle = 55;
        t[0].Power = 45;
        w.Fire(t[0]);
        double lastX = 0;
        for (int i = 0; i < 60 * 10 && w.Shots.Count > 0; i++) { lastX = w.Shots[0].X; w.Tick(); }
        Assert.InRange(lastX, expected.X - 12, expected.X + 12);
    }

    [Fact]
    public void A_cluster_splits_in_five_as_it_starts_to_fall()
    {
        var (w, t) = Flat(200, 900);
        t[0].Weapon = Weapon.Cluster;
        t[0].Angle = 70;
        t[0].Power = 60;
        w.Fire(t[0]);
        Assert.Equal(1, t[0].Ammo[(int)Weapon.Cluster]);
        for (int i = 0; i < 600 && w.Shots.Count == 1; i++) w.Tick();
        Assert.Equal(5, w.Shots.Count);
        Assert.All(w.Shots, s => Assert.True(s.Split));
        RunOut(w);
    }

    [Fact]
    public void A_digger_tunnels_on_and_a_roller_rolls_downhill_before_they_go_off()
    {
        var (w, t) = Flat(200, 900);
        w.Shots.Add(new Shot { X = 500, Y = 499, Vx = 50, Vy = 50, Kind = Weapon.Digger, Owner = t[0] });
        for (int i = 0; i < 5 && w.Shots.Count > 0 && !w.Shots[0].Grounded; i++) w.Tick();
        Assert.True(w.Shots[0].Grounded);
        RunOut(w);
        Assert.True(w.Blasts.Count == 0);
        // The blast went off well along from where it went in.
        Assert.Contains(Enumerable.Range(560, 80), c => w.Ground[c] > 505);

        var (w2, t2) = Flat(100, 950);
        for (int c = 0; c < World.Columns; c++) w2.Ground[c] = 400 + c * 0.2;        // a slope down to the right
        w2.Shots.Add(new Shot { X = 300, Y = 455, Vx = -10, Vy = 20, Kind = Weapon.Roller, Owner = t2[0] });
        for (int i = 0; i < 40 && !w2.Shots[0].Grounded; i++) w2.Tick();
        Assert.Equal(1, w2.Shots[0].Dir);
        for (int i = 0; i < 60 && w2.Shots.Count > 0; i++) w2.Tick();
        Assert.True(w2.Shots.Count == 0 || w2.Shots[0].X > 320);
    }

    [Fact]
    public void A_dirt_bomb_raises_the_ground_and_buries_a_tank()
    {
        var (w, t) = Flat(300, 900);
        w.Shots.Add(new Shot { X = 300, Y = 490, Vx = 0, Vy = 100, Kind = Weapon.Dirt, Owner = t[1] });
        RunOut(w);
        Assert.True(w.GroundAt(300) < 460);
        Assert.Equal(100, t[0].Health);
    }

    [Fact]
    public void The_hard_computer_hits_a_tank_on_flat_ground_most_of_the_time()
    {
        int hits = 0;
        var random = new Random(3);
        for (int trial = 0; trial < 10; trial++)
        {
            var (w, t) = Flat(150 + trial * 10, 820);
            w.Wind = (trial - 5) * 6;
            var plan = Ai.Decide(w, t[0], random);
            var land = Ai.Landing(w, t[0], plan.Angle, plan.Power);
            if (land is { } p && Math.Abs(p.X - t[1].X) < 30) hits++;
        }
        Assert.True(hits >= 6, $"{hits} of 10");
    }

    [Fact]
    public void Turns_go_round_the_tanks_that_are_left()
    {
        var g = Engine();
        g.Go(Seat.Human, Seat.Human, Seat.Human);
        var order = new List<int>();
        for (int k = 0; k < 4; k++)
        {
            var t = g.Current!;
            order.Add(t.Id);
            t.Angle = 90;
            t.Power = 10;                       // straight up, a short way: lands back nearby
            for (int i = 0; i < 60 * 20 && g.Current == t && g.Phase != Phase.RoundOver; i++)
                g.Tick(new Controls(Fire: i % 2 == 0));
            if (k == 1) g.Tanks[(order[0] + 1) % 3].Health = 0;
        }
        Assert.Equal(4, order.Count);
        Assert.NotEqual(order[0], order[1]);
    }

    [Fact]
    public void The_last_tank_standing_wins_the_round_and_the_match_goes_on_to_the_end()
    {
        var g = Engine();
        g.Go(Seat.Human, Seat.Easy);
        Assert.Equal(3, g.Rounds);
        for (int round = 1; round <= 3; round++)
        {
            Assert.Equal(round, g.Round);
            g.Tanks[1].Health = 1;
            var shooter = g.Tanks[0];
            g.GiveTurn(shooter);
            g.World.Explode(g.Tanks[1].X, g.Tanks[1].Y, 30, 40, shooter);
            for (int i = 0; i < 60 * 30 && g.Phase != Phase.RoundOver; i++) g.Tick();
            Assert.Equal(Phase.RoundOver, g.Phase);
            Assert.Equal(shooter, g.RoundWinner);
            for (int i = 0; i < 60 * 10 && g.Phase == Phase.RoundOver && g.State == GameState.Playing; i++) g.Tick();
            if (round < 3)
            {
                for (int i = 0; i < 60 * 5 && g.Phase != Phase.Aim; i++) g.Tick();
            }
        }

        Assert.Equal(GameState.GameOver, g.State);
        Assert.Equal(g.Tanks[0], g.Champion);
        Assert.Equal(3, g.Tanks[0].RoundWins);
        Assert.True(g.Score >= 3 * GameEngine.RoundWinPoints + 300);
    }

    [Fact]
    public void A_good_score_goes_in_the_table()
    {
        var g = Engine();
        g.Go(Seat.Human, Seat.Easy);
        g.Tanks[0].Score = 50_000;
        for (int r = 0; r < 3; r++)
        {
            g.Tanks[1].Health = 1;
            g.GiveTurn(g.Tanks[0]);
            g.World.Explode(g.Tanks[1].X, g.Tanks[1].Y, 30, 40, g.Tanks[0]);
            for (int i = 0; i < 60 * 30 && g.State == GameState.Playing; i++) g.Tick();
        }

        for (int i = 0; i < 60 * 10 && g.State == GameState.GameOver; i++) g.Tick();
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (var ch in "ACE") g.Initials!.Type(ch);
        g.Tick();
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(0, g.NewEntryIndex);
        Assert.Equal("ACE", g.HighScores.Entries[0].Initials);
    }

    [Fact]
    public void The_keys_aim_the_right_way()
    {
        using var input = new InputHandler();
        var g = Engine();
        g.Go(Seat.Human, Seat.Human);
        var t = g.Current!;
        double angle = t.Angle, power = t.Power;
        input.OnKeyDown(Key.Up);
        for (int i = 0; i < 30; i++) g.Tick(input.Player);
        input.OnKeyUp(Key.Up);
        Assert.True(t.Power > power, "up is more power");
        input.OnKeyDown(Key.Right);
        for (int i = 0; i < 30; i++) g.Tick(input.Player);
        input.OnKeyUp(Key.Right);
        Assert.True(t.Angle < angle, "right swings the barrel round to the right");
        input.OnKeyDown(Key.Tab);
        g.Tick(input.Player);
        input.OnKeyUp(Key.Tab);
        Assert.Equal(Weapon.Heavy, t.Weapon);
        input.OnKeyDown(Key.Space);
        g.Tick(input.Player);
        Assert.Equal(Phase.Flight, g.Phase);
    }

    [Fact]
    public void The_setup_screen_sets_the_match()
    {
        var g = Engine();
        g.OpenSetup();
        g.Adjust(0, 1);
        Assert.Equal(3, g.PlayerCount);
        g.Adjust(0, 5);
        Assert.Equal(4, g.PlayerCount);
        g.Adjust(1, 1);
        Assert.Equal(Seat.Easy, g.Seats[0]);
        g.Adjust(g.PlayerCount + 1, 1);
        Assert.Equal(5, g.Rounds);
        g.StartGame();
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(4, g.Tanks.Count);
    }

    [Fact]
    public void The_demonstration_plays_itself_to_the_high_scores()
    {
        var g = Engine(11);
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < 60 * (GameEngine.AttractSeconds + 20) && g.State == GameState.Attract; i++) g.Tick();
        Assert.Equal(GameState.HighScores, g.State);
    }
}
