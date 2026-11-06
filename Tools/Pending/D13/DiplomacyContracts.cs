namespace MapClash
{
    public enum RelationKind
    {
        Peace,
        War,
        Allied
    }

    public sealed class AllianceRequestState
    {
        internal AllianceRequestState(int senderCountryId, int recipientCountryId, long expiresAtTick, bool isRenewal)
        {
            SenderCountryId = senderCountryId;
            RecipientCountryId = recipientCountryId;
            ExpiresAtTick = expiresAtTick;
            IsRenewal = isRenewal;
        }

        public int SenderCountryId { get; }
        public int RecipientCountryId { get; }
        public long ExpiresAtTick { get; }
        public bool IsRenewal { get; }
    }

    public sealed class RelationState
    {
        internal RelationState(int firstCountryId, int secondCountryId)
        {
            FirstCountryId = firstCountryId;
            SecondCountryId = secondCountryId;
        }

        public int FirstCountryId { get; }
        public int SecondCountryId { get; }
        public RelationKind Kind { get; internal set; }
        public AllianceRequestState Request { get; internal set; }
        public long AllianceUntilTick { get; internal set; }
        public bool HasExpiryWarning { get; internal set; }
        public long LastHostileActionTick { get; internal set; }
        public long LastUnsettledTick { get; internal set; }
        public bool IsFirstCountryEmbargoing { get; internal set; }
        public bool IsSecondCountryEmbargoing { get; internal set; }
    }
}
