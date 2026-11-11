using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASalvo.Engine;
using AVASalvo.Views;

namespace AVASalvo.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SALVO_SHOTS to a folder to save them
/// as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 5)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avasalvo-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SALVO_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASalvo-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Plays on, the computer on every seat, until the moment comes.</summary>
    private static bool Until(GameEngine g, GameCanvas c, Func<GameEngine, bool> moment, int seconds = 400)
    {
        for (int i = 0; i < 60 * seconds; i++)
        {
            if (moment(g)) return true;
            g.Tick();
            if (i % 3 == 0) c.Advance(0.05);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_setup()
    {
        var (w, c, g) = Open();
        g.Run(20);
        c.Advance(4.2);
        Shot(w, c, "title");
        g.OpenSetup();
        g.Adjust(0, 1);
        g.Adjust(3, 2);
        g.Run(2);
        Shot(w, c, "setup");
    }

    [AvaloniaFact]
    public void Lining_up_a_shot()
    {
        var (w, c, g) = Open(9);
        g.Go(Seat.Human, Seat.Normal, Seat.Normal);
        var me = g.Tanks[0];
        g.GiveTurn(me);
        me.Angle = me.X < 512 ? 52 : 128;
        me.Power = 64;
        me.Weapon = Weapon.Heavy;
        g.World.Wind = 14;
        c.Advance(1);
        Shot(w, c, "aim");
    }

    [AvaloniaFact]
    public void A_shot_in_flight_and_a_blast()
    {
        var (w, c, g) = Open(4);
        g.Go(Seat.Hard, Seat.Hard, Seat.Normal, Seat.Easy);
        Assert.True(Until(g, c, g => g.Phase == Phase.Flight && g.World.Shots.Any(s => s.Trail.Count > 25)));
        Shot(w, c, "flight");
        Assert.True(Until(g, c, g => g.World.Blasts.Any(b => b.Age is > 0.08 and < 0.16 && !b.Dirt)));
        Shot(w, c, "blast");
    }

    [AvaloniaFact]
    public void Craters_after_a_few_turns()
    {
        var (w, c, g) = Open(13);
        g.Go(Seat.Hard, Seat.Normal, Seat.Normal);
        int turns = 0;
        Tank? last = null;
        Assert.True(Until(g, c, g =>
        {
            if (g.Current != last && g.Phase == Phase.Aim) { turns++; last = g.Current; }
            return turns >= 6 && g.Phase == Phase.Aim && g.PhaseTime > 1;
        }));
        Shot(w, c, "craters");
    }

    [AvaloniaFact]
    public void The_round_and_the_match_over()
    {
        var (w, c, g) = Open(21);
        g.Go(Seat.Human, Seat.Hard);
        // The person simply stands there and lets the computer shoot: it does the work.
        bool PassTurn(GameEngine g)
        {
            if (g.State == GameState.Playing && g.Phase == Phase.Aim && g.Current == g.Tanks[0])
            {
                g.Tanks[0].Health = Math.Min(g.Tanks[0].Health, 30);
                g.GiveTurn(g.Tanks[1]);
            }
            return false;
        }

        Assert.True(Until(g, c, g => PassTurn(g) || g.Phase == Phase.RoundOver && g.PhaseTime > 0.6));
        Shot(w, c, "round-over");
        Assert.True(Until(g, c, g => PassTurn(g) || g.State == GameState.GameOver && g.StateTime > 2, 1200));
        Shot(w, c, "match-over");
    }

    [AvaloniaFact]
    public void The_high_scores()
    {
        var (w, c, g) = Open();
        g.HighScores.Insert("AVA", 3100, 5);
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        for (int i = 0; i < 60 * 200 && g.State != GameState.HighScores; i++) g.Tick();
        Assert.Equal(GameState.HighScores, g.State);
        c.Advance(1.5);
        Shot(w, c, "high-scores");
    }
}
