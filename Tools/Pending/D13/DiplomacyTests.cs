using System;
using System.Reflection;
using NUnit.Framework;

namespace MapClash.Tests
{
    public sealed class DiplomacyTests
    {
        [Test]
        public void OfferRequiresNamedRecipientsConsentAndDoesNotProtectBorder()
        {
            using (var simulation = CreateFixture())
            {
                Command(simulation, CommandKind.OfferAlliance, 1, 2);
                var relation = simulation.GetRelation(1, 2);
                Assert.That(simulation.GetRelation(2, 1), Is.SameAs(relation));
                Assert.That(relation.Kind, Is.EqualTo(RelationKind.Peace));
                Assert.That(relation.Request.SenderCountryId, Is.EqualTo(1));
                Assert.That(relation.Request.RecipientCountryId, Is.EqualTo(2));
                Assert.That(relation.Request.ExpiresAtTick, Is.EqualTo(simulation.Tick + 300));
                Assert.That(simulation.CanAttack(1, 2), Is.True);

                var request = relation.Request;
                Command(simulation, CommandKind.AcceptAlliance, 1, 2);
                Assert.That(relation.Request, Is.SameAs(request));
                Command(simulation, CommandKind.OfferAlliance, 2, 1);
                Assert.That(relation.Request, Is.SameAs(request));
                Command(simulation, CommandKind.AcceptAlliance, 2, 1);
                Assert.That(relation.Kind, Is.EqualTo(RelationKind.Allied));
                Assert.That(relation.Request, Is.Null);
                Assert.That(relation.AllianceUntilTick, Is.EqualTo(simulation.Tick + 2400));
                Assert.That(simulation.CanAttack(1, 2), Is.False);
                Assert.That(simulation.CanAttack(2, 1), Is.False);
            }
        }

        [Test]
        public void RequestExpiresExactlyAtThirtySeconds()
        {
            using (var simulation = CreateFixture())
            {
                Command(simulation, CommandKind.OfferAlliance, 1, 2);
                var deadline = simulation.GetRelation(1, 2).Request.ExpiresAtTick;
                AdvanceTo(simulation, deadline - 1);
                Assert.That(simulation.GetRelation(1, 2).Request, Is.Not.Null);
                Command(simulation, CommandKind.AcceptAlliance, 2, 1);
                Assert.That(simulation.Tick, Is.EqualTo(deadline));
                Assert.That(simulation.GetRelation(1, 2).Request, Is.Null);
                Assert.That(simulation.GetRelation(1, 2).Kind, Is.EqualTo(RelationKind.Peace));
            }
        }

        [Test]
        public void AllianceWarnsOncePerCountryAndRenewalNeedsConsent()
        {
            using (var simulation = CreateFixture())
            {
                MakeAlliance(simulation, 1, 2);
                var relation = simulation.GetRelation(1, 2);
                var originalDeadline = relation.AllianceUntilTick;
                AdvanceTo(simulation, originalDeadline - 300);
                Assert.That(CountEvents(simulation, "diplomacy.allianceExpiring"), Is.EqualTo(2));
                simulation.Advance(0.1);
                Assert.That(CountEvents(simulation, "diplomacy.allianceExpiring"), Is.Zero);

                Command(simulation, CommandKind.RenewAlliance, 1, 2);
                Assert.That(relation.Request.IsRenewal, Is.True);
                Assert.That(relation.AllianceUntilTick, Is.EqualTo(originalDeadline));
                Command(simulation, CommandKind.AcceptAlliance, 2, 1);
                Assert.That(relation.AllianceUntilTick, Is.EqualTo(simulation.Tick + 2400));
                Assert.That(relation.HasExpiryWarning, Is.False);
                AdvanceTo(simulation, originalDeadline);
                Assert.That(relation.Kind, Is.EqualTo(RelationKind.Allied));
            }
        }

