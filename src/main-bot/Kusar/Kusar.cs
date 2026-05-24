using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class Kusar : Bot
{
    private const double WALL_MARGIN = 150.0;
    private const double SOFT_WALL_MARGIN = 220.0;
    private const double LOW_ENERGY = 28.0;

    private const double CLOSE_RANGE = 135.0;
    private const double MID_RANGE = 320.0;
    private const double FAR_RANGE = 560.0;

    private const double IDEAL_ORBIT = 245.0;
    private const double BULLET_BASE_SPEED = 20.0;

    private const int ENEMY_TIMEOUT = 28;
    private const int MAX_PREDICTION_TICKS = 35;

    private readonly Dictionary<int, EnemyInfo> _enemies = new();
    private readonly Random _random = new Random();

    private int _turn = 0;
    private int _moveDirection = 1;
    private int _radarDirection = 1;
    private int _reverseCooldown = 0;
    private int _patrolIndex = 0;
    private int? _targetId = null;

    private readonly (double X, double Y)[] _patrolPoints =
    {
        (220, 220),
        (220, 420),
        (420, 420),
        (420, 220)
    };

    static void Main(string[] args)
    {
        new Kusar().Start();
    }

    public Kusar() : base(BotInfo.FromFile("Kusar.json"))
    {
    }

    public override void Run()
    {
        BodyColor = Color.Black;
        TurretColor = Color.DarkViolet;
        RadarColor = Color.Lime;
        BulletColor = Color.Gold;
        ScanColor = Color.LightGreen;
        GunColor = Color.Purple;
        TracksColor = Color.DarkGray;

        while (IsRunning)
        {
            _turn++;

            EnemyInfo target = PickGreedyTarget();

            if (target != null)
            {
                RadarLock(target);
                GreedyMovement(target);
                PredictiveAim(target);
                SmartFire(target);
            }
            else
            {
                WideRadar();
                Patrol();
            }

            AntiStuck();

            if (_reverseCooldown > 0)
                _reverseCooldown--;

            Go();
        }
    }

    private EnemyInfo PickGreedyTarget()
    {
        RemoveOldEnemies();

        if (_enemies.Count == 0)
            return null;

        EnemyInfo best = null;
        double bestScore = double.MinValue;

        foreach (EnemyInfo enemy in _enemies.Values)
        {
            double score = TargetScore(enemy);

            if (score > bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        return best;
    }

    private double TargetScore(EnemyInfo enemy)
    {
        double distanceScore = 1200.0 / (enemy.Distance + 1.0);
        double killScore = 180.0 / (enemy.Energy + 8.0);
        double hitScore = HitChance(enemy.Distance, enemy.Speed) * 2.2;
        double dangerScore = EnemyDanger(enemy) * 0.7;

        double lockedBonus = 0.0;

        if (_targetId.HasValue && _targetId.Value == enemy.Id)
            lockedBonus = 65.0;

        if (Energy < LOW_ENERGY)
            dangerScore *= 1.8;

        return distanceScore + killScore + hitScore + lockedBonus - dangerScore;
    }

    private double HitChance(double distance, double speed)
    {
        double time = distance / BULLET_BASE_SPEED;
        double escape = Math.Abs(speed) * time;
        double chance = Math.Max(0.0, 1.0 - (escape / 140.0));

        return chance * 100.0;
    }

    private double EnemyDanger(EnemyInfo enemy)
    {
        double energyDanger = enemy.Energy / 100.0 * 45.0;
        double rangeDanger = 900.0 / (enemy.Distance + 1.0);
        double speedDanger = Math.Abs(enemy.Speed) * 1.5;

        return energyDanger + rangeDanger + speedDanger;
    }

    private void RemoveOldEnemies()
    {
        List<int> expired = new();

        foreach (KeyValuePair<int, EnemyInfo> pair in _enemies)
        {
            if (TurnNumber - pair.Value.LastSeenTurn > ENEMY_TIMEOUT)
                expired.Add(pair.Key);
        }

        foreach (int id in expired)
            _enemies.Remove(id);
    }

    private void RadarLock(EnemyInfo target)
    {
        double absoluteBearing = Direction + target.Bearing;
        double radarTurn = NormalizeAngle(absoluteBearing - RadarDirection);

        if (Math.Abs(radarTurn) < 1.0)
            radarTurn = 40.0 * _radarDirection;

        radarTurn += Math.Sign(radarTurn) * 24.0;

        SetTurnRadarLeft(radarTurn);
    }

    private void WideRadar()
    {
        SetTurnRadarLeft(70.0 * _radarDirection);
    }

    private void GreedyMovement(EnemyInfo target)
    {
        if (WillHitWall() || IsNearWall())
        {
            WallRecovery();
            return;
        }

        double wallForce = WallAvoidanceTurn();

        bool shouldReverse =
            _reverseCooldown <= 0 &&
            (
                _random.NextDouble() < 0.04 ||
                (Math.Abs(target.Speed) < 1.0 && _random.NextDouble() < 0.07)
            );

        if (shouldReverse)
        {
            _moveDirection *= -1;
            _reverseCooldown = 18;
        }

        double orbitAngle = target.Bearing + (88.0 * _moveDirection);

        orbitAngle += wallForce;
        orbitAngle += Math.Sin(_turn * 0.23) * 18.0;
        orbitAngle += (_random.NextDouble() - 0.5) * 12.0;

        if (target.Distance < CLOSE_RANGE)
            orbitAngle += 28.0 * _moveDirection;

        if (target.Distance > FAR_RANGE)
            orbitAngle -= 18.0 * _moveDirection;

        SetTurnLeft(NormalizeAngle(orbitAngle));

        double distanceError = target.Distance - IDEAL_ORBIT;
        double speed = 90.0 + Clamp(distanceError * 0.35, -40.0, 50.0);

        if (IsInSoftWallZone())
            speed *= 0.62;

        if (_random.NextDouble() < 0.02)
            speed *= 0.45;

        SetForward(speed * _moveDirection);
    }

    private bool IsNearWall()
    {
        return X < WALL_MARGIN ||
               X > ArenaWidth - WALL_MARGIN ||
               Y < WALL_MARGIN ||
               Y > ArenaHeight - WALL_MARGIN;
    }

    private bool IsInSoftWallZone()
    {
        return X < SOFT_WALL_MARGIN ||
               X > ArenaWidth - SOFT_WALL_MARGIN ||
               Y < SOFT_WALL_MARGIN ||
               Y > ArenaHeight - SOFT_WALL_MARGIN;
    }

    private bool WillHitWall()
    {
        double ticks = 22.0;
        double heading = ToRadians(Direction);

        double futureX = X + Math.Sin(heading) * Speed * ticks;
        double futureY = Y + Math.Cos(heading) * Speed * ticks;

        return futureX < WALL_MARGIN ||
               futureX > ArenaWidth - WALL_MARGIN ||
               futureY < WALL_MARGIN ||
               futureY > ArenaHeight - WALL_MARGIN;
    }

    private double WallAvoidanceTurn()
    {
        double force = 0.0;

        if (X < SOFT_WALL_MARGIN)
            force += 38.0;

        if (X > ArenaWidth - SOFT_WALL_MARGIN)
            force -= 38.0;

        if (Y < SOFT_WALL_MARGIN)
            force += 38.0;

        if (Y > ArenaHeight - SOFT_WALL_MARGIN)
            force -= 38.0;

        return force * _moveDirection;
    }

    private void WallRecovery()
    {
        double centerX = ArenaWidth / 2.0;
        double centerY = ArenaHeight / 2.0;

        double bearingToCenter = BearingTo(centerX, centerY);
        double escapeTurn = NormalizeAngle(bearingToCenter);

        SetTurnLeft(escapeTurn);
        SetForward(210);

        _reverseCooldown = 20;

        if (Math.Abs(Speed) < 0.5)
        {
            _moveDirection *= -1;
            SetBack(160);
            SetTurnLeft(120 * _moveDirection);
        }
    }

    private void PredictiveAim(EnemyInfo target)
    {
        double power = ChooseFirePower(target);
        double bulletSpeed = 20.0 - (3.0 * power);

        double predictedX = target.X;
        double predictedY = target.Y;
        double heading = ToRadians(target.Heading);
        double velocity = target.Speed;
        double turnRate = ToRadians(target.HeadingDelta * 0.65);

        int time = 0;

        while (time < MAX_PREDICTION_TICKS &&
               (++time) * bulletSpeed < DistanceTo(predictedX, predictedY))
        {
            predictedX += Math.Sin(heading) * velocity;
            predictedY += Math.Cos(heading) * velocity;
            heading += turnRate;

            predictedX = Clamp(predictedX, 25.0, ArenaWidth - 25.0);
            predictedY = Clamp(predictedY, 25.0, ArenaHeight - 25.0);
        }

        double bearing = BearingTo(predictedX, predictedY);
        double gunTurn = NormalizeAngle(bearing - (GunDirection - Direction));

        SetTurnGunLeft(gunTurn * 0.95);
    }

    private void SmartFire(EnemyInfo target)
    {
        if (GunHeat > 0)
            return;

        double power = ChooseFirePower(target);
        double aimError = Math.Abs(GunTurnRemaining);

        if (aimError < 4.5)
        {
            SetFire(power);
        }
        else if (target.Distance < CLOSE_RANGE && aimError < 9.0)
        {
            SetFire(Math.Min(1.2, power));
        }
    }

    private double ChooseFirePower(EnemyInfo target)
    {
        double power;

        if (target.Distance < CLOSE_RANGE)
            power = 2.8;
        else if (target.Distance < MID_RANGE)
            power = 2.1;
        else if (target.Distance < FAR_RANGE)
            power = 1.45;
        else
            power = 0.85;

        if (target.Energy < 14.0)
            power = Math.Min(3.0, Math.Max(0.5, target.Energy / 3.5));

        if (Energy < LOW_ENERGY)
            power = Math.Max(0.55, power - 0.8);

        if (Energy < 8.0)
            power = 0.45;

        power = Math.Min(power, Math.Max(0.1, Energy - 0.25));

        return Clamp(power, 0.1, 3.0);
    }

    private void Patrol()
    {
        (double X, double Y) point = _patrolPoints[_patrolIndex];

        double bearing = BearingTo(point.X, point.Y);
        double distance = DistanceTo(point.X, point.Y);

        if (IsNearWall() || WillHitWall())
        {
            WallRecovery();
            return;
        }

        SetTurnLeft(NormalizeAngle(bearing));
        SetForward(Math.Min(120.0, distance));

        if (distance < 70.0)
        {
            _patrolIndex++;

            if (_patrolIndex >= _patrolPoints.Length)
                _patrolIndex = 0;
        }
    }

    private void AntiStuck()
    {
        if (Math.Abs(Speed) < 0.1 && Math.Abs(DistanceRemaining) > 25.0)
        {
            _moveDirection *= -1;
            _reverseCooldown = 15;

            SetBack(120);
            SetTurnLeft(110 * _moveDirection);
        }
    }

    public override void OnScannedBot(ScannedBotEvent e)
    {
        double bearing = BearingTo(e.X, e.Y);
        double distance = DistanceTo(e.X, e.Y);

        if (_enemies.TryGetValue(e.ScannedBotId, out EnemyInfo existing))
        {
            double oldHeading = existing.Heading;

            existing.Update(
                e.X,
                e.Y,
                e.Energy,
                e.Speed,
                e.Direction,
                NormalizeAngle(e.Direction - oldHeading),
                bearing,
                distance,
                TurnNumber
            );
        }
        else
        {
            _enemies[e.ScannedBotId] = new EnemyInfo(
                e.ScannedBotId,
                e.X,
                e.Y,
                e.Energy,
                e.Speed,
                e.Direction,
                0.0,
                bearing,
                distance,
                TurnNumber
            );
        }

        _targetId = e.ScannedBotId;
    }

    public override void OnHitByBullet(HitByBulletEvent e)
    {
        _moveDirection *= -1;
        _reverseCooldown = 20;

        SetBack(130);
        SetTurnLeft(85 * _moveDirection);
    }

    public override void OnHitWall(HitWallEvent e)
    {
        _moveDirection *= -1;
        _reverseCooldown = 25;

        SetBack(190);
        SetTurnLeft(140 * _moveDirection);
    }

    public override void OnHitBot(HitBotEvent e)
    {
        _moveDirection *= -1;
        _reverseCooldown = 16;

        SetBack(e.IsRammed ? 100 : 65);
        SetTurnLeft(75 * _moveDirection);
    }

    public override void OnBotDeath(BotDeathEvent e)
    {
        _enemies.Remove(e.VictimId);

        if (_targetId.HasValue && _targetId.Value == e.VictimId)
            _targetId = null;
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= 360.0;

        if (angle > 180.0)
            angle -= 360.0;

        if (angle < -180.0)
            angle += 360.0;

        return angle;
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private sealed class EnemyInfo
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
        public int LastSeenTurn { get; private set; }

        public EnemyInfo(
            int id,
            double x,
            double y,
            double energy,
            double speed,
            double heading,
            double headingDelta,
            double bearing,
            double distance,
            int turn)
        {
            Id = id;
            Update(x, y, energy, speed, heading, headingDelta, bearing, distance, turn);
        }

        public void Update(
            double x,
            double y,
            double energy,
            double speed,
            double heading,
            double headingDelta,
            double bearing,
            double distance,
            int turn)
        {
            X = x;
            Y = y;
            Energy = energy;
            Speed = speed;
            Heading = heading;
            HeadingDelta = headingDelta;
            Bearing = bearing;
            Distance = distance;
            LastSeenTurn = turn;
        }
    }
}