# D12 building integration draft

Files remain outside Assets until the first land-loop compilation/tests finish.
Copy the implementation to `Assets/_Project/Core/MatchSimulation.Buildings.cs` and
the tests to `Assets/_Project/Tests/EditMode/BuildingSimulationTests.cs` as part of
the next integrated change. They are drafts, not an accepted D12 stage.

## Contract additions

```csharp
public enum BuildingKind { City, Factory, Port, DefensePost, MissileSilo, Sam }
// Append Build, Upgrade to CommandKind; do not reorder existing values.

public sealed class BuildingState
{
    public int Id { get; internal set; }
    public int CountryId { get; internal set; }
    public int Cell { get; internal set; }
    public BuildingKind Kind { get; internal set; }
    public int Level { get; internal set; }
    public long CooldownUntilTick { get; internal set; }
}

// IMatchSimulation:
IReadOnlyList<BuildingState> Buildings { get; }
```

Commands reuse the existing fields:

- Build: TargetCell=placement cell, ObjectId=(int)BuildingKind.
- Upgrade: ObjectId=existing building ID; TargetCell is ignored.
- Build on the same kind of existing own economic building upgrades it, matching GDD §5.3.
- Upgrade never creates a building and is invalid for DefensePost/MissileSilo/Sam.

## SimulationBalance additions and defaults from GDD

```csharp
public double EconomicBuildingBasePrice { get; set; } = 125000;
public double EconomicBuildingPriceMultiplier { get; set; } = 2;
public int MaxEconomicBuildingLevel { get; set; } = 4;
public double BuildingMinimumSpacing { get; set; } = 12;
public double DefensePostFirstPrice { get; set; } = 50000;
public double DefensePostPriceStep { get; set; } = 50000;
public double DefensePostMaxPrice { get; set; } = 250000;
public double DefensePostRadius { get; set; } = 40;
public double DefensePostLossMultiplier { get; set; } = 6;
public double DefensePostCaptureSpeedMultiplier { get; set; } = 0.34;
public double MissileSiloPrice { get; set; } = 1000000;
public double SamFirstPrice { get; set; } = 1500000;
public double SamAdditionalPrice { get; set; } = 3000000;
```

Add these fields to CopyBalance, GameBalanceConfig authored fields/ToBalance, and
ValidateBalance. Prices, radius, spacing must be finite and nonnegative; economic
multiplier must be positive and finite, levels >=1, defense loss multiplier >0,
and capture speed multiplier in (0,1]. Economic defaults generate exact level
prices 125K,250K,500K,1M. Existing CityPopulationBonus=25000 remains the sole city
bonus setting.

## Hooks

1. Constructor: InitializeBuildings() immediately after InitializeCountries().
2. Dispose: DisposeBuildings() before clearing countries; do not leave building
   wrappers populated after match release.
3. ExecuteCommands switch: Build => TryBuild(command); Upgrade => TryUpgrade(command).
4. SetOwner: after changing owner/land counts, call
   UpdateBuildingOwnership(cell, previousOwner, countryId). No map-cell scan is needed.
5. RefreshPopulationLimit: base formula plus CityLevelBonus(country.Id), then apply
   the existing Hard bot multiplier to the resulting capacity.
6. AdvanceWave: multiply attacker loss by GetDefenseLossMultiplier(defenderId,cell).
   Multiply terrain capture speed by GetDefenseSpeedMultiplier(defenderId,cell).
   Defender density casualties are not multiplied by the post.
7. Frontier priority: include defense coverage in the priority, and use the same
   calculation both in AddCandidate and TryGetFrontier. Covered cells should be
   deprioritized relative to ordinary terrain; a safe cost-based score is terrain
   loss factor * defense loss multiplier, followed by own-neighbour count and cell
   index tie-break. A building construction/capture/destruction may change priority
   of existing frontier nodes; refresh affected active enemy frontiers after these
   events (building command frequency, not every tick/full map).
8. D19 can call DestroyBuilding(id) or neutralize the cell through SetOwner(cell,0).
   It is idempotent: a second destruction returns false and produces no event.

## Interpretation and performance

- New DefensePost/Sam prices use currently owned counts, including captured
  buildings. Capture/destruction updates those counts. Root explicitly accepted
  this interpretation during D12 draft preparation.
- Spacing applies between all different buildings, regardless of owner, through
  the horizontal seam. Exactly 12 base cells is allowed. No north/south wrapping.
- Port requires a represented water neighbour. A map's north/south boundary alone
  is not treated as water.
- Capture preserves level and existing cooldown. Neutralization destroys.
- City-level sums and kind counts are cached per country; ownership changes use
  dictionaries keyed by cell/ID. Defense scans only that defender's post list.
- No trains, trade, missiles, SAM interception, fleet, or AI building decisions are
  implemented here. MissileSilo/Sam are placeable structures for later D18 wiring.

## Localization keys

Events: building.constructed, building.upgraded, building.captured, building.destroyed.
Failures: building.invalidPlacement, building.insufficientGold, building.maxLevel,
building.invalidUpgrade. CountryId is the acting/receiving country, or old owner on
destruction. No player-facing literal is embedded in the simulation.

## Audit follow-up from the first land loop

AnnexCountry currently credits half the enemy treasury to Gold without adding that
loot to EarnedGold. If result income includes trophies, use one local `loot` amount
for both Gold and EarnedGold. This draft does not edit the stable Assets baseline.
