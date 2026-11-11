using Avalonia;
using Avalonia.Media;
using AVASalvo.Engine;

namespace AVASalvo.Views;

public sealed partial class GameCanvas
{
    private const double W = 1024, H = 768;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.Setup: DrawSetup(dc); break;
            case GameState.HighScores: DrawTitleBackdrop(dc); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawScene(dc, g.World); DrawInitials(dc); break;
            case GameState.GameOver: DrawScene(dc, g.World); DrawMatchOver(dc); break;
            default:
                DrawScene(dc, g.World);
                DrawPanel(dc);
                switch (g.Phase)
                {
                    case Phase.RoundStart: DrawRoundCard(dc); break;
                    case Phase.RoundOver: DrawRoundOver(dc); break;
                }
                if (g.Message is { } msg)
                {
                    dc.FillRectangle(Brush(0xC0000000), new Rect(0, 96, W, 50));
                    Text(dc, msg, new Point(512, 120), 40, Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, W, H));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 80), 24, Yellow, center: true);
    }

    private static void Card(DrawingContext dc, Rect r)
    {
        dc.FillRectangle(Brush(0xE80A0C18), r, 14);
        dc.DrawRectangle(null, Pen(0xFF8890B8, 3), r, 14, 14);
        dc.DrawRectangle(null, Pen(0xFF3A4062, 2), r.Deflate(6), 10, 10);
    }

    /// <summary>The bottom panel: whose turn, the angle, the power, the weapon, and every tank's score.</summary>
    private void DrawPanel(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xF0101420), new Rect(0, 690, W, 78));
        dc.DrawLine(Pen(0xFF3A4062, 2), new Point(0, 690), new Point(W, 690));
        if (g.Current is { } t && g.Phase is Phase.Aim or Phase.Flight)
        {
            Text(dc, t.Name, new Point(20, 712), 34, t.Colour);
            Text(dc, t.IsComputer && g.State != GameState.Attract ? $"COMPUTER ({t.Seat.ToString().ToUpperInvariant()})" : g.State == GameState.Attract ? "COMPUTER" : "YOUR SHOT", new Point(20, 744), 20, Dim);
            Text(dc, $"ANGLE {t.Angle,5:0.0}", new Point(170, 712), 30, White);
            Text(dc, "POWER", new Point(170, 744), 24, White);
            dc.FillRectangle(Brush(0xFF202838), new Rect(236, 736, 160, 16), 4);
            dc.FillRectangle(Brush(Mix(0xFF5AE85A, Red, t.Power / 100)), new Rect(236, 736, 160 * t.Power / 100, 16), 4);
            Text(dc, $"{t.Power:0}", new Point(410, 744), 24, White);
            var info = World.Weapons[(int)t.Weapon];
            int ammo = t.Ammo[(int)t.Weapon];
            Text(dc, info.Name + (ammo < 0 ? "" : $"  x{ammo}"), new Point(470, 712), 30, Yellow);
            Text(dc, info.Note, new Point(470, 744), 20, Dim);
        }

        int i = 0;
        foreach (var tank in g.Tanks)
        {
            double x = 780 + (i % 2) * 125, y = 712 + (i / 2) * 32;
            string wins = new('*', tank.RoundWins);
            Text(dc, $"{tank.Name} {tank.Score}", new Point(x, y), 24, tank.Alive ? tank.Colour : Shade(tank.Colour, 0.5));
            if (wins.Length > 0) Text(dc, wins, new Point(x + 100, y), 22, Yellow);
            i++;
        }
    }

    private void DrawRoundCard(DrawingContext dc)
    {
        var g = Engine;
        Card(dc, new Rect(312, 230, 400, 170));
        Text(dc, $"ROUND {g.Round} OF {g.Rounds}", new Point(512, 280), 54, Yellow, center: true);
        Text(dc, Palette(g.Round) == Palette(0) ? "A FINE DAY FOR IT" : (g.Round % 4) switch { 1 => "AS THE SUN GOES DOWN", 2 => "BY NIGHT", 3 => "IN THE DESERT", _ => "A FINE DAY FOR IT" }, new Point(512, 345), 30, White, center: true);
    }

    private void DrawRoundOver(DrawingContext dc)
    {
        var g = Engine;
        Card(dc, new Rect(262, 200, 500, 240));
        if (g.RoundWinner is { } win)
        {
            Text(dc, $"{win.Name} WINS THE ROUND", new Point(512, 250), 48, win.Colour, center: true);
            Text(dc, $"+{GameEngine.RoundWinPoints} POINTS", new Point(512, 300), 30, Yellow, center: true);
        }
        else Text(dc, "NOBODY LEFT STANDING", new Point(512, 270), 46, White, center: true);
        Text(dc, g.Round >= g.Rounds ? "THAT'S THE MATCH" : $"ROUND {g.Round + 1} NEXT", new Point(512, 380), 28, Dim, center: true);
    }

    private void DrawMatchOver(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(212, 140, 600, 420));
        if (g.Winner is { } win) Text(dc, $"{win.Name} WINS!", new Point(512, 200), 76, (int)(_clock * 4) % 2 == 0 ? win.Colour : White, center: true);
        Text(dc, "TANK    ROUNDS   POINTS", new Point(512, 280), 30, Dim, center: true);
        int i = 0;
        foreach (var t in g.Standings)
        {
            Text(dc, $"{t.Name,-6}  {t.RoundWins,4}   {t.Score,7:N0}", new Point(512, 326 + i * 44), 36, t.Colour, center: true);
            i++;
        }
        if (g.StateTime > 1.5) Text(dc, "PRESS ENTER", new Point(512, 520), 28, White, center: true);
    }

    // ───────────────────────────── the title and setup ─────────────────────────────

    private void DrawTitleBackdrop(DrawingContext dc)
    {
        DrawSky(dc, 1);
        var pts = new List<Point> { new(0, H) };
        for (int x = 0; x <= 1024; x += 8)
            pts.Add(new Point(x, 560 + 60 * Math.Sin(x * 0.007) + 20 * Math.Sin(x * 0.03)));
        pts.Add(new Point(1024, H));
        Poly(dc, 0xFF4A3420, null, 0, [.. pts]);
    }

    private void DrawTitle(DrawingContext dc)
    {
        DrawTitleBackdrop(dc);
        // Two tanks trading shots on the hills, for ever.
        double y1 = 560 + 60 * Math.Sin(150 * 0.007) + 20 * Math.Sin(150 * 0.03), y2 = 560 + 60 * Math.Sin(870 * 0.007) + 20 * Math.Sin(870 * 0.03);
        var left = new Tank { Id = 0, Name = "", Colour = GameEngine.Colours[0], Seat = Seat.Hard, X = 150, Y = y1, Angle = 55 };
        var right = new Tank { Id = 1, Name = "", Colour = GameEngine.Colours[1], Seat = Seat.Hard, X = 870, Y = y2, Angle = 125 };
        DrawTank(dc, left, false);
        DrawTank(dc, right, false);
        double p = _clock % 3 / 3;
        bool fromLeft = (int)(_clock / 3) % 2 == 0;
        double sx = fromLeft ? 165 + p * 690 : 855 - p * 690, sy = 520 - Math.Sin(p * Math.PI) * 380;
        if (p < 0.96) dc.DrawEllipse(Brush(0xFF202020), Pen(White, 1), new Point(sx, sy), 5, 5);
        else dc.DrawEllipse(Brush(Alpha(0xFFFF9A30, 0.8)), null, new Point(fromLeft ? 855 : 165, (fromLeft ? y2 : y1) - 8), 30, 30);

        dc.FillRectangle(Brush(0x70000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(152, 60, 720, 420));
        Text(dc, "A V A", new Point(512, 100), 40, Dim, center: true);
        Text(dc, "SALVO", new Point(512, 170), 130, Yellow, center: true);
        Text(dc, "AN ARTILLERY DUEL FOR TWO TO FOUR TANKS", new Point(512, 252), 30, White, center: true);
        Text(dc, "TAKE TURNS TO SET YOUR ANGLE AND YOUR POWER, MIND THE WIND,", new Point(512, 300), 23, White, center: true);
        Text(dc, "AND FIRE. BLASTS DIG CRATERS AND THE GROUND FALLS IN; A TANK", new Point(512, 326), 23, White, center: true);
        Text(dc, "THAT FALLS FAR GETS HURT. LAST TANK STANDING WINS THE ROUND.", new Point(512, 352), 23, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(512, 420), 46, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 700, W, 68));
        Text(dc, "LEFT/RIGHT: ANGLE   UP/DOWN: POWER   SHIFT: FINE   TAB, Q, E: WEAPON   SPACE: FIRE   P: PAUSE", new Point(512, 720), 19, 0xFFE0D0B0, center: true);
        Text(dc, "PLAY AGAINST FRIENDS AT ONE KEYBOARD, OR THE COMPUTER", new Point(512, 746), 18, Dim, center: true);
    }

    private static string SeatText(Seat s) => s switch { Seat.Human => "PERSON", Seat.Easy => "COMPUTER: EASY", Seat.Normal => "COMPUTER: NORMAL", _ => "COMPUTER: HARD" };

    private void DrawSetup(DrawingContext dc)
    {
        var g = Engine;
        DrawTitleBackdrop(dc);
        dc.FillRectangle(Brush(0x80000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(212, 90, 600, 560));
        Text(dc, "THE MATCH", new Point(512, 140), 64, Yellow, center: true);
        var rows = new List<(string Label, string Value, uint Colour)> { ("TANKS", g.PlayerCount.ToString(), White) };
        for (int i = 0; i < g.PlayerCount; i++) rows.Add((GameEngine.Names[i], SeatText(g.Seats[i]), GameEngine.Colours[i]));
        rows.Add(("ROUNDS", g.Rounds.ToString(), White));
        rows.Add(("", "PLAY", Yellow));
        for (int r = 0; r < rows.Count; r++)
        {
            double y = 220 + r * 56;
            bool on = r == g.SetupRow;
            if (on) dc.FillRectangle(Brush(0xFF2A3050), new Rect(252, y - 24, 520, 48), 10);
            if (rows[r].Label.Length > 0)
            {
                Text(dc, rows[r].Label, new Point(280, y), 34, rows[r].Colour);
                Text(dc, (on ? "< " : "") + rows[r].Value + (on ? " >" : ""), new Point(744, y), 34, on ? Yellow : White, right: true);
            }
            else Text(dc, rows[r].Value, new Point(512, y), 44, on && (int)(_clock * 4) % 2 == 0 ? White : Yellow, center: true);
        }
        Text(dc, "UP/DOWN: CHOOSE   LEFT/RIGHT: CHANGE   ENTER: PLAY", new Point(512, 616), 24, Dim, center: true);
    }

    // ───────────────────────────── scores ─────────────────────────────

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(182, 70, 660, 640));
        Text(dc, "HIGH SCORES", new Point(512, 124), 64, Yellow, center: true);
        Text(dc, "RANK NAME     SCORE  ROUNDS", new Point(512, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,9:N0}   {e.Stage,4}", new Point(512, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(512, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(182, 140, 660, 460));
        Text(dc, "A NEW HIGH SCORE!", new Point(512, 200), 64, Yellow, center: true);
        Text(dc, $"{g.Champion?.Name} {g.Score:N0}", new Point(512, 270), 52, g.Champion?.Colour ?? White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(512, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(452 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(430 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(512, 560), 26, White, center: true);
    }
}
