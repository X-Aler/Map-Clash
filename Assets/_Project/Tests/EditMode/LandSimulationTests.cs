using System;
using System.Reflection;
using NUnit.Framework;

namespace MapClash.Tests
{
    public sealed class LandSimulationTests
    {
        [Test]
        public void StartingTerritoryExistsDuringCountdownAndCannotAttack()
        {
            using (var simulation = CreateOpenMap(10))
            {
                var player = simulation.GetCountry(1);
                Assert.That(simulation.Phase, Is.EqualTo(MatchPhase.Spawn));
                Assert.That(simulation.GetOwner(player.CapitalCell), Is.EqualTo(1));
                Assert.That(player.LandTiles, Is.GreaterThan(0));
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1,
                    simulation.Map.GetNeighbour(player.CapitalCell, 1), 0.5));
                simulation.Advance(0.1);
                Assert.That(player.DeployedTroops, Is.Zero);
                Assert.That(simulation.Operations.Count, Is.Zero);
            }
        }

        [TestCase(10)]
        [TestCase(40)]
        [TestCase(60)]
        public void CountryCountIncludesExactlyOnePlayer(int countryCount)
        {
            using (var simulation = CreateOpenMap(countryCount))
            {
                simulation.Advance(15);
                Assert.That(simulation.Countries.Count, Is.EqualTo(countryCount));
                var playerCount = 0;
                for (var i = 0; i < simulation.Countries.Count; i++)
                {
                    var country = simulation.Countries[i];
                    playerCount += country.IsPlayer ? 1 : 0;
                    Assert.That(country.LandTiles, Is.EqualTo(1));
                    Assert.That(country.TotalPopulation, Is.EqualTo(25000));
                    Assert.That(country.Gold, Is.EqualTo(50000));
                }

                Assert.That(playerCount, Is.EqualTo(1));
                Assert.That(simulation.Phase, Is.EqualTo(MatchPhase.Playing));
                Assert.That(simulation.ElapsedSeconds, Is.Zero);
            }
        }

        [TestCase(9)]
        [TestCase(61)]
        public void InvalidCountryCountIsRejected(int countryCount)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateOpenMap(countryCount));
        }

        [Test]
        public void SameSeedAndCommandsAreIndependentOfFrameRate()
        {
            using (var first = CreateOpenMap(40))
            using (var second = CreateOpenMap(40))
            {
                first.Advance(15);
                for (var frame = 0; frame < 900; frame++)
                {
                    second.Advance(1.0 / 60);
                }

                var target = first.Map.GetNeighbour(first.GetCountry(1).CapitalCell, 1);
                first.Enqueue(new MatchCommand(CommandKind.Attack, 1, target, 0.25));
                second.Enqueue(new MatchCommand(CommandKind.Attack, 1, target, 0.25));
                first.Advance(3);
                for (var frame = 0; frame < 432; frame++)
                {
                    second.Advance(1.0 / 144);
                }

                AssertEquivalent(first, second);
            }
        }

        [Test]
        public void PauseDiscardsElapsedWallTimeAndResumesPreviousPhase()
        {
            using (var simulation = CreateOpenMap(10))
            {
                simulation.Advance(3);
                var tick = simulation.Tick;
                simulation.SetPaused(true);
                simulation.Advance(1000);
                Assert.That(simulation.Tick, Is.EqualTo(tick));
                simulation.SetPaused(false);
                Assert.That(simulation.Phase, Is.EqualTo(MatchPhase.Spawn));
                simulation.Advance(0.1);
                Assert.That(simulation.Tick, Is.EqualTo(tick + 1));
            }
        }

        [Test]
        public void SpawnSelectionRejectsWaterAndOverlappingCountry()
        {
            using (var simulation = CreateFixture(false))
            {
                var original = simulation.GetCountry(1).CapitalCell;
                simulation.Enqueue(new MatchCommand(CommandKind.ChooseSpawn, 1, 1000));
                simulation.Advance(0.1);
                Assert.That(simulation.GetCountry(1).CapitalCell, Is.EqualTo(original));
                simulation.Enqueue(new MatchCommand(CommandKind.ChooseSpawn, 1, simulation.GetCountry(2).CapitalCell));
                simulation.Advance(0.1);
                Assert.That(simulation.GetCountry(1).CapitalCell, Is.EqualTo(original));
                simulation.Enqueue(new MatchCommand(CommandKind.ChooseSpawn, 1, 10));
                simulation.Advance(0.1);
                Assert.That(simulation.GetCountry(1).CapitalCell, Is.EqualTo(10));
            }
        }

        [Test]
        public void StartingRegionDoesNotJumpAcrossNarrowStrait()
        {
            var terrain = new byte[2048 * 16];
            terrain[2048 * 4 + 8] = 1;
            terrain[2048 * 4 + 9] = 1;
            terrain[2048 * 4 + 10] = 1;
            terrain[2048 * 4 + 12] = 1;
            for (var countryId = 2; countryId <= 10; countryId++)
            {
                terrain[2048 * 4 + countryId * 100] = 1;
            }

            using (var simulation = new MatchSimulation(new WorldMapData(2048, 16, terrain),
                new MatchSettings { CountryCount = 10 }, new SimulationBalance()))
            {
                SetProperty(simulation.GetCountry(1), nameof(CountryState.CapitalCell), 2048 * 4 + 10);
                for (var countryId = 2; countryId <= 10; countryId++)
                {
                    SetProperty(simulation.GetCountry(countryId), nameof(CountryState.CapitalCell), 2048 * 4 + countryId * 100);
                }

                simulation.Advance(15);
                Assert.That(simulation.GetCountry(1).LandTiles, Is.EqualTo(3));
                Assert.That(simulation.GetOwner(2048 * 4 + 12), Is.Zero,
                    "A radius disc must not give the country disconnected land across a strait.");
            }
        }

        [Test]
        public void RoleConversionPreservesPeopleApartFromReportedGrowth()
        {
            using (var simulation = CreateFixture())
            {
                var player = simulation.GetCountry(1);
                var originalTotal = player.TotalPopulation;
                var oldWorkers = player.Workers;
                simulation.Enqueue(new MatchCommand(CommandKind.SetTroopRatio, 1, amount: 0));
                simulation.Advance(0.1);
                Assert.That(player.Workers, Is.GreaterThan(oldWorkers));
                Assert.That(player.TotalPopulation, Is.EqualTo(originalTotal + player.GrowthPerSecond * 0.1).Within(1e-6));
                var expectedConversion = originalTotal * 0.05 * 0.1;
                Assert.That(player.DefendingTroops, Is.EqualTo(17500 - expectedConversion).Within(1e-6));
            }
        }

        [Test]
        public void PopulationAboveLoweredCapacityStopsGrowthWithoutDeletingPeople()
        {
            using (var simulation = CreateFixture())
            {
                var player = simulation.GetCountry(1);
                SetProperty(player, nameof(CountryState.Workers), 500000.0);
                SetProperty(player, nameof(CountryState.DefendingTroops), 500000.0);
                simulation.Advance(0.1);
                Assert.That(player.GrowthPerSecond, Is.Zero);
                Assert.That(player.TotalPopulation, Is.EqualTo(1000000).Within(1e-6));
                Assert.That(player.Gold, Is.GreaterThanOrEqualTo(50000));
            }
        }

        [Test]
        public void WorkersAloneGeneratePassiveIncome()
        {
            using (var simulation = CreateFixture())
            {
                var player = simulation.GetCountry(1);
                var oldGold = player.Gold;
                simulation.Advance(0.1);
                Assert.That(player.IncomePerSecond, Is.EqualTo(player.Workers * 0.05).Within(1e-6));
                Assert.That(player.Gold - oldGold, Is.EqualTo(player.IncomePerSecond * 0.1).Within(1e-6));
                Assert.That(player.EarnedGold, Is.EqualTo(player.Gold - oldGold).Within(1e-6));
            }
        }

        [Test]
        public void TimerWithEqualLargestTerritoriesProducesDraw()
        {
            var balance = NoAdvanceBalance();
            using (var simulation = CreateFixture(true, 0, 1, balance))
            {
                foreach (var country in simulation.Countries)
                {
                    SetProperty(country, nameof(CountryState.DefendingTroops), 0.0);
                    SetProperty(country, nameof(CountryState.Workers), 0.0);
                }
                simulation.Advance(1);
                Assert.That(simulation.Phase, Is.EqualTo(MatchPhase.Finished));
                Assert.That(simulation.Result.IsDraw, Is.True);
                Assert.That(simulation.Result.WinnerCountryId, Is.Zero);
            }
        }

        [Test]
        public void DisposingTwiceClearsOperationsAndDoesNotLeakIntoNextMatch()
        {
            var simulation = CreateFixture();
            simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, 2));
            simulation.Advance(0.1);
            simulation.Dispose();
            simulation.Dispose();
            Assert.That(simulation.Operations.Count, Is.Zero);
            Assert.That(simulation.GetCountry(1).TotalPopulation, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => simulation.Advance(0.1));
            using (var next = CreateFixture())
            {
                Assert.That(next.GetCountry(1).TotalPopulation, Is.EqualTo(25000));
                Assert.That(next.Operations.Count, Is.Zero);
            }
        }

        private static MatchSimulation CreateOpenMap(int countryCount)
        {
            var terrain = new byte[2048 * 16];
            for (var i = 0; i < terrain.Length; i++)
            {
                terrain[i] = 1;
            }

            return new MatchSimulation(new WorldMapData(2048, 16, terrain),
                new MatchSettings { Seed = 7451, CountryCount = countryCount },
                new SimulationBalance { StartingRadius = 0 });
        }

        private static MatchSimulation CreateFixture(bool startMatch = true, double advancePerSecond = 0,
            int timeLimit = 0, SimulationBalance balance = null)
        {
            var terrain = new byte[2048 * 8];
            for (var x = 0; x < 256; x++)
            {
                terrain[x] = 1;
                terrain[2048 + x] = 1;
            }

            for (var i = 2; i < 10; i++)
            {
                terrain[2048 * 5 + 300 + i * 10] = 1;
            }

            terrain[2048 * 5 + 1000] = 1;

            balance = balance ?? NoAdvanceBalance();
            balance.BaseAdvancePerSecond = advancePerSecond;
            var simulation = new MatchSimulation(new WorldMapData(2048, 8, terrain),
                new MatchSettings { Seed = 1337, CountryCount = 10, TimeLimitSeconds = timeLimit }, balance);
            SetProperty(simulation.GetCountry(1), nameof(CountryState.CapitalCell), 0);
            SetProperty(simulation.GetCountry(2), nameof(CountryState.CapitalCell), 1);
            for (var i = 2; i < 10; i++)
            {
                SetProperty(simulation.GetCountry(i + 1), nameof(CountryState.CapitalCell), 2048 * 5 + 300 + i * 10);
            }

            if (startMatch)
            {
                simulation.Advance(15);
            }

            return simulation;
        }

        private static SimulationBalance NoAdvanceBalance()
        {
            return new SimulationBalance
            {
                StartingRadius = 0,
                BaseAdvancePerSecond = 0,
                TroopAdvanceCoefficient = 0,
                MaxAdvancePerSecond = 2000
            };
        }

        private static void SetProperty<T>(CountryState country, string property, T value)
        {
            typeof(CountryState).GetProperty(property, BindingFlags.Instance | BindingFlags.Public).SetValue(country, value);
        }

        private static void PrepareDefenderDensity(MatchSimulation simulation, byte terrain)
        {
            typeof(MatchSimulation).GetMethod("SetOwner", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(simulation, new object[] { 2, 2 });
            SetProperty(simulation.GetCountry(2), nameof(CountryState.DefendingTroops), 200.0);
            simulation.Map.Terrain[1] = terrain;
        }

        private static OperationState FindOperation(MatchSimulation simulation, int countryId)
        {
            for (var i = 0; i < simulation.Operations.Count; i++)
            {
                if (simulation.Operations[i].CountryId == countryId)
                {
                    return simulation.Operations[i];
                }
            }

            return null;
        }

        private static void AssertEquivalent(MatchSimulation first, MatchSimulation second)
        {
            Assert.That(second.Tick, Is.EqualTo(first.Tick));
            Assert.That(second.Phase, Is.EqualTo(first.Phase));
            for (var i = 0; i < first.Countries.Count; i++)
            {
                var firstCountry = first.Countries[i];
                var secondCountry = second.Countries[i];
                Assert.That(secondCountry.LandTiles, Is.EqualTo(firstCountry.LandTiles));
                Assert.That(secondCountry.TotalPopulation, Is.EqualTo(firstCountry.TotalPopulation));
                Assert.That(secondCountry.Gold, Is.EqualTo(firstCountry.Gold));
                Assert.That(secondCountry.CapitalCell, Is.EqualTo(firstCountry.CapitalCell));
            }

            for (var cell = 0; cell < first.Map.Terrain.Length; cell++)
            {
                Assert.That(second.GetOwner(cell), Is.EqualTo(first.GetOwner(cell)));
            }
        }
    }
}
