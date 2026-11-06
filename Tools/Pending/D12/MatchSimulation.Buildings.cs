using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MapClash
{
    public sealed partial class MatchSimulation
    {
        private readonly List<BuildingState> _buildings = new List<BuildingState>();
        private readonly Dictionary<int, BuildingState> _buildingByCell = new Dictionary<int, BuildingState>();
        private readonly Dictionary<int, BuildingState> _buildingById = new Dictionary<int, BuildingState>();
        private ReadOnlyCollection<BuildingState> _buildingView;
        private List<BuildingState>[] _defensePostsByCountry;
        private int[,] _buildingCounts;
        private int[] _cityLevels;
        private int _nextBuildingId = 1;

        public IReadOnlyList<BuildingState> Buildings => _buildingView;

        private void InitializeBuildings()
        {
            _buildingView = _buildings.AsReadOnly();
            _buildingCounts = new int[_countries.Length + 1, 6];
            _cityLevels = new int[_countries.Length + 1];
            _defensePostsByCountry = new List<BuildingState>[_countries.Length + 1];
            for (var countryId = 1; countryId <= _countries.Length; countryId++)
            {
                _defensePostsByCountry[countryId] = new List<BuildingState>();
            }
        }

        private void DisposeBuildings()
        {
            _buildings.Clear();
            _buildingByCell.Clear();
            _buildingById.Clear();
            Array.Clear(_buildingCounts, 0, _buildingCounts.Length);
            Array.Clear(_cityLevels, 0, _cityLevels.Length);
            for (var countryId = 1; countryId <= _countries.Length; countryId++)
            {
                _defensePostsByCountry[countryId].Clear();
            }
        }

        private void TryBuild(MatchCommand command)
        {
            var kind = (BuildingKind)command.ObjectId;
            var country = GetCountry(command.CountryId);
            if (country == null || !country.IsAlive || !IsBuildingKind(kind) ||
                !CanOwnBuildingAt(command.CountryId, command.TargetCell))
            {
                AddBuildingFailure("building.invalidPlacement", command.CountryId);
                return;
            }

            if (_buildingByCell.TryGetValue(command.TargetCell, out var existing))
            {
                if (existing.CountryId == command.CountryId && existing.Kind == kind)
                {
                    TryUpgrade(new MatchCommand(CommandKind.Upgrade, command.CountryId, objectId: existing.Id));
                }
                else
                {
                    AddBuildingFailure("building.invalidPlacement", command.CountryId);
                }

                return;
            }

            if ((kind == BuildingKind.Port && !IsCoastalLand(command.TargetCell)) ||
                !HasBuildingSpacing(command.TargetCell, -1))
            {
                AddBuildingFailure("building.invalidPlacement", command.CountryId);
                return;
            }

            var price = BuildingPrice(command.CountryId, kind, 1);
            if (country.Gold < price)
            {
                AddBuildingFailure("building.insufficientGold", command.CountryId);
                return;
            }

            var building = new BuildingState
            {
                Id = _nextBuildingId++,
                CountryId = command.CountryId,
                Cell = command.TargetCell,
                Kind = kind,
                Level = 1,
                CooldownUntilTick = 0
            };
            country.Gold -= price;
            _buildings.Add(building);
            _buildingByCell.Add(building.Cell, building);
            _buildingById.Add(building.Id, building);
            AddBuildingToCountry(building, country.Id);
            RefreshPopulationLimit(country);
            _events.Add(new MatchEvent("building.constructed", country.Id));
        }

        private void TryUpgrade(MatchCommand command)
        {
            var country = GetCountry(command.CountryId);
            if (country == null || !country.IsAlive ||
                !_buildingById.TryGetValue(command.ObjectId, out var building) ||
                building.CountryId != command.CountryId || !CanOwnBuildingAt(command.CountryId, building.Cell) ||
                !IsEconomicBuilding(building.Kind))
            {
                AddBuildingFailure("building.invalidUpgrade", command.CountryId);
                return;
            }

            if (building.Level >= _balance.MaxEconomicBuildingLevel)
            {
                AddBuildingFailure("building.maxLevel", command.CountryId);
                return;
            }

            if ((building.Kind == BuildingKind.Port && !IsCoastalLand(building.Cell)) ||
                !HasBuildingSpacing(building.Cell, building.Id))
            {
                AddBuildingFailure("building.invalidPlacement", command.CountryId);
                return;
            }

            var price = BuildingPrice(command.CountryId, building.Kind, building.Level + 1);
            if (country.Gold < price)
            {
                AddBuildingFailure("building.insufficientGold", command.CountryId);
                return;
            }

            country.Gold -= price;
            building.Level++;
            if (building.Kind == BuildingKind.City)
            {
                _cityLevels[country.Id]++;
            }

            RefreshPopulationLimit(country);
            _events.Add(new MatchEvent("building.upgraded", country.Id));
        }

        private double BuildingPrice(int countryId, BuildingKind kind, int nextLevel)
        {
            switch (kind)
            {
                case BuildingKind.City:
                case BuildingKind.Factory:
                case BuildingKind.Port:
                    return _balance.EconomicBuildingBasePrice * Math.Pow(_balance.EconomicBuildingPriceMultiplier, nextLevel - 1);
                case BuildingKind.DefensePost:
                    return Math.Min(_balance.DefensePostMaxPrice,
                        _balance.DefensePostFirstPrice + _buildingCounts[countryId, (int)kind] * _balance.DefensePostPriceStep);
                case BuildingKind.MissileSilo:
                    return _balance.MissileSiloPrice;
                case BuildingKind.Sam:
                    return _buildingCounts[countryId, (int)kind] == 0 ? _balance.SamFirstPrice : _balance.SamAdditionalPrice;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private bool CanOwnBuildingAt(int countryId, int cell)
        {
            return cell >= 0 && cell < _owners.Length && Map.IsLand(cell) && _owners[cell] == countryId;
        }

        private bool IsCoastalLand(int cell)
        {
            for (var direction = 0; direction < 4; direction++)
            {
                var neighbour = Map.GetNeighbour(cell, direction);
                // The north/south limit is not water; only a represented water neighbour is a coast.
                if (neighbour >= 0 && !Map.IsLand(neighbour))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasBuildingSpacing(int cell, int ignoredBuildingId)
        {
            var minimumSquared = _balance.BuildingMinimumSpacing * _balance.BuildingMinimumSpacing;
            for (var i = 0; i < _buildings.Count; i++)
            {
                var other = _buildings[i];
                if (other.Id != ignoredBuildingId && SquaredDistance(cell, other.Cell) * _cellArea < minimumSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateBuildingOwnership(int cell, int oldOwner, int newOwner)
        {
            if (!_buildingByCell.TryGetValue(cell, out var building))
            {
                return;
            }

            if (newOwner == 0)
            {
                DestroyBuilding(building.Id);
                return;
            }

            RemoveBuildingFromCountry(building, oldOwner);
            building.CountryId = newOwner;
            AddBuildingToCountry(building, newOwner);
            RefreshPopulationLimit(GetCountry(oldOwner));
            RefreshPopulationLimit(GetCountry(newOwner));
            _events.Add(new MatchEvent("building.captured", newOwner));
        }

        private bool DestroyBuilding(int buildingId)
        {
            if (!_buildingById.TryGetValue(buildingId, out var building))
            {
                return false;
            }

            var owner = building.CountryId;
            RemoveBuildingFromCountry(building, owner);
            _buildingById.Remove(building.Id);
            _buildingByCell.Remove(building.Cell);
            _buildings.Remove(building);
            building.CountryId = 0;
            RefreshPopulationLimit(GetCountry(owner));
            _events.Add(new MatchEvent("building.destroyed", owner));
            return true;
        }

        private void AddBuildingToCountry(BuildingState building, int countryId)
        {
            _buildingCounts[countryId, (int)building.Kind]++;
            if (building.Kind == BuildingKind.City)
            {
                _cityLevels[countryId] += building.Level;
            }
            else if (building.Kind == BuildingKind.DefensePost)
            {
                _defensePostsByCountry[countryId].Add(building);
            }
        }

        private void RemoveBuildingFromCountry(BuildingState building, int countryId)
        {
            _buildingCounts[countryId, (int)building.Kind]--;
            if (building.Kind == BuildingKind.City)
            {
                _cityLevels[countryId] -= building.Level;
            }
            else if (building.Kind == BuildingKind.DefensePost)
            {
                _defensePostsByCountry[countryId].Remove(building);
            }
        }

        private double CityLevelBonus(int countryId)
        {
            return _cityLevels[countryId] * _balance.CityPopulationBonus;
        }

        private double GetDefenseLossMultiplier(int defenderId, int cell)
        {
            return IsDefensePostCovered(defenderId, cell) ? _balance.DefensePostLossMultiplier : 1;
        }

        private double GetDefenseSpeedMultiplier(int defenderId, int cell)
        {
            return IsDefensePostCovered(defenderId, cell) ? _balance.DefensePostCaptureSpeedMultiplier : 1;
        }

        private bool IsDefensePostCovered(int defenderId, int cell)
        {
            if (defenderId <= 0 || defenderId > _countries.Length)
            {
                return false;
            }

            var posts = _defensePostsByCountry[defenderId];
            var radiusSquared = _balance.DefensePostRadius * _balance.DefensePostRadius;
            for (var i = 0; i < posts.Count; i++)
            {
                if (SquaredDistance(cell, posts[i].Cell) * _cellArea <= radiusSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddBuildingFailure(string key, int countryId)
        {
            _events.Add(new MatchEvent(key, countryId));
        }

        private static bool IsBuildingKind(BuildingKind kind)
        {
            return kind >= BuildingKind.City && kind <= BuildingKind.Sam;
        }

        private static bool IsEconomicBuilding(BuildingKind kind)
        {
            return kind == BuildingKind.City || kind == BuildingKind.Factory || kind == BuildingKind.Port;
        }
    }
}
