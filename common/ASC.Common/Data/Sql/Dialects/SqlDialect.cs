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

using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace ASC.Common.Data.Sql
{
    public class SqlDialect : ISqlDialect
    {
        public static readonly ISqlDialect Default = new SqlDialect();


        public virtual string IdentityQuery
        {
            get { return "@@identity"; }
        }

        public virtual string Autoincrement
        {
            get { return "AUTOINCREMENT"; }
        }

        public virtual string InsertIgnore
        {
            get { return "insert ignore"; }
        }


        public virtual bool SupportMultiTableUpdate
        {
            get { return false; }
        }

        public virtual bool SeparateCreateIndex
        {
            get { return true; }
        }


        public virtual string DbTypeToString(DbType type, int size, int precision)
        {
            var s = new StringBuilder(type.ToString().ToLower());
            if (0 < size)
            {
                s.AppendFormat(0 < precision ? "({0}, {1})" : "({0})", size, precision);
            }
            return s.ToString();
        }

        public virtual IsolationLevel GetSupportedIsolationLevel(IsolationLevel il)
        {
            return il;
        }
        public bool ReplaceEnabled
        {
            get { return true; }
        }

        private static SortedDictionary<string, List<string>> keys = new SortedDictionary<string, List<string>>();

        public List<string> GetPrimaryKeyColumns(string tablename)
        {
            List<string> ret;
            if (keys.TryGetValue(tablename.ToLower(), out ret))
                return ret;
            else
                return null;
        }

        private static SortedDictionary<string, List<string>> LoadKeys(DbManager dbManager)
        {
            var x = new SortedDictionary<string, List<string>>();
            var ret = dbManager.ExecuteList("SELECT lower(table_name), lower(column_name) FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE OBJECTPROPERTY(OBJECT_ID(constraint_name), 'IsPrimaryKey') = 1");
            foreach (var r in ret)
            {
                if (!x.ContainsKey((string)r[0]))
                    x.Add((string)r[0], new List<string>());
                x[(string)r[0]].Add((string)r[1]);
            }

            return x;
        }

        internal static void CheckKeys(DbManager dbManager)
        {
            if (keys.Count == 0)
                lock (keys)
                {
                    if (keys.Count == 0)
                    {
                        keys = LoadKeys(dbManager);
                    }
                }
        }
    }
}