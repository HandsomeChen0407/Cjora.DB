namespace Cjora.DB.Services;

public static class SensitiveColumnCache
{
    // key: 小写表名, value: List<(PropertyInfo, 小写列名)>
    private static readonly Dictionary<string, List<(PropertyInfo Prop, string ColumnName)>> _cache = new();
    public static IReadOnlyDictionary<string, List<(PropertyInfo Prop, string ColumnName)>> AllCaches => _cache;

    private static bool _initialized = false;
    private static readonly object _lock = new();

    public static void Init(SqlSugarScope db, params Type[] entityTypes)
    {
        if (_initialized) return; // 已初始化直接返回
        lock (_lock)
        {
            if (_initialized) return;

            foreach (var type in entityTypes)
            {
                var entityInfo = db.EntityMaintenance.GetEntityInfo(type);
                var sensitiveList = new List<(PropertyInfo, string)>();

                foreach (var col in entityInfo.Columns)
                {
                    var prop = type.GetProperty(col.PropertyName);
                    if (prop == null) continue;

                    if (Attribute.IsDefined(prop, typeof(SensitiveColumnAttribute)))
                        sensitiveList.Add((prop, col.DbColumnName.ToLower())); // 列名小写
                }

                var tableNameLower = entityInfo.DbTableName.ToLower();
                if (!_cache.ContainsKey(tableNameLower))
                    _cache[tableNameLower] = sensitiveList;
            }

            _initialized = true;
        }
    }

    public static List<(PropertyInfo Prop, string ColumnName)> Get(string tableName)
    {
        var tableNameLower = tableName.ToLower();
        return _cache.ContainsKey(tableNameLower) ? _cache[tableNameLower] : new List<(PropertyInfo, string)>();
    }
}
