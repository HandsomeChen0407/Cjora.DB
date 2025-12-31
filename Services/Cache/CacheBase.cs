namespace Cjora.DB.Services;

/// <summary>
/// 业务缓存基类
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class CacheBase<T>
{
    /// <summary>
    /// 系统缓存服务
    /// </summary>
    protected static readonly SysCacheService _sysCacheService = App.GetService<SysCacheService>();

    protected string _cacheKey;

    protected string _cacheField;

    public CacheBase(string cacheKey, string cacheField)
    {
        _cacheKey = cacheKey;
        _cacheField = cacheField;
    }

    /// <summary>
    /// 查询缓存
    /// </summary>
    /// <returns></returns>
    public async Task<T?> GetCacheAsync()
    {
        var data = _sysCacheService.HashGetOne<T>(_cacheKey, _cacheField);
        if (data == null)
        {
            data = await RefreshCacheAsync();
        }
        return data;
    }

    /// <summary>
    /// 设置缓存
    /// </summary>
    /// <param name="datas"></param>
    protected void SetCache(Dictionary<string, T> datas)
    {
        if (datas != null && datas.Any())
            _sysCacheService.HashSet(_cacheKey, datas);
    }

    /// <summary>
    /// 删除缓存
    /// </summary>
    public void DelCache()
    {
        _sysCacheService.HashDel<T>(_cacheKey, _cacheField);
    }

    /// <summary>
    /// 删除缓存
    /// </summary>
    /// <param name="fields"></param>
    public void DelCache(string[] fields)
    {
        if (!string.IsNullOrWhiteSpace(_cacheKey) && fields != null && fields.Any())
            _sysCacheService.HashDel<T>(_cacheKey, fields);
    }

    /// <summary>
    /// 刷新缓存
    /// </summary>
    /// <returns></returns>
    public async Task<T?> RefreshCacheAsync()
    {
        try
        {
            var data = await SearchDbAsync();
            if (data != null)
                SetCache(new Dictionary<string, T> {
                    { _cacheField, data }
                });
            else
                DelCache([_cacheField]);
            return data;
        }
        catch (Exception ex)
        {
            DelCache([_cacheField]);
            Furion.Logging.Log.Error($"cache db error：{ex.Message}");
            return default(T);
        }
    }

    /// <summary>
    /// 数据库查询
    /// </summary>
    /// <returns></returns>
    protected abstract Task<T> SearchDbAsync();
}