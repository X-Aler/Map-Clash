using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;

namespace MapClash.Tests
{
    public sealed class UiFlowTests
    {
        private GameApplication _application;
        private VisualElement _root;
        private string _previousLanguage;
        private string _previousName;
        private int _previousCountries;
        private int _previousVictory;
        private int _previousTimer;
        private int _previousDifficulty;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousLanguage = PlayerPrefs.GetString("MapClash.Language", "ru");
            _previousName = PlayerPrefs.GetString("MapClash.NextMatch.Name", "");
            _previousCountries = PlayerPrefs.GetInt("MapClash.NextMatch.Countries", 40);
            _previousVictory = PlayerPrefs.GetInt("MapClash.NextMatch.Victory", 80);
            _previousTimer = PlayerPrefs.GetInt("MapClash.NextMatch.Timer", 0);
            _previousDifficulty = PlayerPrefs.GetInt("MapClash.NextMatch.Difficulty", 1);
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            var deadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < deadline)
            {
                _application = Object.FindFirstObjectByType<GameApplication>();
                var document = Object.FindFirstObjectByType<UIDocument>();
                _root = document != null ? document.rootVisualElement : null;
                if (_root != null && _root.Q<Label>("brandTitle")?.text == "MAP CLASH")
                {
                    yield return null;
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Bootstrap did not finish native UI/localization initialization within 30 seconds.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_application != null)
            {
                _application.ReturnToMenu();
                _application.SetLanguage(_previousLanguage);
            }

            PlayerPrefs.SetString("MapClash.Language", _previousLanguage);
            PlayerPrefs.SetString("MapClash.NextMatch.Name", _previousName);
            PlayerPrefs.SetInt("MapClash.NextMatch.Countries", _previousCountries);
            PlayerPrefs.SetInt("MapClash.NextMatch.Victory", _previousVictory);
            PlayerPrefs.SetInt("MapClash.NextMatch.Timer", _previousTimer);
            PlayerPrefs.SetInt("MapClash.NextMatch.Difficulty", _previousDifficulty);
            PlayerPrefs.Save();
            yield return null;
        }

        [UnityTest]
        public IEnumerator SettingsAreSeparateFromMatchSetupAndReturnToPause()
        {
            var ui = Object.FindFirstObjectByType<GameUi>();
            Assert.That(_root.Q<VisualElement>("matchSetup").Q<Slider>("volumeSlider"), Is.Null);
            Activate("openSettingsButton");
            yield return null;
            Assert.That(_root.Q<VisualElement>("settingsOverlay").ClassListContains("hidden"), Is.False);
            Assert.That(_root.Q<VisualElement>("menuOverlay").ClassListContains("hidden"), Is.True);
            Assert.That(ui.IsOverlayVisible, Is.True);
            _application.SetLanguage("en");
            yield return null;
            Assert.That(_root.Q<Label>("settingsTitle").text, Is.EqualTo("Settings"));
            _application.SetLanguage("ru");
            yield return null;
            Assert.That(_root.Q<Label>("settingsTitle").text, Is.EqualTo("Настройки"));
            var originalVolume = _application.MasterVolume;
            try
            {
                _root.Q<Slider>("volumeSlider").value = 0.32f;
                Assert.That(AudioListener.volume, Is.EqualTo(0.32f).Within(0.001f));
                Assert.That(PlayerPrefs.GetFloat("MapClash.Volume"), Is.EqualTo(0.32f).Within(0.001f));
            }
            finally
            {
                _application.SetMasterVolume(originalVolume);
            }
            Activate("settingsBackButton");
            Assert.That(_root.Q<VisualElement>("menuOverlay").ClassListContains("hidden"), Is.False);
            Activate("openSetupButton");
            Activate("startMatchButton");
            _application.Match.SetPaused(true);
            Activate("pauseSettingsButton");
            Assert.That(_root.Q<VisualElement>("pauseOverlay").ClassListContains("hidden"), Is.True);
            var tick = _application.Match.Tick;
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(_application.Match.Tick, Is.EqualTo(tick));
            Assert.That(ui.TryCloseSettings(), Is.True);
            Assert.That(_root.Q<VisualElement>("pauseOverlay").ClassListContains("hidden"), Is.False);
            Assert.That(_application.Match.Phase, Is.EqualTo(MatchPhase.Paused));
        }

