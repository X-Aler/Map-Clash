using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace MapClash.Tests
{
    public sealed class LandMatchAcceptanceTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo SetOwnerMethod = typeof(MatchSimulation).GetMethod("SetOwner", PrivateInstance);
        private readonly string[] _preferenceKeys =
        {
            "MapClash.Language", "MapClash.NextMatch.Name", "MapClash.NextMatch.Countries",
            "MapClash.NextMatch.Victory", "MapClash.NextMatch.Timer", "MapClash.NextMatch.Difficulty"
        };
        private readonly string[] _stringPreferences = new string[2];
        private readonly int[] _integerPreferences = new int[4];
        private readonly bool[] _hadPreferences = new bool[6];
        private GameApplication _application;
        private VisualElement _root;
        private string _previousLanguage;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousLanguage = PlayerPrefs.GetString("MapClash.Language", "ru");
            for (var index = 0; index < _preferenceKeys.Length; index++)
            {
                _hadPreferences[index] = PlayerPrefs.HasKey(_preferenceKeys[index]);
                if (index < 2)
                {
                    _stringPreferences[index] = PlayerPrefs.GetString(_preferenceKeys[index]);
                }
                else
                {
                    _integerPreferences[index - 2] = PlayerPrefs.GetInt(_preferenceKeys[index]);
                }
            }

            yield return SceneManager.LoadSceneAsync("Bootstrap");
            var deadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < deadline)
            {
                _application = UnityEngine.Object.FindFirstObjectByType<GameApplication>();
                var document = UnityEngine.Object.FindFirstObjectByType<UIDocument>();
                _root = document != null ? document.rootVisualElement : null;
                if (_application != null && _root?.Q<Label>("brandTitle")?.text == "MAP CLASH")
                {
                    // Tests drive simulation ticks explicitly; UI callbacks remain fully active.
                    _application.enabled = false;
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Bootstrap did not initialize native UI/localization within 30 seconds.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_application != null)
            {
                _application.ReturnToMenu();
                _application.SetLanguage(_previousLanguage);
                _application.enabled = true;
            }

            for (var index = 0; index < _preferenceKeys.Length; index++)
            {
                if (!_hadPreferences[index])
                {
                    PlayerPrefs.DeleteKey(_preferenceKeys[index]);
                }
                else if (index < 2)
                {
                    PlayerPrefs.SetString(_preferenceKeys[index], _stringPreferences[index]);
                }
                else
                {
                    PlayerPrefs.SetInt(_preferenceKeys[index], _integerPreferences[index - 2]);
                }
            }

            PlayerPrefs.Save();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LandCaptureProducesRussianVictoryAndRetryCreatesCleanSession()
        {
            Activate("languageRu");
            yield return null;
            var simulation = StartConfiguredMatch();
            var player = simulation.GetCountry(1);
            var target = FindLandNeighbour(simulation, player.CapitalCell);
            SetOwner(simulation, target, 0);

            // Prepare the real map one capture below the configured 50% threshold.
            // Only the following attack is allowed to cross it and create the result.
            var requiredLand = (int)Math.Ceiling(simulation.Map.TotalLandTiles * simulation.Settings.VictoryFraction);
            for (var cell = 0; cell < simulation.Map.Terrain.Length && player.LandTiles < requiredLand - 1; cell++)
            {
                if (cell != target && simulation.Map.IsLand(cell) && simulation.GetOwner(cell) == 0)
                {
                    SetOwner(simulation, cell, 1);
                }
            }

            Assert.That(player.LandTiles, Is.EqualTo(requiredLand - 1));
            Assert.That(simulation.Result, Is.Null);
            SetProperty(player, nameof(CountryState.Gold), 123456789.0);
            _application.Submit(new MatchCommand(CommandKind.Attack, 1, target));
            simulation.Advance(0.2);

            // Enclosed pockets may take priority over the clicked cell. Victory is based
            // on land actually captured by this order, rather than reaching that cell.
            Assert.That(player.LandTiles, Is.GreaterThanOrEqualTo(requiredLand));
            Assert.That(simulation.Result, Is.Not.Null);
            Assert.That(simulation.Result.ReasonKey, Is.EqualTo("result.domination"));
            Assert.That(simulation.Result.WinnerCountryId, Is.EqualTo(1));
            AssertResultAndRetry(simulation, "Победа");
        }

        [UnityTest]
        public IEnumerator LastPlayerCellAnnexationProducesEnglishDefeatAndRetryCreatesCleanSession()
        {
            Activate("languageEn");
            yield return null;
            var simulation = StartConfiguredMatch();
            var player = simulation.GetCountry(1);
            var lastCell = player.CapitalCell;
            var enemyCell = FindLandNeighbour(simulation, lastCell);
            for (var cell = 0; cell < simulation.Map.Terrain.Length; cell++)
            {
                if (cell != lastCell && simulation.GetOwner(cell) == 1)
                {
                    SetOwner(simulation, cell, 0);
                }
            }

            SetOwner(simulation, enemyCell, 2);
            SetProperty(player, nameof(CountryState.DefendingTroops), 0.0);
            SetProperty(player, nameof(CountryState.Gold), 123456789.0);
            SetProperty(simulation.GetCountry(2), nameof(CountryState.DefendingTroops), 1000000.0);
            Assert.That(player.LandTiles, Is.EqualTo(1));
            Assert.That(simulation.Result, Is.Null);
            _application.Submit(new MatchCommand(CommandKind.Attack, 2, lastCell));
            simulation.Advance(0.2);

            Assert.That(simulation.GetOwner(lastCell), Is.EqualTo(2));
            Assert.That(player.LandTiles, Is.Zero);
            Assert.That(player.IsAlive, Is.False);
            Assert.That(simulation.Result, Is.Not.Null);
            Assert.That(simulation.Result.ReasonKey, Is.EqualTo("result.annexed"));
            Assert.That(simulation.Result.WinnerCountryId, Is.Not.EqualTo(1));
            AssertResultAndRetry(simulation, "Defeat", false);
        }

        [Test]
        public void ReinforcementRecallAndCounterattackPreserveLandOperationAccounting()
        {
            using (var simulation = CreateOperationsFixture())
            {
                var player = simulation.GetCountry(1);
                for (var cell = 1; cell < 256; cell++) { SetOwner(simulation, cell, 2); }
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, 1));
                simulation.Advance(0.1);
                Assert.That(simulation.Operations.Count, Is.EqualTo(1));
                var wave = simulation.Operations[0];
                var firstTroops = wave.Troops;
                var reinforcement = Math.Floor(player.DefendingTroops * 0.25);
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, 1));
                simulation.Advance(0.1);
                Assert.That(simulation.Operations.Count, Is.EqualTo(1));
                Assert.That(simulation.Operations[0], Is.SameAs(wave));
                Assert.That(wave.Troops, Is.LessThan(firstTroops + reinforcement));
                Assert.That(wave.Troops, Is.GreaterThan(firstTroops));
                Assert.That(player.DeployedTroops, Is.EqualTo(wave.Troops).Within(1e-6));

                simulation.Enqueue(new MatchCommand(CommandKind.Recall, 1, objectId: wave.Id));
                simulation.Enqueue(new MatchCommand(CommandKind.Recall, 1, objectId: wave.Id));
                simulation.Advance(0.3);
                var survivors = wave.Troops;
                var expectedSurvivors = Math.Floor(survivors * 0.75);
                var reserveBeforeReturn = player.DefendingTroops;
                Assert.That(wave.Phase, Is.EqualTo(OperationPhase.Returning));
                Assert.That(player.DeployedTroops, Is.Zero.Within(1e-6));
                Assert.That(player.ReturningTroops, Is.EqualTo(survivors).Within(1e-6));
                Assert.That(player.DefendingTroops, Is.EqualTo(reserveBeforeReturn).Within(1e-6));
                simulation.Advance(1.8);
                Assert.That(player.ReturningTroops, Is.EqualTo(survivors).Within(1e-6));
                Assert.That(player.DefendingTroops, Is.EqualTo(reserveBeforeReturn).Within(1e-6));
                simulation.Advance(0.1);
                Assert.That(simulation.Operations.Count, Is.Zero);
                Assert.That(player.ReturningTroops, Is.Zero.Within(1e-6));
                Assert.That(player.DefendingTroops, Is.EqualTo(reserveBeforeReturn + expectedSurvivors).Within(1e-6));
                Assert.That(simulation.GetOwner(0), Is.EqualTo(1));
                Assert.That(simulation.GetOwner(255), Is.EqualTo(2));
            }

            using (var simulation = CreateOperationsFixture())
            {
                var playerSent = Math.Floor(simulation.GetCountry(1).DefendingTroops * 0.5);
                var enemySent = Math.Floor(simulation.GetCountry(2).DefendingTroops * 0.25);
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, 1, 0.5));
                simulation.Enqueue(new MatchCommand(CommandKind.Attack, 2, 0, 0.25));
                simulation.Advance(0.1);
                Assert.That(simulation.GetCountry(1).DeployedTroops, Is.EqualTo(playerSent - enemySent).Within(1e-6));
                Assert.That(simulation.GetCountry(2).DeployedTroops, Is.Zero.Within(1e-6));
                Assert.That(simulation.GetOwner(0), Is.EqualTo(1));
                Assert.That(simulation.GetOwner(1), Is.EqualTo(2));
            }
        }

        private MatchSimulation StartConfiguredMatch()
        {
            Activate("openSetupButton");
            _root.Q<SliderInt>("countryCountSlider").value = 10;
            _root.Q<SliderInt>("victorySlider").value = 50;
            _root.Q<Toggle>("timerToggle").value = false;
            Activate("easyButton");
            Activate("startMatchButton");
            var simulation = _application.Match as MatchSimulation;
            Assert.That(simulation, Is.Not.Null);
            simulation.Advance(15);
            Assert.That(simulation.Phase, Is.EqualTo(MatchPhase.Playing));
            return simulation;
        }

        private void AssertResultAndRetry(MatchSimulation previous, string expectedTitle, bool requireLiveWave = true)
        {
            Assert.That(previous.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(previous.ElapsedSeconds, Is.GreaterThan(0));
            if (requireLiveWave)
            {
                Assert.That(previous.Operations.Count, Is.GreaterThan(0), "A live wave must exist to exercise retry disposal.");
            }
            // Run the normal phase-change presentation once without enabling automatic ticks.
            typeof(GameApplication).GetMethod("Update", PrivateInstance).Invoke(_application, null);
            Assert.That(_root.Q<VisualElement>("resultOverlay").ClassListContains("hidden"), Is.False);
            Assert.That(_root.Q<Label>("resultTitle").text, Is.EqualTo(expectedTitle));
            var previousSeed = previous.Settings.Seed;
            Activate("retryButton");
            var next = _application.Match;
            Assert.That(next, Is.Not.Null.And.Not.SameAs(previous));
            Assert.Throws<ObjectDisposedException>(() => previous.Advance(0.1));
            Assert.That(previous.Operations.Count, Is.Zero);
            for (var index = 0; index < previous.Countries.Count; index++)
            {
                Assert.That(previous.Countries[index].TotalPopulation, Is.Zero);
            }

            Assert.That(next.Settings.Seed, Is.Not.EqualTo(previousSeed));
            Assert.That(next.Settings.CountryCount, Is.EqualTo(10));
            Assert.That(next.Settings.VictoryFraction, Is.EqualTo(0.5));
            Assert.That(next.Phase, Is.EqualTo(MatchPhase.Spawn));
            Assert.That(next.Tick, Is.Zero);
            Assert.That(next.ElapsedSeconds, Is.Zero);
            Assert.That(next.Operations.Count, Is.Zero);
            Assert.That(next.Result, Is.Null);
            for (var index = 0; index < next.Countries.Count; index++)
            {
                Assert.That(next.Countries[index].TotalPopulation, Is.EqualTo(25000));
                Assert.That(next.Countries[index].Gold, Is.EqualTo(50000));
            }

            Assert.That(_root.Q<VisualElement>("resultOverlay").ClassListContains("hidden"), Is.True);
            Assert.That(_root.Q<VisualElement>("menuOverlay").ClassListContains("hidden"), Is.True);
        }

        private void Activate(string buttonName)
        {
            var button = _root.Q<Button>(buttonName);
            Assert.That(button, Is.Not.Null, buttonName);
            button.Focus();
            using (var navigation = NavigationSubmitEvent.GetPooled())
            {
                button.SendEvent(navigation);
            }
        }

        private static int FindLandNeighbour(MatchSimulation simulation, int cell)
        {
            for (var direction = 0; direction < 4; direction++)
            {
                var neighbour = simulation.Map.GetNeighbour(cell, direction);
                if (simulation.Map.IsLand(neighbour))
                {
                    return neighbour;
                }
            }

            Assert.Fail("The real-map starting capital must have an adjacent land cell for combat.");
            return -1;
        }

        private static MatchSimulation CreateOperationsFixture()
        {
            var terrain = new byte[2048 * 8];
            for (var cell = 0; cell < 256; cell++)
            {
                terrain[cell] = 1;
                terrain[2048 + cell] = 1;
            }

            for (var countryId = 3; countryId <= 10; countryId++)
            {
                terrain[2048 * 5 + countryId * 100] = 1;
            }

            var balance = new SimulationBalance
            {
                StartingRadius = 0, BaseAdvancePerSecond = 0, TroopAdvanceCoefficient = 0,
                BasePopulation = 1, RoleConversionPerSecond = 0
            };
            var simulation = new MatchSimulation(new WorldMapData(2048, 8, terrain),
                new MatchSettings { Seed = 1337, CountryCount = 10 }, balance);
            SetProperty(simulation.GetCountry(1), nameof(CountryState.CapitalCell), 0);
            SetProperty(simulation.GetCountry(2), nameof(CountryState.CapitalCell), 1);
            for (var countryId = 3; countryId <= 10; countryId++)
            {
                SetProperty(simulation.GetCountry(countryId), nameof(CountryState.CapitalCell), 2048 * 5 + countryId * 100);
            }

            simulation.Advance(15);
            // The AI's reserve gate reads this setting each tick. Raise it only after
            // spawning, preserving actual populations while excluding autonomous commands.
            var simulationBalance = (SimulationBalance)typeof(MatchSimulation)
                .GetField("_balance", PrivateInstance).GetValue(simulation);
            simulationBalance.StartingPopulation = 1000000;
            return simulation;
        }

        private static void SetOwner(MatchSimulation simulation, int cell, int countryId)
        {
            SetOwnerMethod.Invoke(simulation, new object[] { cell, countryId });
        }

        private static void SetProperty<T>(CountryState country, string property, T value)
        {
            typeof(CountryState).GetProperty(property, BindingFlags.Instance | BindingFlags.Public).SetValue(country, value);
        }
    }
}
