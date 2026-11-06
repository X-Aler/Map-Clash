using System;
using VContainer;

namespace MapClash
{
    public sealed class MatchSessionFactory
    {
        private readonly IObjectResolver _applicationScope;
        private readonly GameBalanceConfig _balance;

        public MatchSessionFactory(IObjectResolver applicationScope, GameBalanceConfig balance)
        {
            _applicationScope = applicationScope;
            _balance = balance;
        }

        public MatchSession Create(MatchSettings settings)
        {
            var scope = _applicationScope.CreateScope(builder =>
            {
                builder.RegisterInstance(settings);
                builder.RegisterInstance(_balance.CreateBalance());
                builder.Register<MatchSimulation>(Lifetime.Scoped).As<IMatchSimulation>();
            });
            try
            {
                return new MatchSession(scope, scope.Resolve<IMatchSimulation>());
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }
    }

    public sealed class MatchSession : IDisposable
    {
        private IScopedObjectResolver _scope;

        internal MatchSession(IScopedObjectResolver scope, IMatchSimulation simulation)
        {
            _scope = scope;
            Simulation = simulation;
        }

        public IMatchSimulation Simulation { get; }

        public void Dispose()
        {
            _scope?.Dispose();
            _scope = null;
        }
    }
}