        [UnityTest]
        public IEnumerator OperationCountsFollowFrontLossesReinforcementAndRecall()
        {
            _application.enabled = false;
            _application.StartMatch(new MatchSettings { CountryCount = 10, Seed = 12345 });
            var match = _application.Match;
            match.Advance(15);
            var view = _application.MapView;
            view.RefreshOwnership();
            var target = -1;
            for (var cell = 0; cell < match.Map.Terrain.Length && target < 0; cell++)
            {
                if (match.GetOwner(cell) != 1) { continue; }
                for (var direction = 0; direction < 4; direction++)
                {
                    var candidate = match.Map.GetNeighbour(cell, direction);
                    if (match.Map.IsLand(candidate) && match.GetOwner(candidate) == 0) { target = candidate; break; }
                }
            }
            Assert.That(target, Is.GreaterThanOrEqualTo(0));
            match.Enqueue(new MatchCommand(CommandKind.Attack, 1, target, 0.5));
            match.Advance(0.1);
            OperationState wave = null;
            foreach (var operation in match.Operations)
            {
                if (operation.CountryId == 1) { wave = operation; break; }
            }
            Assert.That(wave, Is.Not.Null);
            view.RefreshOwnership();
            var refresh = typeof(WorldMapView).GetMethod("RefreshLabels", BindingFlags.Instance | BindingFlags.NonPublic);
            refresh.Invoke(view, null);
            var countryLabel = view.transform.Find("Country label 1").GetComponent<TMPro.TextMeshPro>();
            Assert.That(countryLabel.text, Does.EndWith("\n" + match.GetCountry(1).HomePopulation.ToString("N0")));
            Assert.That(match.GetCountry(1).HomePopulation, Is.LessThan(match.GetCountry(1).TotalPopulation));
            var labelObject = view.transform.Find("Operation label " + wave.Id);
            Assert.That(labelObject, Is.Not.Null);
            var label = labelObject.GetComponent<TMPro.TextMeshPro>();
            Assert.That(label.text, Is.EqualTo(System.Math.Ceiling(wave.Troops).ToString("0")));
            var initialTroops = wave.Troops;
            var initialFront = wave.FrontCell;
            var initialLabelPosition = countryLabel.transform.position;
            match.Advance(0.3);
            view.RefreshOwnership();
            refresh.Invoke(view, null);
            // Whole-border expansion can keep the centre at the same point. Verify the
            // current territory's centre below rather than requiring directional motion.
            var worldWidth = (float)typeof(WorldMapView).GetField("_worldWidth", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var cellPosition = typeof(WorldMapView).GetMethod("CellPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            double centerSin = 0, centerCos = 0, centerY = 0;
            var ownedCount = 0;
            for (var cell = 0; cell < match.Map.Terrain.Length; cell++)
            {
                if (match.GetOwner(cell) != 1) { continue; }
                var point = (Vector3)cellPosition.Invoke(view, new object[] { cell });
                var angle = point.x / worldWidth * 2 * System.Math.PI;
                centerSin += System.Math.Sin(angle);
                centerCos += System.Math.Cos(angle);
                centerY += point.y;
                ownedCount++;
            }
            var expectedX = (float)(System.Math.Atan2(centerSin, centerCos) / (2 * System.Math.PI) * worldWidth);
            var xDifference = Mathf.Repeat(countryLabel.transform.position.x - expectedX + worldWidth / 2, worldWidth) - worldWidth / 2;
            Assert.That(xDifference, Is.Zero.Within(match.Map.CellScale * 0.1));
            Assert.That(countryLabel.transform.position.y, Is.EqualTo(centerY / ownedCount).Within(match.Map.CellScale * 0.1));
            Assert.That(match.GetOwner(view.WorldToCell(countryLabel.transform.position)), Is.EqualTo(1),
                "The label anchor must stay inside its country's territory.");
            var mainlandLabelPosition = countryLabel.transform.position;
            var island = match.GetCountry(2).CapitalCell;
            var setOwner = typeof(MatchSimulation).GetMethod("SetOwner", BindingFlags.Instance | BindingFlags.NonPublic);
            setOwner.Invoke(match, new object[] { island, 1 });
            view.RefreshOwnership();
            refresh.Invoke(view, null);
            Assert.That(Vector3.Distance(mainlandLabelPosition, countryLabel.transform.position), Is.LessThan(0.001f),
                "A small disconnected island must not pull the label away from the largest component.");
            setOwner.Invoke(match, new object[] { island, 2 });
            view.RefreshOwnership();
            Assert.That(wave.Troops, Is.LessThan(initialTroops));
            Assert.That(wave.FrontCell, Is.Not.EqualTo(initialFront));
            Assert.That(label.text, Is.EqualTo(System.Math.Ceiling(wave.Troops).ToString("0")));
            var beforeReinforcement = wave.Troops;
            // Reinforce through a currently neutral edge rather than an already captured cell.
            for (var cell = 0; cell < match.Map.Terrain.Length; cell++)
            {
                if (match.GetOwner(cell) != 1) { continue; }
                var found = false;
                for (var direction = 0; direction < 4; direction++)
                {
                    var candidate = match.Map.GetNeighbour(cell, direction);
                    if (match.Map.IsLand(candidate) && match.GetOwner(candidate) == 0)
                    {
                        match.Enqueue(new MatchCommand(CommandKind.Attack, 1, candidate, 0.5));
                        found = true;
                        break;
                    }
                }
                if (found) { break; }
            }
            match.Advance(0.1);
            refresh.Invoke(view, null);
            Assert.That(wave.Troops, Is.GreaterThan(beforeReinforcement));
            Assert.That(label.text, Is.EqualTo(System.Math.Ceiling(wave.Troops).ToString("0")));
            match.Enqueue(new MatchCommand(CommandKind.Recall, 1, objectId: wave.Id));
            match.Advance(0.3);
            refresh.Invoke(view, null);
            yield return null;
            Assert.That(label == null, Is.True, "A recalled wave must remove its front label.");
            Assert.That(match.GetCountry(1).ReturningTroops, Is.GreaterThan(0));
            refresh.Invoke(view, null);
            Assert.That(countryLabel.text, Does.EndWith("\n" + match.GetCountry(1).HomePopulation.ToString("N0")));
            match.Advance(3);
            refresh.Invoke(view, null);
            Assert.That(match.GetCountry(1).ReturningTroops, Is.Zero.Within(0.001));
            Assert.That(countryLabel.text, Does.EndWith("\n" + match.GetCountry(1).HomePopulation.ToString("N0")));
            _application.ReturnToMenu();
            yield return null;
            foreach (var text in view.GetComponentsInChildren<TMPro.TextMeshPro>())
            {
                Assert.That(text.name, Does.Not.StartWith("Operation label "));
            }
            _application.enabled = true;
        }

        [UnityTest]
        public IEnumerator NativeMenuStartsMatchPausesAndReturnsWithoutOldSession()
        {
            Assert.That(_application.IsInMenu, Is.True);
            Activate("openSetupButton");
            Assert.That(_root.Q<VisualElement>("mainMenu").ClassListContains("hidden"), Is.True);
            Assert.That(_root.Q<VisualElement>("matchSetup").ClassListContains("hidden"), Is.False);
            yield return null;
            var tracker = _root.Q<SliderInt>("countryCountSlider")
                .Q<VisualElement>(className: "unity-base-slider__tracker");
            Assert.That(tracker, Is.Not.Null);
            Assert.That(tracker.resolvedStyle.width, Is.GreaterThan(100), "Custom slider theme must provide a usable track.");
            var checkmark = _root.Q<Toggle>("timerToggle")
                .Q<VisualElement>(className: "unity-toggle__checkmark");
            var panelUnitsPerPixel = _root.resolvedStyle.width / Mathf.Max(1, Screen.width);
            Assert.That(checkmark.resolvedStyle.width, Is.EqualTo(18).Within(Mathf.Max(1, panelUnitsPerPixel)),
                "Allow one rendered pixel of rounding at the current Game View scale.");
            _root.Q<SliderInt>("countryCountSlider").value = 10;
            Activate("startMatchButton");
            yield return null;
            Assert.That(_application.Match, Is.Not.Null);
            Assert.That(_application.Match.Countries.Count, Is.EqualTo(10));
            Assert.That(_root.Q<VisualElement>("menuOverlay").ClassListContains("hidden"), Is.True);
            if (_application.Match.Phase == MatchPhase.Paused)
            {
                _application.TogglePause();
            }

            Activate("pauseButton");
            Assert.That(_application.Match.Phase, Is.EqualTo(MatchPhase.Paused));
            Assert.That(_root.Q<VisualElement>("pauseOverlay").ClassListContains("hidden"), Is.False);
            var pausedTick = _application.Match.Tick;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(_application.Match.Tick, Is.EqualTo(pausedTick));
            Activate("pauseMenuButton");
            Assert.That(_application.Match, Is.Null);
            Assert.That(_root.Q<VisualElement>("mainMenu").ClassListContains("hidden"), Is.False);
            Assert.That(_root.Q<VisualElement>("pauseOverlay").ClassListContains("hidden"), Is.True);
            Activate("openSetupButton");
            _root.Q<SliderInt>("countryCountSlider").value = 60;
            Activate("startMatchButton");
            yield return null;
            Assert.That(_application.Match.Countries.Count, Is.EqualTo(60));
            Assert.That(_application.Match.Tick, Is.LessThan(10));
        }

        [UnityTest]
        public IEnumerator LanguageButtonsLocalizeTheSameNativeScreenAndPresetsUpdateBothInputs()
        {
            Activate("languageEn");
            yield return null;
            Assert.That(_root.Q<Label>("setupTitle").text, Is.EqualTo("New match"));
            Activate("languageRu");
            yield return null;
            Assert.That(_root.Q<Label>("setupTitle").text, Is.EqualTo("Новая партия"));
            var ui = Object.FindFirstObjectByType<GameUi>();
            ui.PresentEvents(new[] { new MatchEvent("command.invalid", 1) });
            Assert.That(_root.Q<Label>("notification").ClassListContains("hidden"), Is.False);
            Assert.That(_root.Q<Label>("notification").text, Does.Contain("Действие недоступно"));
            ui.CycleAttackRatio(1);
            Assert.That(ui.AttackRatio, Is.EqualTo(0.35).Within(0.0001));
            Assert.That(_root.Q<SliderInt>("attackRatioSlider").value, Is.EqualTo(35));
            _root.Q<IntegerField>("attackRatioInput").value = 500;
            Assert.That(ui.AttackRatio, Is.EqualTo(0.8).Within(0.0001));
            Assert.That(_root.Q<SliderInt>("attackRatioSlider").value, Is.EqualTo(80));
        }

        [UnityTest]
        public IEnumerator MapNavigationUsesConfiguredSpeedsAndBlocksInputUnderUi()
        {
            var view = Object.FindFirstObjectByType<WorldMapView>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var configField = typeof(WorldMapView).GetField("_cameraConfig", flags);
            var originalConfig = (MapCameraConfig)configField.GetValue(view);
            Assert.That(originalConfig, Is.Not.Null, "Bootstrap must reference the authored camera config.");
            var config = Object.Instantiate(originalConfig);
            var camera = (Camera)typeof(WorldMapView).GetField("_camera", flags).GetValue(view);
            var originalMouse = Mouse.current;
            var originalKeyboard = Keyboard.current;
            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            _application.enabled = false;
            try
            {
                JsonUtility.FromJsonOverwrite("{\"_zoomSensitivity\":0.3,\"_mousePanSpeed\":2,\"_keyboardPanSpeed\":3,\"_panResponse\":0,\"_zoomResponse\":0}", config);
                configField.SetValue(view, config);
                view.Bind(null);
                view.IsInputBlocked = false;
                var initialZoom = camera.orthographicSize;
                InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 1) });
                InputSystem.Update();
                view.Navigate(0.1f);
                Assert.That(camera.orthographicSize, Is.EqualTo(initialZoom * Mathf.Exp(-0.3f)).Within(0.001f));

                var beforeDrag = camera.transform.position;
                InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(20, 10) }.WithButton(MouseButton.Middle));
                InputSystem.Update();
                view.Navigate(0.1f);
                var unitsPerPixel = camera.orthographicSize * 2 / camera.pixelHeight;
                Assert.That(camera.transform.position.x, Is.EqualTo(beforeDrag.x - 20 * unitsPerPixel * 2).Within(0.001f));
                Assert.That(camera.transform.position.y, Is.EqualTo(beforeDrag.y - 10 * unitsPerPixel * 2).Within(0.001f));

                var beforeKeyboard = camera.transform.position;
                InputSystem.QueueStateEvent(mouse, new MouseState());
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
                InputSystem.Update();
                view.Navigate(0.1f);
                Assert.That(camera.transform.position.x,
                    Is.EqualTo(beforeKeyboard.x + camera.orthographicSize * 3 * 0.1f).Within(0.001f));

                view.IsInputBlocked = true;
                var blockedPosition = camera.transform.position;
                var blockedZoom = camera.orthographicSize;
                InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, 1), delta = new Vector2(40, 20) }.WithButton(MouseButton.Middle));
                InputSystem.Update();
                view.Navigate(0.1f);
                Assert.That(camera.transform.position, Is.EqualTo(blockedPosition));
                Assert.That(camera.orthographicSize, Is.EqualTo(blockedZoom));
            }
            finally
            {
                configField.SetValue(view, originalConfig);
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
                originalMouse?.MakeCurrent();
                originalKeyboard?.MakeCurrent();
                Object.Destroy(config);
                view.Bind(null);
                _application.enabled = true;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator BotNamesFollowLocaleInMapHudAndContextWithoutRenamingPlayer()
        {
            _application.enabled = false;
            _application.StartMatch(new MatchSettings { CountryCount = 60, PlayerName = "Моя страна / Custom", Seed = 12345 });
            var match = _application.Match;
            var text = new GameText();
            var ui = Object.FindFirstObjectByType<GameUi>();
            var refreshLabels = typeof(WorldMapView).GetMethod("RefreshLabels", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var language in new[] { "ru", "en", "ru" })
            {
                _application.SetLanguage(language);
                yield return null;
                for (var i = 1; i < match.Countries.Count; i++)
                {
                    var name = text.CountryName(match.Countries[i]);
                    Assert.That(name, Is.Not.Empty);
                    Assert.That(name, Does.Not.Contain("country.bot."));
                    Assert.That(name, Does.Not.Contain("No translation"));
                    if (language == "en")
                    {
                        Assert.That(name, Is.EqualTo(match.Countries[i].Name));
                    }
                    else
                    {
                        Assert.That(name, Is.Not.EqualTo(match.Countries[i].Name));
                    }
                }

                Assert.That(text.CountryName(match.GetCountry(1)), Is.EqualTo("Моя страна / Custom"));
                refreshLabels.Invoke(_application.MapView, null);
                var labels = _application.MapView.GetComponentsInChildren<TMPro.TextMeshPro>();
                var brazil = System.Array.Find(labels, label => label.gameObject.name == "Country label 2");
                Assert.That(brazil.text, Does.StartWith(language == "ru" ? "Бразилия\n" : "Brazil\n"));
                Assert.That(brazil.text, Does.EndWith("\n" + match.GetCountry(2).HomePopulation.ToString("N0")));
                brazil.ForceMeshUpdate();
                var camera = (Camera)typeof(WorldMapView).GetField("_camera", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_application.MapView);
                var labelPixels = brazil.textBounds.size.y * brazil.transform.lossyScale.y * camera.pixelHeight / (2 * camera.orthographicSize);
                Assert.That(labelPixels, Is.GreaterThan(12), "Both label lines must occupy readable pixels, including in the map overview.");
                match.Advance(15);
                ui.ShowContext(match.GetCountry(2).CapitalCell, Vector2.zero);
                Assert.That(_root.Q<Label>("contextTitle").text, Is.EqualTo(language == "ru" ? "Бразилия" : "Brazil"));
                ui.HideContext();
            }

            _application.enabled = true;
        }

        [UnityTest]
        public IEnumerator KeyboardMouseActionsAndRealPanelsRespectInputBoundaries()
        {
            Activate("openSetupButton");
            _root.Q<SliderInt>("countryCountSlider").value = 10;
            Activate("startMatchButton");
            _application.enabled = false;
            var match = _application.Match;
            match.SetPaused(false);
            match.Advance(15);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(GameApplication).GetMethod("Update", flags).Invoke(_application, null);
            yield return null;
            var ui = Object.FindFirstObjectByType<GameUi>();
            var view = _application.MapView;
            var camera = (Camera)typeof(WorldMapView).GetField("_camera", flags).GetValue(view);
            var material = (Material)typeof(WorldMapView).GetField("_material", flags).GetValue(view);
            var readInput = typeof(GameApplication).GetMethod("ReadInput", flags);
            // The Editor can lose focus while the fixture yields, legitimately pausing the match.
            match.SetPaused(false);
            typeof(GameUi).GetMethod("Refresh", flags).Invoke(ui, null);
            var originalMouse = Mouse.current;
            var originalKeyboard = Keyboard.current;
            var mouse = InputSystem.AddDevice<Mouse>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                var target = -1;
                for (var cell = 0; cell < match.Map.Terrain.Length && target < 0; cell++)
                {
                    if (match.GetOwner(cell) != 1) { continue; }
                    for (var direction = 0; direction < 4; direction++)
                    {
                        var neighbour = match.Map.GetNeighbour(cell, direction);
                        if (match.Map.IsLand(neighbour) && match.GetOwner(neighbour) == 0) { target = neighbour; break; }
                    }
                }

                Assert.That(target, Is.GreaterThanOrEqualTo(0));
                view.FocusCell(target);
                view.Navigate(10);
                var screen = (Vector2)camera.WorldToScreenPoint((Vector3)typeof(WorldMapView).GetMethod("CellPosition", flags).Invoke(view, new object[] { target }));
                Assert.That(ui.IsPointerOverUi(screen), Is.False, "Fixture target must be on the exposed map.");
                // Establish the released state before checking frame-based press transitions.
                _ = keyboard.eKey.wasPressedThisFrame;
                _ = keyboard.qKey.wasPressedThisFrame;
                _ = keyboard.escapeKey.wasPressedThisFrame;
                _ = mouse.leftButton.wasPressedThisFrame;
                _ = mouse.rightButton.wasPressedThisFrame;
                SendInput(new MouseState { position = screen }, new KeyboardState());
                SendInput(new MouseState { position = screen }, new KeyboardState(Key.E));
                Assert.That(ui.AttackRatio, Is.EqualTo(0.35));
                SendInput(new MouseState { position = screen }, new KeyboardState());
                SendInput(new MouseState { position = screen }, new KeyboardState(Key.Q));
                Assert.That(ui.AttackRatio, Is.EqualTo(0.25));
                var zoom = camera.orthographicSize;
                SendInput(new MouseState { position = screen, scroll = new Vector2(0, 1) }, new KeyboardState(Key.LeftShift));
                Assert.That(ui.AttackRatio, Is.EqualTo(0.35));
                Assert.That(camera.orthographicSize, Is.EqualTo(zoom).Within(0.0001f), "Shift+wheel changes attack ratio without zooming.");
                SendInput(new MouseState { position = screen }, new KeyboardState(Key.Space));
                Assert.That(material.GetFloat("_TerrainView"), Is.EqualTo(1));
                SendInput(new MouseState { position = screen }, new KeyboardState());
                Assert.That(material.GetFloat("_TerrainView"), Is.Zero);

                SendInput(new MouseState { position = screen }.WithButton(MouseButton.Right), new KeyboardState());
                Assert.That(_root.Q<VisualElement>("contextWheel").ClassListContains("hidden"), Is.False);
                var panelPoint = _root.Q<Button>("contextClose").worldBound.center;
                yield return null;
                panelPoint = _root.Q<Button>("contextClose").worldBound.center;
                var unitsPerPixel = _root.resolvedStyle.width / Screen.width;
                var panelScreen = new Vector2(panelPoint.x / unitsPerPixel, Screen.height - panelPoint.y / unitsPerPixel);
                Assert.That(ui.IsPointerOverUi(panelScreen), Is.True);
                SendInput(new MouseState { position = panelScreen }, new KeyboardState());
                SendInput(new MouseState { position = panelScreen }.WithButton(MouseButton.Left), new KeyboardState());
                match.Advance(0.1);
                Assert.That(HasPlayerOperation(), Is.False, "Clicking the actual context panel must not attack behind it.");
                ui.HideContext();
                SendInput(new MouseState { position = screen }, new KeyboardState());
                match.Advance(0.1);
                Assert.That(HasPlayerOperation(), Is.False, "A press begun over UI must not become a map click on release.");
                SendInput(new MouseState { position = screen }, new KeyboardState());
                SendInput(new MouseState { position = screen }.WithButton(MouseButton.Left), new KeyboardState());
                Assert.That(HasPlayerOperation(), Is.False, "Press alone must not launch troops.");
                var beforeDrag = camera.transform.position;
                var dragScreen = screen + new Vector2(100, 0);
                SendInput(new MouseState { position = dragScreen, delta = new Vector2(100, 0) }.WithButton(MouseButton.Left), new KeyboardState());
                SendInput(new MouseState { position = dragScreen }, new KeyboardState());
                view.Navigate(1);
                Assert.That(camera.transform.position.x, Is.LessThan(beforeDrag.x));
                match.Advance(0.1);
                Assert.That(HasPlayerOperation(), Is.False, "Dragging the map must not attack.");
                view.FocusCell(target);
                view.Navigate(10);
                SendInput(new MouseState { position = screen }, new KeyboardState());
                SendInput(new MouseState { position = screen }.WithButton(MouseButton.Left), new KeyboardState());
                SendInput(new MouseState { position = screen }, new KeyboardState());
                match.Advance(0.1);
                Assert.That(HasPlayerOperation(), Is.True, "An exposed map click must dispatch the real command.");

                SendInput(new MouseState { position = screen }, new KeyboardState(Key.Escape));
                Assert.That(match.Phase, Is.EqualTo(MatchPhase.Paused));
                Assert.That(_root.Q<VisualElement>("pauseOverlay").ClassListContains("hidden"), Is.False);
                var tick = match.Tick;
                SendInput(new MouseState { position = screen }.WithButton(MouseButton.Left), new KeyboardState());
                match.Advance(1);
                Assert.That(match.Tick, Is.EqualTo(tick));
                Activate("pauseSettingsButton");
                SendInput(new MouseState { position = screen }, new KeyboardState(Key.Escape));
                Assert.That(_root.Q<VisualElement>("settingsOverlay").ClassListContains("hidden"), Is.True);
                Assert.That(match.Phase, Is.EqualTo(MatchPhase.Paused), "Escape from settings must preserve pause.");
                SendInput(new MouseState { position = screen }, new KeyboardState());
                SendInput(new MouseState { position = screen }, new KeyboardState(Key.Escape));
                Assert.That(match.Phase, Is.EqualTo(MatchPhase.Playing));
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
                originalMouse?.MakeCurrent();
                originalKeyboard?.MakeCurrent();
                _application.enabled = true;
            }

            void SendInput(MouseState mouseState, KeyboardState keyboardState)
            {
                InputSystem.QueueStateEvent(mouse, mouseState);
                InputSystem.QueueStateEvent(keyboard, keyboardState);
                InputSystem.Update();
                mouse.MakeCurrent();
                keyboard.MakeCurrent();
                readInput.Invoke(_application, null);
            }

            bool HasPlayerOperation()
            {
                foreach (var operation in match.Operations)
                {
                    if (operation.CountryId == 1) { return true; }
                }
                return false;
            }
        }

        private void Activate(string buttonName)
        {
            var button = _root.Q<Button>(buttonName);
            Assert.That(button, Is.Not.Null);
            button.Focus();
            using (var navigation = NavigationSubmitEvent.GetPooled())
            {
                button.SendEvent(navigation);
            }
        }
    }
}
