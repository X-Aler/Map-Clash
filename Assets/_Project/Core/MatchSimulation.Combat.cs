// C# port of lukechatton/roworld SimAttacks.luau, ae7a3505e6f3736aef85fffc1879c0476f819c59.
// GNU AGPL v3; see ThirdParty/RoWorld/NOTICE.md and COPYING.
using System;
using System.Collections.Generic;

namespace MapClash
{
    public sealed partial class MatchSimulation
    {
        private readonly List<Wave> _pendingMerges = new List<Wave>();
        private sealed class Wave
        {
            private double _troops;
            public Wave(OperationState state) { State = state; Troops = state.Troops; }
            public OperationState State { get; }
            public double Troops
            {
                get => _troops;
                set { _troops = value; State.Troops = Math.Max(0, value); }
            }
            public SortedDictionary<long, Stack<int>> Queue { get; } = new SortedDictionary<long, Stack<int>>();
            public HashSet<int> Front { get; } = new HashSet<int>();
            public long StartedTick { get; set; }
            public long FrozenFrom { get; set; }
            public long RetreatAt { get; set; }
            public double PendingTroops { get; set; }
            public int Head { get; set; } = -1;
            public double Chew { get; set; }
            public bool IsCancelled { get; set; }
        }

        private void TryAttack(MatchCommand command)
        {
            var attacker = GetCountry(command.CountryId);
            if (!IsFinite(command.Amount) || command.Amount <= 0 || command.Amount > 1 || !Map.IsLand(command.TargetCell))
            {
                Reject(command.CountryId);
                return;
            }

            var target = _owners[command.TargetCell];
            if (target == command.CountryId)
            {
                Reject(command.CountryId);
                return;
            }

            var troops = Math.Floor(command.Amount * attacker.DefendingTroops);
            if (troops < 1) { Reject(command.CountryId); return; }
            attacker.DefendingTroops -= troops;
            attacker.DeployedTroops += troops;
            CommitTroops(command.CountryId, target, command.TargetCell, troops);
        }

        private void CommitTroops(int countryId, int target, int targetCell, double troops)
        {
            var attacker = GetCountry(countryId);
            if (target > 0)
            {
                var opposing = FindLiveWave(target, countryId);
                if (opposing != null)
                {
                    var cancelled = Math.Min(troops, opposing.Troops);
                    SpendTroops(opposing, cancelled);
                    troops -= cancelled;
                    attacker.DeployedTroops = Math.Max(0, attacker.DeployedTroops - cancelled);
                    if (opposing.Troops < 1) { FinishWave(opposing, false); }
                    if (troops < 1)
                    {
                        attacker.DeployedTroops = Math.Max(0, attacker.DeployedTroops - troops);
                        return;
                    }
                }
            }

            var wave = FindLiveWave(countryId, target);
            if (wave != null)
            {
                if (wave.StartedTick == Tick)
                {
                    wave.Troops += troops;
                    SeedFromBorder(wave);
                }
                else
                {
                    wave.PendingTroops += troops;
                }

                wave.State.TargetCell = targetCell;
                _events.Add(new MatchEvent("attack.reinforced", countryId));
                return;
            }

            StartWave(countryId, target, targetCell, troops);
        }

        private void StartWave(int countryId, int target, int targetCell, double troops)
        {
            var state = new OperationState { Id = _nextOperationId++, CountryId = countryId,
                TargetCountryId = target, TargetCell = targetCell, Troops = troops, Phase = OperationPhase.Advancing };
            var wave = new Wave(state) { StartedTick = Tick };
            SeedFromBorder(wave);
            if (wave.Front.Count == 0)
            {
                GetCountry(countryId).DeployedTroops = Math.Max(0, GetCountry(countryId).DeployedTroops - troops);
                GetCountry(countryId).DefendingTroops += troops;
                Reject(countryId);
                return;
            }

            _waves.Add(wave);
            _operations.Add(state);
            _events.Add(new MatchEvent("attack.started", countryId));
        }

