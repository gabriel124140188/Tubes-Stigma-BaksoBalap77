
using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class SiPengkor : Bot
{
    private const double WALL_MARGIN = 90;
    private const double LOW_ENERGY = 30;
    private const double CLOSE_RANGE = 140;
    private const double MID_RANGE = 340;
    private const double FAR_RANGE = 620;
    private const double ORBIT_DISTANCE = 245;
    private const double BULLET_BASE_SPEED = 20;
    private const int TARGET_TIMEOUT = 30;
    private const int MAX_PREDICT = 36;

    private readonly Dictionary<int, Enemy> _enemies = new();
    private readonly Random _random = new Random();

    private int _turn = 0;
    private int _moveDir = 1;
    private int _reverseCooldown = 0;
    private int? _targetId = null;

    static void Main(string[] args) => new SiPengkor().Start();

    public SiPengkor() : base(BotInfo.FromFile("SiPengkor.json")) { }

    public override void Run()
    {
        BodyColor = Color.FromArgb(40, 40, 40);
        TurretColor = Color.FromArgb(255, 120, 0);
        RadarColor = Color.FromArgb(255, 230, 0);
        BulletColor = Color.Red;
        ScanColor = Color.LightYellow;
        GunColor = Color.FromArgb(150, 60, 0);
        TracksColor = Color.DarkGray;

        while (IsRunning)
        {
            _turn++;

            Enemy target = SelectTarget();

            if (target != null)
            {
                LockRadar(target);
                MoveGreedy(target);
                AimCircular(target);
                FireGreedy(target);
            }
            else
            {
                SetTurnRadarLeft(360);
                SetForward(100);
                SetTurnLeft(18);
            }

            if (_reverseCooldown > 0)
                _reverseCooldown--;

            Go();
        }
    }

    private Enemy SelectTarget()
    {
        CleanEnemies();

        if (_enemies.Count == 0)
            return null;

        Enemy best = null;
        double bestScore = double.MinValue;

        foreach (Enemy e in _enemies.Values)
        {
            double score = Score(e);
            if (score > bestScore)
            {
                bestScore = score;
                best = e;
            }
        }

        return best;
    }

    private double Score(Enemy e)
    {
        double range = 1300 / (e.Distance + 1);
        double kill = 220 / (e.Energy + 6);
        double hit = HitChance(e.Distance, e.Speed) * 2.1;
        double danger = 800 / (e.Distance + 1) + e.Energy * 0.32 + Math.Abs(e.Speed) * 1.2;

        if (Energy < LOW_ENERGY)
            danger *= 1.5;

        if (_targetId.HasValue && _targetId.Value == e.Id)
            hit += 75;

        return range + kill + hit - danger;
    }

    private double HitChance(double distance, double speed)
    {
        double time = distance / BULLET_BASE_SPEED;
        double evade = Math.Abs(speed) * time;
        return Math.Max(0, 1 - evade / 145) * 100;
    }

    private void CleanEnemies()
    {
        List<int> old = new();

        foreach (KeyValuePair<int, Enemy> pair in _enemies)
        {
            if (TurnNumber - pair.Value.LastSeen > TARGET_TIMEOUT)
                old.Add(pair.Key);
        }

        foreach (int id in old)
            _enemies.Remove(id);
    }

    private void LockRadar(Enemy target)
    {
        double absBearing = Direction + target.Bearing;
        double turn = NormalizeAngle(absBearing - RadarDirection);

        if (Math.Abs(turn) < 1)
            turn = 50;

        SetTurnRadarLeft(turn + Math.Sign(turn) * 25);
    }

    private void MoveGreedy(Enemy target)
    {
        if (NearWall() || PredictWall())
        {
            EscapeCenter();
            return;
        }

        if (_reverseCooldown <= 0 && (_random.NextDouble() < 0.05 || Math.Abs(target.Speed) < 1))
        {
            _moveDir *= -1;
            _reverseCooldown = 18;
        }

        double orbit = target.Bearing + 92 * _moveDir;
        orbit += Math.Sin(_turn * 0.27) * 20;
        orbit += (_random.NextDouble() - 0.5) * 13;

        SetTurnLeft(NormalizeAngle(orbit));

        double distanceError = target.Distance - ORBIT_DISTANCE;
        double speed = 90 + Clamp(distanceError * 0.36, -45, 65);

        if (_random.NextDouble() < 0.025)
            speed *= 0.35;

        SetForward(speed * _moveDir);
    }

    private void AimCircular(Enemy target)
    {
        double power = FirePower(target);
        double bulletSpeed = 20 - 3 * power;

        double px = target.X;
        double py = target.Y;
        double heading = ToRadians(target.Heading);
        double velocity = target.Speed;
        double headingDelta = ToRadians(target.HeadingDelta * 0.75);

        int time = 0;

        while (time < MAX_PREDICT && (++time) * bulletSpeed < DistanceTo(px, py))
        {
            px += Math.Sin(heading) * velocity;
            py += Math.Cos(heading) * velocity;
            heading += headingDelta;

            px = Clamp(px, 20, ArenaWidth - 20);
            py = Clamp(py, 20, ArenaHeight - 20);
        }

        double bearing = BearingTo(px, py);
        double gunTurn = NormalizeAngle(bearing - (GunDirection - Direction));

        SetTurnGunLeft(gunTurn * 0.96);
    }

    private void FireGreedy(Enemy target)
    {
        if (GunHeat > 0)
            return;

        double power = FirePower(target);
        double error = Math.Abs(GunTurnRemaining);

        if (error < 4)
            SetFire(power);
        else if (target.Distance < CLOSE_RANGE && error < 9)
            SetFire(Math.Min(1.2, power));
    }

    private double FirePower(Enemy target)
    {
        double p;

        if (target.Distance < CLOSE_RANGE)
            p = 2.9;
        else if (target.Distance < MID_RANGE)
            p = 2.15;
        else if (target.Distance < FAR_RANGE)
            p = 1.45;
        else
            p = 0.85;

        if (target.Energy < 13)
            p = Math.Min(3, Math.Max(0.55, target.Energy / 3.8));

        if (Energy < LOW_ENERGY)
            p = Math.Max(0.55, p - 0.75);

        if (Energy < 8)
            p = 0.45;

        return Clamp(p, 0.1, 3);
    }

    private bool NearWall()
    {
        return X < WALL_MARGIN || X > ArenaWidth - WALL_MARGIN ||
               Y < WALL_MARGIN || Y > ArenaHeight - WALL_MARGIN;
    }

    private bool PredictWall()
    {
        double h = ToRadians(Direction);
        double fx = X + Math.Sin(h) * Speed * 16;
        double fy = Y + Math.Cos(h) * Speed * 16;

        return fx < WALL_MARGIN || fx > ArenaWidth - WALL_MARGIN ||
               fy < WALL_MARGIN || fy > ArenaHeight - WALL_MARGIN;
    }

    private void EscapeCenter()
    {
        double turn = NormalizeAngle(BearingTo(ArenaWidth / 2, ArenaHeight / 2) + 35 * _moveDir);
        SetTurnLeft(turn);
        SetForward(150);

        if (Math.Abs(Speed) < 0.3)
        {
            _moveDir *= -1;
            SetBack(120);
        }
    }

    public override void OnScannedBot(ScannedBotEvent e)
    {
        double bearing = BearingTo(e.X, e.Y);
        double distance = DistanceTo(e.X, e.Y);

        if (_enemies.TryGetValue(e.ScannedBotId, out Enemy old))
        {
            double oldHeading = old.Heading;
            old.Update(e.X, e.Y, e.Energy, e.Speed, e.Direction,
                       NormalizeAngle(e.Direction - oldHeading), bearing, distance, TurnNumber);
        }
        else
        {
            _enemies[e.ScannedBotId] = new Enemy(e.ScannedBotId, e.X, e.Y, e.Energy,
                                                 e.Speed, e.Direction, 0, bearing, distance, TurnNumber);
        }

        _targetId = e.ScannedBotId;
    }

    public override void OnHitByBullet(HitByBulletEvent e)
    {
        _moveDir *= -1;
        _reverseCooldown = 22;
        SetBack(120);
        SetTurnLeft(80 * _moveDir);
    }

    public override void OnHitWall(HitWallEvent e)
    {
        _moveDir *= -1;
        _reverseCooldown = 24;
        SetBack(150);
        SetTurnLeft(115 * _moveDir);
    }

    public override void OnHitBot(HitBotEvent e)
    {
        _moveDir *= -1;
        SetBack(e.IsRammed ? 90 : 50);
    }

    public override void OnBotDeath(BotDeathEvent e)
    {
        _enemies.Remove(e.VictimId);

        if (_targetId.HasValue && _targetId.Value == e.VictimId)
            _targetId = null;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double NormalizeAngle(double angle)
    {
        angle %= 360;
        if (angle > 180) angle -= 360;
        if (angle < -180) angle += 360;
        return angle;
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private sealed class Enemy
    {
        public int Id { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Energy { get; private set; }
        public double Speed { get; private set; }
        public double Heading { get; private set; }
        public double HeadingDelta { get; private set; }
        public double Bearing { get; private set; }
        public double Distance { get; private set; }
        public int LastSeen { get; private set; }

        public Enemy(int id, double x, double y, double energy, double speed,
                     double heading, double headingDelta, double bearing, double distance, int turn)
        {
            Id = id;
            Update(x, y, energy, speed, heading, headingDelta, bearing, distance, turn);
        }

        public void Update(double x, double y, double energy, double speed,
                           double heading, double headingDelta, double bearing, double distance, int turn)
        {
            X = x;
            Y = y;
            Energy = energy;
            Speed = speed;
            Heading = heading;
            HeadingDelta = headingDelta;
            Bearing = bearing;
            Distance = distance;
            LastSeen = turn;
        }
    }
}
