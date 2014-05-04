/* 
 * 
 * (c) Copyright Ascensio System Limited 2010-2014
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as
 * published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version.
 * 
 * http://www.gnu.org/licenses/agpl.html 
 * 
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.Web;
using ASC.Common.Data.AdoProxy;
using ASC.Common.Data.Sql;
using ASC.Common.Web;
using log4net;


namespace ASC.Common.Data
{
    public class DbManager : IDbManager
    {
        private readonly ILog logger = LogManager.GetLogger("ASC.SQL");
        private readonly ProxyContext proxyContext;
        private readonly bool shared;

        private IDbCommand command;
        private ISqlDialect dialect;
        private volatile bool disposed;


        private IDbCommand Command
        {
            get
            {
                CheckDispose();
                if (command == null)
                {
                    command = OpenConnection().CreateCommand();
                }
                if (command.Connection.State == ConnectionState.Closed || command.Connection.State == ConnectionState.Broken)
                {
                    command = OpenConnection().CreateCommand();
                }
                return command;
            }
        }

        public string DatabaseId { get; private set; }

        public bool InTransaction
        {
            get { return Command.Transaction != null; }
        }

        public IDbConnection Connection
        {
            get { return Command.Connection; }
        }


        public DbManager(string databaseId)
            : this(databaseId, true)
        {
        }

        public DbManager(string databaseId, bool shared)
        {
            if (databaseId == null) throw new ArgumentNullException("databaseId");
            DatabaseId = databaseId;
            this.shared = shared;

            if (logger.IsDebugEnabled)
            {
                proxyContext = new ProxyContext(AdoProxyExecutedEventHandler);
            }

            Sql.SqlDialect.CheckKeys(this);
        }

        #region IDisposable Members

        public void Dispose()
        {
            lock (this)
            {
                if (disposed) return;
                disposed = true;
                if (command != null)
                {
                    if (command.Connection != null) command.Connection.Dispose();
                    command.Dispose();
                    command = null;
                }
            }
        }

        #endregion

        public static DbManager FromHttpContext(string databaseId)
        {
            if (HttpContext.Current != null)
            {
                var dbManager = DisposableHttpContext.Current[databaseId] as DbManager;
                if (dbManager == null || dbManager.disposed)
                {
                    dbManager = new DbManager(databaseId);
                    DisposableHttpContext.Current[databaseId] = dbManager;
                }
                return dbManager;
            }
            return new DbManager(databaseId);
        }

        private IDbConnection OpenConnection()
        {
            CheckDispose();
            IDbConnection connection = null;
            string key = null;
            if (shared && HttpContext.Current != null)
            {
                key = string.Format("Connection {0}|{1}", GetDialect(), DbRegistry.GetConnectionString(DatabaseId));
                connection = DisposableHttpContext.Current[key] as IDbConnection;
                if (connection != null)
                {
                    var state = ConnectionState.Closed;
                    var disposed = false;
                    try
                    {
                        state = connection.State;
                    }
                    catch (ObjectDisposedException)
                    {
                        disposed = true;
                    }
                    if (!disposed && (state == ConnectionState.Closed || state == ConnectionState.Broken))
                    {
                        if (string.IsNullOrEmpty(connection.ConnectionString))
                        {
                            connection.ConnectionString = DbRegistry.GetConnectionString(DatabaseId).ConnectionString;
                        }
                        connection.Open();
                        return connection;
                    }
                }
            }
            connection = DbRegistry.CreateDbConnection(DatabaseId);
            if (proxyContext != null)
            {
                connection = new DbConnectionProxy(connection, proxyContext);
            }
            connection.Open();
            if (shared && HttpContext.Current != null) DisposableHttpContext.Current[key] = connection;
            return connection;
        }

        public IDbTransaction BeginTransaction()
        {
            if (InTransaction) throw new InvalidOperationException("Transaction already open.");

            Command.Transaction = Command.Connection.BeginTransaction();

            var tx = new DbTransaction(Command.Transaction);
            tx.Unavailable += TransactionUnavailable;
            return tx;
        }

        public IDbTransaction BeginTransaction(IsolationLevel il)
        {
            if (InTransaction) throw new InvalidOperationException("Transaction already open.");

            il = GetDialect().GetSupportedIsolationLevel(il);
            Command.Transaction = Command.Connection.BeginTransaction(il);

            var tx = new DbTransaction(Command.Transaction);
            tx.Unavailable += TransactionUnavailable;
            return tx;
        }

        public IDbTransaction BeginTransaction(bool nestedIfAlreadyOpen)
        {
            return nestedIfAlreadyOpen && InTransaction ? new DbNestedTransaction(Command.Transaction) : BeginTransaction();
        }

        public List<object[]> ExecuteList(string sql, params object[] parameters)
        {
            return Command.ExecuteList(sql, parameters);
        }

        public List<object[]> ExecuteList(ISqlInstruction sql)
        {
            try
            {
                if (sql is SqlQuery)
                    ((SqlQuery)sql).CheckGroups();
                return Command.ExecuteList(sql, GetDialect());
            }
            catch(Exception e)
            {
                throw e;
            }
        }

        public List<T> ExecuteList<T>(ISqlInstruction sql, Converter<IDataRecord, T> converter)
        {
            return Command.ExecuteList(sql, GetDialect(), converter);
        }

        public T ExecuteScalar<T>(string sql, params object[] parameters)
        {
            return Command.ExecuteScalar<T>(sql, parameters);
        }

        public T ExecuteScalar<T>(ISqlInstruction sql)
        {
            return Command.ExecuteScalar<T>(sql, GetDialect());
        }

        public int ExecuteNonQuery(string sql, params object[] parameters)
        {
            return Command.ExecuteNonQuery(sql, parameters);
        }

        public int ExecuteNonQuery(ISqlInstruction sql)
        {
            return Command.ExecuteNonQuery(sql, GetDialect());
        }

        public int ExecuteBatch(IEnumerable<ISqlInstruction> batch)
        {
            if (batch == null) throw new ArgumentNullException("batch");

            var affected = 0;
            using (var tx = BeginTransaction())
            {
                foreach (var sql in batch)
                {
                    affected += ExecuteNonQuery(sql);
                }
                tx.Commit();
            }
            return affected;
        }

        private void TransactionUnavailable(object sender, EventArgs e)
        {
            if (Command.Transaction != null)
            {
                Command.Transaction = null;
            }
        }

        private void CheckDispose()
        {
            if (disposed) throw new ObjectDisposedException(GetType().FullName);
        }

        private ISqlDialect GetDialect()
        {
            return dialect ?? (dialect = DbRegistry.GetSqlDialect(DatabaseId));
        }

        private void AdoProxyExecutedEventHandler(ExecutedEventArgs a)
        {
            ThreadContext.Properties["duration"] = a.Duration.TotalMilliseconds;
            ThreadContext.Properties["sql"] = RemoveWhiteSpaces(a.Sql);
            ThreadContext.Properties["sqlParams"] = RemoveWhiteSpaces(a.SqlParameters);
            logger.Debug(a.SqlMethod);
        }

        private string RemoveWhiteSpaces(string str)
        {
            return !string.IsNullOrEmpty(str) ?
                str.Replace(Environment.NewLine, " ").Replace("\n", "").Replace("\r", "").Replace("\t", " ") :
                string.Empty;
        }
    }
}