using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace MapClash
{
    public sealed class GameUi : MonoBehaviour
    {
        private const int PlayerCountryId = 1;
        private const string PreferencePrefix = "MapClash.NextMatch.";
        private readonly int[] _presets = { 10, 25, 35, 50, 75 };
        private readonly GameText _text = new GameText();
        private readonly Dictionary<string, Label> _labels = new Dictionary<string, Label>();
        private readonly List<OperationRow> _operationRows = new List<OperationRow>();
        private GameApplication _application;
        private VisualElement _root;
        private VisualElement _menuOverlay;
        private VisualElement _pauseOverlay;
        private VisualElement _resultOverlay;
        private VisualElement _settingsOverlay;
        private bool _isSettingsVisible;
        private VisualElement _hud;
        private VisualElement _mainMenu;
        private VisualElement _matchSetup;
        private VisualElement _spawnPanel;
        private VisualElement _contextWheel;
        private ScrollView _operationsList;
        private TextField _playerNameInput;
        private SliderInt _countryCountSlider;
        private SliderInt _victorySlider;
        private SliderInt _attackRatioSlider;
        private IntegerField _attackRatioInput;
        private SliderInt _troopRatioSlider;
        private Toggle _timerToggle;
        private Slider _volumeSlider;
        private ProgressBar _dominationProgress;
        private VisualElement _victoryMarker;
        private IVisualElementScheduledItem _refreshSchedule;
        private AiDifficulty _difficulty = AiDifficulty.Normal;
        private MatchSettings _lastSettings;
        private int _attackPercent = 25;
        private int _contextCell = -1;
        private float _notificationExpiresAt;
        private bool _isInitialized;

        public double AttackRatio => _attackPercent / 100.0;
        public bool IsOverlayVisible => _isSettingsVisible || _application == null || _application.Match == null ||
            _application.Match.Phase == MatchPhase.Paused || _application.Match.Phase == MatchPhase.Finished;

        public void Initialize(GameApplication application, UIDocument document)
        {
            if (_isInitialized)
            {
                throw new InvalidOperationException("GameUi has already been initialized.");
            }

            if (application == null || document == null)
            {
                throw new ArgumentNullException(application == null ? nameof(application) : nameof(document));
            }

            _application = application;
            _root = document.rootVisualElement;
            if (_root.Q<VisualElement>("gameRoot") == null)
            {
                var template = Resources.Load<VisualTreeAsset>("MapClashUi");
                if (template == null)
                {
                    throw new InvalidOperationException("Missing Resources/MapClashUi.uxml.");
                }

                template.CloneTree(_root);
            }

            _root.pickingMode = PickingMode.Ignore;
            CacheElements();
            BindActions();
            LoadSettings();
            _application.StateChanged += OnApplicationStateChanged;
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
            _isInitialized = true;
            Localize();
            Refresh();
            _refreshSchedule = _root.schedule.Execute(Refresh).Every(250);
        }

        public void CycleAttackRatio(int direction)
        {
            if (direction > 0)
            {
                for (var index = 0; index < _presets.Length; index++)
                {
                    if (_presets[index] > _attackPercent)
                    {
                        SetAttackPercent(_presets[index]);
                        return;
                    }
                }

                SetAttackPercent(_presets[0]);
                return;
            }

            for (var index = _presets.Length - 1; index >= 0; index--)
            {
                if (_presets[index] < _attackPercent)
                {
                    SetAttackPercent(_presets[index]);
                    return;
                }
            }

            SetAttackPercent(_presets[_presets.Length - 1]);
        }

        public void PresentEvents(IReadOnlyList<MatchEvent> events)
        {
            if (!_isInitialized || events == null)
            {
                return;
            }

            for (var index = events.Count - 1; index >= 0; index--)
            {
                var notification = events[index];
                if (notification.CountryId != 0 && notification.CountryId != PlayerCountryId)
                {
                    continue;
                }

                SetLabel("notification", _text.Get("event." + notification.Key, notification.Detail));
                _notificationExpiresAt = Time.unscaledTime + 4;
                SetVisible(_labels["notification"], true);
                break;
            }
        }

        public void ShowContext(int cell, Vector2 panelPosition)
        {
            var match = _application.Match;
            if (IsOverlayVisible || match.Phase != MatchPhase.Playing || !match.Map.IsLand(cell))
            {
                HideContext();
                return;
            }

            _contextCell = cell;
            var owner = match.GetOwner(cell);
            var title = owner == 0 ? _text.Get("context.freeLand") : _text.CountryName(match.GetCountry(owner));
            SetLabel("contextTitle", title);
            var attack = _root.Q<Button>("contextAttack");
            attack.SetEnabled(owner != PlayerCountryId);
            var panelWidth = _root.resolvedStyle.width;
            var panelHeight = _root.resolvedStyle.height;
            // These coordinates depend on the current pointer and panel bounds.
            _contextWheel.style.translate = new Translate(
                Mathf.Clamp(panelPosition.x - 130, 12, Mathf.Max(12, panelWidth - 272)),
                Mathf.Clamp(panelPosition.y - 85, 100, Mathf.Max(100, panelHeight - 390)), 0);
            SetVisible(_contextWheel, true);
        }

        public void HideContext()
        {
            if (_contextWheel != null)
            {
                SetVisible(_contextWheel, false);
            }

            _contextCell = -1;
        }

        public bool TryCloseSettings()
        {
            if (!_isSettingsVisible)
            {
                return false;
            }

            SetSettingsVisible(false);
            return true;
        }

        private void SetSettingsVisible(bool isVisible)
        {
            _isSettingsVisible = isVisible;
            HideContext();
            Refresh();
        }

        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (_root == null || _root.panel == null)
            {
                return false;
            }

            if (IsOverlayVisible)
            {
                return true;
            }

            var panelPosition = RuntimePanelUtils.ScreenToPanel(_root.panel,
                new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = _root.panel.Pick(panelPosition);
            return picked != null && picked != _root && picked.name != "gameRoot" && picked.name != "hud";
        }

        private void CacheElements()
        {
            _menuOverlay = Required<VisualElement>("menuOverlay");
            _pauseOverlay = Required<VisualElement>("pauseOverlay");
            _resultOverlay = Required<VisualElement>("resultOverlay");
            _settingsOverlay = Required<VisualElement>("settingsOverlay");
            _hud = Required<VisualElement>("hud");
            _mainMenu = Required<VisualElement>("mainMenu");
            _matchSetup = Required<VisualElement>("matchSetup");
            _spawnPanel = Required<VisualElement>("spawnPanel");
            _contextWheel = Required<VisualElement>("contextWheel");
            _operationsList = Required<ScrollView>("operationsList");
            _playerNameInput = Required<TextField>("playerNameInput");
            _countryCountSlider = Required<SliderInt>("countryCountSlider");
            _victorySlider = Required<SliderInt>("victorySlider");
            _attackRatioSlider = Required<SliderInt>("attackRatioSlider");
            _attackRatioInput = Required<IntegerField>("attackRatioInput");
            _troopRatioSlider = Required<SliderInt>("troopRatioSlider");
            _timerToggle = Required<Toggle>("timerToggle");
            _volumeSlider = Required<Slider>("volumeSlider");
            _dominationProgress = Required<ProgressBar>("dominationProgress");
            _victoryMarker = Required<VisualElement>("victoryMarker");
            _root.Query<Label>().ForEach(label =>
            {
                if (!string.IsNullOrEmpty(label.name))
                {
                    _labels[label.name] = label;
                }
            });
        }

        private void BindActions()
        {
            BindButton("openSetupButton", () => SetSetupVisible(true));
            BindButton("openSettingsButton", () => SetSettingsVisible(true));
            BindButton("pauseSettingsButton", () => SetSettingsVisible(true));
            BindButton("settingsBackButton", () => SetSettingsVisible(false));
            BindButton("backButton", () => SetSetupVisible(false));
            BindButton("startMatchButton", StartConfiguredMatch);
            BindButton("quitButton", _application.Quit);
            BindButton("languageRu", () => _application.SetLanguage("ru"));
            BindButton("languageEn", () => _application.SetLanguage("en"));
            BindButton("easyButton", () => SetDifficulty(AiDifficulty.Easy));
            BindButton("normalButton", () => SetDifficulty(AiDifficulty.Normal));
            BindButton("hardButton", () => SetDifficulty(AiDifficulty.Hard));
            BindButton("pauseButton", _application.TogglePause);
            BindButton("resumeButton", _application.TogglePause);
            BindButton("pauseMenuButton", _application.ReturnToMenu);
            BindButton("resultMenuButton", _application.ReturnToMenu);
            BindButton("retryButton", RetryMatch);
            BindButton("contextClose", HideContext);
            BindButton("contextAttack", SubmitContextAttack);
            for (var index = 0; index < _presets.Length; index++)
            {
                var percent = _presets[index];
                BindButton("ratio" + percent, () => SetAttackPercent(percent));
            }

            _attackRatioSlider.RegisterValueChangedCallback(change => SetAttackPercent(change.newValue));
            _attackRatioInput.RegisterValueChangedCallback(change => SetAttackPercent(change.newValue));
            _countryCountSlider.RegisterValueChangedCallback(_ => RefreshSetupLabels());
            _victorySlider.RegisterValueChangedCallback(_ => RefreshSetupLabels());
            _volumeSlider.RegisterValueChangedCallback(change => _application.SetMasterVolume(change.newValue));
            _troopRatioSlider.RegisterValueChangedCallback(change =>
            {
                _application.Submit(new MatchCommand(CommandKind.SetTroopRatio, PlayerCountryId,
                    amount: change.newValue / 100.0));
                RefreshRoleLabel();
            });
        }

        private void LoadSettings()
        {
            _playerNameInput.SetValueWithoutNotify(PlayerPrefs.GetString(PreferencePrefix + "Name", ""));
            _countryCountSlider.SetValueWithoutNotify(Mathf.Clamp(PlayerPrefs.GetInt(PreferencePrefix + "Countries", 40), 10, 60));
            _victorySlider.SetValueWithoutNotify(Mathf.Clamp(PlayerPrefs.GetInt(PreferencePrefix + "Victory", 80), 50, 100));
            _timerToggle.SetValueWithoutNotify(PlayerPrefs.GetInt(PreferencePrefix + "Timer", 0) != 0);
            _volumeSlider.SetValueWithoutNotify(_application.MasterVolume);
            _difficulty = (AiDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(PreferencePrefix + "Difficulty", 1), 0, 2);
            SetDifficulty(_difficulty);
            SetAttackPercent(25);
        }

        private void StartConfiguredMatch()
        {
            var playerName = _playerNameInput.value.Trim();
            _lastSettings = new MatchSettings
            {
                Seed = unchecked((int)DateTime.UtcNow.Ticks),
                CountryCount = Mathf.Clamp(_countryCountSlider.value, 10, 60),
                PlayerName = string.IsNullOrWhiteSpace(playerName) ? _text.Get("player.defaultName") : playerName,
                Difficulty = _difficulty,
                VictoryFraction = Mathf.Clamp(_victorySlider.value, 50, 100) / 100.0,
                TimeLimitSeconds = _timerToggle.value ? 20 * 60 : 0
            };
            PlayerPrefs.SetString(PreferencePrefix + "Name", playerName);
            PlayerPrefs.SetInt(PreferencePrefix + "Countries", _lastSettings.CountryCount);
            PlayerPrefs.SetInt(PreferencePrefix + "Victory", _victorySlider.value);
            PlayerPrefs.SetInt(PreferencePrefix + "Timer", _timerToggle.value ? 1 : 0);
            PlayerPrefs.SetInt(PreferencePrefix + "Difficulty", (int)_difficulty);
            PlayerPrefs.Save();
            BeginMatch(_lastSettings);
        }

        private void RetryMatch()
        {
            if (_lastSettings == null)
            {
                _application.ReturnToMenu();
                return;
            }

            BeginMatch(new MatchSettings
            {
                Seed = unchecked((int)DateTime.UtcNow.Ticks),
                CountryCount = _lastSettings.CountryCount,
                PlayerName = _lastSettings.PlayerName,
                Difficulty = _lastSettings.Difficulty,
                VictoryFraction = _lastSettings.VictoryFraction,
                TimeLimitSeconds = _lastSettings.TimeLimitSeconds
            });
        }

        private void BeginMatch(MatchSettings settings)
        {
            HideContext();
            SetAttackPercent(25);
            _troopRatioSlider.SetValueWithoutNotify(70);
            SetVisible(_labels["notification"], false);
            _application.StartMatch(settings);
            Refresh();
        }

        private void SetDifficulty(AiDifficulty difficulty)
        {
            _difficulty = difficulty;
            Required<Button>("easyButton").EnableInClassList("selected", difficulty == AiDifficulty.Easy);
            Required<Button>("normalButton").EnableInClassList("selected", difficulty == AiDifficulty.Normal);
            Required<Button>("hardButton").EnableInClassList("selected", difficulty == AiDifficulty.Hard);
        }

        private void SetAttackPercent(int percent)
        {
            _attackPercent = Mathf.Clamp(percent, 1, 80);
            _attackRatioSlider.SetValueWithoutNotify(_attackPercent);
            _attackRatioInput.SetValueWithoutNotify(_attackPercent);
            for (var index = 0; index < _presets.Length; index++)
            {
                Required<Button>("ratio" + _presets[index]).EnableInClassList("selected", _presets[index] == _attackPercent);
            }
        }

        private void SetSetupVisible(bool isVisible)
        {
            SetVisible(_mainMenu, !isVisible);
            SetVisible(_matchSetup, isVisible);
            RefreshSetupLabels();
        }

        private void SubmitContextAttack()
        {
            if (_contextCell >= 0)
            {
                _application.Submit(new MatchCommand(CommandKind.Attack, PlayerCountryId, _contextCell, AttackRatio));
            }

            HideContext();
        }

        private void OnApplicationStateChanged()
        {
            HideContext();
            if (_application.Match == null)
            {
                SetSetupVisible(false);
            }

            Refresh();
        }

        private void OnLocaleChanged(Locale locale)
        {
            Localize();
            Refresh();
        }

        private void Localize()
        {
            SetLabel("menuEyebrow", _text.Get("menu.eyebrow"));
            SetLabel("brandTitle", _text.Get("game.title"));
            SetLabel("menuDescription", _text.Get("menu.description"));
            SetLabel("countryNameLabel", _text.Get("menu.countryName"));
            SetLabel("setupTitle", _text.Get("setup.title"));
            SetLabel("settingsTitle", _text.Get("settings.title"));
            SetLabel("mapName", _text.Get("setup.worldMap"));
            SetLabel("difficultyLabel", _text.Get("setup.difficulty"));
            SetLabel("pauseTitle", _text.Get("pause.title"));
            SetLabel("resultEyebrow", _text.Get("result.eyebrow"));
            SetLabel("spawnTitle", _text.Get("spawn.title"));
            SetLabel("spawnHelp", _text.Get("spawn.help"));
            SetLabel("attackTitle", _text.Get("hud.attackTitle"));
            SetLabel("reserveHelp", _text.Get("hud.reserveHelp"));
            SetLabel("operationsTitle", _text.Get("hud.operations"));
            SetLabel("noOperations", _text.Get("hud.noOperations"));
            SetLabel("mapHelp", _text.Get("hud.mapHelp"));
            SetButtonText("openSetupButton", "menu.play");
            SetButtonText("openSettingsButton", "settings.title");
            SetButtonText("pauseSettingsButton", "settings.title");
            SetButtonText("settingsBackButton", "common.back");
            SetButtonText("quitButton", "menu.quit");
            SetButtonText("backButton", "common.back");
            SetButtonText("startMatchButton", "setup.start");
            SetButtonText("easyButton", "difficulty.easy");
            SetButtonText("normalButton", "difficulty.normal");
            SetButtonText("hardButton", "difficulty.hard");
            SetButtonText("pauseButton", "hud.pause");
            SetButtonText("resumeButton", "pause.resume");
            SetButtonText("pauseMenuButton", "common.mainMenu");
            SetButtonText("resultMenuButton", "common.mainMenu");
            SetButtonText("retryButton", "result.retry");
            SetButtonText("contextAttack", "context.attack");
            SetButtonText("contextClose", "common.close");
            _timerToggle.label = _text.Get("setup.timer");
            _volumeSlider.label = _text.Get("setup.volume");
            Required<Button>("languageRu").EnableInClassList("selected", _application.Language == "ru");
            Required<Button>("languageEn").EnableInClassList("selected", _application.Language == "en");
            RefreshSetupLabels();
            RefreshRoleLabel();
        }

        private void RefreshSetupLabels()
        {
            SetLabel("countryCountText", _text.Get("setup.countries", _countryCountSlider.value));
            SetLabel("countryCountHelp", _text.Get("setup.countryHelp", _countryCountSlider.value - 1));
            SetLabel("victoryText", _text.Get("setup.victory", _victorySlider.value));
        }

        private void RefreshRoleLabel()
        {
            SetLabel("roleText", _text.Get("hud.roles", _troopRatioSlider.value, 100 - _troopRatioSlider.value));
        }

        private void Refresh()
        {
            if (!_isInitialized)
            {
                return;
            }

            var match = _application.Match;
            SetVisible(_settingsOverlay, _isSettingsVisible);
            SetVisible(_menuOverlay, match == null && !_isSettingsVisible);
            SetVisible(_hud, match != null);
            SetVisible(_pauseOverlay, match != null && match.Phase == MatchPhase.Paused && !_isSettingsVisible);
            SetVisible(_resultOverlay, match != null && match.Phase == MatchPhase.Finished);
            _application.MapView.IsInputBlocked = IsOverlayVisible;
            if (match == null)
            {
                return;
            }

            SetVisible(_spawnPanel, match.Phase == MatchPhase.Spawn);
            if (match.Phase == MatchPhase.Spawn)
            {
                SetLabel("spawnCountdown", _text.Get("spawn.countdown", Math.Ceiling(match.SpawnSecondsRemaining)));
            }

            var player = match.GetCountry(PlayerCountryId);
            RefreshCountry(player, match);
            RefreshOperations(match);
            if (Time.unscaledTime > _notificationExpiresAt)
            {
                SetVisible(_labels["notification"], false);
            }
            if (match.Phase == MatchPhase.Finished)
            {
                RefreshResult(match, player);
            }
        }

        private void RefreshCountry(CountryState player, IMatchSimulation match)
        {
            if (player == null)
            {
                return;
            }

            var leader = player;
            for (var index = 0; index < match.Countries.Count; index++)
            {
                var country = match.Countries[index];
                if (country.LandTiles > leader.LandTiles)
                {
                    leader = country;
                }
            }

            var denominator = Math.Max(1, match.Map.TotalLandTiles);
            var fraction = player.LandTiles * 100.0 / denominator;
            SetLabel("countryName", player.Name);
            SetLabel("populationText", _text.Get("hud.population", player.HomePopulation, player.MaxPopulation));
            SetLabel("growthText", _text.Get("hud.growth", player.GrowthPerSecond));
            var growthRatio = player.MaxPopulation > 0 ? player.HomePopulation / player.MaxPopulation : 0;
            _labels["growthText"].EnableInClassList("positive", growthRatio >= 0.4 && growthRatio <= 0.55);
            SetLabel("goldText", _text.Get("hud.gold", player.Gold, player.IncomePerSecond));
            SetLabel("troopsText", _text.Get("hud.troops", player.DefendingTroops, player.DeployedTroops, player.ReturningTroops));
            SetLabel("leaderText", _text.Get("hud.leader", _text.CountryName(leader), leader.LandTiles * 100.0 / denominator));
            SetLabel("dominationText", _text.Get("hud.domination", fraction, match.Settings.VictoryFraction * 100));
            _dominationProgress.SetValueWithoutNotify((float)(leader.LandTiles * 100.0 / denominator));
            // The threshold marker scales with the live progress-bar width.
            _victoryMarker.style.translate = new Translate(
                (float)(Math.Max(0, _dominationProgress.contentRect.width - 2) * match.Settings.VictoryFraction), 0, 0);
            var isAtWar = false;
            for (var index = 0; index < match.Operations.Count; index++)
            {
                var operation = match.Operations[index];
                if (operation.TargetCountryId > 0 && operation.Phase == OperationPhase.Advancing &&
                    (operation.CountryId == PlayerCountryId || operation.TargetCountryId == PlayerCountryId))
                {
                    isAtWar = true;
                    break;
                }
            }

            SetLabel("stateIndicator", _text.Get(isAtWar ? "hud.atWar" : "hud.protected"));
            _labels["stateIndicator"].EnableInClassList("at-war", isAtWar);
            var seconds = match.Settings.TimeLimitSeconds > 0
                ? Math.Max(0, match.Settings.TimeLimitSeconds - match.ElapsedSeconds)
                : match.ElapsedSeconds;
            SetLabel("matchClock", _text.Get("hud.clock", (int)seconds / 60, (int)seconds % 60));
        }

        private void RefreshOperations(IMatchSimulation match)
        {
            var visibleCount = 0;
            for (var index = 0; index < match.Operations.Count; index++)
            {
                var operation = match.Operations[index];
                if (operation.CountryId != PlayerCountryId)
                {
                    continue;
                }

                if (visibleCount == _operationRows.Count)
                {
                    var row = new OperationRow();
                    _operationRows.Add(row);
                    _operationsList.Add(row.Root);
                    var capturedRow = row;
                    row.Recall.clicked += () => _application.Submit(new MatchCommand(CommandKind.Recall,
                        PlayerCountryId, objectId: capturedRow.OperationId));
                }

                var currentRow = _operationRows[visibleCount++];
                currentRow.OperationId = operation.Id;
                SetVisible(currentRow.Root, true);
                var targetName = operation.TargetCountryId == 0
                    ? _text.Get("context.freeLand")
                    : _text.CountryName(match.GetCountry(operation.TargetCountryId));
                currentRow.Text.text = _text.Get(operation.Phase == OperationPhase.Returning
                    ? "operation.returning" : "operation.advancing", targetName, operation.Troops);
                currentRow.Recall.text = _text.Get("operation.recall");
                currentRow.Recall.SetEnabled(operation.Phase == OperationPhase.Advancing && match.Phase == MatchPhase.Playing);
            }

            for (var index = visibleCount; index < _operationRows.Count; index++)
            {
                SetVisible(_operationRows[index].Root, false);
            }

            SetVisible(_labels["noOperations"], visibleCount == 0);
        }

        private void RefreshResult(IMatchSimulation match, CountryState player)
        {
            if (match.Result == null)
            {
                return;
            }

            var title = match.Result.IsDraw ? "result.draw"
                : match.Result.WinnerCountryId == PlayerCountryId ? "result.victory" : "result.defeat";
            SetLabel("resultTitle", _text.Get(title));
            var seconds = (int)match.Result.DurationSeconds;
            SetLabel("resultStats", _text.Get("result.stats", seconds / 60, seconds % 60,
                player.PeakLandFraction * 100, player.EarnedGold));
        }

        private T Required<T>(string elementName) where T : VisualElement
        {
            return _root.Q<T>(elementName) ?? throw new InvalidOperationException("Missing UI element: " + elementName);
        }

        private void BindButton(string elementName, Action action)
        {
            Required<Button>(elementName).clicked += action;
        }

        private void SetButtonText(string elementName, string key)
        {
            Required<Button>(elementName).text = _text.Get(key);
        }

        private void SetLabel(string elementName, string text)
        {
            _labels[elementName].text = text;
        }

        private static void SetVisible(VisualElement element, bool isVisible)
        {
            element.EnableInClassList("hidden", !isVisible);
        }

        private void OnDestroy()
        {
            _refreshSchedule?.Pause();
            if (_application != null)
            {
                _application.StateChanged -= OnApplicationStateChanged;
            }

            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        }

        private sealed class OperationRow
        {
            public OperationRow()
            {
                Root = new VisualElement();
                Root.AddToClassList("row");
                Root.AddToClassList("operation-row");
                Text = new Label();
                Text.AddToClassList("operation-text");
                Recall = new Button();
                Recall.AddToClassList("recall");
                Root.Add(Text);
                Root.Add(Recall);
            }

            public VisualElement Root { get; }
            public Label Text { get; }
            public Button Recall { get; }
            public int OperationId { get; set; }
        }
    }
}
