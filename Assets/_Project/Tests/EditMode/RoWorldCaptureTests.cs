using System;
using System.Reflection;
using NUnit.Framework;

namespace MapClash.Tests
{
    public sealed class RoWorldCaptureTests
    {
        [TestCase(0, 0, (byte)1, 60)]
        [TestCase(1, 0, (byte)1, 40)]
        [TestCase(2, 0, (byte)1, 20)]
        [TestCase(3, 0, (byte)1, 0)]
        [TestCase(4, 0, (byte)1, -20)]
        [TestCase(0, 0, (byte)2, 70)]
        [TestCase(0, 6, (byte)3, 128)]
        public void ReferenceQueuePriorityVectors(int neighbours, int random, byte terrain, long expected)
        {
            Assert.That(RoWorldCombatRules.QueuePriority(7, random, neighbours, terrain), Is.EqualTo(28 + expected));
        }

        [TestCase(1000, 4, 4.125)]
        [TestCase(1, 2, 25)]
        [TestCase(100000, 10, 0.25)]
        public void ReferenceNeutralCostVectors(double troops, double border, double expectedTime)
        {
            RoWorldCombatRules.TileCosts(1, troops, 0, 1, 1, border, true, out var loss, out var defence, out var time);
            Assert.That(loss, Is.EqualTo(16));
            Assert.That(defence, Is.Zero);
            Assert.That(time, Is.EqualTo(expectedTime).Within(1e-9));
        }

        [Test]
        public void ReferenceEnemyMidpointCostVector()
        {
            RoWorldCombatRules.TileCosts(1, 300000, 300000, 300000, 300000, 10, false,
                out var loss, out var defence, out var time);
            Assert.That(loss, Is.EqualTo(20.7766).Within(1e-7));
            Assert.That(defence, Is.EqualTo(1));
            Assert.That(time, Is.EqualTo(0.10416228070175439).Within(1e-9));
        }

