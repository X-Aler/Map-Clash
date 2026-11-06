# D13 — integration proposal

Status: staged outside Assets; root owns actual integration and Unity verification.
Readiness: 95%; goal and balance follow GDD §3.3/§6.1, with edge cases below
confirmed by root. No new package, scope or permanent architecture is introduced.

## Files

- `DiplomacyContracts.cs` → runtime Core alongside `MatchContracts.cs`.
- `MatchSimulation.Diplomacy.cs` → runtime Core as another existing partial.
- `DiplomacyTests.cs` → existing EditMode test assembly.

## MatchContracts additions

Append command enum members without changing the existing numeric values:

```csharp
OfferAlliance, AcceptAlliance, RenewAlliance, BreakAlliance,
SetEmbargo, SendGold, SendTroops
```

Append `int targetCountryId = 0` to the `MatchCommand` constructor, assign it,
and expose `public int TargetCountryId { get; }`. Preserve existing parameter
order and defaults. Country targets never occupy `TargetCell` or `ObjectId`.

`Amount` meanings:

| Command | Amount |
|---|---|
| Offer / Accept / Renew / Break alliance | Ignored |
| SetEmbargo | Exactly 1 to enable; exactly 0 to disable |
| SendGold | Exact finite positive currency amount; cannot exceed sender's gold |
| SendTroops | Requested fraction in (0,1]; send=floor(min(fraction×D,(1-reserve)×D)) |

`AcceptAlliance`: actor is the request recipient, targetCountryId is the sender.
Gold and troops are transfers, not newly earned income. Workers/deployed/returning
troops never fund a donation. Recipient capacity does not delete donated people;
the existing population system stops growth while over capacity.

Append `int targetCountryId = 0` after `detail` in the `MatchEvent` constructor,
assign it and expose `public int TargetCountryId { get; }`. Existing events retain
their signature/behavior. Diplomacy events carry typed pair identities instead
of packing IDs into display strings. CountryId is the affected/request-recipient
country; TargetCountryId is the other country. Betrayal and war are world events.
Expiry warning emits one event for each alliance participant, once per term.

Add these public members to `IMatchSimulation`:

```csharp
IReadOnlyList<RelationState> Relations { get; }
RelationState GetRelation(int firstCountryId, int secondCountryId);
bool CanAttack(int attackingCountryId, int targetCountryId);
bool CanTrade(int firstCountryId, int secondCountryId);
long GetTraitorTicksRemaining(int countryId);
double GetTraitorDefenseMultiplier(int countryId);
double GetTraitorAdvanceMultiplier(int countryId);
```

`GetRelation(a,b)` and `GetRelation(b,a)` return the same read-only-to-consumers
pair model. Embargo flags are directional; either side blocks foreign trade.
CanTrade(a,a) permits internal trade for a living country. Dead/invalid countries
cannot trade or attack; targetCountryId=0 is neutral land for CanAttack only.

## Balance additions

Add properties to SimulationBalance, copy them in CopyBalance, and expose through
the established balance config where authoring is needed:

```csharp
public int AllianceRequestTicks { get; set; } = 300;
public int AllianceDurationTicks { get; set; } = 2400;
public int AllianceExpiryWarningTicks { get; set; } = 300;
public int TraitorDurationTicks { get; set; } = 300;
public int PeaceInactivityTicks { get; set; } = 600;
public double TraitorDefenseMultiplier { get; set; } = 0.5;
public double TraitorAdvanceMultiplier { get; set; } = 0.8;
```

Validation: durations positive; warning ≥0 and < alliance duration; defense/advance
multipliers finite and in [0,1]. Existing reserve fraction remains the single
source for attack and military assistance.

## Required hooks

1. Constructor: call `InitializeDiplomacy()` after `InitializeCountries()`.
2. `Step()`: inside the Playing guard call `TickDiplomacy()` **before** bot
   decisions and `ExecuteCommands()`. This expires deadlines before commands at
   their boundary and observes operations completing later in the same tick.
3. `ExecuteCommands()`: default branch calls `TryExecuteDiplomacyCommand(command)`;
   only unrecognized commands get the existing Reject. Recognized invalid commands
   already emit exactly one rejection.
4. `TryAttack()`: validate `CanAttack(actor,targetOwner)` before any resource
   mutation. After a valid send is confirmed, call
   `RegisterHostileAction(actor,targetOwner)` once for both a new wave and a
   reinforcement. Neutral expansion does not declare war.
5. `ProcessOperations()`: multiply the country's advance budget by
   `GetTraitorAdvanceMultiplier(country.Id)`.
6. `AdvanceWave()`: multiply attacker loss by
   `GetTraitorDefenseMultiplier(defenderId)` (0.5 on a traitor). Do not halve the
   defender's own troop loss; the GDD multiplier changes enemy attack cost.
7. `AnnexCountry()`: call `ClearCountryDiplomacy(defeated.Id)` immediately after
   setting IsAlive=false. All its requests, alliances, embargo flags and traitor
   state are cleared without affecting other pairs.
8. `Dispose()`: call `DisposeDiplomacy()` once before returning. Captured read models
   are also cleared; no static match state is introduced.
9. Later D16–D19: use CanAttack before hostile landing/nuclear launch and call
   RegisterHostileAction after successful validation/payment. Extend
   HasUnfinishedHostileOperations with real naval/missile operations; do not count
   ordinary trade as war activity. D20 AI and UI use these same queued commands.

## Confirmed edge cases

- One live request per unordered pair; crossed/duplicate offers do not silently
  accept or extend a request. Only the named recipient can accept before expiry.
- A pending offer does not grant protection. A valid hostile action cancels it.
- RenewAlliance is a new 30-second request while allied; only the other country's
  acceptance starts a fresh 240-second term. No unilateral extension.
- Acceptance ends current advancing pair land waves through natural 100% return.
  Already returning waves remain returning; troop ownership is preserved.
- Natural expiry restores peace without traitor penalties. Manual break sets the
  actor's 300-tick penalty immediately, even if no attack follows.
- Peace requires **600 uninterrupted ticks** without hostile actions or unfinished
  pair combat operations. Returning land waves count until arrival. The full
  interval starts after the last operation finishes, not only after its launch.
- Pause does not consume any diplomacy deadline. All timers use simulation Tick.

## Localization keys emitted

`diplomacy.allianceRequested`, `diplomacy.renewalRequested`,
`diplomacy.allianceAccepted`, `diplomacy.allianceRenewed`,
`diplomacy.allianceExpiring`, `diplomacy.allianceExpired`,
`diplomacy.requestExpired`, `diplomacy.betrayal`,
`diplomacy.warDeclared`, `diplomacy.peaceRestored`,
`diplomacy.embargoStarted`, `diplomacy.embargoEnded`,
`diplomacy.goldReceived`, `diplomacy.troopsReceived`.

Root must add RU/EN table entries and UI/green-border presentation separately.
Draft implementation and isolated verification do not close D13 in the plan;
actual compile, Unity tests, integration and gameplay verification are still due.
