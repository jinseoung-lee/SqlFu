using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using SqlFu.DDL;

namespace SqlFu.Internals
{
    internal class SqlCache
    {
        public string SelectSingleSql { get; set; }
        public string InsertSql { get; set; }
    }

    internal class TableInfo
    {
        public string Name { get; internal set; }
        public string PrimaryKey { get; private set; }
        public string[] Excludes { get; private set; }
        
        Dictionary<DbEngine,SqlCache> _sqlCache=new Dictionary<DbEngine, SqlCache>();

        internal SqlCache GetCache(DbEngine provider) => _sqlCache.GetValueOrCreate(provider, () => new SqlCache());

        public string[] ConvertToString { get; private set; }
        public bool AutoGenerated { get; set; }
        public IfTableExists CreationOptions { get; set; }
        public string[] Columns { get; private set; }

        public TableInfo(string name)
        {
            Name = name;
            PrimaryKey = "Id";
            Excludes = new string[0];
            ConvertToString = new string[0];
            AutoGenerated = false;
            Columns = new string[0];
        }

        public TableInfo(Type t)
        {
            if (t.IsValueType || t == typeof (object))
                throw new InvalidOperationException("A table can't be System.Object or just a value");
            var tab = t.GetSingleAttribute<TableAttribute>(true);
            if (tab != null)
            {
                Name = tab.Name;
                PrimaryKey = tab.PrimaryKey;
                AutoGenerated = tab.AutoGenerated;
                CreationOptions = tab.CreationOptions;
            }
            else
            {
                Name = t.Name;
                PrimaryKey = "Id";
                AutoGenerated = false;
            }

            var exclude = new List<string>();
            var tstring = new List<string>();
            var columns = new List<string>();

            if (t != typeof (ExpandoObject))
            {
                foreach (var p in t.GetProperties())
                {
                    var ig = p.GetSingleAttribute<IgnoreAttribute>(true);
                    if (ig == null)
                    {
                        columns.Add(p.Name);

                        var qr = p.GetSingleAttribute<QueryOnlyAttribute>(true);
                        if (qr != null)
                        {
                            exclude.Add(p.Name);
                        }
                        var tos = p.GetSingleAttribute<InsertAsStringAttribute>(true);
                        if (tos != null)
                        {
                            tstring.Add(p.Name);
                        }
                    }
                }
            }
            Excludes = exclude.ToArray();
            ConvertToString = tstring.ToArray();
            Columns = columns.ToArray();
        }

        private static readonly ConcurrentDictionary<Type, TableInfo> _cache =
            new ConcurrentDictionary<Type, TableInfo>();

        public static TableInfo ForType(Type t)
        {
            TableInfo ti = null;
            if (!_cache.TryGetValue(t, out ti))
            {
                ti = new TableInfo(t);
                _cache.TryAdd(t, ti);
            }
            return ti;
        }
    }
}
