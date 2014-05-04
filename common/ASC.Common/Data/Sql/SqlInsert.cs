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
using System.Diagnostics;
using System.Text;

namespace ASC.Common.Data.Sql
{
    [DebuggerTypeProxy(typeof(SqlDebugView))]
    public class SqlInsert : ISqlInstruction
    {
        private readonly List<string> columns = new List<string>();
        private readonly string table;
        private readonly List<object> values = new List<object>();
        private List<object> valuesReplace = null;

        private int identityPosition = -1;
        private object nullValue;
        private SqlQuery query;
        private bool replaceExists;
        private bool ignoreExists;
        private bool returnIdentity;


        public SqlInsert(string table)
            : this(table, true)
        {
        }

        public SqlInsert(string table, bool replaceExists)
        {
            this.table = table;
            ReplaceExists(replaceExists);
        }

        //public string ToStringOld(ISqlDialect dialect)
        //{
        //    var sql = new StringBuilder();

        //    if (ignoreExists)
        //    {
        //        sql.Append(dialect.InsertIgnore);
        //    }
        //    else
        //    {
        //        sql.Append(replaceExists ? "replace" : "insert");
        //    }
        //    sql.AppendFormat(" into {0}", table);
        //    bool identityInsert = IsIdentityInsert();
        //    if (0 < columns.Count)
        //    {
        //        sql.Append("(");
        //        for (int i = 0; i < columns.Count; i++)
        //        {
        //            if (identityInsert && identityPosition == i) continue;
        //            sql.AppendFormat("{0},", columns[i]);
        //        }
        //        sql.Remove(sql.Length - 1, 1).Append(")");
        //    }
        //    if (query != null)
        //    {
        //        sql.AppendFormat(" {0}", query.ToString(dialect));
        //        return sql.ToString();
        //    }
        //    sql.Append(" values (");
        //    for (int i = 0; i < values.Count; i++)
        //    {
        //        if (identityInsert && identityPosition == i)
        //        {
        //            continue;
        //        }
        //        sql.Append("?");
        //        if (i + 1 == values.Count)
        //        {
        //            sql.Append(")");
        //        }
        //        else if (0 < columns.Count && (i + 1) % columns.Count == 0)
        //        {
        //            sql.Append("),(");
        //        }
        //        else
        //        {
        //            sql.Append(",");
        //        }
        //    }

        //    if (returnIdentity)
        //    {
        //        sql.AppendFormat("; select {0}", identityInsert ? dialect.IdentityQuery : "?");
        //    }
        //    return sql.ToString();
        //}

