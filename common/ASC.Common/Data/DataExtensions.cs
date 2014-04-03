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
using System.Text;
using ASC.Common.Data.Sql;

namespace ASC.Common.Data
{
    public static class DataExtensions
    {
        public static List<object[]> ExecuteList(this IDbConnection connection, string sql, params object[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                return command.ExecuteList(sql, parameters);
            }
        }

        public static T ExecuteScalar<T>(this IDbConnection connection, string sql, params object[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                return command.ExecuteScalar<T>(sql, parameters);
            }
        }

        public static int ExecuteNonQuery(this IDbConnection connection, string sql, params object[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                return command.ExecuteNonQuery(sql, parameters);
            }
        }

        public static IDbCommand CreateCommand(this IDbConnection connection, string sql, params object[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.AddParameters(parameters);
            return command;
        }


        public static IDbCommand AddParameter(this IDbCommand command, string name, object value)
        {
            var p = command.CreateParameter();
            if (!string.IsNullOrEmpty(name))
            {
                p.ParameterName = name.StartsWith("@") ? name : "@" + name;
            }
            if (value == null)
            {
                p.Value = DBNull.Value;
            }
            else if (value is Enum)
            {
                p.Value = ((Enum)value).ToString("d");
            }
            else if (value is DateTime)
            {
                var d = (DateTime)value;
                p.Value = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, DateTimeKind.Unspecified);
            }
            else
            {
                p.Value = value;
            }
            command.Parameters.Add(p);
            return command;
        }

        public static IDbCommand AddParameters(this IDbCommand command, params object[] parameters)
        {
            if (parameters == null) return command;
            foreach (var value in parameters)
            {
                if (value != null && value.GetType().Name.StartsWith("<>f__AnonymousType"))
                {
                    foreach (var p in value.GetType().GetProperties())
                    {
                        command.AddParameter(p.Name, p.GetValue(value, null));
                    }
                }
                else
                {
                    command.AddParameter(null, value);
                }
            }
            return command;
        }

        public static List<object[]> ExecuteList(this IDbCommand command)
        {
            return ExecuteList(command, command.CommandText, null);
        }

        public static List<object[]> ExecuteList(this IDbCommand command, string sql, params object[] parameters)
        {
            command.CommandText = sql;
            if (parameters != null)
            {
                command.Parameters.Clear();
                command.AddParameters(parameters);
            }
            return ExecuteListReader(command);
        }

        private static List<object[]> ExecuteListReader(IDbCommand command)
        {
            var result = new List<object[]>();
            using (var reader = command.ExecuteReader())
            {
                var fieldCount = reader.FieldCount;
                while (reader.Read())
                {
                    var row = new object[fieldCount];
                    for (var i = 0; i < fieldCount; i++)
                    {
                        row[i] = reader[i];
                        if (DBNull.Value.Equals(row[i])) row[i] = null;
                    }
                    result.Add(row);
                }
            }
            return result;
        }

        public static T ExecuteScalar<T>(this IDbCommand command)
        {
            return ExecuteScalar<T>(command, command.CommandText, null);
        }

        public static T ExecuteScalar<T>(this IDbCommand command, string sql, params object[] parameters)
        {
            command.CommandText = sql;
            if (parameters != null)
            {
                command.Parameters.Clear();
                command.AddParameters(parameters);
            }

            var scalar = command.ExecuteScalar();

            if (scalar == null || scalar == DBNull.Value)
            {
                return default(T);
            }
            var scalarType = typeof(T);
            if (scalarType == typeof(object))
            {
                return (T)scalar;
            }
            if (scalarType.Name == "Nullable`1")
            {
                scalarType = scalarType.GetGenericArguments()[0];
            }
            return (T)Convert.ChangeType(scalar, scalarType);
        }

        public static int ExecuteNonQuery(this IDbCommand command, string sql, params object[] parameters)
        {
            command.CommandText = sql;
            command.Parameters.Clear();
            command.AddParameters(parameters);
            return command.ExecuteNonQuery();
        }

        public static List<object[]> ExecuteList(this IDbCommand command, ISqlInstruction sql, ISqlDialect dialect)
        {
            ApplySqlInstruction(command, sql, dialect);
            return command.ExecuteList();
        }

        public static List<T> ExecuteList<T>(this IDbCommand command, ISqlInstruction sql, ISqlDialect dialect, Converter<IDataRecord, T> mapper)
        {
            ApplySqlInstruction(command, sql, dialect);
            var result = new List<T>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add(mapper(reader));
                }
            }
            return result;
        }

        public static T ExecuteScalar<T>(this IDbCommand command, ISqlInstruction sql, ISqlDialect dialect)
        {
            ApplySqlInstruction(command, sql, dialect);
            return command.ExecuteScalar<T>();
        }

        public static int ExecuteNonQuery(this IDbCommand command, ISqlInstruction sql, ISqlDialect dialect)
        {
            ApplySqlInstruction(command, sql, dialect);
            return command.ExecuteNonQuery();
        }

        private static void ApplySqlInstruction(IDbCommand command, ISqlInstruction sql, ISqlDialect dialect)
        {
            var sqlStr = sql.ToString(dialect);
            var parameters = sql.GetParameters();
            command.Parameters.Clear();

            var sqlParts = sqlStr.Split('?');
            var sqlBuilder = new StringBuilder();
            for (var i = 0; i < sqlParts.Length - 1; i++)
            {
                var name = "p" + i;
                if (parameters[i] is DateTime && (DateTime)parameters[i] == DateTime.MinValue)
                    parameters[i] = DBNull.Value;
                command.AddParameter(name, parameters[i]);
                sqlBuilder.AppendFormat("{0}@{1}", sqlParts[i], name);
            }
            sqlBuilder.Append(sqlParts[sqlParts.Length - 1]);
            command.CommandText = sqlBuilder.ToString();
        }


        public static T Get<T>(this IDataRecord r, int i)
        {
            if (r.IsDBNull(i))
            {
                return default(T);
            }

            var value = r.GetValue(i);
            if (typeof(T) == typeof(Guid))
            {
                if (r[i] is string)
                    value = new Guid((string)r[i]);
                //value = r.GetGuid(i);
            }
            return (T)Convert.ChangeType(value, typeof(T));
        }

        public static T Get<T>(this IDataRecord r, string name)
        {
            return Get<T>(r, r.GetOrdinal(name));
        }
    }
}