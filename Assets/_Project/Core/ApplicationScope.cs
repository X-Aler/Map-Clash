using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MapClash
{
    public sealed class ApplicationScope : LifetimeScope
    {
        [SerializeField] private TextAsset _terrainData;
        [SerializeField] private GameBalanceConfig _balance;
        [SerializeField] private GameApplication _application;

        protected override void Configure(IContainerBuilder builder)
        {
            if (_terrainData == null || _balance == null || _application == null)
            {
                throw new System.InvalidOperationException("MapClash: Bootstrap requires world data, game balance and application references.");
            }

            builder.RegisterInstance(WorldMapData.Load(_terrainData.bytes));
            builder.RegisterInstance(_balance);
            builder.Register<MatchSessionFactory>(Lifetime.Singleton);
            builder.RegisterComponent(_application);
        }
    }
}
