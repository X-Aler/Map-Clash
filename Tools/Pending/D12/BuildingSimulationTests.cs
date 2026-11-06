using System;
using System.Reflection;
using NUnit.Framework;

namespace MapClash.Tests
{
    public sealed class BuildingSimulationTests
    {
        [TestCase(BuildingKind.City)]
        [TestCase(BuildingKind.Factory)]
        [TestCase(BuildingKind.Port)]
        public void EconomicLevelsHaveExactPricesAndStopAtLevelFour(BuildingKind kind)
        {
            using (var simulation = CreateFixture())
            {
                var cell = Cell(simulation, 40, 2);
                var country = simulation.GetCountry(1);
                var gold = country.Gold;
                Build(simulation, 1, kind, cell);
                var building = simulation.Buildings[0];
                Assert.That(country.Gold, Is.EqualTo(gold - 125000));
                Upgrade(simulation, 1, building.Id);
                Assert.That(country.Gold, Is.EqualTo(gold - 125000 - 250000));
                Upgrade(simulation, 1, building.Id);
                Assert.That(country.Gold, Is.EqualTo(gold - 125000 - 250000 - 500000));
                Upgrade(simulation, 1, building.Id);
                Assert.That(country.Gold, Is.EqualTo(gold - 125000 - 250000 - 500000 - 1000000));
                var afterLevelFour = country.Gold;
                Upgrade(simulation, 1, building.Id);
                Assert.That(building.Level, Is.EqualTo(4));
                Assert.That(country.Gold, Is.EqualTo(afterLevelFour));
                Assert.That(simulation.Buildings.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void RebuildingSameOwnEconomicKindUpgradesWithoutSelfSpacingFailure()
        {
            using (var simulation = CreateFixture())
            {
                var cell = Cell(simulation, 20, 2);
                var originalGold = simulation.GetCountry(1).Gold;
                Build(simulation, 1, BuildingKind.City, cell);
                Build(simulation, 1, BuildingKind.City, cell);
                Assert.That(simulation.Buildings.Count, Is.EqualTo(1));
                Assert.That(simulation.Buildings[0].Level, Is.EqualTo(2));
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(originalGold - 125000 - 250000));
            }
        }

        [Test]
        public void DifferentKindOnOccupiedCellAndForeignUpgradeDoNotSpendGold()
        {
            using (var simulation = CreateFixture())
            {
                var cell = Cell(simulation, 20, 2);
                Build(simulation, 1, BuildingKind.City, cell);
                var playerGold = simulation.GetCountry(1).Gold;
                var foreignGold = simulation.GetCountry(2).Gold;
                Build(simulation, 1, BuildingKind.Factory, cell);
                Upgrade(simulation, 2, simulation.Buildings[0].Id);
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(playerGold));
                Assert.That(simulation.GetCountry(2).Gold, Is.EqualTo(foreignGold));
                Assert.That(simulation.Buildings[0].Kind, Is.EqualTo(BuildingKind.City));
                Assert.That(simulation.Buildings[0].Level, Is.EqualTo(1));
            }
        }

        [Test]
        public void InvalidPlacementAndInsufficientGoldLeaveResourcesAndBuildingsUnchanged()
        {
            using (var simulation = CreateFixture())
            {
                SetCountryProperty(simulation.GetCountry(1), nameof(CountryState.Gold), 124999.0);
                var gold = simulation.GetCountry(1).Gold;
                Build(simulation, 1, BuildingKind.City, Cell(simulation, 20, 2));
                Build(simulation, 1, BuildingKind.City, Cell(simulation, 520, 2));
                Build(simulation, 1, BuildingKind.City, Cell(simulation, 40, 3));
                Build(simulation, 1, (BuildingKind)99, Cell(simulation, 20, 2));
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(gold));
                Assert.That(simulation.Buildings.Count, Is.Zero);
            }
        }

