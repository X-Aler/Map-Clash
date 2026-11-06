using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VContainer;

namespace MapClash.Tests
{
    public sealed class MatchScopeTests
    {
        [Test]
        public void DisposingMatchTwiceRejectsCommandsAndNextMatchIsFresh()
        {
            var balance = ScriptableObject.CreateInstance<GameBalanceConfig>();
            try
            {
                var data = File.ReadAllBytes(Path.Combine(Application.dataPath, "_Project/Maps/Resources/WorldTerrain.bytes"));
                var map = WorldMapData.Load(data);
                var builder = new ContainerBuilder();
                builder.RegisterInstance(map);
                builder.RegisterInstance(balance);
                builder.Register<MatchSessionFactory>(Lifetime.Singleton);
                using (var application = builder.Build())
                {
                    var factory = application.Resolve<MatchSessionFactory>();
                    var first = factory.Create(new MatchSettings { CountryCount = 10 });
                    first.Simulation.Advance(16);
                    Assert.That(first.Simulation.Tick, Is.GreaterThan(0));
                    first.Dispose();
                    first.Dispose();
                    Assert.Throws<ObjectDisposedException>(() => first.Simulation.Enqueue(new MatchCommand(CommandKind.Attack, 1, 0)));
                    using (var second = factory.Create(new MatchSettings { CountryCount = 60 }))
                    {
                        Assert.That(second.Simulation.Tick, Is.Zero);
                        Assert.That(second.Simulation.Phase, Is.EqualTo(MatchPhase.Spawn));
                        Assert.That(second.Simulation.Countries.Count, Is.EqualTo(60));
                        Assert.That(second.Simulation.Operations.Count, Is.Zero);
                        Assert.That(second.Simulation.GetCountry(1).TotalPopulation, Is.EqualTo(25000));
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(balance);
            }
        }
    }
}
