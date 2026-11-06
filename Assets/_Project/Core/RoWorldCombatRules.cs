// C# port of lukechatton/roworld Rules.luau (attacks), ae7a3505e6f3736aef85fffc1879c0476f819c59.
// GNU AGPL v3; see ThirdParty/RoWorld/NOTICE.md and COPYING.
using System;

namespace MapClash
{
    public static class RoWorldCombatRules
    {
        public static double TerrainMagnitude(byte terrain) => terrain == 3 ? 120 : terrain == 2 ? 100 : 80;
        public static double TerrainCost(byte terrain) => terrain == 3 ? 25 : terrain == 2 ? 20 : 16.5;

        public static long QueuePriority(long tick, int randomZeroToSix, int ownedNeighbours, byte terrain)
        {
            var magnitude = terrain == 3 ? 2.0 : terrain == 2 ? 1.5 : 1.0;
            var factor = 4 - 2 * ownedNeighbours + (int)Math.Floor(2 * magnitude + 0.5);
            return 4 * tick + (10 + randomZeroToSix) * factor;
        }

        public static double LargeTerritoryBonus(double area, double depth)
        {
            return 1 - depth / (1 + Math.Exp(-2.5 * (Math.Log(Math.Max(area, 1)) - Math.Log(300000))));
        }

        public static void TileCosts(byte terrain, double troops, double defenderTroops,
            double attackerArea, double defenderArea, double border, bool neutral,
            out double attackerLoss, out double defenderLoss, out double tickFraction, double currentDefenderArea = -1)
        {
            var magnitude = TerrainMagnitude(terrain);
            var tileCost = TerrainCost(terrain);
            if (neutral)
            {
                attackerLoss = magnitude / 5;
                defenderLoss = 0;
                tickFraction = Math.Max(5, Math.Min(100, 2000 * tileCost / troops)) / (Math.Max(border, 1) * 2);
            }
            else
            {
                var density = defenderTroops / Math.Max(currentDefenderArea >= 0 ? currentDefenderArea : defenderArea, 1);
                var ratio = defenderTroops / troops;
                var defenderBonus = LargeTerritoryBonus(defenderArea, 0.3);
                var lossBonus = 0.463 * LargeTerritoryBonus(attackerArea, 0.7) * defenderBonus;
                var speedBonus = LargeTerritoryBonus(attackerArea, 0.73) * defenderBonus / (8.55 * Math.Max(border, 1));
                attackerLoss = magnitude * Math.Max(0.6, Math.Min(2, ratio)) * (lossBonus + 0.0039 * density);
                defenderLoss = Math.Floor(density);
                tickFraction = Math.Max(0.82, Math.Min(7.5, ratio)) *
                    Math.Max(1, Math.Min(50, ratio / 20)) * tileCost * speedBonus;
            }

            tickFraction = Math.Max(1e-6, tickFraction);
        }
    }
}
