namespace MassTransit.EntityFrameworkCoreIntegration.Saga.Context
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using GreenPipes;
    using MassTransit.Context;
    using MassTransit.Saga;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Storage;


    public class EntityFrameworkSagaRepositoryContextFactory<TSaga> :
        ISagaRepositoryContextFactory<TSaga>
        where TSaga : class, ISaga
    {
        readonly ISagaDbContextFactory<TSaga> _dbContextFactory;
        readonly ISagaConsumeContextFactory<DbContext, TSaga> _consumeContextFactory;
        readonly ISagaRepositoryLockStrategy<TSaga> _lockStrategy;

        public EntityFrameworkSagaRepositoryContextFactory(ISagaDbContextFactory<TSaga> dbContextFactory,
            ISagaConsumeContextFactory<DbContext, TSaga> consumeContextFactory, ISagaRepositoryLockStrategy<TSaga> lockStrategy)
        {
            _dbContextFactory = dbContextFactory;
            _consumeContextFactory = consumeContextFactory;
            _lockStrategy = lockStrategy;
        }

        public void Probe(ProbeContext context)
        {
            var dbContext = _dbContextFactory.Create();
            try
            {
                context.Add("persistence", "entity-framework");
                context.Add("entities", dbContext.Model.GetEntityTypes().Select(type => type.Name).ToArray());
            }
            finally
            {
                dbContext.Dispose();
            }
        }

        public async Task Send<T>(ConsumeContext<T> context, IPipe<SagaRepositoryContext<TSaga, T>> next)
            where T : class
        {
            var dbContext = _dbContextFactory.CreateScoped(context);
            try
            {
                Task Send() =>
                    WithinTransaction(dbContext, context.CancellationToken, () =>
                    {
                        var repositoryContext = new DbContextSagaRepositoryContext<TSaga, T>(dbContext, context, _consumeContextFactory, _lockStrategy);

                        return next.Send(repositoryContext);
                    });

                var executionStrategy = dbContext.Database.CreateExecutionStrategy();
                if (executionStrategy is ExecutionStrategy)
                {
                    await executionStrategy.ExecuteAsync(Send).ConfigureAwait(false);
                }
                else
                {
                    await Send().ConfigureAwait(false);
                }
            }
            finally
            {
                _dbContextFactory.Release(dbContext);
            }
        }

        public async Task SendQuery<T>(ConsumeContext<T> context, ISagaQuery<TSaga> query, IPipe<SagaRepositoryQueryContext<TSaga, T>> next)
            where T : class
        {
            var dbContext = _dbContextFactory.CreateScoped(context);
            try
            {
                async Task Send()
                {
                    var lockContext = await _lockStrategy.CreateLockContext(dbContext, query, context.CancellationToken).ConfigureAwait(false);

                    var repositoryContext = new DbContextSagaRepositoryContext<TSaga, T>(dbContext, context, _consumeContextFactory, _lockStrategy);

                    await WithinTransaction(dbContext, context.CancellationToken, async () =>
                    {
                        var instances = await lockContext.Load().ConfigureAwait(false);

                        var queryContext = new LoadedSagaRepositoryQueryContext<TSaga, T>(repositoryContext, instances);

                        await next.Send(queryContext).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }

                var executionStrategy = dbContext.Database.CreateExecutionStrategy();
                if (executionStrategy is ExecutionStrategy)
                {
                    await executionStrategy.ExecuteAsync(Send).ConfigureAwait(false);
                }
                else
                {
                    await Send().ConfigureAwait(false);
                }
            }
            finally
            {
                _dbContextFactory.Release(dbContext);
            }
        }

        public async Task<T> Execute<T>(Func<SagaRepositoryContext<TSaga>, Task<T>> asyncMethod, CancellationToken cancellationToken = default)
            where T : class
        {
            var dbContext = _dbContextFactory.Create();
            try
            {
                Task<T> Send() =>
                    WithinTransaction(dbContext, cancellationToken, () =>
                    {
                        var sagaRepositoryContext = new DbContextSagaRepositoryContext<TSaga>(dbContext, cancellationToken);

                        return asyncMethod(sagaRepositoryContext);
                    });

                var executionStrategy = dbContext.Database.CreateExecutionStrategy();
                if (executionStrategy is ExecutionStrategy)
                {
                    return await executionStrategy.ExecuteAsync(Send).ConfigureAwait(false);
                }
                else
                {
                    return await Send().ConfigureAwait(false);
                }
            }
            finally
            {
                _dbContextFactory.Release(dbContext);
            }
        }

        async Task WithinTransaction(DbContext context, CancellationToken cancellationToken, Func<Task> callback)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(_lockStrategy.IsolationLevel, cancellationToken).ConfigureAwait(false);

            void Rollback()
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception innerException)
                {
                    LogContext.Warning?.Log(innerException, "Transaction rollback failed");
                }
            }

            try
            {
                await callback().ConfigureAwait(false);

                transaction.Commit();
            }
            catch (DbUpdateConcurrencyException)
            {
                Rollback();
                throw;
            }
            catch (DbUpdateException)
            {
                Rollback();
                throw;
            }
            catch (Exception)
            {
                Rollback();
                throw;
            }
        }

        async Task<T> WithinTransaction<T>(DbContext context, CancellationToken cancellationToken, Func<Task<T>> callback)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(_lockStrategy.IsolationLevel, cancellationToken).ConfigureAwait(false);

            void Rollback()
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception innerException)
                {
                    LogContext.Warning?.Log(innerException, "Transaction rollback failed");
                }
            }

            try
            {
                var result = await callback().ConfigureAwait(false);

                transaction.Commit();

                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                Rollback();
                throw;
            }
            catch (DbUpdateException)
            {
                Rollback();
                throw;
            }
            catch (Exception)
            {
                Rollback();
                throw;
            }
        }
    }
}