        [Test]
        public void ClickSelectsOwnerAndDoesNotAttractFrontToPoint()
        {
            using (var first = CreateMap(true))
            using (var second = CreateMap(true))
            {
                first.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(150, 10)));
                second.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(150, 15)));
                first.Advance(1);
                second.Advance(1);
                for (var cell = 0; cell < first.Map.Terrain.Length; cell++)
                {
                    Assert.That(first.GetOwner(cell), Is.EqualTo(second.GetOwner(cell)), "Click coordinates must not steer a country attack.");
                }
            }
        }

        [Test]
        public void AttackStartsNextTickAndSeedsWholeNeutralBorderIncludingConcavity()
        {
            using (var simulation = CreateMap())
            {
                for (var y = 6; y <= 16; y++) { SetOwner(simulation, Cell(100, y), 1); SetOwner(simulation, Cell(110, y), 1); }
                for (var x = 100; x <= 110; x++) { SetOwner(simulation, Cell(x, 6), 1); }
                var before = simulation.GetCountry(1).LandTiles;
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(145, 15)));
                simulation.Advance(0.1);
                Assert.That(simulation.GetCountry(1).LandTiles, Is.EqualTo(before));
                simulation.Advance(0.5);
                var inside = 0;
                var back = 0;
                for (var y = 7; y <= 15; y++)
                {
                    for (var x = 101; x <= 109; x++) { if (simulation.GetOwner(Cell(x, y)) == 1) { inside++; } }
                    if (simulation.GetOwner(Cell(99, y)) == 1) { back++; }
                }
                Assert.That(inside, Is.GreaterThan(0), "Open U-shaped recesses must expand without a surrounding ring.");
                Assert.That(back, Is.GreaterThan(0), "The opposite border is part of the same expansion.");
                Assert.That(simulation.GetOwner(Cell(145, 15)), Is.Zero);
            }
        }

        [Test]
        public void EnemyAttackSeedsBothAlreadyTouchingIslandsButDoesNotCrossWater()
        {
            using (var simulation = CreateIslands(true))
            {
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(130, 5)));
                simulation.Advance(0.4);
                Assert.That(simulation.GetOwner(Cell(81, 5)), Is.EqualTo(1));
                Assert.That(simulation.GetOwner(Cell(81, 9)), Is.EqualTo(1));
                Assert.That(simulation.GetOwner(Cell(90, 7)), Is.Zero);
                Assert.That(simulation.Map.IsLand(Cell(90, 7)), Is.False);
            }
        }

        [Test]
        public void EnemyIslandWithoutOurBorderRemainsUnreached()
        {
            using (var simulation = CreateIslands(false))
            {
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(130, 5)));
                simulation.Advance(2);
                Assert.That(simulation.GetOwner(Cell(81, 5)), Is.EqualTo(1));
                Assert.That(simulation.GetOwner(Cell(80, 9)), Is.EqualTo(2));
                Assert.That(simulation.GetOwner(Cell(81, 9)), Is.EqualTo(2));
            }
        }

        [Test]
        public void SameTickOrdersMergeWithoutAdvancingOrDuplicatingTroops()
        {
            using (var simulation = CreateMap())
            {
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(105, 10)));
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(95, 10)));
                simulation.Advance(0.1);
                var operation = Find(simulation, 1);
                Assert.That(simulation.Operations.Count, Is.EqualTo(1));
                Assert.That(operation.Troops, Is.EqualTo(4375 + 3281));
                Assert.That(simulation.GetCountry(1).DeployedTroops, Is.EqualTo(operation.Troops));
                var initial = simulation.GetCountry(1).LandTiles;
                var send = Math.Floor(simulation.GetCountry(1).DefendingTroops * 0.25);
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(140, 10)));
                simulation.Advance(0.1);
                Assert.That(Find(simulation, 1), Is.SameAs(operation));
                Assert.That(simulation.GetCountry(1).LandTiles, Is.GreaterThan(initial));
                Assert.That(operation.Troops, Is.LessThan(4375 + 3281 + send));
                Assert.That(simulation.GetCountry(1).DeployedTroops, Is.EqualTo(operation.Troops).Within(1e-6));
            }
        }

        [Test]
        public void CounterattackCancelsAtOrderTimeBeforeLandChanges()
        {
            using (var simulation = CreateIslands(true))
            {
                var before = simulation.GetCountry(1).LandTiles;
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(81, 5)));
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 2, Cell(80, 5)));
                simulation.Advance(0.1);
                Assert.That(simulation.GetCountry(1).DeployedTroops, Is.Zero.Within(1e-6));
                Assert.That(simulation.GetCountry(2).DeployedTroops, Is.Zero.Within(1e-6));
                Assert.That(simulation.GetCountry(1).LandTiles, Is.EqualTo(before));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RecallContinuesTwoTicksThenFreezesAndArrivesOnReferenceTick(bool enemy)
        {
            using (var simulation = CreateMap(enemy))
            {
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(enemy ? 150 : 140, 10)));
                simulation.Advance(0.1);
                var operation = Find(simulation, 1);
                var recallTick = simulation.Tick + 1;
                simulation.Enqueue(new MatchCommand(CommandKind.Recall, 1, objectId: operation.Id));
                simulation.Advance(0.3);
                Assert.That(operation.Phase, Is.EqualTo(OperationPhase.Returning));
                Assert.That(operation.ReturnAtTick, Is.EqualTo(recallTick + 21));
                var survivors = operation.Troops;
                var land = simulation.GetCountry(1).LandTiles;
                simulation.Enqueue(new MatchCommand(CommandKind.Recall, 1, objectId: operation.Id));
                simulation.Advance(1.8);
                Assert.That(simulation.GetCountry(1).LandTiles, Is.EqualTo(land));
                var home = simulation.GetCountry(1).HomePopulation;
                simulation.Advance(0.1);
                Assert.That(Find(simulation, 1), Is.Null);
                Assert.That(simulation.GetCountry(1).HomePopulation - home,
                    Is.EqualTo(simulation.GetCountry(1).GrowthPerSecond * 0.1 + Math.Floor(survivors * (enemy ? 0.75 : 1))).Within(1e-6));
                Assert.That(simulation.GetCountry(1).ReturningTroops, Is.Zero.Within(1e-6));
            }
        }

        [Test]
        public void ReinforcingFrozenAttackResumesSameOperation()
        {
            using (var simulation = CreateMap())
            {
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(140, 10)));
                simulation.Advance(0.1);
                var operation = Find(simulation, 1);
                simulation.Enqueue(new MatchCommand(CommandKind.Recall, 1, objectId: operation.Id));
                simulation.Advance(0.3);
                Assert.That(operation.Phase, Is.EqualTo(OperationPhase.Returning));
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(145, 10)));
                simulation.Advance(0.1);
                Assert.That(Find(simulation, 1), Is.SameAs(operation));
                Assert.That(operation.Phase, Is.EqualTo(OperationPhase.Advancing));
                Assert.That(simulation.GetCountry(1).ReturningTroops, Is.Zero.Within(1e-6));
                Assert.That(simulation.GetCountry(1).DeployedTroops, Is.EqualTo(operation.Troops).Within(1e-6));
            }
        }

        [Test]
        public void LastTileStillFallsWhenItsCostExceedsRemainingTroops()
        {
            using (var simulation = CreateMap())
            {
                Set(simulation.GetCountry(1), nameof(CountryState.DefendingTroops), 1.0);
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(140, 10), 1));
                simulation.Advance(0.3);
                Assert.That(simulation.GetCountry(1).LandTiles, Is.EqualTo(2));
                Assert.That(simulation.GetCountry(1).DeployedTroops, Is.Zero.Within(1e-6));
                Assert.That(Find(simulation, 1), Is.Null);
            }
        }

        [Test]
        public void ReferenceQueueUsesMinimumKeyAndLastInFirstOutTies()
        {
            using (var simulation = CreateMap())
            {
                var type = typeof(MatchSimulation).GetNestedType("Wave", BindingFlags.NonPublic);
                var wave = Activator.CreateInstance(type, new object[] { new OperationState() });
                var queue = (System.Collections.Generic.SortedDictionary<long, System.Collections.Generic.Stack<int>>)
                    type.GetProperty("Queue").GetValue(wave);
                var tied = new System.Collections.Generic.Stack<int>();
                tied.Push(1); tied.Push(2);
                var earlier = new System.Collections.Generic.Stack<int>(); earlier.Push(3);
                queue.Add(10, tied); queue.Add(-2, earlier);
                var pop = typeof(MatchSimulation).GetMethod("Pop", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(pop.Invoke(simulation, new[] { wave }), Is.EqualTo(3));
                Assert.That(pop.Invoke(simulation, new[] { wave }), Is.EqualTo(2));
                Assert.That(pop.Invoke(simulation, new[] { wave }), Is.EqualTo(1));
                Assert.That(pop.Invoke(simulation, new[] { wave }), Is.EqualTo(-1));
            }
        }

        [Test]
        public void EmptyFrontRefundsImmediatelyWithoutNaturalReturnDelay()
        {
            using (var simulation = CreateTinyMap(2048))
            {
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, 4 * 2048 + 21));
                simulation.Advance(0.3);
                Assert.That(simulation.GetOwner(4 * 2048 + 21), Is.EqualTo(1));
                Assert.That(Find(simulation, 1), Is.Null);
                Assert.That(simulation.GetCountry(1).ReturningTroops, Is.Zero);
                Assert.That(simulation.GetCountry(1).DefendingTroops, Is.EqualTo(17500 - 16));
            }
        }

        [Test]
        public void CoarseTileCanConsumeTroopsWithoutCompletingWholeTile()
        {
            using (var simulation = CreateTinyMap(1024))
            {
                Set(simulation.GetCountry(1), nameof(CountryState.DefendingTroops), 1.0);
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, 4 * 1024 + 21, 1));
                simulation.Advance(0.3);
                Assert.That(simulation.GetOwner(4 * 1024 + 21), Is.Zero);
                Assert.That(simulation.GetCountry(1).LandTiles, Is.EqualTo(1));
                Assert.That(Find(simulation, 1), Is.Null);
            }
        }

        [Test]
        public void FinalTileOverdrawIsRetainedWhenReinforcementMergesSameTick()
        {
            using (var simulation = CreateTinyMap(2048))
            {
                Set(simulation.GetCountry(1), nameof(CountryState.DefendingTroops), 1.0);
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, 4 * 2048 + 21, 1));
                simulation.Advance(0.1);
                Set(simulation.GetCountry(1), nameof(CountryState.DefendingTroops), 20.0);
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, 4 * 2048 + 21, 1));
                simulation.Advance(0.1);
                Assert.That(Find(simulation, 1).Troops, Is.EqualTo(1 - 16 + 20));
                Assert.That(simulation.GetCountry(1).DeployedTroops, Is.EqualTo(5));
            }
        }

        private static MatchSimulation CreateTinyMap(int width)
        {
            var terrain = new byte[width * 16];
            terrain[4 * width + 20] = 1; terrain[4 * width + 21] = 1;
            for (var id = 2; id <= 10; id++) { terrain[10 * width + id * 50] = 1; }
            var simulation = new MatchSimulation(new WorldMapData(width, 16, terrain),
                new MatchSettings { CountryCount = 10, VictoryFraction = 1 },
                new SimulationBalance { StartingRadius = 0, RoleConversionPerSecond = 0, BasePopulation = 1 });
            Set(simulation.GetCountry(1), nameof(CountryState.CapitalCell), 4 * width + 20);
            for (var id = 2; id <= 10; id++) { Set(simulation.GetCountry(id), nameof(CountryState.CapitalCell), 10 * width + id * 50); }
            simulation.Advance(15);
            return simulation;
        }

        private static MatchSimulation CreateMap(bool enemy = false)
        {
            var terrain = new byte[2048 * 24];
            for (var y = 4; y <= 18; y++) for (var x = 80; x <= 160; x++) { terrain[Cell(x, y)] = 1; }
            for (var id = 2; id <= 10; id++) { terrain[Cell(id * 100, 22)] = 1; }
            var simulation = Start(terrain, Cell(100, 10));
            if (enemy)
            {
                for (var y = 4; y <= 18; y++) for (var x = 115; x <= 160; x++) { SetOwner(simulation, Cell(x, y), 2); }
                for (var y = 8; y <= 12; y++) for (var x = 110; x <= 114; x++) { SetOwner(simulation, Cell(x, y), 1); }
            }
            return simulation;
        }

        private static MatchSimulation CreateIslands(bool bothTouching)
        {
            var terrain = new byte[2048 * 24];
            foreach (var y in new[] { 5, 9 }) for (var x = 80; x <= 160; x++) { terrain[Cell(x, y)] = 1; }
            for (var id = 2; id <= 10; id++) { terrain[Cell(id * 100, 22)] = 1; }
            var simulation = Start(terrain, Cell(80, 5));
            foreach (var y in new[] { 5, 9 }) for (var x = 80; x <= 160; x++) { SetOwner(simulation, Cell(x, y), x == 80 && (y == 5 || bothTouching) ? 1 : 2); }
            return simulation;
        }

        private static MatchSimulation Start(byte[] terrain, int capital)
        {
            var simulation = new MatchSimulation(new WorldMapData(2048, 24, terrain),
                new MatchSettings { CountryCount = 10, Seed = 12345, VictoryFraction = 1 }, new SimulationBalance { StartingRadius = 0 });
            Set(simulation.GetCountry(1), nameof(CountryState.CapitalCell), capital);
            for (var id = 2; id <= 10; id++) { Set(simulation.GetCountry(id), nameof(CountryState.CapitalCell), Cell(id * 100, 22)); }
            simulation.Advance(15);
            return simulation;
        }

        private static int Cell(int x, int y) => y * 2048 + x;
        private static void Set(CountryState country, string property, object value) => typeof(CountryState).GetProperty(property).SetValue(country, value);
        private static void SetOwner(MatchSimulation simulation, int cell, int owner) => typeof(MatchSimulation)
            .GetMethod("SetOwner", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(simulation, new object[] { cell, owner });
        private static OperationState Find(MatchSimulation simulation, int country)
        {
            foreach (var operation in simulation.Operations) { if (operation.CountryId == country) { return operation; } }
            return null;
        }
    }
}
