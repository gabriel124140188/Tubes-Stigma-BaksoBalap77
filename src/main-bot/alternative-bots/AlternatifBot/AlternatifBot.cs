
using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class AlternatifBot : Bot
{
    private const double WALL_MARGIN = 85;
    private const double SAFE_DISTANCE = 260;
    private const double LOW_ENERGY = 24;
    private const double CLOSE_RANGE = 125;
    private const double MID_RANGE = 300;
    private const double FAR_RANGE = 580;
    private const int ENEMY_TIMEOUT = 24;
    private const int MAX_PREDICT = 34;

    private readonly Dictionary<int, EnemyMemory> _memory = new();
    private readonly Random _random = new Random();

    private int _turnCount = 0;
    private int _strafe = 1;
    private int _switchDelay = 0;
    private int? _mainTarget = null;
    private int _patrolIndex = 0;

    private readonly (double X, double Y)[] _patrol =
    {
        (130, 130),
        (670, 130),
        (670, 470),
        (130, 470)
    };

    static void Main(string[] args) => new AlternatifBot().Start();

    public AlternatifBot() : base(BotInfo.FromFile("AlternatifBot.json")) { }

    public override void Run()
    {
        BodyColor = Color.FromArgb(20, 80, 20);
        TurretColor = Color.FromArgb(0, 180, 80);
        RadarColor = Color.Cyan;
        BulletColor = Color.White;
        ScanColor = Color.LightCyan;
        GunColor = Color.FromArgb(0, 90, 70);
        TracksColor = Color.DarkGreen;

        while (IsRunning)
        {
            _turnCount++;

            EnemyMemory target = ChooseTarget();

            if (target != null)
            {
                RadarFocus(target);
                MoveWeighted(target);
                AimLinear(target);
                FireControlled(target);
            }
            else
            {
                SetTurnRadarLeft(360);
                Patrol();
            }

            if (_switchDelay > 0)
                _switchDelay--;

            Go();
        }
    }

    private EnemyMemory ChooseTarget()
    {
        CleanMemory();

        if (_memory.Count == 0)
            return null;

        EnemyMemory chosen = null;
        double best = double.MinValue;

        foreach (EnemyMemory e in _memory.Values)
        {
            double score = Utility(e);

            if (score > best)
            {
                best = score;
                chosen = e;
            }
        }

        return chosen;
    }

    private double Utility(EnemyMemory e)
    {
        double closeness = 1100 / (e.Distance + 1);
        double finish = 260 / (e.Energy + 7);
        double accuracy = Accuracy(e);
        double threat = Threat(e);
        double locked = _mainTarget.HasValue && _mainTarget.Value == e.Id ? 80 : 0;
        double survivalWeight = Energy < LOW_ENERGY ? 1.7 : 0.85;

        return closeness * 1.25 + finish * 2.0 + accuracy * 2.35 + locked - threat * survivalWeight;
    }

    private double Accuracy(EnemyMemory e)
    {
        double bulletTime = e.Distance / 14;
        double dodge = Math.Abs(e.Speed) * bulletTime;
        return Math.Max(0, 1 - dodge / 150) * 100;
    }

    private double Threat(EnemyMemory e)
    {
        return 1000 / (e.Distance + 1) + e.Energy * 0.38 + Math.Abs(e.Speed) * 1.3;
    }

    private void CleanMemory()
    {
        List<int> old = new();

        foreach (KeyValuePair<int, EnemyMemory> pair in _memory)
        {
            if (TurnNumber - pair.Value.LastSeen > ENEMY_TIMEOUT)
                old.Add(pair.Key);
        }

        foreach (int id in old)
            _memory.Remove(id);
    }

    private void RadarFocus(EnemyMemory target)
    {
        double absBearing = Direction + target.Bearing;
        double turn = NormalizeAngle(absBearing - RadarDirection);

        if (Math.Abs(turn) < 1)
            turn = 60;

        SetTurnRadarLeft(turn + Math.Sign(turn) * 18);
    }

    private void MoveWeighted(EnemyMemory target)
    {
        if (NearWall() || PredictWall())
        {
            EscapeWall();
            return;
        }

        if (_switchDelay <= 0 && (_random.NextDouble() < 0.04 || target.Distance < CLOSE_RANGE))
        {
            _strafe *= -1;
            _switchDelay = 16;
        }

        double angle = target.Bearing + 90 * _strafe;
        double distanceError = target.Distance - SAFE_DISTANCE;

        if (target.Distance < CLOSE_RANGE)
            angle += 28 * _strafe;
        else if (target.Distance > FAR_RANGE)
            angle -= 18 * _strafe;

        angle += Math.Sin(_turnCount * 0.21) * 16;
        angle += (_random.NextDouble() - 0.5) * 10;

        SetTurnLeft(NormalizeAngle(angle));

        double speed = 100 + Clamp(distanceError * 0.32, -50, 50);

        if (Energy < LOW_ENERGY)
            speed *= 0.75;

        SetForward(speed * _strafe);
    }

    private void AimLinear(EnemyMemory target)
    {
        double power = FirePower(target);
        double bulletSpeed = 20 - 3 * power;

        double px = target.X;
        double py = target.Y;
        double heading = ToRadians(target.Heading);
        double velocity = target.Speed;

        int tick = 0;

        while (tick < MAX_PREDICT && (++tick) * bulletSpeed < DistanceTo(px, py))
        {
            px += Math.Sin(heading) * velocity;
            py += Math.Cos(heading) * velocity;

            px = Clamp(px, 20, ArenaWidth - 20);
            py = Clamp(py, 20, ArenaHeight - 20);
        }

        double bearing = BearingTo(px, py);
        double gunTurn = NormalizeAngle(bearing - (GunDirection - Direction));

        SetTurnGunLeft(gunTurn);
    }

    private void FireControlled(EnemyMemory target)
    {
        if (GunHeat > 0)
            return;

        double power = FirePower(target);
        double error = Math.Abs(GunTurnRemaining);

        if (error < 5)
            SetFire(power);
        else if (target.Distance < CLOSE_RANGE && error < 11)
            SetFire(Math.Min(power, 1));
    }

    private double FirePower(EnemyMemory target)
    {
        double p;

        if (target.Distance < CLOSE_RANGE)
            p = 2.75;
        else if (target.Distance < MID_RANGE)
            p = 2.05;
        else if (target.Distance < FAR_RANGE)
            p = 1.35;
        else
            p = 0.75;

        if (target.Energy < 12)
            p = Math.Min(3, Math.Max(0.45, target.Energy / 3.6));

        if (Energy < LOW_ENERGY)
            p = Math.Max(0.45, p - 0.85);

        return Clamp(p, 0.1, 3);
    }

    private void Patrol()
    {
        (double X, double Y) point = _patrol[_patrolIndex];

        double bearing = BearingTo(point.X, point.Y);
        double distance = DistanceTo(point.X, point.Y);

        SetTurnLeft(NormalizeAngle(bearing));
        SetForward(Math.Min(150, distance));

        if (distance < 65)
        {
            _patrolIndex++;
            if (_patrolIndex >= _patrol.Length)
                _patrolIndex = 0;
        }
    }

    private bool NearWall()
    {
        return X < WALL_MARGIN || X > ArenaWidth - WALL_MARGIN ||
               Y < WALL_MARGIN || Y > ArenaHeight - WALL_MARGIN;
    }

    private bool PredictWall()
    {
        double heading = ToRadians(Direction);
        double nx = X + Math.Sin(heading) * Speed * 17;
        double ny = Y + Math.Cos(heading) * Speed * 17;

        return nx < WALL_MARGIN || nx > ArenaWidth - WALL_MARGIN ||
               ny < WALL_MARGIN || ny > ArenaHeight - WALL_MARGIN;
    }

    private void EscapeWall()
    {
        double angle = BearingTo(ArenaWidth / 2, ArenaHeight / 2) + 30 * _strafe;

        SetTurnLeft(NormalizeAngle(angle));
        SetForward(150);

        if (Math.Abs(Speed) < 0.2)
        {
            _strafe *= -1;
            SetBack(130);
        }
    }

    public override void OnScannedBot(ScannedBotEvent e)
    {
        double bearing = BearingTo(e.X, e.Y);
        double distance = DistanceTo(e.X, e.Y);

        if (_memory.TryGetValue(e.ScannedBotId, out EnemyMemory old))
        {
            old.Update(e.X, e.Y, e.Energy, e.Speed, e.Direction, bearing, distance, TurnNumber);
        }
        else
        {
            _memory[e.ScannedBotId] = new EnemyMemory(e.ScannedBotId, e.X, e.Y, e.Energy,
                                                       e.Speed, e.Direction, bearing, distance, TurnNumber);
        }

        _mainTarget = e.ScannedBotId;
    }

    public override void OnHitByBullet(HitByBulletEvent e)
    {
        _strafe *= -1;
        _switchDelay = 18;
        SetBack(110);
        SetTurnLeft(75 * _strafe);
    }

    public override void OnHitWall(HitWallEvent e)
    {
        _strafe *= -1;
        _switchDelay = 20;
        SetBack(140);
        SetTurnLeft(120 * _strafe);
    }

    public override void OnHitBot(HitBotEvent e)
    {
        _strafe *= -1;
        SetBack(e.IsRammed ? 90 : 55);
    }

    public override void OnBotDeath(BotDeathEvent e)
    {
        _memory.Remove(e.VictimId);

        if (_mainTarget.HasValue && _mainTarget.Value == e.VictimId)
            _mainTarget = null;
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

    private sealed class EnemyMemory
    {
        public int Id { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Energy { get; private set; }
        public double Speed { get; private set; }
        public double Heading { get; private set; }
        public double Bearing { get; private set; }
        public double Distance { get; private set; }
        public int LastSeen { get; private set; }

        public EnemyMemory(int id, double x, double y, double energy, double speed,
                           double heading, double bearing, double distance, int turn)
        {
            Id = id;
            Update(x, y, energy, speed, heading, bearing, distance, turn);
        }

        public void Update(double x, double y, double energy, double speed,
                           double heading, double bearing, double distance, int turn)
        {
            X = x;
            Y = y;
            Energy = energy;
            Speed = speed;
            Heading = heading;
            Bearing = bearing;
            Distance = distance;
            LastSeen = turn;
        }
    }
}
