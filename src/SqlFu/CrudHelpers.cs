﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using SqlFu.Expressions;
using SqlFu.Internals;

namespace SqlFu
{
    public static class CrudHelpers
    {
     
        #region Insert

        /// <summary>
        /// Inserts into database
        /// </summary>
        /// <param name="db"></param>
        /// <param name="table">Table name</param>
        /// <param name="data">Column and Values</param>
        /// <param name="idIsIdentity">By default if the object has an Id property, it's considered to be autoincremented</param>
        /// <returns></returns>
        public static LastInsertId Insert(this DbConnection db, string table, object data, bool idIsIdentity = true)
        {
            table.MustNotBeEmpty();
            data.MustNotBeNull();
            var ti = new TableInfo(table);
            ti.AutoGenerated = idIsIdentity;
            return Insert(db, ti, data);
        }

        public static LastInsertId Insert<T>(this DbConnection db, T data) where T : class
        {
            data.MustNotBeNull();
            return Insert(db, TableInfo.ForType(typeof (T)), data);
        }


        private static LastInsertId Insert(DbConnection db, TableInfo tableInfo, object data)
        {
            var provider = db.GetProvider();
            List<object> args = null;

            var arguments = data.ToDictionary();
            if (tableInfo.InsertSql == null)
            {
                var sb = new StringBuilder("Insert into");
                sb.AppendFormat(" {0} (", provider.EscapeName(tableInfo.Name));

                args = FillArgs(arguments, tableInfo, provider, sb);

                if (args.Count == 0)
                {
                    throw new InvalidOperationException("There are no values to insert");
                }

                sb.Remove(sb.Length - 1, 1);

                sb.Append(") values(");

                for (var i = 0; i < args.Count; i++)
                {
                    sb.Append("@" + i + ",");
                }
                sb.Remove(sb.Length - 1, 1);
                sb.Append(")");
                tableInfo.InsertSql = sb.ToString();
            }
            if (args == null)
            {
                args = FillArgs(arguments, tableInfo, provider);
            }

            LastInsertId rez;
            using (var st = new ControlledQueryStatement(db, tableInfo.InsertSql, args.ToArray()))
            {
                st.Reusable = true;
                rez = db.GetProvider().ExecuteInsert(st.Command, tableInfo.PrimaryKey);                
            }
            return rez;
        }

        /// <summary>
        /// Write column names and returns values to be inserted
        /// </summary>
        /// <param name="arguments"></param>
        /// <param name="tableInfo"></param>
        /// <param name="provider"></param>
        /// <param name="sb"></param>
        /// <returns></returns>
        private static List<object> FillArgs(IDictionary<string, object> arguments, TableInfo tableInfo,
                                             IHaveDbProvider provider,
                                             StringBuilder sb = null)
        {
            var args = new List<object>();
            foreach (var col in arguments)
            {
                if (col.Key == tableInfo.PrimaryKey)
                {
                    if (tableInfo.AutoGenerated) continue;
                }

                if (tableInfo.Excludes.Any(n => n.Equals(col.Key, StringComparison.InvariantCulture))) continue;
                if (sb != null) sb.AppendFormat("{0},", provider.EscapeName(col.Key));
                if (tableInfo.ConvertToString.Any(t => t == col.Key))
                {
                    args.Add(col.Value.ToString());
                }
                else
                {
                    args.Add(col.Value);
                }
            }
            return args;
        }

        #endregion

        #region Update

        /// <summary>
        /// If both poco has id property and the Id arg is specified, the arg is used
        /// </summary>
        /// <param name="db"></param>
        /// <param name="table"></param>
        /// <param name="data"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static int Update(this DbConnection db, string table, object data, object id = null)
        {
            var ti = new TableInfo(table);
            return Update(db, ti, data, id);
        }

        /// <summary>
        /// If both poco has id property and the Id arg is specified, the arg is used
        /// </summary>
        public static int Update<T>(this DbConnection db, object data, object id = null)
        {
            var ti = TableInfo.ForType(typeof (T));
            return Update(db, ti, data, id);
        }

        public static int UpdateWhereColumn(this DbConnection db, string tableName, object data, string colName,
                                            object columnValue)
        {
            tableName.MustNotBeEmpty();
            colName.MustNotBeEmpty();
            columnValue.MustNotBeNull();
            return Update(db, new TableInfo(tableName), data, columnValue, colName);
        }

        public static int Update<T>(this DbConnection db, object data, Expression<Func<T, bool>> criteria)
        {
            var args = data.ToDictionary();
            var ti = TableInfo.ForType(typeof (T));
            var updater = db.Update<T>();
            foreach (var kv in args)
            {
                if (ti.PrimaryKey == kv.Key) continue;
                if (ti.Excludes.Any(d => d == kv.Key)) continue;
                updater.Set(kv.Key, kv.Value);
            }
            return updater.Where(criteria).Execute();
        }

        /// <summary>
        /// Gets update table builder
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <returns></returns>
        public static IBuildUpdateTable<T> Update<T>(this DbConnection db)
        {
            return new UpdateTableBuilder<T>(db);
        }

        private static int Update(DbConnection db, TableInfo ti, object data, object id = null,
                                  string filterColumn = null)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("update {0} set", db.EscapeIdentifier(ti.Name));
            var d = data.ToDictionary();
            bool hasId = false;
            int i = 0;
            var args = new List<object>();
            var provider = db.GetProvider();

            foreach (var k in d)
            {
                if (k.Key == ti.PrimaryKey)
                {
                    hasId = true;
                    continue;
                }
                if (ti.Excludes.Any(c => c == k.Key)) continue;
                sb.AppendFormat(" {0}={1},", db.EscapeIdentifier(k.Key), provider.ParamPrefix + i);
                if (ti.ConvertToString.Any(s => s == k.Key))
                {
                    args.Add(k.Value.ToString());
                }
                else
                {
                    args.Add(k.Value);
                }
                i++;
            }
            sb.Remove(sb.Length - 1, 1);

            if (filterColumn.IsNullOrEmpty())
            {
                filterColumn = ti.PrimaryKey;
            }

            if (id != null || hasId)
            {
                sb.AppendFormat(" where {0}={1}", provider.EscapeName(filterColumn), provider.ParamPrefix + i);
                hasId = true;
                if (id == null) id = d[ti.PrimaryKey];
            }

            if (hasId) args.Add(id);

            return db.Execute(sb.ToString(), args.ToArray());
        }

        #endregion

        public static int DeleteFrom<T>(this DbConnection db, string condition, params object[] args)
        {
            var ti = TableInfo.ForType(typeof (T));
            return
                db.Execute(
                    string.Format("delete from {0} where {1}", db.GetProvider().EscapeName(ti.Name), condition), args);
        }

        public static int DeleteFrom<T>(this DbConnection db, Expression<Func<T, bool>> criteria = null)
        {
            var builder = new ExpressionSqlBuilder<T>(db.GetProvider().BuilderHelper);
            builder.WriteDelete();
            if (criteria != null)
            {
                builder.Where(criteria);
            }
            return db.Execute(builder.ToString(), builder.Parameters.ToArray());
        }
    }
}