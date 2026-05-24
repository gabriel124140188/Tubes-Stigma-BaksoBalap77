using System;
using System.Collections.Generic;
using System.Drawing;
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;

public class AtapBocor : Bot
{
    private const double WALL_MARGIN = 85;
    private const double LOW_ENERGY = 35;
    private const double CRITICAL_ENERGY = 15;

    private const double CLOSE_RANGE = 160;
    private const double MID_RANGE = 380;
    private const double FAR_RANGE = 650;

    private const double IDEAL_DISTANCE = 250;
    private const int ENEMY_TIMEOUT = 28;
    private const int MAX_PREDICT = 42;

    private readonly Dictionary<int, EnemyInfo> _enemies = new();
    private readonly Random _rng = new Random();

    private int _moveDir = 1;
    private int _reverseCooldown = 0;
    private int _turn = 0;
    private int? _lockedTargetId = null;

    static void Main(string[] args) => new AtapBocor().Start();

    public AtapBocor() : base(BotInfo.FromFile("AtapBocor.json")) { }

    public override void Run()
    {
        BodyColor = Color.FromArgb(70, 0, 0);
        TurretColor = Color.OrangeRed;
        RadarColor = Color.Gold;
        BulletColor = Color.Red;
        ScanColor = Color.Yellow;
        GunColor = Color.DarkRed;
        TracksColor = Color.Black;

        while (IsRunning)
        {
            _turn++;

            EnemyInfo target = PickBestTarget();

            if (target != null)
            {
                LockRadar(target);
                SmartMovement(target);
                SmartAim(target);
                SmartFire(target);
            }
            else
            {
                SearchMode();
            }

            if (_reverseCooldown > 0)
                _reverseCooldown--;

            Go();
        }
    }

    // =========================
    // TARGETING GREEDY
    // =========================
    private EnemyInfo PickBestTarget()
    {
        CleanOldEnemies();

        if (_enemies.Count == 0)
            return null;

        EnemyInfo best = null;
        double bestScore = double.MinValue;

        foreach (EnemyInfo e in _enemies.Values)
        {
            double score = TargetScore(e);

            if (score > bestScore)
            {
                bestScore = score;
                best = e;
            }
        }

        return best;
    }

    private double TargetScore(EnemyInfo e)
    {
        double distanceScore = 1600.0 / (e.Distance + 1);
        double weakScore = 420.0 / (e.Energy + 4);
        double hitScore = HitChance(e) * 3.2;
        double lockBonus = (_lockedTargetId.HasValue && _lockedTargetId.Value == e.Id) ? 150 : 0;

        double dangerPenalty = 0;

        if (Energy < LOW_ENERGY)
        {
            dangerPenalty += e.Energy * 0.35;
            dangerPenalty += 850.0 / (e.Distance + 1);
            dangerPenalty += Math.Abs(e.Speed) * 1.5;
        }
        else
        {
            dangerPenalty += e.Energy * 0.10;
            dangerPenalty += 420.0 / (e.Distance + 1);
        }

        if (e.Energy < 15)
            weakScore += 220;

        if (e.Distance < CLOSE_RANGE)
            distanceScore += 100;

        return distanceScore + weakScore + hitScore + lockBonus - dangerPenalty;
    }

    private double HitChance(EnemyInfo e)
    {
        double bulletSpeed = 14.0;
        double time = e.Distance / bulletSpeed;
        double evade = Math.Abs(e.Speed) * time;

        double chance = 1.0 - evade / 165.0;
        return Clamp(chance, 0, 1) * 100;
    }

    private void CleanOldEnemies()
    {
        List<int> remove = new();

        foreach (var pair in _enemies)
        {
            if (TurnNumber - pair.Value.LastSeenTurn > ENEMY_TIMEOUT)
                remove.Add(pair.Key);
        }

        foreach (int id in remove)
            _enemies.Remove(id);
    }