        private Wave FindLiveWave(int countryId, int target)
        {
            for (var i = 0; i < _waves.Count; i++)
            {
                var wave = _waves[i];
                if (!wave.IsCancelled && wave.State.CountryId == countryId && wave.State.TargetCountryId == target) { return wave; }
            }

            return null;
        }

        private Wave FindAdvancingWave(int countryId, int targetCountryId)
        {
            var wave = FindLiveWave(countryId, targetCountryId);
            return wave != null && wave.RetreatAt == 0 ? wave : null;
        }

        private void SeedFromBorder(Wave wave)
        {
            wave.Queue.Clear();
            wave.Front.Clear();
            wave.Head = -1;
            wave.Chew = 0;
            foreach (var border in _borders[wave.State.CountryId]) { AddNeighbours(wave, border); }
            foreach (var bucket in wave.Queue)
            {
                wave.State.FrontCell = bucket.Value.Peek();
                break;
            }
        }

        private void AddNeighbours(Wave wave, int cell)
        {
            // Reference order: up, down, left, right. The host map retains its world seam.
            for (var index = 0; index < 4; index++)
            {
                var direction = index == 0 ? 2 : index == 1 ? 3 : index == 2 ? 0 : 1;
                var next = Map.GetNeighbour(cell, direction);
                if (!Map.IsLand(next) || _owners[next] != wave.State.TargetCountryId) { continue; }
                var key = RoWorldCombatRules.QueuePriority(Tick, NextRandom(7),
                    CountOwnedNeighbours(next, wave.State.CountryId), Map.Terrain[next]);
                if (!wave.Queue.TryGetValue(key, out var bucket))
                {
                    bucket = new Stack<int>();
                    wave.Queue.Add(key, bucket);
                }

                bucket.Push(next); // Reference bucket ties are last-in, first-out; duplicates are intentional.
                wave.Front.Add(next);
            }
        }

        private int Pop(Wave wave)
        {
            long key = 0;
            Stack<int> first = null;
            foreach (var bucket in wave.Queue) { key = bucket.Key; first = bucket.Value; break; }
            if (first == null) { return -1; }
            var cell = first.Pop();
            if (first.Count == 0) { wave.Queue.Remove(key); }
            wave.Front.Remove(cell);
            return cell;
        }

        private void TryRecall(MatchCommand command)
        {
            for (var i = 0; i < _waves.Count; i++)
            {
                var wave = _waves[i];
                if (!wave.IsCancelled && wave.State.Id == command.ObjectId && wave.State.CountryId == command.CountryId)
                {
                    if (wave.RetreatAt == 0)
                    {
                        wave.FrozenFrom = Tick + 2;
                        wave.RetreatAt = Tick + 21;
                        wave.State.ReturnAtTick = wave.RetreatAt;
                    }
                    return;
                }
            }
            Reject(command.CountryId);
        }

        private void ProcessOperations()
        {
            var count = _waves.Count;
            for (var i = 0; i < count; i++)
            {
                var wave = _waves[i];
                if (!wave.IsCancelled && wave.StartedTick != Tick) { AdvanceWave(wave); }
            }

            _pendingMerges.Clear();
            for (var i = 0; i < count; i++)
            {
                if (_waves[i].PendingTroops > 0) { _pendingMerges.Add(_waves[i]); }
            }

            foreach (var wave in _pendingMerges)
            {
                var pending = wave.PendingTroops;
                wave.PendingTroops = 0;
                if (pending <= 0) { continue; }
                var country = GetCountry(wave.State.CountryId);
                if (!country.IsAlive) { continue; }
                if (wave.State.TargetCountryId > 0 && !GetCountry(wave.State.TargetCountryId).IsAlive)
                {
                    country.DeployedTroops = Math.Max(0, country.DeployedTroops - pending);
                    country.DefendingTroops += pending;
                }
                else if (wave.IsCancelled)
                {
                    CommitTroops(country.Id, wave.State.TargetCountryId, wave.State.TargetCell, pending);
                }
                else
                {
                    if (wave.State.Phase == OperationPhase.Returning)
                    {
                        country.ReturningTroops = Math.Max(0, country.ReturningTroops - wave.Troops);
                        country.DeployedTroops += wave.Troops;
                    }
                    wave.State.Phase = OperationPhase.Advancing;
                    wave.Troops += pending;
                    wave.RetreatAt = 0;
                    wave.FrozenFrom = 0;
                    wave.State.ReturnAtTick = 0;
                    SeedFromBorder(wave);
                }
            }

            _pendingMerges.Clear();

            for (var i = _waves.Count - 1; i >= 0; i--)
            {
                if (_waves[i].IsCancelled) { _waves.RemoveAt(i); _operations.RemoveAt(i); }
            }

            // The source permits the final tile to overdraw an attack. Keep that raw
            // amount for same-tick reinforcement, while host population pools stay nonnegative.
            foreach (var country in _countries) { country.DeployedTroops = 0; country.ReturningTroops = 0; }
            foreach (var wave in _waves)
            {
                var country = GetCountry(wave.State.CountryId);
                if (!country.IsAlive) { continue; }
                if (wave.State.Phase == OperationPhase.Returning) { country.ReturningTroops += Math.Max(0, wave.Troops); }
                else { country.DeployedTroops += Math.Max(0, wave.Troops + wave.PendingTroops); }
            }
        }

