using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MapClash
{
    public sealed class WorldMapView : MonoBehaviour
    {
        private const int BlockSize = 64;
        private const float BaseCellWorldSize = 0.1f;
        private const float DragThresholdPixels = 6;
        [SerializeField] private Camera _camera;
        [SerializeField] private Shader _mapShader;
        [SerializeField] private TMP_FontAsset _labelFont;
        [SerializeField] private MapCameraConfig _cameraConfig;
        private WorldMapData _map;
        private IMatchSimulation _match;
        private Texture2D _terrainTexture;
        private Texture2D _ownershipTexture;
        private Material _material;
        private Sprite _sprite;
        private Color32[] _owners;
        private Color32[] _block;
        private bool[] _dirtyBlocks;
        private int _blocksWide;
        private float _worldWidth;
        private float _worldHeight;
        private float _cellWorldSize;
        private float _targetZoom;
        private Vector3 _targetPosition;
        private readonly Color[] _colors = new Color[64];
        private readonly GameText _text = new GameText();
        private TextMeshPro[] _labels;
        private readonly HashSet<int>[] _countryCells = new HashSet<int>[64];
        private readonly Vector3[] _componentCenters = new Vector3[64];
        private readonly bool[] _centerDirty = new bool[64];
        private bool[] _componentVisited;
        private int[] _componentQueue;
        private readonly int[] _centerCount = new int[64];
        private float _labelTimer;
        private bool _isTerrainShown;
        private bool _isLeftPointerHeld;
        private bool _isLeftDragging;
        private Vector2 _leftPressPosition;
        private readonly Dictionary<int, TextMeshPro> _operationLabels = new Dictionary<int, TextMeshPro>();
        private readonly List<int> _finishedOperationIds = new List<int>();

        public int HoveredCell { get; private set; } = -1;
        public bool IsInputBlocked { get; set; }
        public bool WasMapClicked { get; private set; }

        public void Initialize(WorldMapData map)
        {
            if (_camera == null || _mapShader == null || _labelFont == null || _cameraConfig == null)
            {
                Debug.LogError("MapClash: WorldMapView requires camera, shader, label font and camera config.", this);
                enabled = false;
                return;
            }

            _map = map;
            _componentVisited = new bool[map.Terrain.Length];
            _componentQueue = new int[map.Terrain.Length];
            _cellWorldSize = (float)map.CellScale * BaseCellWorldSize;
            _worldWidth = map.Width * _cellWorldSize;
            _worldHeight = map.Height * _cellWorldSize;
            _camera.backgroundColor = new Color32(22, 37, 48, 255);
            _terrainTexture = NewTexture(map.Width, map.Height);
            _ownershipTexture = NewTexture(map.Width, map.Height);
            var pixels = new Color32[map.Terrain.Length];
            _owners = new Color32[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = TerrainColor(map.Terrain[i]);
                _owners[i] = new Color32(0, 0, 0, 255);
            }

            _terrainTexture.SetPixels32(pixels);
            _terrainTexture.Apply(false);
            _ownershipTexture.SetPixels32(_owners);
            _ownershipTexture.Apply(false);
            _material = new Material(_mapShader);
            _material.SetTexture("_Ownership", _ownershipTexture);
            _colors[0] = new Color32(192, 192, 170, 255);
            _colors[1] = new Color32(64, 196, 175, 255);
            for (var i = 2; i < _colors.Length; i++)
            {
                _colors[i] = Color.HSVToRGB(Mathf.Repeat(i * 0.618033989f, 1), 0.40f + i % 3 * 0.10f, 0.81f + i % 2 * 0.12f);
            }

            _material.SetColorArray("_CountryColors", _colors);
            // Water is transparent in the source mask; a tight mesh would cut off coastlines.
            _sprite = Sprite.Create(_terrainTexture, new Rect(0, 0, map.Width, map.Height),
                new Vector2(0.5f, 0.5f), 1 / _cellWorldSize, 0, SpriteMeshType.FullRect);
            for (var copy = -1; copy <= 1; copy++)
            {
                var surface = new GameObject("World surface " + copy);
                surface.transform.SetParent(transform, false);
                surface.transform.localPosition = new Vector3(copy * _worldWidth, 0, 0);
                var renderer = surface.AddComponent<SpriteRenderer>();
                renderer.sprite = _sprite;
                renderer.sharedMaterial = _material;
            }

            _blocksWide = (map.Width + BlockSize - 1) / BlockSize;
            _dirtyBlocks = new bool[_blocksWide * ((map.Height + BlockSize - 1) / BlockSize)];
            _block = new Color32[BlockSize * BlockSize];
            ResetCamera();
        }

        public void Bind(IMatchSimulation match)
        {
            foreach (var label in _operationLabels.Values)
            {
                Destroy(label.gameObject);
            }
            _operationLabels.Clear();
            for (var i = 1; i < _countryCells.Length; i++)
            {
                _countryCells[i] = new HashSet<int>();
            }
            Array.Clear(_centerCount, 0, _centerCount.Length);
            _match = match;
            Array.Clear(_owners, 0, _owners.Length);
            _ownershipTexture.SetPixels32(_owners);
            _ownershipTexture.Apply(false);
            if (_labels != null)
            {
                foreach (var label in _labels)
                {
                    Destroy(label.gameObject);
                }
            }

            _labels = match == null ? null : new TextMeshPro[match.Countries.Count];
            if (match != null)
            {
                for (var i = 0; i < _labels.Length; i++)
                {
                    var labelObject = new GameObject("Country label " + match.Countries[i].Id);
                    labelObject.transform.SetParent(transform, false);
                    var label = labelObject.AddComponent<TextMeshPro>();
                    label.font = _labelFont;
                    label.alignment = TextAlignmentOptions.Center;
                    label.fontSize = 36;
                    label.fontStyle = FontStyles.Bold;
                    label.color = new Color32(248, 248, 236, 255);
                    label.outlineWidth = 0.3f;
                    label.outlineColor = new Color32(18, 20, 24, 255);
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    label.sortingOrder = 10;
                    _labels[i] = label;
                }
            }

            ResetCamera();
            _material.SetFloat("_MenuDim", match == null ? 1 : 0);
            RefreshOwnership();
        }

        public void RefreshOwnership()
        {
            if (_match == null)
            {
                return;
            }

            var changedCells = _match.ChangedCells;
            for (var i = 0; i < changedCells.Count; i++)
            {
                var cell = changedCells[i];
                var owner = _match.GetOwner(cell);
                var previousOwner = _owners[cell].r;
                if (owner != previousOwner)
                {
                    UpdateCountryCenter(previousOwner, cell, -1);
                    UpdateCountryCenter(owner, cell, 1);
                }
                _owners[cell] = new Color32((byte)owner, 0, 0, 255);
                _dirtyBlocks[(cell / _map.Width / BlockSize) * _blocksWide + cell % _map.Width / BlockSize] = true;
            }

            var isDirty = false;
            for (var blockIndex = 0; blockIndex < _dirtyBlocks.Length; blockIndex++)
            {
                if (!_dirtyBlocks[blockIndex])
                {
                    continue;
                }

                var x = blockIndex % _blocksWide * BlockSize;
                var y = blockIndex / _blocksWide * BlockSize;
                var width = Math.Min(BlockSize, _map.Width - x);
                var height = Math.Min(BlockSize, _map.Height - y);
                for (var row = 0; row < height; row++)
                {
                    Array.Copy(_owners, (y + row) * _map.Width + x, _block, row * width, width);
                }

                // The world grid is divisible by 64; small test maps use SetPixel below.
                if (width == BlockSize && height == BlockSize)
                {
                    _ownershipTexture.SetPixels32(x, y, width, height, _block);
                }
                else
                {
                    for (var row = 0; row < height; row++)
                    {
                        for (var column = 0; column < width; column++)
                        {
                            _ownershipTexture.SetPixel(x + column, y + row, _block[row * width + column]);
                        }
                    }
                }

                _dirtyBlocks[blockIndex] = false;
                isDirty = true;
            }

            if (isDirty)
            {
                _ownershipTexture.Apply(false);
            }
        }

        public void Navigate(float deltaSeconds)
        {
            WasMapClicked = false;
            if (_map == null)
            {
                return;
            }

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            var screenPosition = mouse == null ? Vector2.zero : mouse.position.ReadValue();
            var worldPosition = _camera.ScreenToWorldPoint(screenPosition);
            HoveredCell = WorldToCell(worldPosition);
            if (IsInputBlocked || mouse == null)
            {
                _isLeftPointerHeld = false;
                _isLeftDragging = false;
            }
            if (!IsInputBlocked)
            {
                if (keyboard != null)
                {
                    var move = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                        (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                    _targetPosition += (Vector3)(move * (_targetZoom * _cameraConfig.KeyboardPanSpeed * deltaSeconds));
                }

                var leftDrag = Vector2.zero;
                if (mouse != null)
                {
                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        _isLeftPointerHeld = true;
                        _isLeftDragging = false;
                        _leftPressPosition = screenPosition;
                    }

                    if (_isLeftPointerHeld && mouse.leftButton.isPressed)
                    {
                        var displacement = screenPosition - _leftPressPosition;
                        if (!_isLeftDragging && displacement.sqrMagnitude >= DragThresholdPixels * DragThresholdPixels)
                        {
                            _isLeftDragging = true;
                            leftDrag = displacement;
                        }
                        else if (_isLeftDragging)
                        {
                            leftDrag = mouse.delta.ReadValue();
                        }
                    }

                    if (_isLeftPointerHeld && mouse.leftButton.wasReleasedThisFrame)
                    {
                        WasMapClicked = !_isLeftDragging && (screenPosition - _leftPressPosition).sqrMagnitude <
                            DragThresholdPixels * DragThresholdPixels;
                        _isLeftPointerHeld = false;
                        _isLeftDragging = false;
                    }
                }

                if (mouse != null && (mouse.middleButton.isPressed || _isLeftDragging))
                {
                    var delta = _isLeftDragging ? leftDrag : mouse.delta.ReadValue();
                    var drag = delta * (_camera.orthographicSize * 2 / Math.Max(1, _camera.pixelHeight)) * _cameraConfig.MousePanSpeed;
                    _targetPosition -= new Vector3(drag.x, drag.y, 0);
                }

                if (mouse != null && (keyboard == null || (!keyboard.leftShiftKey.isPressed && !keyboard.rightShiftKey.isPressed)))
                {
                    var scroll = mouse.scroll.ReadValue().y;
                    // Input System defaults to normalized scroll. Legacy Windows mode uses 120 units per notch.
                    if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange &&
                        (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor))
                    {
                        scroll /= 120;
                    }

                    _targetZoom = Mathf.Clamp(_targetZoom * Mathf.Exp(-scroll * _cameraConfig.ZoomSensitivity), _cameraConfig.MinimumZoom, MaxZoom());
                }
            }

            var isTerrainShown = keyboard != null && keyboard.spaceKey.isPressed;
            if (_isTerrainShown != isTerrainShown)
            {
                _isTerrainShown = isTerrainShown;
                _material.SetFloat("_TerrainView", isTerrainShown ? 1 : 0);
            }

            _targetPosition.x = Mathf.Repeat(_targetPosition.x + _worldWidth * 0.5f, _worldWidth) - _worldWidth * 0.5f;
            _targetPosition.y = Mathf.Clamp(_targetPosition.y, -_worldHeight * 0.5f, _worldHeight * 0.5f);
            var current = _camera.transform.position;
            if (Mathf.Abs(current.x - _targetPosition.x) > _worldWidth * 0.5f)
            {
                current.x += Mathf.Sign(_targetPosition.x - current.x) * _worldWidth;
            }

            var panBlend = _cameraConfig.PanResponse == 0 ? 1 : 1 - Mathf.Exp(-_cameraConfig.PanResponse * deltaSeconds);
            var zoomBlend = _cameraConfig.ZoomResponse == 0 ? 1 : 1 - Mathf.Exp(-_cameraConfig.ZoomResponse * deltaSeconds);
            _camera.transform.position = Vector3.Lerp(current, _targetPosition, panBlend);
            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _targetZoom, zoomBlend);
            _labelTimer += deltaSeconds;
            if (_labelTimer >= 0.25f)
            {
                _labelTimer = 0;
                RefreshLabels();
            }
        }

        public int WorldToCell(Vector3 position)
        {
            var x = Mathf.FloorToInt(Mathf.Repeat(position.x + _worldWidth * 0.5f, _worldWidth) / _cellWorldSize);
            var y = Mathf.FloorToInt((position.y + _worldHeight * 0.5f) / _cellWorldSize);
            return y < 0 || y >= _map.Height ? -1 : y * _map.Width + Math.Min(x, _map.Width - 1);
        }

        public void FocusCell(int cell)
        {
            if (cell < 0 || cell >= _map.Terrain.Length)
            {
                return;
            }

            _targetPosition = CellPosition(cell);
            _targetPosition.z = -10;
            _targetZoom = Mathf.Clamp(_cameraConfig.FocusZoom, _cameraConfig.MinimumZoom, MaxZoom());
        }

        private void ResetCamera()
        {
            _isLeftPointerHeld = false;
            _isLeftDragging = false;
            WasMapClicked = false;
            _targetPosition = new Vector3(0, 0, -10);
            _targetZoom = MaxZoom();
            _camera.transform.position = _targetPosition;
            _camera.orthographic = true;
            _camera.orthographicSize = _targetZoom;
        }

        private float MaxZoom()
        {
            var overview = Mathf.Max(_worldHeight * 0.59f, _worldWidth / (Mathf.Max(0.5f, _camera.aspect) * 2) * 1.05f);
            return Mathf.Max(_cameraConfig.MinimumZoom, overview * _cameraConfig.OverviewZoomMultiplier);
        }

        private void RefreshLabels()
        {
            if (_match == null || _labels == null)
            {
                return;
            }

            for (var i = 0; i < _labels.Length; i++)
            {
                var country = _match.Countries[i];
                var label = _labels[i];
                var isVisible = country.IsAlive && country.CapitalCell >= 0 && (_match.Phase == MatchPhase.Spawn || country.LandTiles > 0);
                label.gameObject.SetActive(isVisible);
                if (!isVisible)
                {
                    continue;
                }

                var position = CellPosition(country.CapitalCell);
                var id = country.Id;
                if (_match.Phase != MatchPhase.Spawn && _centerCount[id] > 0)
                {
                    if (_centerDirty[id])
                    {
                        RefreshLargestComponentCenter(id);
                    }
                    position = _componentCenters[id];
                }
                var cameraX = _camera.transform.position.x;
                position.x += Mathf.Round((cameraX - position.x) / _worldWidth) * _worldWidth;
                label.transform.position = position + new Vector3(0, 0, -1);
                label.transform.localScale = Vector3.one * (_camera.orthographicSize * 0.016f);
                label.text = _text.CountryName(country) + "\n" + country.HomePopulation.ToString("N0");
            }

            RefreshOperationLabels();
        }

        private void UpdateCountryCenter(int countryId, int cell, int change)
        {
            if (countryId == 0)
            {
                return;
            }

            if (change > 0) { _countryCells[countryId].Add(cell); }
            else { _countryCells[countryId].Remove(cell); }
            _centerCount[countryId] += change;
            _centerDirty[countryId] = true;
        }

        private void RefreshLargestComponentCenter(int countryId)
        {
            var cells = _countryCells[countryId];
            foreach (var cell in cells) { _componentVisited[cell] = false; }
            var tail = 0;
            var largestStart = 0;
            var largestEnd = 0;
            foreach (var cell in cells)
            {
                if (_componentVisited[cell]) { continue; }
                var start = tail;
                _componentVisited[cell] = true;
                _componentQueue[tail++] = cell;
                for (var head = start; head < tail; head++)
                {
                    for (var direction = 0; direction < 4; direction++)
                    {
                        var neighbour = _map.GetNeighbour(_componentQueue[head], direction);
                        if (neighbour < 0 || _componentVisited[neighbour] || !cells.Contains(neighbour)) { continue; }
                        _componentVisited[neighbour] = true;
                        _componentQueue[tail++] = neighbour;
                    }
                }
                if (tail - start > largestEnd - largestStart)
                {
                    largestStart = start;
                    largestEnd = tail;
                }
            }

            double sin = 0, cos = 0, y = 0;
            for (var i = largestStart; i < largestEnd; i++)
            {
                var point = CellPosition(_componentQueue[i]);
                var angle = point.x / _worldWidth * 2 * Math.PI;
                sin += Math.Sin(angle);
                cos += Math.Cos(angle);
                y += point.y;
            }
            var center = new Vector3((float)(Math.Atan2(sin, cos) / (2 * Math.PI) * _worldWidth),
                (float)(y / Math.Max(1, largestEnd - largestStart)), -1);
            var closestDistance = float.MaxValue;
            for (var i = largestStart; i < largestEnd; i++)
            {
                var point = CellPosition(_componentQueue[i]);
                var dx = Mathf.Repeat(point.x - center.x + _worldWidth / 2, _worldWidth) - _worldWidth / 2;
                var distance = dx * dx + (point.y - center.y) * (point.y - center.y);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    _componentCenters[countryId] = point;
                }
            }
            _centerDirty[countryId] = false;
        }

        private void RefreshOperationLabels()
        {
            _finishedOperationIds.Clear();
            foreach (var pair in _operationLabels)
            {
                var isAdvancing = false;
                for (var i = 0; i < _match.Operations.Count; i++)
                {
                    var operation = _match.Operations[i];
                    if (operation.Id == pair.Key && operation.Phase == OperationPhase.Advancing && operation.Troops > 0)
                    {
                        isAdvancing = true;
                        break;
                    }
                }

                if (!isAdvancing)
                {
                    _finishedOperationIds.Add(pair.Key);
                }
            }

            for (var i = 0; i < _finishedOperationIds.Count; i++)
            {
                var id = _finishedOperationIds[i];
                Destroy(_operationLabels[id].gameObject);
                _operationLabels.Remove(id);
            }

            for (var i = 0; i < _match.Operations.Count; i++)
            {
                var operation = _match.Operations[i];
                if (operation.Phase != OperationPhase.Advancing || operation.Troops <= 0)
                {
                    continue;
                }

                if (!_operationLabels.TryGetValue(operation.Id, out var label))
                {
                    var labelObject = new GameObject("Operation label " + operation.Id);
                    labelObject.transform.SetParent(transform, false);
                    label = labelObject.AddComponent<TextMeshPro>();
                    label.font = _labelFont;
                    label.alignment = TextAlignmentOptions.Center;
                    label.fontSize = 32;
                    label.outlineWidth = 0.25f;
                    label.outlineColor = new Color32(18, 20, 24, 255);
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    label.sortingOrder = 11;
                    label.color = new Color32(248, 248, 236, 255);
                    _operationLabels.Add(operation.Id, label);
                }

                var position = CellPosition(operation.FrontCell);
                position.x += Mathf.Round((_camera.transform.position.x - position.x) / _worldWidth) * _worldWidth;
                label.transform.position = position + new Vector3(0, -_camera.orthographicSize * 0.04f, -1);
                label.transform.localScale = Vector3.one * (_camera.orthographicSize * 0.016f);
                if (operation.TargetCountryId > 0)
                {
                    var defender = _match.GetCountry(operation.TargetCountryId);
                    label.text = _text.Get("operation.front", Math.Ceiling(operation.Troops),
                        Math.Ceiling(defender?.DefendingTroops ?? 0));
                }
                else
                {
                    label.SetText("{0:0}", (float)Math.Ceiling(operation.Troops));
                }
            }
        }

        private Vector3 CellPosition(int cell)
        {
            return new Vector3((cell % _map.Width + 0.5f) * _cellWorldSize - _worldWidth * 0.5f,
                (cell / _map.Width + 0.5f) * _cellWorldSize - _worldHeight * 0.5f, -1);
        }

        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Point,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp
            };
        }

        private static Color32 TerrainColor(byte terrain)
        {
            switch (terrain)
            {
                case 1: return new Color32(193, 197, 171, 255);
                case 2: return new Color32(159, 164, 139, 255);
                case 3: return new Color32(114, 127, 120, 255);
                default: return new Color32(24, 43, 55, 0);
            }
        }

        private void OnDestroy()
        {
            if (_terrainTexture != null) { Destroy(_terrainTexture); }
            if (_ownershipTexture != null) { Destroy(_ownershipTexture); }
            if (_material != null) { Destroy(_material); }
            if (_sprite != null) { Destroy(_sprite); }
        }
    }
}
