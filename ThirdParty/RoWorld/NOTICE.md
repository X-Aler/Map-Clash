# RoWorld land-combat port

The land-combat implementation in Assets/_Project/Core/MatchSimulation.Combat.cs and RoWorldCombatRules.cs is a C# port derived from:

https://github.com/lukechatton/roworld
Commit: ae7a3505e6f3736aef85fffc1879c0476f819c59
Original modules: src/shared/Core/SimAttacks.luau and the attacks section of src/shared/Core/Rules.luau.
License: GNU Affero General Public License, version 3 (full text in COPYING).

The project owner explicitly authorized the AGPL port on 2026-11-06. Retain this notice and COPYING with distributions of the derived code and comply with the AGPL source/distribution/network-use terms. This notice does not grant rights beyond the original license.

Integration adapters: Map Clash ownership/events/population model and existing wrapped world map; terrain 1/2/3 maps to plains/highlands/mountains; all Map Clash countries use the reference's player-country combat rules, including AI-controlled participants. Unimplemented reference structures, radiation and diplomacy have no modifiers. Map Clash's deterministic RNG and border enumeration remain the host simulation's, so equal seeds across the two different complete games do not promise identical pixel sequences.