        private void AdvanceWave(Wave wave)
        {
            var country = GetCountry(wave.State.CountryId);
            var target = GetCountry(wave.State.TargetCountryId);
            if (!country.IsAlive || (target != null && !target.IsAlive)) { FinishWave(wave, true); return; }
            if (wave.RetreatAt > 0)
            {
                if (Tick >= wave.RetreatAt)
                {
                    FinishWave(wave, true, target == null ? 1 : 0.75);
                    return;
                }
                if (Tick >= wave.FrozenFrom)
                {
                    if (wave.State.Phase != OperationPhase.Returning)
                    {
                        country.DeployedTroops = Math.Max(0, country.DeployedTroops - wave.Troops);
                        country.ReturningTroops += wave.Troops;
                        wave.State.Phase = OperationPhase.Returning;
                        _events.Add(new MatchEvent("operation.returning", country.Id));
                    }
                    return;
                }
            }

            var border = wave.Front.Count * Map.CellScale + NextRandom(5);
            var attackerArea = country.LandTiles * _cellArea;
            var defenderArea = target == null ? 1 : Math.Max(1, target.LandTiles * _cellArea);
            var budget = 1.0;
            var taken = 0;
            var cap = Math.Max(1, (int)Math.Floor(572 / _cellArea));
            while (budget > 0)
            {
                if (wave.Troops < 1) { FinishWave(wave, false); return; }
                if (wave.Head >= 0 && (_owners[wave.Head] != wave.State.TargetCountryId ||
                    CountOwnedNeighbours(wave.Head, country.Id) == 0)) { wave.Head = -1; wave.Chew = 0; }
                if (wave.Head < 0)
                {
                    if (taken >= cap) { break; }
                    var cell = Pop(wave);
                    if (cell < 0) { FinishWave(wave, true); return; }
                    if (_owners[cell] != wave.State.TargetCountryId || !Map.IsLand(cell) || CountOwnedNeighbours(cell, country.Id) == 0) { continue; }
                    // The reference queues neighbours BEFORE ownership changes.
                    AddNeighbours(wave, cell);
                    wave.Head = cell;
                    wave.Chew = 0;
                }

                var head = wave.Head;
                wave.State.FrontCell = head;
                RoWorldCombatRules.TileCosts(Map.Terrain[head], wave.Troops,
                    target == null ? 0 : target.DefendingTroops, attackerArea, defenderArea, border, target == null,
                    out var loss, out var defenderLoss, out var tickFraction,
                    target == null ? 1 : target.LandTiles * _cellArea);
                var need = _cellArea - wave.Chew;
                var can = Math.Ceiling(budget / tickFraction);
                if (loss > 0) { can = Math.Min(can, Math.Ceiling(wave.Troops / loss)); }
                var progress = Math.Min(can, need);
                budget -= progress * tickFraction;
                SpendTroops(wave, progress * loss);
                if (target != null) { target.DefendingTroops = Math.Max(0, target.DefendingTroops - progress * defenderLoss); }
                if (can < need) { wave.Chew += can; continue; }
                wave.Head = -1;
                wave.Chew = 0;
                SetOwner(head, country.Id);
                taken++;
                if (target != null && (target.LandTiles == 0 ||
                    (target.LandTiles * _cellArea < 100 && wave.Troops > target.DefendingTroops)))
                {
                    AnnexLand(target, country);
                    FinishWave(wave, true);
                    return;
                }
            }
        }

