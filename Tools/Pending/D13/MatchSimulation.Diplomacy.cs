using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MapClash
{
    public sealed partial class MatchSimulation
    {
        private RelationState[] _relations;
        private ReadOnlyCollection<RelationState> _relationView;
        private long[] _traitorUntilTicks;

        public IReadOnlyList<RelationState> Relations
        {
            get
            {
                if (_isDisposed)
                {
                    return Array.Empty<RelationState>();
                }

                InitializeDiplomacy();
                return _relationView;
            }
        }

        public RelationState GetRelation(int firstCountryId, int secondCountryId)
        {
            if (_isDisposed || firstCountryId == secondCountryId ||
                GetCountry(firstCountryId) == null || GetCountry(secondCountryId) == null)
            {
                return null;
            }

            InitializeDiplomacy();
            var first = Math.Min(firstCountryId, secondCountryId);
            var second = Math.Max(firstCountryId, secondCountryId);
            return _relations[(second - 1) * (second - 2) / 2 + first - 1];
        }

        public bool CanAttack(int attackingCountryId, int targetCountryId)
        {
            var attacker = GetCountry(attackingCountryId);
            if (_isDisposed || attacker == null || !attacker.IsAlive || attackingCountryId == targetCountryId)
            {
                return false;
            }

            if (targetCountryId == 0)
            {
                return true;
            }

            return TryGetLivingDiplomacyPair(attackingCountryId, targetCountryId, out var relation) &&
                !IsAllianceActive(relation);
        }

        public bool CanTrade(int firstCountryId, int secondCountryId)
        {
            if (_isDisposed)
            {
                return false;
            }

            var firstCountry = GetCountry(firstCountryId);
            if (firstCountry == null || !firstCountry.IsAlive)
            {
                return false;
            }

            if (firstCountryId == secondCountryId)
            {
                return true;
            }

            return TryGetLivingDiplomacyPair(firstCountryId, secondCountryId, out var relation) &&
                relation.Kind != RelationKind.War && !relation.IsFirstCountryEmbargoing &&
                !relation.IsSecondCountryEmbargoing;
        }

        public long GetTraitorTicksRemaining(int countryId)
        {
            var country = GetCountry(countryId);
            if (_isDisposed || country == null || !country.IsAlive)
            {
                return 0;
            }

            InitializeDiplomacy();
            return Math.Max(0, _traitorUntilTicks[countryId] - Tick);
        }

        public double GetTraitorDefenseMultiplier(int countryId)
        {
            return GetTraitorTicksRemaining(countryId) > 0 ? _balance.TraitorDefenseMultiplier : 1;
        }

        public double GetTraitorAdvanceMultiplier(int countryId)
        {
            return GetTraitorTicksRemaining(countryId) > 0 ? _balance.TraitorAdvanceMultiplier : 1;
        }

        private void InitializeDiplomacy()
        {
            if (_relations != null)
            {
                return;
            }

            _relations = new RelationState[_countries.Length * (_countries.Length - 1) / 2];
            _traitorUntilTicks = new long[_countries.Length + 1];
            var index = 0;
            for (var second = 2; second <= _countries.Length; second++)
            {
                for (var first = 1; first < second; first++)
                {
                    _relations[index++] = new RelationState(first, second);
                }
            }

            _relationView = Array.AsReadOnly(_relations);
        }

        private bool TryExecuteDiplomacyCommand(MatchCommand command)
        {
            switch (command.Kind)
            {
                case CommandKind.OfferAlliance:
                case CommandKind.AcceptAlliance:
                case CommandKind.RenewAlliance:
                case CommandKind.BreakAlliance:
                case CommandKind.SetEmbargo:
                case CommandKind.SendGold:
                case CommandKind.SendTroops:
                    break;
                default:
                    return false;
            }

            if (_isDisposed || Phase != MatchPhase.Playing ||
                !TryGetLivingDiplomacyPair(command.CountryId, command.TargetCountryId, out var relation))
            {
                Reject(command.CountryId);
                return true;
            }

            bool isAccepted;
            switch (command.Kind)
            {
                case CommandKind.OfferAlliance:
                    isAccepted = TryOfferAlliance(command.CountryId, command.TargetCountryId, relation, false);
                    break;
                case CommandKind.RenewAlliance:
                    isAccepted = TryOfferAlliance(command.CountryId, command.TargetCountryId, relation, true);
                    break;
                case CommandKind.AcceptAlliance:
                    isAccepted = TryAcceptAlliance(command.CountryId, command.TargetCountryId, relation);
                    break;
                case CommandKind.BreakAlliance:
                    isAccepted = TryBreakAlliance(command.CountryId, command.TargetCountryId, relation);
                    break;
                case CommandKind.SetEmbargo:
                    isAccepted = TrySetEmbargo(command.CountryId, command.TargetCountryId, command.Amount, relation);
                    break;
                case CommandKind.SendGold:
                    isAccepted = TrySendGold(command.CountryId, command.TargetCountryId, command.Amount, relation);
                    break;
                default:
                    isAccepted = TrySendTroops(command.CountryId, command.TargetCountryId, command.Amount, relation);
                    break;
            }

            if (!isAccepted)
            {
                Reject(command.CountryId);
            }

            return true;
        }

        private bool TryOfferAlliance(int senderId, int recipientId, RelationState relation, bool isRenewal)
        {
            if (IsAllianceActive(relation) != isRenewal ||
                (relation.Request != null && Tick < relation.Request.ExpiresAtTick))
            {
                return false;
            }

            relation.Request = new AllianceRequestState(senderId, recipientId,
                Tick + _balance.AllianceRequestTicks, isRenewal);
            AddDiplomacyEvent(isRenewal ? "diplomacy.renewalRequested" : "diplomacy.allianceRequested", recipientId, senderId);
            return true;
        }

        private bool TryAcceptAlliance(int recipientId, int senderId, RelationState relation)
        {
            var request = relation.Request;
            if (request == null || request.SenderCountryId != senderId || request.RecipientCountryId != recipientId ||
                Tick >= request.ExpiresAtTick || (request.IsRenewal && !IsAllianceActive(relation)))
            {
                return false;
            }

            var isRenewal = request.IsRenewal;
            relation.Kind = RelationKind.Allied;
            relation.Request = null;
            relation.AllianceUntilTick = Tick + _balance.AllianceDurationTicks;
            relation.HasExpiryWarning = false;
            // Consent ends active direct fighting without destroying surviving troops.
            for (var i = 0; i < _waves.Count; i++)
            {
                var wave = _waves[i];
                if (!wave.IsCancelled && wave.State.Phase == OperationPhase.Advancing &&
                    IsPair(wave.State.CountryId, wave.State.TargetCountryId, recipientId, senderId))
                {
                    BeginReturn(wave, 1);
                }
            }

            AddDiplomacyEvent(isRenewal ? "diplomacy.allianceRenewed" : "diplomacy.allianceAccepted", recipientId, senderId);
            return true;
        }

        private bool TryBreakAlliance(int countryId, int otherCountryId, RelationState relation)
        {
            if (!IsAllianceActive(relation))
            {
                return false;
            }

            EndAlliance(relation);
            _traitorUntilTicks[countryId] = Tick + _balance.TraitorDurationTicks;
            AddDiplomacyEvent("diplomacy.betrayal", countryId, otherCountryId);
            return true;
        }

        private bool TrySetEmbargo(int countryId, int otherCountryId, double amount, RelationState relation)
        {
            if (amount != 0 && amount != 1)
            {
                return false;
            }

            var enabled = amount == 1;
            var previous = countryId == relation.FirstCountryId ? relation.IsFirstCountryEmbargoing : relation.IsSecondCountryEmbargoing;
            if (countryId == relation.FirstCountryId)
            {
                relation.IsFirstCountryEmbargoing = enabled;
            }
            else
            {
                relation.IsSecondCountryEmbargoing = enabled;
            }

            if (previous != enabled)
            {
                AddDiplomacyEvent(enabled ? "diplomacy.embargoStarted" : "diplomacy.embargoEnded", countryId, otherCountryId);
            }

            return true;
        }

        private bool TrySendGold(int countryId, int otherCountryId, double amount, RelationState relation)
        {
            if (!IsAllianceActive(relation) || !IsFinite(amount) || amount <= 0)
            {
                return false;
            }

            var sender = GetCountry(countryId);
            var recipient = GetCountry(otherCountryId);
            if (amount > sender.Gold || !IsFinite(recipient.Gold + amount))
            {
                return false;
            }

            sender.Gold -= amount;
            recipient.Gold += amount;
            AddDiplomacyEvent("diplomacy.goldReceived", otherCountryId, countryId);
            return true;
        }

        private bool TrySendTroops(int countryId, int otherCountryId, double ratio, RelationState relation)
        {
            if (!IsAllianceActive(relation) || !IsFinite(ratio) || ratio <= 0 || ratio > 1)
            {
                return false;
            }

            var sender = GetCountry(countryId);
            var recipient = GetCountry(otherCountryId);
            var send = Math.Floor(Math.Min(ratio * sender.DefendingTroops,
                (1 - _balance.MinReserveFraction) * sender.DefendingTroops));
            if (send < 1 || !IsFinite(send) || !IsFinite(recipient.DefendingTroops + send))
            {
                return false;
            }

            sender.DefendingTroops -= send;
            recipient.DefendingTroops += send;
            AddDiplomacyEvent("diplomacy.troopsReceived", otherCountryId, countryId);
            return true;
        }

        private void RegisterHostileAction(int countryId, int otherCountryId)
        {
            if (!TryGetLivingDiplomacyPair(countryId, otherCountryId, out var relation) || IsAllianceActive(relation))
            {
                return;
            }

            var wasAtWar = relation.Kind == RelationKind.War;
            relation.Kind = RelationKind.War;
            relation.Request = null;
            relation.AllianceUntilTick = 0;
            relation.HasExpiryWarning = false;
            relation.LastHostileActionTick = Tick;
            relation.LastUnsettledTick = Tick;
            if (!wasAtWar)
            {
                AddDiplomacyEvent("diplomacy.warDeclared", countryId, otherCountryId);
            }
        }

        private void TickDiplomacy()
        {
            InitializeDiplomacy();
            for (var i = 0; i < _relations.Length; i++)
            {
                var relation = _relations[i];
                if (!GetCountry(relation.FirstCountryId).IsAlive || !GetCountry(relation.SecondCountryId).IsAlive)
                {
                    ClearRelation(relation);
                    continue;
                }

                var request = relation.Request;
                if (request != null && Tick >= request.ExpiresAtTick)
                {
                    relation.Request = null;
                    AddDiplomacyEvent("diplomacy.requestExpired", request.RecipientCountryId, request.SenderCountryId);
                }

                if (relation.Kind == RelationKind.Allied)
                {
                    if (Tick >= relation.AllianceUntilTick)
                    {
                        EndAlliance(relation);
                        AddDiplomacyEvent("diplomacy.allianceExpired", relation.FirstCountryId, relation.SecondCountryId);
                    }
                    else if (!relation.HasExpiryWarning &&
                        relation.AllianceUntilTick - Tick <= _balance.AllianceExpiryWarningTicks)
                    {
                        relation.HasExpiryWarning = true;
                        AddDiplomacyEvent("diplomacy.allianceExpiring", relation.FirstCountryId, relation.SecondCountryId);
                        AddDiplomacyEvent("diplomacy.allianceExpiring", relation.SecondCountryId, relation.FirstCountryId);
                    }
                }
                else if (relation.Kind == RelationKind.War)
                {
                    if (HasUnfinishedHostileOperations(relation.FirstCountryId, relation.SecondCountryId))
                    {
                        relation.LastUnsettledTick = Tick;
                    }
                    else if (Tick - relation.LastUnsettledTick >= _balance.PeaceInactivityTicks)
                    {
                        relation.Kind = RelationKind.Peace;
                        AddDiplomacyEvent("diplomacy.peaceRestored", relation.FirstCountryId, relation.SecondCountryId);
                    }
                }
            }
        }

        private bool HasUnfinishedHostileOperations(int firstCountryId, int secondCountryId)
        {
            for (var i = 0; i < _operations.Count; i++)
            {
                var operation = _operations[i];
                if (IsPair(operation.CountryId, operation.TargetCountryId, firstCountryId, secondCountryId))
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearCountryDiplomacy(int countryId)
        {
            if (_relations == null)
            {
                return;
            }

            _traitorUntilTicks[countryId] = 0;
            for (var i = 0; i < _relations.Length; i++)
            {
                var relation = _relations[i];
                if (relation.FirstCountryId == countryId || relation.SecondCountryId == countryId)
                {
                    ClearRelation(relation);
                }
            }
        }

        private void DisposeDiplomacy()
        {
            if (_relations == null)
            {
                return;
            }

            for (var i = 0; i < _relations.Length; i++)
            {
                ClearRelation(_relations[i]);
            }

            Array.Clear(_traitorUntilTicks, 0, _traitorUntilTicks.Length);
        }

        private bool TryGetLivingDiplomacyPair(int firstCountryId, int secondCountryId, out RelationState relation)
        {
            relation = GetRelation(firstCountryId, secondCountryId);
            return relation != null && GetCountry(firstCountryId).IsAlive && GetCountry(secondCountryId).IsAlive;
        }

        private bool IsAllianceActive(RelationState relation)
        {
            return relation != null && relation.Kind == RelationKind.Allied && Tick < relation.AllianceUntilTick;
        }

        private static bool IsPair(int first, int second, int expectedFirst, int expectedSecond)
        {
            return (first == expectedFirst && second == expectedSecond) ||
                (first == expectedSecond && second == expectedFirst);
        }

        private static void EndAlliance(RelationState relation)
        {
            relation.Kind = RelationKind.Peace;
            relation.Request = null;
            relation.AllianceUntilTick = 0;
            relation.HasExpiryWarning = false;
        }

        private static void ClearRelation(RelationState relation)
        {
            EndAlliance(relation);
            relation.LastHostileActionTick = 0;
            relation.LastUnsettledTick = 0;
            relation.IsFirstCountryEmbargoing = false;
            relation.IsSecondCountryEmbargoing = false;
        }

        private void AddDiplomacyEvent(string key, int countryId, int targetCountryId)
        {
            _events.Add(new MatchEvent(key, countryId, targetCountryId: targetCountryId));
        }
    }
}
