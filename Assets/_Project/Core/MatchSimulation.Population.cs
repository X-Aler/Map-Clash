using System;

namespace MapClash
{
    public sealed partial class MatchSimulation
    {
        private void RefreshPopulationLimit(CountryState country)
        {
            var baseArea = country.LandTiles * _cellArea;
            country.MaxPopulation = 2 * (Math.Pow(baseArea, 0.6) * 1000 + _balance.BasePopulation);
            if (!country.IsPlayer && Settings.Difficulty == AiDifficulty.Hard)
            {
                country.MaxPopulation *= 1.1;
            }
        }

        private void UpdatePopulationAndIncome()
        {
            for (var i = 0; i < _countries.Length; i++)
            {
                var country = _countries[i];
                if (!country.IsAlive)
                {
                    continue;
                }

                RefreshPopulationLimit(country);
                var homePopulation = country.HomePopulation;
                var targetTroops = homePopulation * country.TargetTroopRatio;
                var maxConversion = homePopulation * _balance.RoleConversionPerSecond * TickDuration;
                var conversion = Math.Max(-maxConversion, Math.Min(maxConversion, targetTroops - country.DefendingTroops));
                country.DefendingTroops += conversion;
                country.Workers -= conversion;

                var freeCapacity = Math.Max(0, country.MaxPopulation - country.TotalPopulation);
                var baseGrowth = (10 + Math.Pow(homePopulation, 0.73) / 4) * Math.Max(0, 1 - homePopulation / country.MaxPopulation);
                if (!country.IsPlayer && Settings.Difficulty == AiDifficulty.Hard)
                {
                    baseGrowth *= 1.1;
                }

                var troopGrowth = baseGrowth * country.TargetTroopRatio;
                var workerGrowth = baseGrowth * (1 - country.TargetTroopRatio) * _balance.WorkerGrowthMultiplier;
                var totalGrowth = troopGrowth + workerGrowth;
                if (totalGrowth > freeCapacity && totalGrowth > 0)
                {
                    var capacityFactor = freeCapacity / totalGrowth;
                    troopGrowth *= capacityFactor;
                    workerGrowth *= capacityFactor;
                }

                country.DefendingTroops += troopGrowth;
                country.Workers += workerGrowth;
                country.GrowthPerSecond = (troopGrowth + workerGrowth) * SimulationBalance.TicksPerSecond;
                country.IncomePerSecond = country.Workers * _balance.WorkerIncomePerSecond;
                var income = country.IncomePerSecond * TickDuration;
                country.Gold += income;
                country.EarnedGold += income;
            }
        }
    }
}