        public string ToString(ISqlDialect dialect)
        {
            var sql = new StringBuilder();

            //if (IsUpdate() && !dialect.ReplaceEnabled)
            //{
            //    sql.AppendFormat("update {0} set ", table);
            //    for (int i = 0; i < columns.Count; i++)
            //    {
            //        if (identityPosition == i) continue;
            //        sql.AppendFormat("{0} = ?", columns[i]);
            //        if (i < columns.Count - 1)
            //            sql.Append(",");
            //    }
            //    sql.AppendFormat(" where {0}={1}", columns[identityPosition], values[identityPosition].ToString());
            //    if (returnIdentity)
            //    {
            //        sql.AppendFormat("; select {0}", values[identityPosition].ToString());
            //    }
            //    return sql.ToString();
            //}

            if (replaceExists && dialect.ReplaceEnabled)
            {
                var keys = dialect.GetPrimaryKeyColumns(table);
                for (int i = 0; i < columns.Count; i++)
                    columns[i] = columns[i].ToLower();
                valuesReplace = new List<object>();
                bool comma = false;
                if (keys != null && keys.Count > 0)
                {
                    //update
                    sql.AppendFormat("update {0} set ", table);
                    //set
                    for (int i = 0; i < columns.Count; i++)
                        if (!keys.Contains(columns[i]))
                        {
                            if (comma)
                                sql.Append(",");
                            sql.AppendFormat("{0} = ?", columns[i]);
                            comma = true;
                            valuesReplace.Add(values[i]);
                        }
                    sql.Append(" where ");
                    comma = false;
                    for (int i = 0; i < columns.Count; i++)
                        if (keys.Contains(columns[i]))
                        {
                            if (comma)
                                sql.Append(" and ");

                            sql.AppendFormat("{0} = ?", columns[i]);

                            comma = true;

                            valuesReplace.Add(values[i]);
                        }

                    sql.AppendLine();

                    sql.Append("if (@@rowcount = 0)");
                    sql.AppendLine();
                }

                //insert
                sql.AppendFormat("insert into {0}", table);

                bool identityInsert = IsIdentityInsert();
                if (0 < columns.Count)
                {
                    sql.Append("(");
                    for (int i = 0; i < columns.Count; i++)
                    {
                        if (identityInsert && identityPosition == i) continue;
                        sql.AppendFormat("{0},", columns[i]);
                    }
                    sql.Remove(sql.Length - 1, 1).Append(")");
                }

                sql.Append(" values (");
                for (int i = 0; i < values.Count; i++)
                {
                    if (identityInsert && identityPosition == i)
                    {
                        continue;
                    }
                    sql.Append("?");
                    valuesReplace.Add(values[i]);
                    if (i + 1 == values.Count)
                    {
                        sql.Append(")");
                    }
                    else if (0 < columns.Count && (i + 1) % columns.Count == 0)
                    {
                        sql.Append("),(");
                    }
                    else
                    {
                        sql.Append(",");
                    }
                }

                if (returnIdentity)
                {
                    sql.AppendFormat("; select {0}", identityInsert ? dialect.IdentityQuery : "?");
                    if (!identityInsert)
                        valuesReplace.Add(values[identityPosition]);
                }
                return sql.ToString();

            }

            if (ignoreExists)
            {
                sql.Append(dialect.InsertIgnore);
            }
            else
            {
                sql.Append(replaceExists && dialect.ReplaceEnabled ? "replace" : "insert");
            }
            sql.AppendFormat(" into {0}", table);
            bool identityInsertx = IsIdentityInsert();
            if (0 < columns.Count)
            {
                sql.Append("(");
                for (int i = 0; i < columns.Count; i++)
                {
                    if (identityInsertx && identityPosition == i) continue;
                    sql.AppendFormat("{0},", columns[i]);
                }
                sql.Remove(sql.Length - 1, 1).Append(")");
            }
            if (query != null)
            {
                sql.AppendFormat(" {0}", query.ToString(dialect));
                return sql.ToString();
            }
            sql.Append(" values (");
            for (int i = 0; i < values.Count; i++)
            {
                if (identityInsertx && identityPosition == i)
                {
                    continue;
                }
                sql.Append("?");
                if (i + 1 == values.Count)
                {
                    sql.Append(")");
                }
                else if (0 < columns.Count && (i + 1) % columns.Count == 0)
                {
                    sql.Append("),(");
                }
                else
                {
                    sql.Append(",");
                }
            }

            if (returnIdentity)
            {
                sql.AppendFormat("; select {0}", identityInsertx ? dialect.IdentityQuery : "?");
            }
            return sql.ToString();
        }

        private bool IsUpdate()
        {
            return identityPosition >= 0 && values[identityPosition] != null && !Equals(values[identityPosition], nullValue);
        }

        public object[] GetParameters()
        {
            if (query != null)
            {
                return query.GetParameters();
            }

            if (valuesReplace != null)
                return valuesReplace.ToArray();

            var copy = new List<object>(values);

            if (IsUpdate())
            {
                copy.RemoveAt(identityPosition);
                return copy.ToArray();
            }

            if (IsIdentityInsert())
            {
                copy.RemoveAt(identityPosition);
            }
            else if (returnIdentity)
            {
                copy.Add(copy[identityPosition]);
            }
            return copy.ToArray();
        }


        public SqlInsert InColumns(params string[] columns)
        {
            this.columns.AddRange(columns);
            return this;
        }

        public SqlInsert Values(params object[] values)
        {
            this.values.AddRange(values);
            return this;
        }

        public SqlInsert Values(SqlQuery query)
        {
            this.query = query;
            return this;
        }

        public SqlInsert InColumnValue(string column, object value)
        {
            return InColumns(column).Values(value);
        }

        public SqlInsert ReplaceExists(bool replaceExists)
        {
            this.replaceExists = replaceExists;
            return this;
        }

        public SqlInsert IgnoreExists(bool ignoreExists)
        {
            this.ignoreExists = ignoreExists;
            return this;
        }

        public SqlInsert Identity<TIdentity>(int position, TIdentity nullValue)
        {
            return Identity(position, nullValue, false);
        }

        public SqlInsert Identity<TIdentity>(int position, TIdentity nullValue, bool returnIdentity)
        {
            identityPosition = position;
            this.nullValue = nullValue;
            this.returnIdentity = returnIdentity;
            return this;
        }

        public override string ToString()
        {
            return ToString(SqlDialect.Default);
        }

        private bool IsIdentityInsert()
        {
            if (identityPosition < 0) return false;
            if (values[identityPosition] != null && nullValue != null &&
                values[identityPosition].GetType() != nullValue.GetType())
            {
                throw new InvalidCastException(string.Format("Identity null value must be {0} type.",
                                                             values[identityPosition].GetType()));
            }
            return Equals(values[identityPosition], nullValue);
        }
    }
}