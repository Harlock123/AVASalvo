namespace AVASalvo.Engine;

public enum GameState { Title, Setup, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The round's card: which round, and the field.</summary>
    RoundStart,
    /// <summary>A tank lining up its shot: a person at the keys, or the computer making up its mind.</summary>
    Aim,
    /// <summary>Shots in the air, blasts going off, tanks falling: wait for it all to stop.</summary>
    Flight,
    /// <summary>One tank left (or none): the round's result.</summary>
    RoundOver,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, FireHeavy, Split, Boom, BigBoom, Dirt, Dig, Thud, Hit, TankDeath, Turn, Select, Weapon, RoundWin,
    TitleTune, MatchOver,
}

/// <summary>One frame of a player's controls.</summary>
public readonly record struct Controls(int Turn = 0, int Charge = 0, bool Fire = false, int Weapon = 0, bool Fine = false);

/// <summary>
/// The whole game with no UI: the title and setup screens, the match of rounds, whose turn it is, scoring and the
/// high-score table. <see cref="Update"/> runs in fixed 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 18, HighScoreSeconds = 10, RoundStartSeconds = 2.4, RoundOverSeconds = 4;
    public const double OverSeconds = 6, AttractSeconds = 90;
    public const int RoundWinPoints = 300;

    public static readonly uint[] Colours = [0xFFFF5A4A, 0xFF4AB8FF, 0xFFFFD24A, 0xFF6AE86A];
    public static readonly string[] Names = ["RED", "BLUE", "GOLD", "GREEN"];
    public static readonly int[] RoundChoices = [1, 3, 5, 7];

    private double _accumulator, _idle, _cpuTimer;
    private bool _startPrev, _firePrev, _tunePending = true;
    private int _weaponPrev, _setupMovePrev, _setupTurnPrev;
    private Ai.Plan? _plan;
    private readonly Random _random;

    public GameEngine(HighScoreTable? table = null, int? seed = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        _random = seed is { } s ? new Random(s) : new Random();
        Seats = [Seat.Human, Seat.Normal, Seat.Easy, Seat.Easy];
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.RoundStart;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();

    // The setup screen's choices.
    public Seat[] Seats { get; }
    public int PlayerCount { get; private set; } = 2;
    public int RoundsIndex { get; private set; } = 1;
    public int SetupRow { get; private set; }
    public int Rounds => RoundChoices[RoundsIndex];

    public int Round { get; private set; }
    public Tank? Current { get; private set; }
    public Tank? RoundWinner { get; private set; }
    /// <summary>The human with the best score at the end of a match, whose initials go in the table.</summary>
    public Tank? Champion { get; private set; }
    public int Score => Champion?.Score ?? 0;
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= World.Step)
        {
            _accumulator -= World.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c = default, bool start = false)
    {
        double dt = World.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0 && (MessageTime -= dt) <= 0) Message = null;
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && !_firePrev) OpenSetup();
                else if (_idle > TitleIdleSeconds) StartAttract();
                _firePrev = c.Fire;
                break;
            case GameState.Setup:
                SetupStep(c, startPressed);
                break;
            case GameState.Attract:
                Simulate(dt, default);
                if (startPressed) { OpenSetup(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds || startPressed && StateTime > 1.5)
                {
                    if (Champion is { } champ && HighScores.Qualifies(champ.Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Rounds);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) OpenSetup();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    // ───────────────────────────── setup ─────────────────────────────

    public void OpenSetup()
    {
        State = GameState.Setup;
        StateTime = 0;
        SetupRow = 0;
        _setupMovePrev = 1;
        _setupTurnPrev = 1;
        _firePrev = true;
        Play(Sfx.Select);
    }

    /// <summary>The setup screen's rows: how many tanks, who drives each, how many rounds, and PLAY.</summary>
    public int SetupRows => 2 + PlayerCount + 1;

    private void SetupStep(Controls c, bool startPressed)
    {
        int move = c.Charge != _setupMovePrev ? c.Charge : 0;
        int turn = c.Turn != _setupTurnPrev ? c.Turn : 0;
        bool fire = c.Fire && !_firePrev;
        _setupMovePrev = c.Charge;
        _setupTurnPrev = c.Turn;
        _firePrev = c.Fire;
        // Up the screen is up the list.
        if (move != 0)
        {
            SetupRow = (SetupRow - move + SetupRows) % SetupRows;
            Play(Sfx.Select);
        }

        if (turn != 0) { Adjust(SetupRow, turn); Play(Sfx.Select); }
        if (startPressed || fire && SetupRow == SetupRows - 1) StartGame();
        else if (fire) { Adjust(SetupRow, 1); Play(Sfx.Select); }
    }

    /// <summary>Changes the setup row's choice: one step right (+1) or left (−1).</summary>
    public void Adjust(int row, int by)
    {
        if (row == 0) PlayerCount = Math.Clamp(PlayerCount + by, 2, 4);
        else if (row <= PlayerCount) Seats[row - 1] = (Seat)(((int)Seats[row - 1] + by + 4) % 4);
        else if (row == PlayerCount + 1) RoundsIndex = (RoundsIndex + by + RoundChoices.Length) % RoundChoices.Length;
        SetupRow = Math.Min(SetupRow, SetupRows - 1);
    }

    // ───────────────────────────── a match ─────────────────────────────

    private List<Tank> _tanks = [];
    private int _turnIndex;

    public IReadOnlyList<Tank> Tanks => _tanks;

    public void StartGame() => BeginMatch(Seats.Take(PlayerCount).ToArray(), GameState.Playing);

    private void StartAttract() => BeginMatch([Seat.Hard, Seat.Normal, Seat.Normal], GameState.Attract);

    private void BeginMatch(Seat[] seats, GameState state)
    {
        _tanks = seats.Select((s, i) => new Tank { Id = i, Name = Names[i], Colour = Colours[i], Seat = s }).ToList();
        State = state;
        StateTime = 0;
        Round = 0;
        Champion = null;
        NewEntryIndex = -1;
        _turnIndex = _random.Next(_tanks.Count);
        NextRound();
    }

    private void NextRound()
    {
        Round++;
        World = new World { Round = Round };
        World.Build(_random.Next(), _tanks);
        RoundWinner = null;
        SetPhase(Phase.RoundStart);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void BeginTurn()
    {
        var alive = _tanks.Where(t => t.Alive).ToList();
        if (alive.Count <= 1) { EndRound(); return; }
        for (int k = 0; k < _tanks.Count; k++)
        {
            _turnIndex = (_turnIndex + 1) % _tanks.Count;
            if (_tanks[_turnIndex].Alive) break;
        }

        Current = _tanks[_turnIndex];
        World.ShiftWind();
        _plan = null;
        _cpuTimer = 0;
        _firePrev = true;
        SetPhase(Phase.Aim);
        Play(Sfx.Turn);
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.RoundStart:
                if (PhaseTime > RoundStartSeconds) BeginTurn();
                break;
            case Phase.Aim:
                if (_tanks.Count(k => k.Alive) <= 1) { EndRound(); break; }
                if (Current is not { } t || !t.Alive) { BeginTurn(); break; }
                if (t.IsComputer || State == GameState.Attract) ComputerAim(t, dt);
                else PlayerAim(t, c, dt);
                break;
            case Phase.Flight:
                w.Tick();
                if (!w.Busy && PhaseTime > 0.4) AfterShot();
                break;
            case Phase.RoundOver:
                w.Tick();
                if (PhaseTime > RoundOverSeconds)
                {
                    if (Round >= Rounds) EndMatch();
                    else NextRound();
                }
                break;
        }

        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void PlayerAim(Tank t, Controls c, double dt)
    {
        double rate = c.Fine ? 8 : 40;
        // Left turns the barrel to the left, which is up the angle.
        t.Angle = Math.Clamp(t.Angle - c.Turn * rate * dt, 0, 180);
        t.Power = Math.Clamp(t.Power + c.Charge * (c.Fine ? 6 : 30) * dt, 5, 100);
        if (c.Weapon != 0 && c.Weapon != _weaponPrev) CycleWeapon(t, c.Weapon);
        _weaponPrev = c.Weapon;
        bool fire = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        if (fire) Shoot(t);
    }

    /// <summary>Steps to the next weapon the tank has some of.</summary>
    public void CycleWeapon(Tank t, int dir)
    {
        int n = World.Weapons.Length;
        for (int k = 1; k <= n; k++)
        {
            var next = (Weapon)(((int)t.Weapon + dir * k % n + n) % n);
            if (t.Ammo[(int)next] != 0) { t.Weapon = next; break; }
        }
        Play(Sfx.Weapon);
    }

    private void ComputerAim(Tank t, double dt)
    {
        _cpuTimer += dt;
        if (_cpuTimer < 0.5) return;
        _plan ??= Ai.Decide(World, t, _random);
        // The barrel swings round and the power winds up, as a person's would, then it fires.
        double da = _plan.Angle - t.Angle, dp = _plan.Power - t.Power;
        t.Angle += Math.Clamp(da, -60 * dt, 60 * dt);
        t.Power += Math.Clamp(dp, -45 * dt, 45 * dt);
        if (Math.Abs(da) < 0.5 && Math.Abs(dp) < 0.5 && _cpuTimer > 1.2)
        {
            t.Angle = _plan.Angle;
            t.Power = _plan.Power;
            if (t.Ammo[(int)_plan.Weapon] != 0) t.Weapon = _plan.Weapon;
            Shoot(t);
        }
    }

    private void Shoot(Tank t)
    {
        World.Fire(t);
        SetPhase(Phase.Flight);
    }

    private void AfterShot()
    {
        // A kill is worth announcing.
        foreach (var h in World.Hits.Where(h => h.Killed))
        {
            Message = h.By == h.To ? $"{h.To.Name} GOT ITSELF" : $"{h.By.Name} DESTROYS {h.To.Name}";
            MessageTime = 2.2;
        }

        if (_tanks.Count(t => t.Alive) <= 1) EndRound();
        else BeginTurn();
    }

    private void EndRound()
    {
        RoundWinner = _tanks.FirstOrDefault(t => t.Alive);
        if (RoundWinner is { } win)
        {
            win.Score += RoundWinPoints;
            win.RoundWins++;
        }
        Current = null;
        SetPhase(Phase.RoundOver);
        Play(Sfx.RoundWin);
    }

    /// <summary>The tanks in the order they finished: round wins first, then points.</summary>
    public IEnumerable<Tank> Standings => _tanks.OrderByDescending(t => t.RoundWins).ThenByDescending(t => t.Score);

    private void EndMatch()
    {
        Play(Sfx.MatchOver);
        Champion = _tanks.Where(t => !t.IsComputer).OrderByDescending(t => t.Score).FirstOrDefault();
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    public Tank? Winner => _tanks.Count == 0 ? null : Standings.First();

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Message = null;
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State == GameState.Playing) State = GameState.Paused;
    }

    public void Resume()
    {
        if (State == GameState.Paused) State = GameState.Playing;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    /// <summary>Straight into a match with these seats, past the setup and the round card.</summary>
    public void Go(params Seat[] seats)
    {
        BeginMatch(seats, GameState.Playing);
        BeginTurn();
    }

    /// <summary>Makes this tank the one to play.</summary>
    public void GiveTurn(Tank t)
    {
        _turnIndex = t.Id;
        Current = t;
        _plan = null;
        _cpuTimer = 0;
        SetPhase(Phase.Aim);
    }
}
