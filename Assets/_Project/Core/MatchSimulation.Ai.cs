namespace MapClash
{
    public sealed partial class MatchSimulation
    {
        private void QueueBotDecisions()
        {
            var decisionPeriod = Settings.Difficulty == AiDifficulty.Easy ? 20 : SimulationBalance.TicksPerSecond;
            for (var i = 1; i < _countries.Length; i++)
            {
                var country = _countries[i];
                if (!country.IsAlive || Tick % decisionPeriod != country.Id % decisionPeriod ||
                    country.DefendingTroops < _balance.StartingPopulation * 0.2 ||
                    country.DeployedTroops > country.DefendingTroops)
                {
                    continue;
                }

                var existingExpansion = FindAdvancingWave(country.Id, 0);
                if (existingExpansion != null && existingExpansion.State.Troops > country.DefendingTroops * 0.2)
                {
                    continue;
                }

                var neutralCell = -1;
                var neutralCandidates = 0;
                var enemyCell = -1;
                var bestEnemyScore = 0.0;
                foreach (var border in _borders[country.Id])
                {
                    for (var direction = 0; direction < 4; direction++)
                    {
                        var cell = Map.GetNeighbour(border, direction);
                        if (cell < 0 || !Map.IsLand(cell))
                        {
                            continue;
                        }

                        var owner = _owners[cell];
                        if (owner == 0)
                        {
                            neutralCandidates++;
                            if (NextRandom(neutralCandidates) == 0)
                            {
                                neutralCell = cell;
                            }
                        }
                        else if (owner != country.Id)
                        {
                            var enemy = GetCountry(owner);
                            var score = country.DefendingTroops / (1 + enemy.DefendingTroops);
                            if (score > bestEnemyScore)
                            {
                                bestEnemyScore = score;
                                enemyCell = cell;
                            }
                        }
                    }
                }

                var targetCell = neutralCell;
                var aggressionThreshold = Settings.Difficulty == AiDifficulty.Easy ? 2 : 1.35;
                if (enemyCell >= 0 && bestEnemyScore >= aggressionThreshold && (neutralCell < 0 || NextRandom(5) == 0))
                {
                    targetCell = enemyCell;
                }

                if (targetCell >= 0)
                {
                    var ratio = Settings.Difficulty == AiDifficulty.Easy ? 0.1 : Settings.Difficulty == AiDifficulty.Hard ? 0.35 : 0.25;
                    Enqueue(new MatchCommand(CommandKind.Attack, country.Id, targetCell, ratio));
                }
            }
        }
    }
}
