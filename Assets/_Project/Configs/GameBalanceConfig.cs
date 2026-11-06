using UnityEngine;

namespace MapClash
{
    [CreateAssetMenu(menuName = "Map Clash/Game Balance")]
    public sealed class GameBalanceConfig : ScriptableObject
    {
        [SerializeField] private double _startingPopulation = 25000;
        [SerializeField] private double _startingGold = 50000;
        [SerializeField] private double _startingTroopRatio = 0.7;
        [SerializeField] private double _basePopulation = 50000;
        [SerializeField, Min(0), Tooltip("Additional population capacity granted by each owned city.")]
        private double _cityPopulationBonus = 25000;
        [SerializeField] private double _workerIncomePerSecond = 0.05;
        [SerializeField, Min(0.0001f), Tooltip("Multiplier applied to worker-driven population growth.")]
        private double _workerGrowthMultiplier = 1.3;
        [SerializeField] private double _roleConversionPerSecond = 0.05;
        [SerializeField, Range(0, 1), Tooltip("Minimum fraction of home troops reserved when deploying an attack.")]
        [HideInInspector] private double _minReserveFraction = 0.2;
        [SerializeField, HideInInspector] private double _baseTileCost = 2;
        [SerializeField, Min(0), Tooltip("Radius of initial territory around each capital, in map units.")]
        private double _startingRadius = 4;
        [SerializeField, Min(0), Tooltip("Time allowed to select a spawn before the match begins, in seconds.")]
        private int _spawnSeconds = 15;
        [SerializeField, Min(0), Tooltip("Delay before returning troops arrive home, in simulation ticks (10 ticks per second).")]
        [HideInInspector] private int _returnTicks = 20;
        [SerializeField, Range(0, 1), Tooltip("Fraction of deployed troops that survives a recall.")]
        [HideInInspector] private double _recallSurvivalFraction = 0.75;
        [SerializeField, Min(0), Tooltip("Base attack advance budget per second before the troop contribution.")]
        [HideInInspector] private double _baseAdvancePerSecond = 100;
        [SerializeField, Min(0), Tooltip("Additional attack advance budget per second contributed by each deployed troop.")]
        [HideInInspector] private double _troopAdvanceCoefficient = 0.01;
        [SerializeField, Min(0.0001f), Tooltip("Maximum attack advance budget per second.")]
        [HideInInspector] private double _maxAdvancePerSecond = 2000;
        [SerializeField, Range(0, 1), Tooltip("Capture spread: 0 follows only the route to the click; 1 can spread over the entire connected target territory while troops last. Intermediate values give irregular, directed expansion.")]
        [HideInInspector] private double _captureSpread = 0.5;

        public SimulationBalance CreateBalance()
        {
            return new SimulationBalance
            {
                StartingPopulation = _startingPopulation,
                StartingGold = _startingGold,
                StartingTroopRatio = _startingTroopRatio,
                BasePopulation = _basePopulation,
                CityPopulationBonus = _cityPopulationBonus,
                WorkerIncomePerSecond = _workerIncomePerSecond,
                WorkerGrowthMultiplier = _workerGrowthMultiplier,
                RoleConversionPerSecond = _roleConversionPerSecond,
                MinReserveFraction = _minReserveFraction,
                BaseTileCost = _baseTileCost,
                StartingRadius = _startingRadius,
                SpawnSeconds = _spawnSeconds,
                ReturnTicks = _returnTicks,
                RecallSurvivalFraction = _recallSurvivalFraction,
                BaseAdvancePerSecond = _baseAdvancePerSecond,
                TroopAdvanceCoefficient = _troopAdvanceCoefficient,
                MaxAdvancePerSecond = _maxAdvancePerSecond,
                CaptureSpread = _captureSpread
            };
        }
    }
}
