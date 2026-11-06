using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MapClash
{
    public sealed partial class MatchSimulation : IMatchSimulation
    {
        private const double TickDuration = 1.0 / SimulationBalance.TicksPerSecond;
        private readonly SimulationBalance _balance;
        private readonly CountryState[] _countries;
        private readonly ReadOnlyCollection<CountryState> _countryView;
        private readonly List<OperationState> _operations = new List<OperationState>();
        private readonly List<Wave> _waves = new List<Wave>();
        private readonly List<int> _changedCells = new List<int>();
        private readonly List<MatchEvent> _events = new List<MatchEvent>();
        private readonly Queue<MatchCommand> _commands = new Queue<MatchCommand>();
        private readonly ReadOnlyCollection<OperationState> _operationView;
        private readonly ReadOnlyCollection<int> _changedCellView;
        private readonly ReadOnlyCollection<MatchEvent> _eventView;
        private readonly int[] _owners;
        private readonly bool[] _isChanged;
        private readonly BorderIndex[] _borders;
        private readonly int[] _landCells;
        private readonly int[] _landComponents;
        private readonly double _cellArea;
        private uint _randomState;
        private double _accumulator;
        private long _spawnTicks;
        private long _playingTicks;
        private int _nextOperationId = 1;
        private MatchPhase _phaseBeforePause;
        private bool _isDisposed;

        public MatchSimulation(WorldMapData map, MatchSettings settings, SimulationBalance balance)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (settings.CountryCount < 10 || settings.CountryCount > 60)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.CountryCount), "Country count includes the player and must be 10–60.");
            }

            if (!IsFinite(settings.VictoryFraction) || settings.VictoryFraction <= 0 || settings.VictoryFraction > 1 || settings.TimeLimitSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "Victory fraction must be greater than zero and at most one; time limit cannot be negative.");
            }

            if (map.TotalLandTiles < settings.CountryCount)
            {
                throw new ArgumentException("The map has fewer land cells than countries.", nameof(map));
            }

            Settings = new MatchSettings
            {
                Seed = settings.Seed,
                CountryCount = settings.CountryCount,
                PlayerName = settings.PlayerName ?? string.Empty,
                Difficulty = settings.Difficulty,
                VictoryFraction = settings.VictoryFraction,
                TimeLimitSeconds = settings.TimeLimitSeconds
            };
            _balance = CopyBalance(balance ?? throw new ArgumentNullException(nameof(balance)));
            ValidateBalance(_balance);
            _cellArea = map.CellScale * map.CellScale;
            _owners = new int[map.Width * map.Height];
            _isChanged = new bool[_owners.Length];
            _countries = new CountryState[Settings.CountryCount];
            _borders = new BorderIndex[Settings.CountryCount + 1];
            _landCells = new int[map.TotalLandTiles];
            _landComponents = new int[_owners.Length];
            BuildLandComponents();

            _randomState = unchecked((uint)Settings.Seed) ^ 0x9E3779B9u;
            if (_randomState == 0)
            {
                _randomState = 1;
            }

            _countryView = Array.AsReadOnly(_countries);
            _operationView = _operations.AsReadOnly();
            _changedCellView = _changedCells.AsReadOnly();
            _eventView = _events.AsReadOnly();
            Phase = MatchPhase.Spawn;
            InitializeCountries();
            PrepareStartingRegions();
        }

        public WorldMapData Map { get; }
        public MatchSettings Settings { get; }
        public MatchPhase Phase { get; private set; }
        public long Tick { get; private set; }
        public double ElapsedSeconds => _playingTicks * TickDuration;
        public double SpawnSecondsRemaining => Math.Max(0, _balance.SpawnSeconds - _spawnTicks * TickDuration);
        public IReadOnlyList<CountryState> Countries => _countryView;
        public IReadOnlyList<OperationState> Operations => _operationView;
        public IReadOnlyList<int> ChangedCells => _changedCellView;
        public IReadOnlyList<MatchEvent> Events => _eventView;
        public MatchResult Result { get; private set; }

        public int GetOwner(int cell)
        {
            return cell >= 0 && cell < _owners.Length ? _owners[cell] : 0;
        }

        public CountryState GetCountry(int countryId)
        {
            return countryId > 0 && countryId <= _countries.Length ? _countries[countryId - 1] : null;
        }

        public void Enqueue(MatchCommand command)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(MatchSimulation));
            }

            if (Phase != MatchPhase.Finished && Phase != MatchPhase.Paused)
            {
                _commands.Enqueue(command);
            }
        }

        public void Advance(double deltaSeconds)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(MatchSimulation));
            }

            if (!IsFinite(deltaSeconds) || deltaSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }

            ClearFrameChanges();
            if (Phase == MatchPhase.Paused || Phase == MatchPhase.Finished)
            {
                return;
            }

            _accumulator += deltaSeconds;
            while (_accumulator + 1e-9 >= TickDuration && Phase != MatchPhase.Finished)
            {
                _accumulator -= TickDuration;
                if (_accumulator < 0)
                {
                    _accumulator = 0;
                }

                Step();
            }
        }

        public void SetPaused(bool isPaused)
        {
            if (_isDisposed || Phase == MatchPhase.Finished)
            {
                return;
            }

            if (isPaused && Phase != MatchPhase.Paused)
            {
                _phaseBeforePause = Phase;
                Phase = MatchPhase.Paused;
            }
            else if (!isPaused && Phase == MatchPhase.Paused)
            {
                Phase = _phaseBeforePause;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _commands.Clear();
            _waves.Clear();
            _operations.Clear();
            ClearFrameChanges();
            Array.Clear(_owners, 0, _owners.Length);
            for (var i = 0; i < _countries.Length; i++)
            {
                var country = _countries[i];
                country.IsAlive = false;
                country.Workers = 0;
                country.DefendingTroops = 0;
                country.DeployedTroops = 0;
                country.ReturningTroops = 0;
                country.LandTiles = 0;
                _borders[country.Id].Clear();
            }

            Phase = MatchPhase.Finished;
        }

        private void Step()
        {
            Tick++;
            if (Phase == MatchPhase.Playing)
            {
                QueueBotDecisions();
            }

            ExecuteCommands();
            if (Phase == MatchPhase.Spawn)
            {
                _spawnTicks++;
                if (_spawnTicks >= _balance.SpawnSeconds * SimulationBalance.TicksPerSecond)
                {
                    BeginMatch();
                }

                return;
            }

            _playingTicks++;
            UpdatePopulationAndIncome();
            ProcessOperations();
            CheckResult();
        }

        private void ExecuteCommands()
        {
            while (_commands.Count > 0)
            {
                var command = _commands.Dequeue();
                var country = GetCountry(command.CountryId);
                if (country == null || !country.IsAlive)
                {
                    Reject(command.CountryId);
                    continue;
                }

                if (command.Kind == CommandKind.ChooseSpawn)
                {
                    if (Phase == MatchPhase.Spawn && CanPlaceCapital(command.CountryId, command.TargetCell))
                    {
                        country.CapitalCell = command.TargetCell;
                        PrepareStartingRegions();
                        _events.Add(new MatchEvent("spawn.selected", command.CountryId));
                    }
                    else
                    {
                        Reject(command.CountryId);
                    }

                    continue;
                }

                if (Phase != MatchPhase.Playing)
                {
                    Reject(command.CountryId);
                    continue;
                }

                switch (command.Kind)
                {
                    case CommandKind.Attack:
                        TryAttack(command);
                        break;
                    case CommandKind.Recall:
                        TryRecall(command);
                        break;
                    case CommandKind.SetTroopRatio:
                        if (IsFinite(command.Amount) && command.Amount >= 0 && command.Amount <= 1)
                        {
                            country.TargetTroopRatio = command.Amount;
                        }
                        else
                        {
                            Reject(command.CountryId);
                        }

                        break;
                    default:
                        Reject(command.CountryId);
                        break;
                }
            }
        }

        private void ClearFrameChanges()
        {
            for (var i = 0; i < _changedCells.Count; i++)
            {
                _isChanged[_changedCells[i]] = false;
            }

            _changedCells.Clear();
            _events.Clear();
        }

        private void Reject(int countryId)
        {
            _events.Add(new MatchEvent("command.invalid", countryId));
        }

        private int NextRandom(int exclusiveMaximum)
        {
            var value = _randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _randomState = value;
            return (int)(value % (uint)exclusiveMaximum);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static SimulationBalance CopyBalance(SimulationBalance source)
        {
            return new SimulationBalance
            {
                StartingPopulation = source.StartingPopulation,
                StartingGold = source.StartingGold,
                StartingTroopRatio = source.StartingTroopRatio,
                BasePopulation = source.BasePopulation,
                CityPopulationBonus = source.CityPopulationBonus,
                WorkerIncomePerSecond = source.WorkerIncomePerSecond,
                WorkerGrowthMultiplier = source.WorkerGrowthMultiplier,
                RoleConversionPerSecond = source.RoleConversionPerSecond,
                MinReserveFraction = source.MinReserveFraction,
                BaseTileCost = source.BaseTileCost,
                StartingRadius = source.StartingRadius,
                SpawnSeconds = source.SpawnSeconds,
                ReturnTicks = source.ReturnTicks,
                RecallSurvivalFraction = source.RecallSurvivalFraction,
                BaseAdvancePerSecond = source.BaseAdvancePerSecond,
                TroopAdvanceCoefficient = source.TroopAdvanceCoefficient,
                MaxAdvancePerSecond = source.MaxAdvancePerSecond,
                CaptureSpread = source.CaptureSpread
            };
        }

        private static void ValidateBalance(SimulationBalance balance)
        {
            var positive = new[]
            {
                balance.StartingPopulation, balance.BasePopulation, balance.WorkerGrowthMultiplier,
                balance.BaseTileCost, balance.MaxAdvancePerSecond
            };
            for (var i = 0; i < positive.Length; i++)
            {
                if (!IsFinite(positive[i]) || positive[i] <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(balance), "Population, growth multiplier, tile cost and maximum advance must be positive and finite.");
                }
            }

            var nonNegative = new[]
            {
                balance.StartingGold, balance.CityPopulationBonus, balance.WorkerIncomePerSecond,
                balance.RoleConversionPerSecond, balance.StartingRadius,
                balance.BaseAdvancePerSecond, balance.TroopAdvanceCoefficient
            };
            for (var i = 0; i < nonNegative.Length; i++)
            {
                if (!IsFinite(nonNegative[i]) || nonNegative[i] < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(balance), "Balance values must be nonnegative and finite.");
                }
            }

            if (!IsFinite(balance.StartingTroopRatio) || balance.StartingTroopRatio < 0 || balance.StartingTroopRatio > 1 ||
                !IsFinite(balance.MinReserveFraction) || balance.MinReserveFraction < 0 || balance.MinReserveFraction > 1 ||
                !IsFinite(balance.RecallSurvivalFraction) || balance.RecallSurvivalFraction < 0 || balance.RecallSurvivalFraction > 1 ||
                !IsFinite(balance.CaptureSpread) || balance.CaptureSpread < 0 || balance.CaptureSpread > 1 ||
                balance.ReturnTicks < 0 || balance.SpawnSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(balance), "Fractions must be in [0,1] and timers cannot be negative.");
            }
        }
    }
}
