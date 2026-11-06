"""Prepare an isolated review copy; never changes Assets or invokes Unity."""
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
OUTPUT = HERE / "Snapshot"
OUTPUT.mkdir(exist_ok=True)
CORE = ROOT / "Assets/_Project/Core"


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError(f"Expected one integration anchor: {before}")
    return source.replace(before, after, 1)


contract = (CORE / "MatchContracts.cs").read_text(encoding="utf-8")
contract = replace_once(contract, "ChooseSpawn, Attack, Recall, SetTroopRatio", "ChooseSpawn, Attack, Recall, SetTroopRatio, OfferAlliance, AcceptAlliance, RenewAlliance, BreakAlliance, SetEmbargo, SendGold, SendTroops")
contract = replace_once(contract, "int objectId = -1)", "int objectId = -1, int targetCountryId = 0)")
contract = replace_once(contract, "ObjectId = objectId;", "ObjectId = objectId;\n            TargetCountryId = targetCountryId;")
contract = replace_once(contract, "public int ObjectId { get; }", "public int ObjectId { get; }\n        public int TargetCountryId { get; }")
contract = replace_once(contract, 'string detail = "")', 'string detail = "", int targetCountryId = 0)')
contract = replace_once(contract, "Detail = detail;", "Detail = detail;\n            TargetCountryId = targetCountryId;")
contract = replace_once(contract, "public string Detail { get; }", "public string Detail { get; }\n        public int TargetCountryId { get; }")
balance = """
        public int AllianceRequestTicks { get; set; } = 300;
        public int AllianceDurationTicks { get; set; } = 2400;
        public int AllianceExpiryWarningTicks { get; set; } = 300;
        public int TraitorDurationTicks { get; set; } = 300;
        public int PeaceInactivityTicks { get; set; } = 600;
        public double TraitorDefenseMultiplier { get; set; } = 0.5;
        public double TraitorAdvanceMultiplier { get; set; } = 0.8;
"""
contract = replace_once(contract, "public const int TicksPerSecond = 10;", "public const int TicksPerSecond = 10;" + balance)
(OUTPUT / "MatchContracts.cs").write_text(contract, encoding="utf-8")

for path in CORE.glob("MatchSimulation*.cs"):
    source = path.read_text(encoding="utf-8")
    if path.name == "MatchSimulation.cs":
        source = replace_once(source, "InitializeCountries();", "InitializeCountries();\n            InitializeDiplomacy();")
        source = replace_once(source, "_isDisposed = true;", "_isDisposed = true;\n            DisposeDiplomacy();")
        source = replace_once(source, "QueueBotDecisions();", "TickDiplomacy();\n                QueueBotDecisions();")
        source = replace_once(source, "default:\n                        Reject(command.CountryId);", "default:\n                        if (!TryExecuteDiplomacyCommand(command))\n                        {\n                            Reject(command.CountryId);\n                        }")
        names = ["AllianceRequestTicks", "AllianceDurationTicks", "AllianceExpiryWarningTicks", "TraitorDurationTicks", "PeaceInactivityTicks", "TraitorDefenseMultiplier", "TraitorAdvanceMultiplier"]
        copies = ",\n".join(f"                {name} = source.{name}" for name in names)
        source = replace_once(source, "MaxAdvancePerSecond = source.MaxAdvancePerSecond", "MaxAdvancePerSecond = source.MaxAdvancePerSecond,\n" + copies)
    elif path.name == "MatchSimulation.Combat.cs":
        source = replace_once(source, "if (targetOwner == command.CountryId)", "if (!CanAttack(command.CountryId, targetOwner))")
        source = replace_once(source, "var wave = FindAdvancingWave(command.CountryId, targetOwner);", "RegisterHostileAction(command.CountryId, targetOwner);\n            var wave = FindAdvancingWave(command.CountryId, targetOwner);")
        source = replace_once(source, "var waveCredit = budget * TickDuration", "var waveCredit = budget * GetTraitorAdvanceMultiplier(country.Id) * TickDuration")
        source = replace_once(source, "+ defenderLoss) * terrainLoss;", "+ defenderLoss) * terrainLoss * GetTraitorDefenseMultiplier(defenderId);")
        source = replace_once(source, "defeated.IsAlive = false;", "defeated.IsAlive = false;\n            ClearCountryDiplomacy(defeated.Id);")
    (OUTPUT / path.name).write_text(source, encoding="utf-8")

(OUTPUT / "WorldMapData.cs").write_text((ROOT / "Assets/_Project/Maps/WorldMapData.cs").read_text(encoding="utf-8"), encoding="utf-8")
print("Prepared isolated D13 core snapshot; no Unity assets modified.")