        [Test]
        public void NaturalExpiryDoesNotCreateATraitor()
        {
            using (var simulation = CreateFixture())
            {
                MakeAlliance(simulation, 1, 2);
                var relation = simulation.GetRelation(1, 2);
                AdvanceTo(simulation, relation.AllianceUntilTick);
                Assert.That(relation.Kind, Is.EqualTo(RelationKind.Peace));
                Assert.That(simulation.GetTraitorTicksRemaining(1), Is.Zero);
                Assert.That(simulation.GetTraitorTicksRemaining(2), Is.Zero);
                Assert.That(simulation.CanAttack(1, 2), Is.True);
            }
        }

        [Test]
        public void ManualBreakAppliesImmediateThirtySecondPenalty()
        {
            using (var simulation = CreateFixture())
            {
                MakeAlliance(simulation, 1, 2);
                Command(simulation, CommandKind.BreakAlliance, 1, 2);
                var breakTick = simulation.Tick;
                Assert.That(simulation.GetRelation(1, 2).Kind, Is.EqualTo(RelationKind.Peace));
                Assert.That(simulation.GetTraitorTicksRemaining(1), Is.EqualTo(300));
                Assert.That(simulation.GetTraitorDefenseMultiplier(1), Is.EqualTo(0.5));
                Assert.That(simulation.GetTraitorAdvanceMultiplier(1), Is.EqualTo(0.8));
                Assert.That(simulation.GetTraitorDefenseMultiplier(2), Is.EqualTo(1));
                AdvanceTo(simulation, breakTick + 299);
                Assert.That(simulation.GetTraitorTicksRemaining(1), Is.EqualTo(1));
                simulation.Advance(0.1);
                Assert.That(simulation.GetTraitorDefenseMultiplier(1), Is.EqualTo(1));
                Assert.That(simulation.GetTraitorAdvanceMultiplier(1), Is.EqualTo(1));
            }
        }

