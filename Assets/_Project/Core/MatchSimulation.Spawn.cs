using System;
using System.Collections.Generic;

namespace MapClash
{
    public sealed partial class MatchSimulation
    {
        private readonly List<int> _startingCells = new List<int>();
        private void BuildLandComponents()
        {
            var component = 0;
            var readIndex = 0;
            var writeIndex = 0;
            // The BFS output doubles as the stable land-cell list used for seeded spawn selection.
            for (var cell = 0; cell < _owners.Length; cell++)
            {
                if (!Map.IsLand(cell) || _landComponents[cell] != 0)
                {
                    continue;
                }

                component++;
                _landComponents[cell] = component;
                _landCells[writeIndex++] = cell;
                while (readIndex < writeIndex)
                {
                    var connectedCell = _landCells[readIndex++];
                    for (var direction = 0; direction < 4; direction++)
                    {
                        var neighbour = Map.GetNeighbour(connectedCell, direction);
                        if (neighbour >= 0 && Map.IsLand(neighbour) && _landComponents[neighbour] == 0)
                        {
                            _landComponents[neighbour] = component;
                            _landCells[writeIndex++] = neighbour;
                        }
                    }
                }
            }
        }

        private void InitializeCountries()
        {
            for (var i = 0; i < _countries.Length; i++)
            {
                var country = new CountryState
                {
                    Id = i + 1,
                    Name = i == 0 ? Settings.PlayerName : BotCountryName(i),
                    IsPlayer = i == 0,
                    Workers = _balance.StartingPopulation * (1 - _balance.StartingTroopRatio),
                    DefendingTroops = _balance.StartingPopulation * _balance.StartingTroopRatio,
                    Gold = _balance.StartingGold,
                    TargetTroopRatio = _balance.StartingTroopRatio
                };
                _countries[i] = country;
                _borders[country.Id] = new BorderIndex();
                country.CapitalCell = FindCapital(country.Id);
                if (country.CapitalCell < 0)
                {
                    throw new ArgumentException("The map cannot fit all nonoverlapping starting regions with the configured radius.", nameof(Map));
                }
            }
        }

        private int FindCapital(int countryId)
        {
            for (var attempt = 0; attempt < 4096; attempt++)
            {
                var candidate = _landCells[NextRandom(_landCells.Length)];
                if (CanPlaceCapital(countryId, candidate))
                {
                    return candidate;
                }
            }

            var offset = NextRandom(_landCells.Length);
            for (var i = 0; i < _landCells.Length; i++)
            {
                var candidate = _landCells[(i + offset) % _landCells.Length];
                if (CanPlaceCapital(countryId, candidate))
                {
                    return candidate;
                }
            }

            return -1;
        }

