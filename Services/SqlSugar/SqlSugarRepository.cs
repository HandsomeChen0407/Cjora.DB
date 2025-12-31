namespace Cjora.DB.Services;

/// <summary>
/// SqlSugar 实体仓储
/// </summary>
/// <typeparam name="T"></typeparam>
public class SqlSugarRepository<T> : SimpleClient<T> where T : class, new()
{
    protected ITenant iTenant;

    /// <summary>
    /// 系统缓存服务
    /// </summary>
    protected static readonly SysCacheService _sysCacheService = App.GetService<SysCacheService>();

    /// <summary>
    /// 每个租户一把锁（避免并发重复 AddConnection）
    /// 租户数量需可控
    /// </summary>
    private static readonly ConcurrentDictionary<long, object> _tenantLocks = new();

    public SqlSugarRepository(string? deviceImei = null)
    {
        // 获取 SqlSugar 多租户管理器
        iTenant = App.GetRequiredService<ISqlSugarClient>().AsTenant();

        // 默认兜底：先绑定主库连接
        base.Context = iTenant.GetConnectionScope(SqlSugarConst.MainConfigId);

        // 若实体贴有多库特性，则返回指定库连接
        if (typeof(T).IsDefined(typeof(TenantAttribute), false))
        {
            base.Context = iTenant.GetConnectionScopeWithAttr<T>();
            return;
        }

        // 若实体贴有系统表特性，则返回默认库连接
        if (typeof(T).IsDefined(typeof(SysTableAttribute), false))
        {
            base.Context = iTenant.GetConnectionScope(SqlSugarConst.MainConfigId);
            return;
        }

        string? tenantId;
        if (!string.IsNullOrWhiteSpace(deviceImei))
            // 设备维度解析租户
            tenantId = _sysCacheService.Get<string>($"{CacheConst.KeyDeviceTenantId}{deviceImei}");
        else
            // 用户维度解析租户
            tenantId = App.User?.FindFirst(ClaimConst.TenantId)?.Value;

        // 解析失败：记录错误并回退主库（宽容策略）
        if (!long.TryParse(tenantId, out var tenantLongId)) 
        {
            Furion.Logging.Log.Error(
            "租户ID解析失败，TenantId={TenantId}, DeviceImei={DeviceImei}, Entity={Entity}",
            tenantId,
            deviceImei,
            typeof(T).FullName);

            return;
        }

        // 主库或非法租户：保持主库连接
        if (tenantLongId <= 0 || tenantId == SqlSugarConst.MainConfigId) return;

        // 切换至租户库
        var sqlSugarScopeProvider = GetTenantDbConnectionScope(iTenant, tenantLongId);
        if (sqlSugarScopeProvider == null) return;
        base.Context = sqlSugarScopeProvider;
    }

    /// <summary>
    /// 获取租户缓存
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    public TenantDto? GetTenantCache(long tenantId)
    {
        return _sysCacheService.Get<List<TenantDto>>(CacheConst.KeyTenantAES)?.First(u => u.Id == tenantId);
    }

    /// <summary>
    /// 获取（或创建）指定租户的数据库连接作用域
    /// </summary>
    /// <param name="iTenant">SqlSugar 多租户管理器（全局单例）</param>
    /// <param name="租户唯一标识，用于区分不同数据库连接">租户id</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public SqlSugarScopeProvider GetTenantDbConnectionScope(ITenant iTenant, long tenantId)
    {
        // 若租户库连接已存在，直接返回（快速路径）
        if (iTenant.IsAnyConnection(tenantId.ToString()))
            return iTenant.GetConnectionScope(tenantId.ToString());

        // 基于租户维度的细粒度锁，避免不同租户之间互相阻塞
        var locker = _tenantLocks.GetOrAdd(tenantId, _ => new object());
        lock (locker)
        {
            // 双重检查：防止并发场景下重复创建连接
            if (iTenant.IsAnyConnection(tenantId.ToString()))
                return iTenant.GetConnectionScope(tenantId.ToString());

            // 从缓存中获取租户数据库配置（必须已初始化）
            var tenant = GetTenantCache(tenantId);
            if (tenant == null)
            {
                throw new InvalidOperationException(
                    $"未找到租户配置，TenantId={tenantId}，请检查租户缓存或初始化流程");
            }

            // 获取系统默认（主库）数据库配置，用于继承公共设置
            var dbOptions = App.GetOptions<DbConnectionOptions>();
            var mainConnConfig = dbOptions.ConnectionConfigs.First(u => u.ConfigId.ToString() == SqlSugarConst.MainConfigId);

            // 构造租户数据库连接配置
            var tenantConnConfig = new DbConnectionConfig
            {
                ConfigId = tenant.Id.ToString(),
                DbType = tenant.DbType,
                IsAutoCloseConnection = true,
                ConnectionString = SensitiveColumnAESHelper.Decrypt(tenant.Connection)
            };

            // 动态注册租户库连接
            iTenant.AddConnection(tenantConnConfig);

            // 获取租户库连接作用域
            var sqlSugarScopeProvider = iTenant.GetConnectionScope(tenantId.ToString());

            // 初始化 SqlSugar 全局/租户级配置
            SqlSugarSetup.SetDbConfig(tenantConnConfig);
            SqlSugarSetup.SetDbAop(sqlSugarScopeProvider, dbOptions.EnableSqlAopLog);

            return sqlSugarScopeProvider;
        }
    }
}