        private void SpendTroops(Wave wave, double loss)
        {
            var spent = Math.Min(Math.Max(0, wave.Troops), loss);
            wave.Troops -= loss;
            var country = GetCountry(wave.State.CountryId);
            if (wave.State.Phase == OperationPhase.Returning) { country.ReturningTroops = Math.Max(0, country.ReturningTroops - spent); }
            else { country.DeployedTroops = Math.Max(0, country.DeployedTroops - spent); }
        }

        private void FinishWave(Wave wave, bool refund, double keep = 1)
        {
            if (wave.IsCancelled) { return; }
            var country = GetCountry(wave.State.CountryId);
            if (wave.State.Phase == OperationPhase.Returning) { country.ReturningTroops = Math.Max(0, country.ReturningTroops - Math.Max(0, wave.Troops)); }
            else { country.DeployedTroops = Math.Max(0, country.DeployedTroops - Math.Max(0, wave.Troops)); }
            if (refund && country.IsAlive) { country.DefendingTroops += Math.Floor(Math.Max(0, wave.Troops) * keep); }
            wave.Troops = 0;
            wave.IsCancelled = true;
            wave.Queue.Clear();
            wave.Front.Clear();
        }

        private void AnnexLand(CountryState victim, CountryState conqueror)
        {
            conqueror.Gold += victim.Gold * 0.5;
            victim.Gold = 0;
            var tiles = new List<int>();
            var seen = new HashSet<int>();
            foreach (var border in _borders[victim.Id]) { if (seen.Add(border)) { tiles.Add(border); } }
            for (var index = 0; index < tiles.Count; index++)
            {
                for (var direction = 0; direction < 4; direction++)
                {
                    var next = Map.GetNeighbour(tiles[index], direction);
                    if (next >= 0 && _owners[next] == victim.Id && seen.Add(next)) { tiles.Add(next); }
                }
            }

            for (var pass = 0; pass < 100; pass++)
            {
                var progressed = false;
                for (var index = 0; index < tiles.Count; index++)
                {
                    var cell = tiles[index];
                    if (_owners[cell] != victim.Id) { continue; }
                    var taker = CountOwnedNeighbours(cell, conqueror.Id) > 0 ? conqueror.Id : 0;
                    if (taker == 0)
                    {
                        for (var direction = 0; direction < 4; direction++)
                        {
                            var next = Map.GetNeighbour(cell, direction);
                            var owner = next >= 0 ? _owners[next] : 0;
                            if (owner > 0 && owner != victim.Id) { taker = owner; break; }
                        }
                    }
                    if (taker > 0) { SetOwner(cell, taker); progressed = true; }
                }
                if (!progressed) { break; }
            }

            if (victim.LandTiles == 0)
            {
                victim.IsAlive = false;
                victim.Workers = 0;
                victim.DefendingTroops = 0;
                victim.GrowthPerSecond = 0;
                victim.IncomePerSecond = 0;
                for (var i = 0; i < _waves.Count; i++)
                {
                    if (_waves[i].State.CountryId == victim.Id) { FinishWave(_waves[i], false); }
                }
                victim.DeployedTroops = 0;
                victim.ReturningTroops = 0;
                _borders[victim.Id].Clear();
                _events.Add(new MatchEvent("country.annexed", victim.Id));
            }
        }

        private int CountOwnedNeighbours(int cell, int countryId)
        {
            var count = 0;
            for (var direction = 0; direction < 4; direction++)
            {
                var next = Map.GetNeighbour(cell, direction);
                if (next >= 0 && _owners[next] == countryId) { count++; }
            }
            return count;
        }
    }
}