        [TestCase(2048)]
        [TestCase(1024)]
        public void PortRequiresRepresentedAdjacentWaterOnOwnLand(int mapWidth)
        {
            using (var simulation = CreateFixture(mapWidth))
            {
                var gold = simulation.GetCountry(1).Gold;
                Build(simulation, 1, BuildingKind.Port, Cell(simulation, 20, 2));
                Build(simulation, 1, BuildingKind.Port, Cell(simulation, 20, 0));
                Assert.That(simulation.Buildings.Count, Is.Zero);
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(gold));
                Build(simulation, 1, BuildingKind.Port, Cell(simulation, 40, 2));
                Assert.That(simulation.Buildings.Count, Is.EqualTo(1));
                Assert.That(simulation.Buildings[0].Kind, Is.EqualTo(BuildingKind.Port));
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(gold - 125000));
            }
        }

        [TestCase(2048, 11, false)]
        [TestCase(2048, 12, true)]
        [TestCase(1024, 5, false)]
        [TestCase(1024, 6, true)]
        public void BuildingSpacingUsesBaseDistanceAndAllowsExactBoundary(int mapWidth, int offset, bool canPlace)
        {
            using (var simulation = CreateFixture(mapWidth))
            {
                Build(simulation, 1, BuildingKind.City, Cell(simulation, 20, 2));
                var gold = simulation.GetCountry(1).Gold;
                Build(simulation, 1, BuildingKind.Factory, Cell(simulation, 20 + offset, 2));
                Assert.That(simulation.Buildings.Count, Is.EqualTo(canPlace ? 2 : 1));
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(gold - (canPlace ? 125000 : 0)));
            }
        }

        [Test]
        public void BuildingSpacingIncludesForeignBuildingsAndHorizontalSeam()
        {
            using (var simulation = CreateFixture())
            {
                ClaimCell(simulation, Cell(simulation, 2045, 2), 2);
                Build(simulation, 2, BuildingKind.City, Cell(simulation, 2045, 2));
                var gold = simulation.GetCountry(1).Gold;
                Build(simulation, 1, BuildingKind.City, Cell(simulation, 2, 2));
                Assert.That(simulation.Buildings.Count, Is.EqualTo(1));
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(gold));
            }
        }

        [Test]
        public void DefensePostPricesIncreaseToCap()
        {
            using (var simulation = CreateFixture())
            {
                var expectedGold = simulation.GetCountry(1).Gold;
                double[] prices = { 50000, 100000, 150000, 200000, 250000, 250000 };
                for (var i = 0; i < prices.Length; i++)
                {
                    Build(simulation, 1, BuildingKind.DefensePost, Cell(simulation, 20 + i * 20, 2));
                    expectedGold -= prices[i];
                    Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(expectedGold));
                }

                Assert.That(simulation.Buildings.Count, Is.EqualTo(6));
            }
        }

        [Test]
        public void StrategicBuildingPricesUseExactInitialBalance()
        {
            using (var simulation = CreateFixture())
            {
                var gold = simulation.GetCountry(1).Gold;
                Build(simulation, 1, BuildingKind.MissileSilo, Cell(simulation, 20, 2));
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(gold - 1000000));
                Build(simulation, 1, BuildingKind.Sam, Cell(simulation, 40, 2));
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(gold - 1000000 - 1500000));
                Build(simulation, 1, BuildingKind.Sam, Cell(simulation, 60, 2));
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(gold - 1000000 - 1500000 - 3000000));
            }
        }

        [TestCase(BuildingKind.DefensePost)]
        [TestCase(BuildingKind.MissileSilo)]
        [TestCase(BuildingKind.Sam)]
        public void NonEconomicBuildingsCannotUpgrade(BuildingKind kind)
        {
            using (var simulation = CreateFixture())
            {
                Build(simulation, 1, kind, Cell(simulation, 20, 2));
                var building = simulation.Buildings[0];
                var gold = simulation.GetCountry(1).Gold;
                Upgrade(simulation, 1, building.Id);
                Build(simulation, 1, kind, building.Cell);
                Assert.That(building.Level, Is.EqualTo(1));
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(gold));
                Assert.That(simulation.Buildings.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void DefenseCoverageDoesNotStackAcrossPosts()
        {
            using (var simulation = CreateFixture())
            {
                Build(simulation, 1, BuildingKind.DefensePost, Cell(simulation, 20, 2));
                Build(simulation, 1, BuildingKind.DefensePost, Cell(simulation, 40, 2));
                Assert.That(DefenseMultiplier(simulation, "GetDefenseLossMultiplier", 1, Cell(simulation, 30, 2)), Is.EqualTo(6));
                Assert.That(DefenseMultiplier(simulation, "GetDefenseSpeedMultiplier", 1, Cell(simulation, 30, 2)), Is.EqualTo(0.34));
                Assert.That(DefenseMultiplier(simulation, "GetDefenseLossMultiplier", 2, Cell(simulation, 30, 2)), Is.EqualTo(1));
                Assert.That(DefenseMultiplier(simulation, "GetDefenseSpeedMultiplier", 2, Cell(simulation, 30, 2)), Is.EqualTo(1));
            }
        }

        [TestCase(2048, 40)]
        [TestCase(1024, 20)]
        public void DefenseRadiusUsesBaseDistanceAndIncludesBoundary(int mapWidth, int radiusCells)
        {
            using (var simulation = CreateFixture(mapWidth))
            {
                Build(simulation, 1, BuildingKind.DefensePost, Cell(simulation, 20, 2));
                Assert.That(DefenseMultiplier(simulation, "GetDefenseLossMultiplier", 1, Cell(simulation, 20 + radiusCells, 2)), Is.EqualTo(6));
                Assert.That(DefenseMultiplier(simulation, "GetDefenseLossMultiplier", 1, Cell(simulation, 21 + radiusCells, 2)), Is.EqualTo(1));
            }
        }

        [Test]
        public void CapturedPostAffectsNewOwnersPriceAndDefenseOnly()
        {
            using (var simulation = CreateFixture())
            {
                var cell = Cell(simulation, 20, 2);
                Build(simulation, 1, BuildingKind.DefensePost, cell);
                ClaimCell(simulation, cell, 2);
                var gold = simulation.GetCountry(2).Gold;
                Build(simulation, 2, BuildingKind.DefensePost, Cell(simulation, 520, 2));
                Assert.That(simulation.GetCountry(2).Gold, Is.EqualTo(gold - 100000));
                Assert.That(DefenseMultiplier(simulation, "GetDefenseLossMultiplier", 1, cell), Is.EqualTo(1));
                Assert.That(DefenseMultiplier(simulation, "GetDefenseLossMultiplier", 2, cell), Is.EqualTo(6));
            }
        }

        [TestCase(2048)]
        [TestCase(1024)]
        public void CityLevelsTransferAndUpdatePopulationCapacityImmediately(int mapWidth)
        {
            using (var simulation = CreateFixture(mapWidth))
            {
                var cell = Cell(simulation, 20, 2);
                var initialLimit = simulation.GetCountry(1).MaxPopulation;
                Build(simulation, 1, BuildingKind.City, cell);
                Upgrade(simulation, 1, simulation.Buildings[0].Id);
                Assert.That(simulation.GetCountry(1).MaxPopulation, Is.EqualTo(initialLimit + 50000).Within(1e-6));
                var building = simulation.Buildings[0];
                SetBuildingProperty(building, nameof(BuildingState.CooldownUntilTick), simulation.Tick + 50);
                var cooldown = building.CooldownUntilTick;
                ClaimCell(simulation, cell, 2);
                Assert.That(building.CountryId, Is.EqualTo(2));
                Assert.That(building.Level, Is.EqualTo(2));
                Assert.That(building.CooldownUntilTick, Is.EqualTo(cooldown));
                Assert.That(simulation.GetCountry(1).MaxPopulation, Is.EqualTo(ExpectedCapacity(simulation, 1, 0)).Within(1e-6));
                Assert.That(simulation.GetCountry(2).MaxPopulation, Is.EqualTo(ExpectedCapacity(simulation, 2, 2)).Within(1e-6));
            }
        }

        [Test]
        public void NeutralizationDestroysBuildingAndRepeatedDestructionIsIdempotent()
        {
            using (var simulation = CreateFixture())
            {
                var cell = Cell(simulation, 20, 2);
                Build(simulation, 1, BuildingKind.City, cell);
                var building = simulation.Buildings[0];
                var population = simulation.GetCountry(1).TotalPopulation;
                ClaimCell(simulation, cell, 0);
                Assert.That(simulation.Buildings.Count, Is.Zero);
                Assert.That(building.CountryId, Is.Zero);
                Assert.That(simulation.GetCountry(1).MaxPopulation, Is.EqualTo(ExpectedCapacity(simulation, 1, 0)).Within(1e-6));
                Assert.That(simulation.GetCountry(1).TotalPopulation, Is.EqualTo(population));
                var events = simulation.Events.Count;
                var destroyedAgain = (bool)typeof(MatchSimulation)
                    .GetMethod("DestroyBuilding", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(simulation, new object[] { building.Id });
                Assert.That(destroyedAgain, Is.False);
                Assert.That(simulation.Events.Count, Is.EqualTo(events));
            }
        }

        [Test]
        public void DisposingMatchClearsExposedBuildingsAndAllowsFreshIds()
        {
            var simulation = CreateFixture();
            Build(simulation, 1, BuildingKind.City, Cell(simulation, 20, 2));
            simulation.Dispose();
            simulation.Dispose();
            Assert.That(simulation.Buildings.Count, Is.Zero);
            using (var next = CreateFixture())
            {
                Build(next, 1, BuildingKind.City, Cell(next, 20, 2));
                Assert.That(next.Buildings[0].Id, Is.EqualTo(1));
            }
        }

        private static MatchSimulation CreateFixture(int width = 2048)
        {
            var terrain = new byte[width * 8];
            for (var cell = 0; cell < terrain.Length; cell++)
            {
                terrain[cell] = 1;
            }

            terrain[width * 3 + 40] = 0;
            var simulation = new MatchSimulation(new WorldMapData(width, 8, terrain),
                new MatchSettings { CountryCount = 10, Seed = 7451 },
                new SimulationBalance
                {
                    StartingRadius = 0,
                    StartingGold = 20000000,
                    WorkerIncomePerSecond = 0,
                    BaseAdvancePerSecond = 0,
                    TroopAdvanceCoefficient = 0
                });
            SetCountryProperty(simulation.GetCountry(1), nameof(CountryState.CapitalCell), width * 2 + 20);
            SetCountryProperty(simulation.GetCountry(2), nameof(CountryState.CapitalCell), width * 2 + 520);
            for (var countryId = 3; countryId <= 10; countryId++)
            {
                SetCountryProperty(simulation.GetCountry(countryId), nameof(CountryState.CapitalCell),
                    width * 6 + (countryId * 71 + 400) % width);
            }

            simulation.Advance(15);
            for (var y = 0; y < 6; y++)
            {
                for (var x = 0; x < 160; x++)
                {
                    if (simulation.Map.IsLand(width * y + x))
                    {
                        ClaimCell(simulation, width * y + x, 1);
                    }
                }

                for (var x = 500; x < 650; x++)
                {
                    ClaimCell(simulation, width * y + x, 2);
                }
            }

            // Let the simulation publish the authored fixture's limits without changing territory.
            simulation.Advance(0.1);
            return simulation;
        }

        private static int Cell(MatchSimulation simulation, int x, int y)
        {
            return simulation.Map.Width * y + x;
        }

        private static void Build(MatchSimulation simulation, int countryId, BuildingKind kind, int cell)
        {
            simulation.Enqueue(new MatchCommand(CommandKind.Build, countryId, cell, objectId: (int)kind));
            simulation.Advance(0.1);
        }

        private static void Upgrade(MatchSimulation simulation, int countryId, int buildingId)
        {
            simulation.Enqueue(new MatchCommand(CommandKind.Upgrade, countryId, objectId: buildingId));
            simulation.Advance(0.1);
        }

        private static void ClaimCell(MatchSimulation simulation, int cell, int countryId)
        {
            typeof(MatchSimulation).GetMethod("SetOwner", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(simulation, new object[] { cell, countryId });
        }

        private static void SetCountryProperty<T>(CountryState country, string name, T value)
        {
            typeof(CountryState).GetProperty(name).SetValue(country, value);
        }

        private static void SetBuildingProperty<T>(BuildingState building, string name, T value)
        {
            typeof(BuildingState).GetProperty(name).SetValue(building, value);
        }

        private static double DefenseMultiplier(MatchSimulation simulation, string method, int countryId, int cell)
        {
            return (double)typeof(MatchSimulation).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(simulation, new object[] { countryId, cell });
        }

        private static double ExpectedCapacity(MatchSimulation simulation, int countryId, int cityLevels)
        {
            var area = simulation.GetCountry(countryId).LandTiles * simulation.Map.CellScale * simulation.Map.CellScale;
            return 2 * (Math.Pow(area, 0.6) * 1000 + 50000) + cityLevels * 25000;
        }
    }
}
