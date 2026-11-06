using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;
using VContainer;

namespace MapClash
{
    public sealed class GameApplication : MonoBehaviour
    {
        [SerializeField] private WorldMapView _mapView;
        [SerializeField] private GameUi _ui;
        [SerializeField] private UIDocument _document;
        private MatchSessionFactory _matchFactory;
        private WorldMapData _map;
        private MatchSession _session;
        private MatchPhase _previousPhase;
        private bool _isReady;

        public event Action StateChanged;

        public IMatchSimulation Match => _session?.Simulation;
        public WorldMapView MapView => _mapView;
        public bool IsInMenu => Match == null;
        public string Language { get; private set; } = "ru";
        public float MasterVolume { get; private set; } = 0.7f;

        [Inject]
        public void Construct(MatchSessionFactory matchFactory, WorldMapData map)
        {
            _matchFactory = matchFactory;
            _map = map;
        }

        private IEnumerator Start()
        {
            if (_matchFactory == null || _mapView == null || _ui == null || _document == null)
            {
                Debug.LogError("MapClash: missing application injection or scene references.", this);
                enabled = false;
                yield break;
            }

            yield return LocalizationSettings.InitializationOperation;
            yield return LocalizationSettings.StringDatabase.GetTableAsync("Game", LocalizationSettings.AvailableLocales.GetLocale("en"));
            yield return LocalizationSettings.StringDatabase.GetTableAsync("Game", LocalizationSettings.AvailableLocales.GetLocale("ru"));
            Language = PlayerPrefs.GetString("MapClash.Language", "ru");
            ApplyLanguage();
            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("MapClash.Volume", 0.7f));
            AudioListener.volume = MasterVolume;
            _mapView.Initialize(_map);
            _ui.Initialize(this, _document);
            _isReady = true;
            Debug.Log("MapClash: application ready.", this);
        }

        public void StartMatch(MatchSettings settings)
        {
            if (!_isReady)
            {
                return;
            }

            _session?.Dispose();
            _session = _matchFactory.Create(settings);
            _mapView.Bind(Match);
            _previousPhase = Match.Phase;
            StateChanged?.Invoke();
            Debug.Log("MapClash: match created; countries=" + settings.CountryCount + "; seed=" + settings.Seed, this);
        }

        public void ReturnToMenu()
        {
            _session?.Dispose();
            _session = null;
            _mapView.Bind(null);
            StateChanged?.Invoke();
        }

        public void Submit(MatchCommand command)
        {
            Match?.Enqueue(command);
        }

        public void TogglePause()
        {
            if (Match == null || Match.Phase == MatchPhase.Finished)
            {
                return;
            }

            Match.SetPaused(Match.Phase != MatchPhase.Paused);
            StateChanged?.Invoke();
        }

        public void SetLanguage(string code)
        {
            Language = code == "en" ? "en" : "ru";
            ApplyLanguage();
            PlayerPrefs.SetString("MapClash.Language", Language);
            PlayerPrefs.Save();
            StateChanged?.Invoke();
        }

        public void SetMasterVolume(float value)
        {
            MasterVolume = Mathf.Clamp01(value);
            AudioListener.volume = MasterVolume;
            PlayerPrefs.SetFloat("MapClash.Volume", MasterVolume);
            PlayerPrefs.Save();
        }

        public void Quit()
        {
            Application.Quit();
        }

        private void Update()
        {
            if (!_isReady)
            {
                return;
            }

            ReadInput();
            if (Match == null)
            {
                return;
            }

            Match.Advance(Time.unscaledDeltaTime);
            if (Match.Events.Count > 0)
            {
                _ui.PresentEvents(Match.Events);
            }

            _mapView.RefreshOwnership();
            if (_previousPhase != Match.Phase)
            {
                if (Match.Phase == MatchPhase.Playing && _previousPhase == MatchPhase.Spawn)
                {
                    _mapView.FocusCell(Match.GetCountry(1).CapitalCell);
                }

                _previousPhase = Match.Phase;
                StateChanged?.Invoke();
            }
        }

        private void ReadInput()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (!_ui.TryCloseSettings())
                {
                    TogglePause();
                }
            }

            var isPointerOverUi = mouse != null && _ui.IsPointerOverUi(mouse.position.ReadValue());
            _mapView.IsInputBlocked = _ui.IsOverlayVisible || isPointerOverUi;
            _mapView.Navigate(Time.unscaledDeltaTime);
            if (Match == null || _ui.IsOverlayVisible || mouse == null)
            {
                return;
            }

            if (keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame) { _ui.CycleAttackRatio(-1); }
                if (keyboard.eKey.wasPressedThisFrame) { _ui.CycleAttackRatio(1); }
                if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                {
                    if (mouse.scroll.ReadValue().y != 0) { _ui.CycleAttackRatio(mouse.scroll.ReadValue().y > 0 ? 1 : -1); }
                }
            }

            if (isPointerOverUi)
            {
                return;
            }

            var cell = _mapView.HoveredCell;
            if (cell < 0)
            {
                return;
            }

            if (_mapView.WasMapClicked)
            {
                var kind = Match.Phase == MatchPhase.Spawn ? CommandKind.ChooseSpawn : CommandKind.Attack;
                Submit(new MatchCommand(kind, 1, cell, _ui.AttackRatio));
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                var pointer = mouse.position.ReadValue();
                var panelPosition = RuntimePanelUtils.ScreenToPanel(_document.rootVisualElement.panel, new Vector2(pointer.x, Screen.height - pointer.y));
                _ui.ShowContext(cell, panelPosition);
            }
        }

        private void ApplyLanguage()
        {
            var locale = LocalizationSettings.AvailableLocales.GetLocale(Language);
            if (locale != null)
            {
                LocalizationSettings.SelectedLocale = locale;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && Match != null && (Match.Phase == MatchPhase.Playing || Match.Phase == MatchPhase.Spawn))
            {
                Match.SetPaused(true);
                StateChanged?.Invoke();
            }
        }

        private void OnDestroy()
        {
            _session?.Dispose();
            _session = null;
            StateChanged = null;
        }
    }
}