        private bool CanPlaceCapital(int countryId, int cell)
        {
            if (cell < 0 || cell >= _owners.Length || !Map.IsLand(cell))
            {
                return false;
            }

            var radius = (int)Math.Floor(_balance.StartingRadius / Map.CellScale);
            var minimumDistance = (double)radius * 2 + 1;
            var minimumSquared = minimumDistance * minimumDistance;
            for (var i = 0; i < _countries.Length; i++)
            {
                var other = _countries[i];
                if (other != null && other.Id != countryId && other.CapitalCell >= 0 && SquaredDistance(cell, other.CapitalCell) < minimumSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private void PrepareStartingRegions()
        {
            for (var i = 0; i < _startingCells.Count; i++)
            {
                SetOwner(_startingCells[i], 0);
            }
            _startingCells.Clear();
            var radius = (int)Math.Floor(_balance.StartingRadius / Map.CellScale);
            var radiusSquared = (long)radius * radius;
            var frontier = new Queue<int>();
            for (var i = 0; i < _countries.Length; i++)
            {
                var country = _countries[i];
                frontier.Enqueue(country.CapitalCell);
                SetOwner(country.CapitalCell, country.Id);
                _startingCells.Add(country.CapitalCell);
                while (frontier.Count > 0)
                {
                    var cell = frontier.Dequeue();
                    for (var direction = 0; direction < 4; direction++)
                    {
                        var neighbour = Map.GetNeighbour(cell, direction);
                        if (neighbour >= 0 && _owners[neighbour] == 0 && Map.IsLand(neighbour) &&
                            SquaredDistance(country.CapitalCell, neighbour) <= radiusSquared)
                        {
                            SetOwner(neighbour, country.Id);
                            _startingCells.Add(neighbour);
                            frontier.Enqueue(neighbour);
                        }
                    }
                }

                RefreshPopulationLimit(country);
                country.PeakLandFraction = (double)country.LandTiles / Map.TotalLandTiles;
            }

        }

        private void BeginMatch()
        {
            PrepareStartingRegions();
            Phase = MatchPhase.Playing;
            _events.Add(new MatchEvent("match.started"));
            CheckResult();
        }

        private long SquaredDistance(int first, int second)
        {
            var dx = Math.Abs(first % Map.Width - second % Map.Width);
            dx = Math.Min(dx, Map.Width - dx);
            var dy = first / Map.Width - second / Map.Width;
            return (long)dx * dx + (long)dy * dy;
        }

        private void SetOwner(int cell, int countryId)
        {
            var previousOwner = _owners[cell];
            if (previousOwner == countryId)
            {
                return;
            }

            _owners[cell] = countryId;
            if (previousOwner > 0)
            {
                _countries[previousOwner - 1].LandTiles--;
                _borders[previousOwner].Remove(cell);
            }

            if (countryId > 0)
            {
                _countries[countryId - 1].LandTiles++;
            }

            if (!_isChanged[cell])
            {
                _isChanged[cell] = true;
                _changedCells.Add(cell);
            }

            RefreshBorderCell(cell);
            for (var direction = 0; direction < 4; direction++)
            {
                var neighbour = Map.GetNeighbour(cell, direction);
                if (neighbour >= 0)
                {
                    RefreshBorderCell(neighbour);
                }
            }
        }

        private void RefreshBorderCell(int cell)
        {
            var owner = _owners[cell];
            if (owner <= 0)
            {
                return;
            }

            for (var direction = 0; direction < 4; direction++)
            {
                var neighbour = Map.GetNeighbour(cell, direction);
                if (neighbour >= 0 && Map.IsLand(neighbour) && _owners[neighbour] != owner)
                {
                    _borders[owner].Add(cell);
                    return;
                }
            }

            _borders[owner].Remove(cell);
        }

        private void CheckResult()
        {
            if (!GetCountry(1).IsAlive)
            {
                FinishMatch(FindLargestCountry(), false, "result.annexed");
                return;
            }

            for (var i = 0; i < _countries.Length; i++)
            {
                var country = _countries[i];
                var fraction = (double)country.LandTiles / Map.TotalLandTiles;
                country.PeakLandFraction = Math.Max(country.PeakLandFraction, fraction);
                if (country.IsAlive && fraction >= Settings.VictoryFraction)
                {
                    FinishMatch(country.Id, false, "result.domination");
                    return;
                }
            }

            if (Settings.TimeLimitSeconds > 0 && _playingTicks >= (long)Settings.TimeLimitSeconds * SimulationBalance.TicksPerSecond)
            {
                var winnerId = FindLargestCountry();
                var largestArea = GetCountry(winnerId).LandTiles;
                var ties = 0;
                for (var i = 0; i < _countries.Length; i++)
                {
                    if (_countries[i].IsAlive && _countries[i].LandTiles == largestArea)
                    {
                        ties++;
                    }
                }

                FinishMatch(ties > 1 ? 0 : winnerId, ties > 1, "result.time");
            }
        }

        private int FindLargestCountry()
        {
            var winner = _countries[0];
            for (var i = 1; i < _countries.Length; i++)
            {
                if (_countries[i].LandTiles > winner.LandTiles)
                {
                    winner = _countries[i];
                }
            }

            return winner.Id;
        }

        private void FinishMatch(int winnerId, bool isDraw, string reasonKey)
        {
            Result = new MatchResult
            {
                WinnerCountryId = winnerId,
                IsDraw = isDraw,
                DurationSeconds = ElapsedSeconds,
                ReasonKey = reasonKey
            };
            Phase = MatchPhase.Finished;
            _commands.Clear();
            _events.Add(new MatchEvent("match.finished", winnerId));
        }

        private static string BotCountryName(int index)
        {
            // Real-country names are content identifiers; localized display names are provided by the view.
            string[] names =
            {
                "Brazil", "Japan", "France", "Canada", "India", "Australia", "Egypt", "Mexico", "Italy", "Argentina",
                "Norway", "Kenya", "Spain", "Indonesia", "Germany", "Chile", "Sweden", "South Korea", "Nigeria", "Türkiye",
                "Poland", "South Africa", "Thailand", "Peru", "Greece", "New Zealand", "Vietnam", "Finland", "Morocco", "Colombia",
                "Portugal", "Philippines", "Austria", "Malaysia", "Ireland", "Pakistan", "Denmark", "Ethiopia", "Uruguay", "Nepal",
                "Switzerland", "Kazakhstan", "Belgium", "Tanzania", "Czechia", "Ghana", "Romania", "Algeria", "Hungary", "Iceland",
                "Senegal", "Croatia", "Tunisia", "Ecuador", "Bulgaria", "Panama", "Mongolia", "Slovakia", "Costa Rica"
            };
            return names[index - 1];
        }
    }
}
