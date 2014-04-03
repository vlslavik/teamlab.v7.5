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

using System.Data;

namespace ASC.Common.Data.Sql
{
    public interface ISqlDialect
    {
        string IdentityQuery { get; }

        string Autoincrement { get; }

        string InsertIgnore { get; }


        bool SupportMultiTableUpdate { get; }

        bool SeparateCreateIndex { get; }

        bool ReplaceEnabled { get; }

        string DbTypeToString(DbType type, int size, int precision);

        IsolationLevel GetSupportedIsolationLevel(IsolationLevel il);
    }
}