    // =========================
    // RADAR
    // =========================
    private void LockRadar(EnemyInfo target)
    {
        double absoluteBearing = Direction + target.Bearing;
        double radarTurn = NormalizeAngle(absoluteBearing - RadarDirection);

        if (Math.Abs(radarTurn) < 1)
            radarTurn = 45;

        SetTurnRadarLeft(radarTurn + Math.Sign(radarTurn) * 35);
    }

    private void SearchMode()
    {
        SetTurnRadarLeft(360);
        SetTurnLeft(24);
        SetForward(140);
    }

    // =========================
    // MOVEMENT
    // =========================
    private void SmartMovement(EnemyInfo target)
    {
        if (NearWall() || PredictWall())
        {
            EscapeWall();
            return;
        }

        bool enemyFired = target.EnergyDrop > 0.1 && target.EnergyDrop <= 3.0;

        if (_reverseCooldown <= 0)
        {
            if (enemyFired || _rng.NextDouble() < 0.055 || target.Distance < CLOSE_RANGE)
            {
                _moveDir *= -1;
                _reverseCooldown = enemyFired ? 10 : 16;
            }
        }

        double orbitAngle = target.Bearing + (92 * _moveDir);

        if (target.Distance < CLOSE_RANGE)
            orbitAngle += 38 * _moveDir;
        else if (target.Distance > FAR_RANGE)
            orbitAngle -= 26 * _moveDir;

        orbitAngle += Math.Sin(_turn * 0.34) * 26;
        orbitAngle += (_rng.NextDouble() - 0.5) * 16;

        SetTurnLeft(NormalizeAngle(orbitAngle));

        double distanceError = target.Distance - IDEAL_DISTANCE;
        double speed = 115 + Clamp(distanceError * 0.40, -55, 75);

        if (enemyFired)
            speed *= 1.15;

        if (_rng.NextDouble() < 0.03)
            speed *= 0.35;

        if (Energy < CRITICAL_ENERGY)
            speed *= 0.75;

        SetForward(speed * _moveDir);
    }

    private bool NearWall()
    {
        return X < WALL_MARGIN ||
               X > ArenaWidth - WALL_MARGIN ||
               Y < WALL_MARGIN ||
               Y > ArenaHeight - WALL_MARGIN;
    }

    private bool PredictWall()
    {
        double heading = ToRadians(Direction);
        double futureX = X + Math.Sin(heading) * Speed * 18;
        double futureY = Y + Math.Cos(heading) * Speed * 18;

        return futureX < WALL_MARGIN ||
               futureX > ArenaWidth - WALL_MARGIN ||
               futureY < WALL_MARGIN ||
               futureY > ArenaHeight - WALL_MARGIN;
    }

    private void EscapeWall()
    {
        double centerBearing = BearingTo(ArenaWidth / 2, ArenaHeight / 2);
        double turn = NormalizeAngle(centerBearing + 45 * _moveDir);

        SetTurnLeft(turn);
        SetForward(175);

        if (Math.Abs(Speed) < 0.4)
        {
            _moveDir *= -1;
            _reverseCooldown = 20;
            SetBack(150);
        }
    }

    // =========================
    // AIMING
    // =========================
    private void SmartAim(EnemyInfo target)
    {
        double power = SelectFirePower(target);
        double bulletSpeed = 20 - 3 * power;

        double px = target.X;
        double py = target.Y;
        double heading = ToRadians(target.Heading);
        double velocity = target.Speed;
        double headingDelta = ToRadians(target.HeadingDelta * 0.72);

        int time = 0;

        while (time < MAX_PREDICT && (++time) * bulletSpeed < DistanceTo(px, py))
        {
            px += Math.Sin(heading) * velocity;
            py += Math.Cos(heading) * velocity;
            heading += headingDelta;

            px = Clamp(px, 22, ArenaWidth - 22);
            py = Clamp(py, 22, ArenaHeight - 22);
        }

        double predictedBearing = BearingTo(px, py);
        double gunTurn = NormalizeAngle(predictedBearing - (GunDirection - Direction));

        SetTurnGunLeft(gunTurn * 0.95);
    }

