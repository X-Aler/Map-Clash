using System;
using System.Collections.Generic;

namespace MapClash
{
    public enum MatchPhase { Spawn, Playing, Paused, Finished }
    public enum AiDifficulty { Easy, Normal, Hard }
    public enum CommandKind { ChooseSpawn, Attack, Recall, SetTroopRatio }
    public enum OperationPhase { Advancing, Returning }

    public sealed class MatchSettings
    {
        public int Seed { get; set; } = 12345;
        public int CountryCount { get; set; } = 40;
        public string PlayerName { get; set; } = "";
        public AiDifficulty Difficulty { get; set; } = AiDifficulty.Normal;
        public double VictoryFraction { get; set; } = 0.8;
        public int TimeLimitSeconds { get; set; }
    }

    public sealed class SimulationBalance
    {
        public const int TicksPerSecond = 10;
        public double StartingPopulation { get; set; } = 25000;
        public double StartingGold { get; set; } = 50000;
        public double StartingTroopRatio { get; set; } = 0.7;
        public double BasePopulation { get; set; } = 50000;
        public double CityPopulationBonus { get; set; } = 25000;
        public double WorkerIncomePerSecond { get; set; } = 0.05;
        public double WorkerGrowthMultiplier { get; set; } = 1.3;
        public double RoleConversionPerSecond { get; set; } = 0.05;
        public double MinReserveFraction { get; set; } = 0.2;
        public double BaseTileCost { get; set; } = 2;
        public double StartingRadius { get; set; } = 4;
        public int SpawnSeconds { get; set; } = 15;
        public int ReturnTicks { get; set; } = 20;
        public double RecallSurvivalFraction { get; set; } = 0.75;
        public double BaseAdvancePerSecond { get; set; } = 100;
        public double TroopAdvanceCoefficient { get; set; } = 0.01;
        public double MaxAdvancePerSecond { get; set; } = 2000;
        public double CaptureSpread { get; set; } = 0.5;
    }

    public readonly struct MatchCommand
    {
        public MatchCommand(CommandKind kind, int countryId, int targetCell = -1,
            double amount = 0.25, int objectId = -1)
        {
            Kind = kind;
            CountryId = countryId;
            TargetCell = targetCell;
            Amount = amount;
            ObjectId = objectId;
        }

        public CommandKind Kind { get; }
        public int CountryId { get; }
        public int TargetCell { get; }
        public double Amount { get; }
        public int ObjectId { get; }
    }

    public sealed class CountryState
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        public bool IsPlayer { get; internal set; }
        public bool IsAlive { get; internal set; } = true;
        public int CapitalCell { get; internal set; } = -1;
        public int LandTiles { get; internal set; }
        public double Workers { get; internal set; }
        public double DefendingTroops { get; internal set; }
        public double DeployedTroops { get; internal set; }
        public double ReturningTroops { get; internal set; }
        public double Gold { get; internal set; }
        public double MaxPopulation { get; internal set; }
        public double GrowthPerSecond { get; internal set; }
        public double IncomePerSecond { get; internal set; }
        public double TargetTroopRatio { get; internal set; } = 0.7;
        public double PeakLandFraction { get; internal set; }
        public double EarnedGold { get; internal set; }
        public double HomePopulation => Workers + DefendingTroops;
        public double TotalPopulation => HomePopulation + DeployedTroops + ReturningTroops;
    }

    public sealed class OperationState
    {
        public int Id { get; internal set; }
        public int CountryId { get; internal set; }
        public int TargetCountryId { get; internal set; }
        public int TargetCell { get; internal set; }
        public int FrontCell { get; internal set; }
        public double Troops { get; internal set; }
        public OperationPhase Phase { get; internal set; }
        public long ReturnAtTick { get; internal set; }
    }

    public readonly struct MatchEvent
    {
        public MatchEvent(string key, int countryId = 0, string detail = "")
        {
            Key = key;
            CountryId = countryId;
            Detail = detail;
        }

        public string Key { get; }
        public int CountryId { get; }
        public string Detail { get; }
    }

    public sealed class MatchResult
    {
        public int WinnerCountryId { get; internal set; }
        public bool IsDraw { get; internal set; }
        public double DurationSeconds { get; internal set; }
        public string ReasonKey { get; internal set; }
    }

    public interface IMatchSimulation : IDisposable
    {
        WorldMapData Map { get; }
        MatchSettings Settings { get; }
        MatchPhase Phase { get; }
        long Tick { get; }
        double ElapsedSeconds { get; }
        double SpawnSecondsRemaining { get; }
        IReadOnlyList<CountryState> Countries { get; }
        IReadOnlyList<OperationState> Operations { get; }
        IReadOnlyList<int> ChangedCells { get; }
        IReadOnlyList<MatchEvent> Events { get; }
        MatchResult Result { get; }
        int GetOwner(int cell);
        CountryState GetCountry(int countryId);
        void Enqueue(MatchCommand command);
        void Advance(double deltaSeconds);
        void SetPaused(bool isPaused);
    }
}