        [Test]
        public void AlliedAttackIsRejectedBeforeAnyTroopsAreSpent()
        {
            using (var simulation = CreateFixture())
            {
                MakeAlliance(simulation, 1, 2);
                var troops = simulation.GetCountry(1).DefendingTroops;
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(2), 0.75));
                simulation.Advance(0.1);
                Assert.That(simulation.GetCountry(1).DefendingTroops, Is.EqualTo(troops));
                Assert.That(simulation.GetCountry(1).DeployedTroops, Is.Zero);
                Assert.That(simulation.Operations.Count, Is.Zero);
                Assert.That(simulation.GetRelation(1, 2).Kind, Is.EqualTo(RelationKind.Allied));
            }
        }

        [Test]
        public void HostileAttackCancelsOfferAndPeaceWaitsForFullCalmInterval()
        {
            using (var simulation = CreateFixture())
            {
                Command(simulation, CommandKind.OfferAlliance, 2, 1);
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(2), 0.25));
                simulation.Advance(0.1);
                var relation = simulation.GetRelation(1, 2);
                Assert.That(relation.Kind, Is.EqualTo(RelationKind.War));
                Assert.That(relation.Request, Is.Null);
                Assert.That(simulation.CanTrade(1, 2), Is.False);
                var operationId = simulation.Operations[0].Id;
                simulation.Advance(61);
                Assert.That(relation.Kind, Is.EqualTo(RelationKind.War), "An unfinished operation prevents peace regardless of launch age.");

                simulation.Enqueue(new MatchCommand(CommandKind.Recall, 1, objectId: operationId));
                simulation.Advance(0.1);
                var returnTick = simulation.Operations[0].ReturnAtTick;
                AdvanceTo(simulation, returnTick);
                Assert.That(simulation.Operations.Count, Is.Zero);
                AdvanceTo(simulation, returnTick + 599);
                Assert.That(relation.Kind, Is.EqualTo(RelationKind.War));
                simulation.Advance(0.1);
                Assert.That(relation.Kind, Is.EqualTo(RelationKind.Peace));
                Assert.That(simulation.CanTrade(2, 1), Is.True);
            }
        }

        [Test]
        public void AcceptanceReturnsExistingAttackWithoutRecallPenalty()
        {
            using (var simulation = CreateFixture())
            {
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(2), 0.25));
                simulation.Advance(0.1);
                var survivors = simulation.Operations[0].Troops;
                Command(simulation, CommandKind.OfferAlliance, 2, 1);
                Command(simulation, CommandKind.AcceptAlliance, 1, 2);
                Assert.That(simulation.Operations[0].Phase, Is.EqualTo(OperationPhase.Returning));
                Assert.That(simulation.Operations[0].Troops, Is.EqualTo(survivors));
                Assert.That(simulation.GetCountry(1).DeployedTroops, Is.Zero);
                Assert.That(simulation.GetCountry(1).ReturningTroops, Is.EqualTo(survivors));
                AdvanceTo(simulation, simulation.Operations[0].ReturnAtTick);
                Assert.That(simulation.GetCountry(1).DefendingTroops, Is.EqualTo(1000));
                Assert.That(simulation.GetCountry(1).ReturningTroops, Is.Zero);
                Assert.That(simulation.GetRelation(1, 2).Kind, Is.EqualTo(RelationKind.Allied));
            }
        }

        [Test]
        public void DirectionalEmbargoBlocksBothDirectionsUntilBothSidesLiftIt()
        {
            using (var simulation = CreateFixture())
            {
                Command(simulation, CommandKind.SetEmbargo, 1, 2, 1);
                Command(simulation, CommandKind.SetEmbargo, 2, 1, 1);
                Command(simulation, CommandKind.SetEmbargo, 1, 2, 0);
                var relation = simulation.GetRelation(1, 2);
                Assert.That(relation.IsFirstCountryEmbargoing, Is.False);
                Assert.That(relation.IsSecondCountryEmbargoing, Is.True);
                Assert.That(simulation.CanTrade(1, 2), Is.False);
                Assert.That(simulation.CanTrade(2, 1), Is.False);
                Command(simulation, CommandKind.SetEmbargo, 2, 1, 0);
                Assert.That(simulation.CanTrade(1, 2), Is.True);
                Assert.That(simulation.CanTrade(1, 1), Is.True);
                Assert.That(simulation.CanTrade(1, 0), Is.False);
            }
        }

        [Test]
        public void AssistanceTransfersOwnGoldAndTroopsWithPerCommandReserve()
        {
            using (var simulation = CreateFixture())
            {
                MakeAlliance(simulation, 1, 2);
                var first = simulation.GetCountry(1);
                var second = simulation.GetCountry(2);
                var totalGold = first.Gold + second.Gold;
                var totalPeople = first.TotalPopulation + second.TotalPopulation;
                Command(simulation, CommandKind.SendGold, 1, 2, 123.5);
                Assert.That(first.Gold, Is.EqualTo(50000 - 123.5));
                Assert.That(second.Gold, Is.EqualTo(50000 + 123.5));
                Assert.That(first.Gold + second.Gold, Is.EqualTo(totalGold));
                Command(simulation, CommandKind.SendTroops, 1, 2, 1);
                Assert.That(first.DefendingTroops, Is.EqualTo(200));
                Assert.That(second.DefendingTroops, Is.EqualTo(1800));
                Assert.That(first.TotalPopulation + second.TotalPopulation, Is.EqualTo(totalPeople));
                Command(simulation, CommandKind.SendTroops, 2, 1, 0.25);
                Assert.That(second.DefendingTroops, Is.EqualTo(1350), "Bot and player commands use the same reserve rule.");
                Assert.That(first.TotalPopulation + second.TotalPopulation, Is.EqualTo(totalPeople));
                Assert.That(first.DeployedTroops + first.ReturningTroops + second.DeployedTroops + second.ReturningTroops, Is.Zero);
            }
        }

        [Test]
        public void DonatedTroopsCanExceedRecipientCapacityWithoutDeletionOrGrowth()
        {
            using (var simulation = CreateFixture())
            {
                MakeAlliance(simulation, 1, 2);
                var first = simulation.GetCountry(1);
                var second = simulation.GetCountry(2);
                SetProperty(first, nameof(CountryState.DefendingTroops), 10000.0);
                SetProperty(second, nameof(CountryState.DefendingTroops), 0.0);
                SetProperty(second, nameof(CountryState.Workers), second.MaxPopulation - 100);
                var total = first.TotalPopulation + second.TotalPopulation;
                Command(simulation, CommandKind.SendTroops, 1, 2, 1);
                Assert.That(second.DefendingTroops, Is.EqualTo(8000));
                Assert.That(second.TotalPopulation, Is.GreaterThan(second.MaxPopulation));
                Assert.That(second.GrowthPerSecond, Is.Zero);
                Assert.That(first.TotalPopulation + second.TotalPopulation, Is.EqualTo(total));
            }
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1.1)]
        public void InvalidTroopRatioDoesNotMutateResources(double ratio)
        {
            using (var simulation = CreateFixture())
            {
                MakeAlliance(simulation, 1, 2);
                Command(simulation, CommandKind.SendTroops, 1, 2, ratio);
                Assert.That(simulation.GetCountry(1).DefendingTroops, Is.EqualTo(1000));
                Assert.That(simulation.GetCountry(2).DefendingTroops, Is.EqualTo(1000));
                Assert.That(CountEvents(simulation, "command.invalid"), Is.EqualTo(1));
            }
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(50001)]
        public void InvalidGoldAmountDoesNotMutateResources(double amount)
        {
            using (var simulation = CreateFixture())
            {
                MakeAlliance(simulation, 1, 2);
                Command(simulation, CommandKind.SendGold, 1, 2, amount);
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(50000));
                Assert.That(simulation.GetCountry(2).Gold, Is.EqualTo(50000));
                Assert.That(CountEvents(simulation, "command.invalid"), Is.EqualTo(1));
            }
        }

        [Test]
        public void AssistanceAndInvalidCountryCommandsCannotBypassAllianceChecks()
        {
            using (var simulation = CreateFixture())
            {
                Command(simulation, CommandKind.SendGold, 1, 2, 100);
                Command(simulation, CommandKind.SendTroops, 2, 1, 0.25);
                Command(simulation, CommandKind.OfferAlliance, 1, 1);
                Command(simulation, CommandKind.OfferAlliance, 1, 0);
                Command(simulation, CommandKind.OfferAlliance, 1, 11);
                Command(simulation, CommandKind.SetEmbargo, 1, 2, double.NaN);
                Command(simulation, CommandKind.BreakAlliance, 1, 2);
                Assert.That(simulation.GetCountry(1).Gold, Is.EqualTo(50000));
                Assert.That(simulation.GetCountry(1).DefendingTroops, Is.EqualTo(1000));
                Assert.That(simulation.GetCountry(2).DefendingTroops, Is.EqualTo(1000));
                Assert.That(simulation.GetRelation(1, 2).Request, Is.Null);
                Assert.That(simulation.CanTrade(1, 2), Is.True);
                Assert.That(simulation.GetTraitorTicksRemaining(1), Is.Zero);
            }
        }

        [Test]
        public void AnnexationClearsEveryRelationOfDestroyedCountryOnly()
        {
            using (var simulation = CreateFixture(2000))
            {
                MakeAlliance(simulation, 2, 4);
                Command(simulation, CommandKind.OfferAlliance, 2, 3);
                Command(simulation, CommandKind.SetEmbargo, 2, 5, 1);
                MakeAlliance(simulation, 1, 6);
                SetProperty(simulation.GetCountry(2), nameof(CountryState.DefendingTroops), 0.0);
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, Cell(2), 0.75));
                simulation.Advance(0.1);
                Assert.That(simulation.GetCountry(2).IsAlive, Is.False);
                for (var countryId = 1; countryId <= 10; countryId++)
                {
                    if (countryId == 2)
                    {
                        continue;
                    }

                    var relation = simulation.GetRelation(countryId, 2);
                    Assert.That(relation.Kind, Is.EqualTo(RelationKind.Peace));
                    Assert.That(relation.Request, Is.Null);
                    Assert.That(relation.IsFirstCountryEmbargoing || relation.IsSecondCountryEmbargoing, Is.False);
                    Assert.That(simulation.CanTrade(countryId, 2), Is.False);
                }

                Assert.That(simulation.GetRelation(1, 6).Kind, Is.EqualTo(RelationKind.Allied));
            }
        }

        [Test]
        public void PauseAndDisposeDoNotLeakDiplomacyStateIntoAnotherMatch()
        {
            var simulation = CreateFixture();
            MakeAlliance(simulation, 1, 2);
            var captured = simulation.GetRelation(1, 2);
            var deadline = captured.AllianceUntilTick;
            var tick = simulation.Tick;
            simulation.SetPaused(true);
            simulation.Advance(1000);
            Assert.That(simulation.Tick, Is.EqualTo(tick));
            Assert.That(captured.AllianceUntilTick, Is.EqualTo(deadline));
            simulation.SetPaused(false);
            simulation.Dispose();
            simulation.Dispose();
            Assert.That(captured.Kind, Is.EqualTo(RelationKind.Peace));
            Assert.That(simulation.Relations.Count, Is.Zero);
            Assert.That(simulation.CanAttack(1, 2), Is.False);

            using (var next = CreateFixture())
            {
                Assert.That(next.GetRelation(1, 2).Kind, Is.EqualTo(RelationKind.Peace));
                Assert.That(next.GetRelation(1, 2).Request, Is.Null);
                Assert.That(next.GetTraitorTicksRemaining(1), Is.Zero);
            }
        }

        private static MatchSimulation CreateFixture(double baseAdvance = 0)
        {
            var terrain = new byte[2048 * 4];
            for (var countryId = 1; countryId <= 10; countryId++)
            {
                terrain[Cell(countryId)] = 1;
            }

            var simulation = new MatchSimulation(new WorldMapData(2048, 4, terrain),
                new MatchSettings { CountryCount = 10, VictoryFraction = 1, Difficulty = AiDifficulty.Easy },
                new SimulationBalance
                {
                    SpawnSeconds = 0,
                    StartingRadius = 0,
                    BaseAdvancePerSecond = baseAdvance,
                    TroopAdvanceCoefficient = 0,
                    WorkerIncomePerSecond = 0,
                    RoleConversionPerSecond = 0
                });
            for (var countryId = 1; countryId <= 10; countryId++)
            {
                SetProperty(simulation.GetCountry(countryId), nameof(CountryState.CapitalCell), Cell(countryId));
            }

            simulation.Advance(0.1);
            for (var countryId = 1; countryId <= 10; countryId++)
            {
                var country = simulation.GetCountry(countryId);
                // Freeze growth through the documented capacity rule, and keep AI
                // troops below its decision reserve. No test-only runtime hook.
                SetProperty(country, nameof(CountryState.Workers), 1000000.0);
                SetProperty(country, nameof(CountryState.DefendingTroops), countryId <= 2 ? 1000.0 : 0.0);
            }

            return simulation;
        }

        private static int Cell(int countryId)
        {
            return 2048 + (countryId <= 2 ? 9 + countryId : countryId * 100);
        }

        private static void MakeAlliance(MatchSimulation simulation, int sender, int recipient)
        {
            Command(simulation, CommandKind.OfferAlliance, sender, recipient);
            Command(simulation, CommandKind.AcceptAlliance, recipient, sender);
        }

        private static void Command(MatchSimulation simulation, CommandKind kind, int actor, int target, double amount = 0.25)
        {
            simulation.Enqueue(new MatchCommand(kind, actor, amount: amount, targetCountryId: target));
            simulation.Advance(0.1);
        }

        private static void AdvanceTo(MatchSimulation simulation, long tick)
        {
            simulation.Advance((tick - simulation.Tick) / 10.0);
            Assert.That(simulation.Tick, Is.EqualTo(tick));
        }

        private static int CountEvents(MatchSimulation simulation, string key)
        {
            var count = 0;
            for (var i = 0; i < simulation.Events.Count; i++)
            {
                if (simulation.Events[i].Key == key)
                {
                    count++;
                }
            }

            return count;
        }

        private static void SetProperty(object target, string name, object value)
        {
            target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
        }
    }
}