    // =========================
    // FIRE CONTROL
    // =========================
    private void SmartFire(EnemyInfo target)
    {
        if (GunHeat > 0)
            return;

        double power = SelectFirePower(target);
        double error = Math.Abs(GunTurnRemaining);

        if (target.Distance < CLOSE_RANGE && error < 9)
        {
            SetFire(Math.Min(power, 2.4));
            return;
        }

        if (error < 4.5)
        {
            SetFire(power);
            return;
        }

        if (target.Energy < 10 && error < 8)
        {
            SetFire(Math.Min(power, 1.3));
        }
    }

    private double SelectFirePower(EnemyInfo target)
    {
        double power;

        if (target.Distance < CLOSE_RANGE)
            power = 3.0;
        else if (target.Distance < MID_RANGE)
            power = 2.25;
        else if (target.Distance < FAR_RANGE)
            power = 1.55;
        else
            power = 0.95;

        if (target.Energy < 16)
            power = Math.Min(power, Math.Max(0.55, target.Energy / 3.4));

        if (Energy < CRITICAL_ENERGY)
            power -= 1.10;
        else if (Energy < LOW_ENERGY)
            power -= 0.55;

        if (Energy < 7)
            power = 0.35;

        power = Math.Min(power, Energy - 0.25);

        return Clamp(power, 0.1, 3.0);
    }

    // =========================
    // EVENTS
    // =========================
    public override void OnScannedBot(ScannedBotEvent e)
    {
        double bearing = BearingTo(e.X, e.Y);
        double distance = DistanceTo(e.X, e.Y);

        if (_enemies.TryGetValue(e.ScannedBotId, out EnemyInfo old))
        {
            double oldHeading = old.Heading;
            double oldEnergy = old.Energy;

            old.Update(
                e.X,
                e.Y,
                e.Energy,
                e.Speed,
                e.Direction,
                NormalizeAngle(e.Direction - oldHeading),
                oldEnergy - e.Energy,
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
                0,
                0,
                bearing,
                distance,
                TurnNumber
            );
        }

        _lockedTargetId = e.ScannedBotId;
    }

    public override void OnHitByBullet(HitByBulletEvent e)
    {
        _moveDir *= -1;
        _reverseCooldown = 18;

        SetBack(145);
        SetTurnLeft(95 * _moveDir);
    }

    public override void OnHitWall(HitWallEvent e)
    {
        _moveDir *= -1;
        _reverseCooldown = 22;

        SetBack(170);
        SetTurnLeft(125 * _moveDir);
    }

    public override void OnHitBot(HitBotEvent e)
    {
        _moveDir *= -1;
        _reverseCooldown = 16;

        SetBack(e.IsRammed ? 120 : 75);
        SetTurnLeft(80 * _moveDir);
    }

    public override void OnBotDeath(BotDeathEvent e)
    {
        _enemies.Remove(e.VictimId);

        if (_lockedTargetId.HasValue && _lockedTargetId.Value == e.VictimId)
            _lockedTargetId = null;
    }

    // =========================
    // HELPERS
    // =========================
    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= 360;

        if (angle > 180)
            angle -= 360;

        if (angle < -180)
            angle += 360;

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
        public double EnergyDrop { get; private set; }
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
            double energyDrop,
            double bearing,
            double distance,
            int turn)
        {
            Id = id;
            Update(x, y, energy, speed, heading, headingDelta, energyDrop, bearing, distance, turn);
        }

        public void Update(
            double x,
            double y,
            double energy,
            double speed,
            double heading,
            double headingDelta,
            double energyDrop,
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
            EnergyDrop = energyDrop;
            Bearing = bearing;
            Distance = distance;
            LastSeenTurn = turn;
        }
    }
}