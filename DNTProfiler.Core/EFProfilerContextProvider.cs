﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity.Core.Metadata.Edm;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Infrastructure.Interception;
using System.Linq;
using System.Threading.Tasks;
using DNTProfiler.Common.Profiler;
using DNTProfiler.Common.Threading;
using DNTProfiler.Common.Toolkit;

namespace DNTProfiler.EntityFramework.Core
{
    public static class EFProfilerContextProvider
    {
        private static readonly ConcurrentDictionary<int, HashSet<string>> _keys = new ConcurrentDictionary<int, HashSet<string>>();

        public static DbCommandContext GetLoggedDbCommand<TResult>(DbCommand command, DbCommandInterceptionContext<TResult> interceptionContext)
        {
            var context = new DbCommandContext
            {
                IsAsync = interceptionContext.IsAsync,
                IsCanceled = interceptionContext.TaskStatus.HasFlag(TaskStatus.Canceled),
                Exception = interceptionContext.OriginalException ?? interceptionContext.Exception
            };
            setBaseInfo(interceptionContext, context);
            return context;
        }

        public static DbConnectionContext GetLoggedDbConnection(IDbConnection connection, BeginTransactionInterceptionContext interceptionContext)
        {
            var context = new DbConnectionContext
            {
                IsAsync = interceptionContext.IsAsync,
                IsCanceled = interceptionContext.TaskStatus.HasFlag(TaskStatus.Canceled),
                Exception = interceptionContext.OriginalException ?? interceptionContext.Exception,
                ConnectionString = connection.ConnectionString,
                TransactionId =
                    interceptionContext.Result == null
                        ? (int?)null
                        : UniqueIdExtensions<DbTransaction>.GetUniqueId(interceptionContext.Result).ToInt(),
                IsolationLevel =
                    interceptionContext.Result == null ? IsolationLevel.Unspecified : interceptionContext.Result.IsolationLevel
            };
            setBaseInfo(interceptionContext, context);
            return context;
        }

        public static DbConnectionContext GetLoggedDbConnection(DbConnection connection, MutableInterceptionContext interceptionContext)
        {
            var context = new DbConnectionContext
            {
                IsAsync = interceptionContext.IsAsync,
                IsCanceled = interceptionContext.TaskStatus.HasFlag(TaskStatus.Canceled),
                Exception = interceptionContext.OriginalException ?? interceptionContext.Exception,
                ConnectionString = connection.ConnectionString
            };
            setBaseInfo(interceptionContext, context);
            if (context.ConnectionId == null)
            {
                context.ConnectionId = UniqueIdExtensions<DbConnection>.GetUniqueId(connection).ToInt();
            }
            return context;
        }

        public static DbTransactionContext GetLoggedDbTransaction(DbTransaction transaction, MutableInterceptionContext interceptionContext)
        {
            var context = new DbTransactionContext
            {
                IsAsync = interceptionContext.IsAsync,
                IsCanceled = interceptionContext.TaskStatus.HasFlag(TaskStatus.Canceled),
                Exception = interceptionContext.OriginalException ?? interceptionContext.Exception
            };
            setBaseInfo(interceptionContext, context);
            context.TransactionId = UniqueIdExtensions<DbTransaction>.GetUniqueId(transaction).ToInt();
            return context;
        }

        public static DbCommandContext GetLoggedResult<TResult>(DbCommand command, DbCommandInterceptionContext<TResult> interceptionContext, long? elapsedMilliseconds, DataTable dataTable)
        {
            var context = new DbCommandContext
            {
                IsAsync = interceptionContext.IsAsync,
                IsCanceled = interceptionContext.TaskStatus.HasFlag(TaskStatus.Canceled),
                Exception = interceptionContext.OriginalException ?? interceptionContext.Exception,
                Result = interceptionContext.Result,
                ElapsedMilliseconds = elapsedMilliseconds,
                DataTable = dataTable,
                Keys = getKeys(interceptionContext)
            };
            setBaseInfo(interceptionContext, context);
            return context;
        }

        private static ISet<string> getKeys(DbInterceptionContext interceptionContext)
        {
            if (interceptionContext.ObjectContexts == null || !interceptionContext.ObjectContexts.Any())
            {
                return new HashSet<string>();
            }

            var objectContext = interceptionContext.ObjectContexts.First();
            var objectContextId = UniqueIdExtensions<ObjectContext>.GetUniqueId(objectContext).ToInt();

            HashSet<string> keys;
            if (_keys.TryGetValue(objectContextId, out keys))
            {
                return keys;
            }

            var results = new HashSet<string>();
            var entityProps = objectContext.MetadataWorkspace.GetItems<EntityType>(DataSpace.SSpace);
            foreach (var entityType in entityProps)
            {
                foreach (var edmProperty in entityType.KeyProperties)
                {
                    results.Add(edmProperty.Name);
                }
            }

            _keys.TryAdd(objectContextId, results);

            return results;
        }

        private static void setBaseInfo(DbInterceptionContext interceptionContext, DbContextBase info)
        {
            if (interceptionContext.ObjectContexts == null || !interceptionContext.ObjectContexts.Any())
                return;

            var context = interceptionContext.ObjectContexts.First(); //todo: ??
            info.ObjectContextId = UniqueIdExtensions<ObjectContext>.GetUniqueId(context).ToInt();
            info.ObjectContextName = context.DefaultContainerName;
            if (context.TransactionHandler != null && context.TransactionHandler.Connection != null)
            {
                info.ConnectionId = UniqueIdExtensions<DbConnection>.GetUniqueId(context.TransactionHandler.Connection).ToInt();
            }
        }
    }
}