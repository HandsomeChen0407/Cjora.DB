namespace Cjora.DB.Services;

public static class SqlSugarFilter
{
    /// <summary>
    /// 缓存全局查询过滤器（内存缓存）
    /// </summary>
    private static readonly ICache _cache = Cache.Default;

    /// <summary>
    /// 删除用户机构缓存
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="dbConfigId"></param>
    public static void DeleteUserOrgCache(long userId, string dbConfigId)
    {
        var sysCacheService = App.GetService<SysCacheService>();

        // 删除用户机构集合缓存
        sysCacheService.Remove($"{CacheConst.KeyUserOrg}{userId}");
        // 删除用户选中机构
        sysCacheService.HashDel<List<long>>(CacheConst.KeyUserOrgSelectProduct, userId.ToString());
        // 删除最大数据权限缓存
        sysCacheService.Remove($"{CacheConst.KeyRoleMaxDataScope}{userId}");
        // 删除用户机构（数据范围）缓存——过滤器
        _cache.Remove($"db:{dbConfigId}:orgList:{userId}");
        // 删除用户机构（拦截器绑定）
        _cache.Remove($"db:{dbConfigId}:orgIdList:{userId}:data");
    }

    /// <summary>
    /// 获取用户机构缓存
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public static List<long>? GetUserOrgCache(long userId)
    {
        var sysCacheService = App.GetService<SysCacheService>();
        var productType = App.User?.FindFirst(ClaimConst.ProductType)?.Value;
        var orgIdList = sysCacheService.Get<List<long>>($"{CacheConst.KeyUserOrg}{userId}");
        if (orgIdList != null && orgIdList.Any())
        {
            var orgIdSelectList = sysCacheService.HashGetOne<List<long>>(CacheConst.KeyUserOrgSelectProduct + productType, userId.ToString());
            if (orgIdSelectList != null && orgIdSelectList.Any()) 
            {
                var data = orgIdList.Where(it => orgIdSelectList.Contains(it));
                if (data != null && data.Any())
                    return data.ToList();
                else
                    return null;
            }
            else
                return orgIdList;
        }
        else
            return null;
    }

    /// <summary>
    /// 配置用户机构集合过滤器
    /// </summary>
    public static void SetOrgEntityFilter(SqlSugarScopeProvider db)
    {
        // 若仅本人数据、全部数据，则直接返回
        var maxDataScope = SetDataScopeFilter(db);
        if (maxDataScope == 0 || maxDataScope == (int)DataScopeEnum.Self) return;

        var userId = App.User?.FindFirst(ClaimConst.UserId)?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return;

        // 获取用户所属机构
        var orgIds = GetUserOrgCache(userId.ParseToLong());
        if (orgIds == null || orgIds.Count == 0) return;

        db.QueryFilter.AddTableFilter<IOrgIdFilter>(u => u.CreateOrgId != null && orgIds.Contains(u.CreateOrgId.Value));
    }

    /// <summary>
    /// 配置用户仅本人数据过滤器
    /// </summary>
    private static int SetDataScopeFilter(SqlSugarScopeProvider db)
    {
        var maxDataScope = (int)DataScopeEnum.All;

        var userId = App.User?.FindFirst(ClaimConst.UserId)?.Value;
        if (string.IsNullOrWhiteSpace(userId)) return maxDataScope;

        var idNumber = userId.ParseToLong();
        // 获取用户最大数据范围---仅本人数据
        maxDataScope = App.GetRequiredService<SysCacheService>().Get<int>(CacheConst.KeyRoleMaxDataScope + userId);
        if (maxDataScope != (int)DataScopeEnum.Self) return maxDataScope;

        db.QueryFilter.AddTableFilter<IUserIdFilter>(u => idNumber == u.CreateUserId);
        return maxDataScope;
    }

    /// <summary>
    /// 配置自定义过滤器
    /// </summary>
    public static void SetCustomEntityFilter(SqlSugarScopeProvider db)
    {
        // 配置自定义缓存
        var userId = App.User?.FindFirst(ClaimConst.UserId)?.Value;
        var cacheKey = $"db:{db.CurrentConnectionConfig.ConfigId}:custom:{userId}";
        var tableFilterItemList = _cache.Get<List<TableFilterItem<object>>>(cacheKey);
        if (tableFilterItemList == null)
        {
            // 获取自定义实体过滤器
            var entityFilterTypes = App.EffectiveTypes.Where(u => !u.IsInterface && !u.IsAbstract && u.IsClass
                && u.GetInterfaces().Any(i => i.HasImplementedRawGeneric(typeof(IEntityFilter))));
            if (!entityFilterTypes.Any()) return;

            var tableFilterItems = new List<TableFilterItem<object>>();
            foreach (var entityFilter in entityFilterTypes)
            {
                var instance = Activator.CreateInstance(entityFilter);
                var entityFilterMethod = entityFilter.GetMethod("AddEntityFilter");
                var entityFilters = ((IList)entityFilterMethod?.Invoke(instance, null))?.Cast<object>();
                if (entityFilters == null) continue;

                foreach (var u in entityFilters)
                {
                    var tableFilterItem = (TableFilterItem<object>)u;
                    var entityType = tableFilterItem.GetType().GetProperty("type", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tableFilterItem, null) as Type;
                    // 排除非当前数据库实体
                    var tAtt = entityType.GetCustomAttribute<TenantAttribute>();
                    if ((tAtt != null && db.CurrentConnectionConfig.ConfigId.ToString() != tAtt.configId.ToString()) ||
                        (tAtt == null && db.CurrentConnectionConfig.ConfigId.ToString() != SqlSugarConst.MainConfigId))
                        continue;

                    tableFilterItems.Add(tableFilterItem);
                    db.QueryFilter.Add(tableFilterItem);
                }
            }
            _cache.Add(cacheKey, tableFilterItems);
        }
        else
        {
            tableFilterItemList.ForEach(u =>
            {
                db.QueryFilter.Add(u);
            });
        }
    }
}

/// <summary>
/// 自定义实体过滤器接口
/// </summary>
public interface IEntityFilter
{
    /// <summary>
    /// 实体过滤器
    /// </summary>
    /// <returns></returns>
    IEnumerable<TableFilterItem<object>> AddEntityFilter();
}