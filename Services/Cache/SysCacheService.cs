using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Web;

namespace Cjora.DB.Services;

/// <summary>
/// 系统缓存服务
/// </summary>
public class SysCacheService: ISingleton
{
    private static ICacheProvider _cacheProvider;
    private readonly CacheOptions _cacheOptions;

    public SysCacheService(ICacheProvider cacheProvider, IOptions<CacheOptions> cacheOptions)
    {
        _cacheProvider = cacheProvider;
        _cacheOptions = cacheOptions.Value;
    }

    /// <summary>
    /// 申请分布式锁
    /// </summary>
    /// <param name="key">要锁定的key</param>
    /// <param name="msExpire">锁过期时间，超过该时间如果没有主动释放则自动释放锁，必须整数秒，单位毫秒</param>
    /// <param name="msTimeout">锁等待时间，申请加锁时如果遇到冲突则等待的最大时间，单位毫秒</param>
    /// <param name="throwOnFailure">失败时是否抛出异常,如不抛出异常，可通过判断返回null得知申请锁失败</param>
    /// <returns></returns>
    public IDisposable? BeginCacheLock(string key, int msExpire = 10000, int msTimeout = 500, bool throwOnFailure = true)
    {
        try
        {
            return _cacheProvider.Cache.AcquireLock(key, msTimeout, msExpire, throwOnFailure);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 原子性减少
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="value"></param>
    /// <returns></returns>
    public long StringDecrement(string key, int value)
    {
        return _cacheProvider.Cache.Decrement(key, value);
    }

    /// <summary>
    /// 原子性增加
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="value"></param>
    /// <returns></returns>
    public long StringIncrement(string key, int value)
    {
        return _cacheProvider.Cache.Increment(key, value);
    }

    /// <summary>
    /// 获取缓存键名集合
    /// </summary>
    /// <returns></returns>
    public List<string> GetKeyList()
    {
        return _cacheProvider.Cache == Cache.Default
            ? _cacheProvider.Cache.Keys.Where(u => u.StartsWith(_cacheOptions.Prefix)).Select(u => u[_cacheOptions.Prefix.Length..]).OrderBy(u => u).ToList()
            : ((FullRedis)_cacheProvider.Cache).Search($"{_cacheOptions.Prefix}*", int.MaxValue).Select(u => u[_cacheOptions.Prefix.Length..]).OrderBy(u => u).ToList();
    }

    /// <summary>
    /// 增加缓存
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public bool Set(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return _cacheProvider.Cache.Set($"{_cacheOptions.Prefix}{key}", value);
    }

    /// <summary>
    /// 增加缓存并设置过期时间
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <param name="expire"></param>
    /// <returns></returns>
    public bool Set(string key, object value, TimeSpan expire)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return _cacheProvider.Cache.Set($"{_cacheOptions.Prefix}{key}", value, expire);
    }

    public async Task<TR> AdGetAsync<TR>(String cacheName, Func<Task<TR>> del, TimeSpan? expiry = default(TimeSpan?)) where TR : class
    {
        return await AdGetAsync<TR>(cacheName, del, new object[] { }, expiry);
    }

    public async Task<TR> AdGetAsync<TR, T1>(String cacheName, Func<T1, Task<TR>> del, T1 t1, TimeSpan? expiry = default(TimeSpan?)) where TR : class
    {
        return await AdGetAsync<TR>(cacheName, del, new object[] { t1 }, expiry);
    }

    public async Task<TR> AdGetAsync<TR, T1, T2>(String cacheName, Func<T1, T2, Task<TR>> del, T1 t1, T2 t2, TimeSpan? expiry = default(TimeSpan?)) where TR : class
    {
        return await AdGetAsync<TR>(cacheName, del, new object[] { t1, t2 }, expiry);
    }

    public async Task<TR> AdGetAsync<TR, T1, T2, T3>(String cacheName, Func<T1, T2, T3, Task<TR>> del, T1 t1, T2 t2, T3 t3, TimeSpan? expiry = default(TimeSpan?)) where TR : class
    {
        return await AdGetAsync<TR>(cacheName, del, new object[] { t1, t2, t3 }, expiry);
    }

    private async Task<T> AdGetAsync<T>(string cacheName, Delegate del, Object[] obs, TimeSpan? expiry) where T : class
    {
        var key = Key(cacheName, obs);
        // 使用分布式锁
        using (_cacheProvider.Cache.AcquireLock($@"lock:AdGetAsync:{cacheName}", 1000))
        {
            var value = Get<T>(key);
            value ??= await ((dynamic)del).DynamicInvokeAsync(obs);
            Set(key, value);
            return value;
        }
    }

    public T Get<T>(String cacheName, object t1)
    {
        return Get<T>(cacheName, new object[] { t1 });
    }

    public T Get<T>(String cacheName, object t1, object t2)
    {
        return Get<T>(cacheName, new object[] { t1, t2 });
    }

    public T Get<T>(String cacheName, object t1, object t2, object t3)
    {
        return Get<T>(cacheName, new object[] { t1, t2, t3 });
    }

    private T Get<T>(String cacheName, Object[] obs)
    {
        var key = cacheName + ":" + obs.Aggregate(string.Empty, (current, o) => current + $"<{o}>");
        return Get<T>(key);
    }

    private static string Key(string cacheName, object[] obs)
    {
        foreach (var obj in obs)
        {
            if (obj is TimeSpan)
            {
                throw new Exception("缓存参数类型不能能是:TimeSpan类型");
            }
        }
        StringBuilder sb = new StringBuilder(cacheName + ":");
        foreach (var a in obs)
        {
            sb.Append($@"<{KeySingle(a)}>");
        }
        return sb.ToString();
    }

    public static string KeySingle(object t)
    {
        if (t.GetType().IsClass && !t.GetType().IsPrimitive)
        {
            return JsonConvert.SerializeObject(t);
        }
        return t?.ToString();
    }

    /// <summary>
    /// 获取缓存的剩余生存时间
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public TimeSpan GetExpire(string key)
    {
        return _cacheProvider.Cache.GetExpire(key);
    }

    /// <summary>
    /// 获取缓存
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public T Get<T>(string key)
    {
        return _cacheProvider.Cache.Get<T>($"{_cacheOptions.Prefix}{key}");
    }

    /// <summary>
    /// 获取缓存
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="key"></param>
    /// <returns></returns>
    [NonAction]
    public T GetWithOutPrefix<T>(string key)
    {
        return _cacheProvider.Cache.Get<T>($"{key}");
    }

    /// <summary>
    /// 删除缓存
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public int Remove(string key)
    {
        return _cacheProvider.Cache.Remove($"{_cacheOptions.Prefix}{key}");
    }

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    /// <returns></returns>
    public void Clear()
    {
        _cacheProvider.Cache.Clear();

        Cache.Default.Clear();
    }

    /// <summary>
    /// 检查缓存是否存在
    /// </summary>
    /// <param name="key">键</param>
    /// <returns></returns>
    public bool ExistKey(string key)
    {
        return _cacheProvider.Cache.ContainsKey($"{_cacheOptions.Prefix}{key}");
    }

    /// <summary>
    /// 根据键名前缀删除缓存
    /// </summary>
    /// <param name="prefixKey">键名前缀</param>
    /// <returns></returns>
    public int RemoveByPrefixKey(string prefixKey)
    {
        var delKeys = _cacheProvider.Cache == Cache.Default
            ? _cacheProvider.Cache.Keys.Where(u => u.StartsWith($"{_cacheOptions.Prefix}{prefixKey}")).ToArray()
            : ((FullRedis)_cacheProvider.Cache).Search($"{_cacheOptions.Prefix}{prefixKey}*", int.MaxValue).ToArray();
        return _cacheProvider.Cache.Remove(delKeys);
    }

    /// <summary>
    /// 根据键名前缀获取键名集合
    /// </summary>
    /// <param name="prefixKey">键名前缀</param>
    /// <returns></returns>
    public List<string> GetKeysByPrefixKey(string prefixKey)
    {
        return _cacheProvider.Cache == Cache.Default
            ? _cacheProvider.Cache.Keys.Where(u => u.StartsWith($"{_cacheOptions.Prefix}{prefixKey}")).Select(u => u[_cacheOptions.Prefix.Length..]).ToList()
            : ((FullRedis)_cacheProvider.Cache).Search($"{_cacheOptions.Prefix}{prefixKey}*", int.MaxValue).Select(u => u[_cacheOptions.Prefix.Length..]).ToList();
    }

    /// <summary>
    /// 获取缓存值
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public object GetValue(string key)
    {
        // 若Key经过URL编码则进行解码
        if (Regex.IsMatch(key, @"%[0-9a-fA-F]{2}"))
            key = HttpUtility.UrlDecode(key);

        return _cacheProvider.Cache == Cache.Default
            ? _cacheProvider.Cache.Get<object>($"{_cacheOptions.Prefix}{key}")
            : _cacheProvider.Cache.Get<string>($"{_cacheOptions.Prefix}{key}");
    }

    /// <summary>
    /// 获取或添加缓存（在数据不存在时执行委托请求数据）
    /// </summary>
    /// <param name="key"></param>
    /// <param name="callback"></param>
    /// <param name="expire">过期时间，单位秒</param>
    /// <returns></returns>
    public T GetOrAdd<T>(string key, Func<string, T> callback, int expire = -1)
    {
        if (string.IsNullOrWhiteSpace(key)) return default;
        return _cacheProvider.Cache.GetOrAdd($"{_cacheOptions.Prefix}{key}", callback, expire);
    }

    /// <summary>
    /// Hash匹配
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public RedisHash<string, T> GetHashMap<T>(string key)
    {
        return _cacheProvider.Cache.GetDictionary<T>(key) as RedisHash<string, T>;
    }

    /// <summary>
    /// 批量添加HASH
    /// </summary>
    /// <param name="key"></param>
    /// <param name="dic"></param>
    /// <returns></returns>
    public bool HashSet<T>(string key, Dictionary<string, T> dic)
    {
        var hash = GetHashMap<T>(key);
        return hash.HMSet(dic);
    }

    /// <summary>
    /// 添加一条HASH
    /// </summary>
    /// <param name="key"></param>
    /// <param name="hashKey"></param>
    /// <param name="value"></param>
    public void HashAdd<T>(string key, string hashKey, T value)
    {
        var hash = GetHashMap<T>(key);
        hash.Add(hashKey, value);
    }

    /// <summary>
    /// 获取多条HASH
    /// </summary>
    /// <param name="key"></param>
    /// <param name="fields"></param>
    /// <returns></returns>
    public List<T> HashGet<T>(string key, params string[] fields)
    {
        var hash = GetHashMap<T>(key);
        var result = hash.HMGet(fields);
        return result.ToList();
    }

    /// <summary>
    /// 获取一条HASH
    /// </summary>
    /// <param name="key"></param>
    /// <param name="field"></param>
    /// <returns></returns>
    public T HashGetOne<T>(string key, string field)
    {
        var hash = GetHashMap<T>(key);
        var result = hash.HMGet(new string[] { field });
        return result[0];
    }

    /// <summary>
    /// 根据KEY获取所有HASH
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public IDictionary<string, T> HashGetAll<T>(string key)
    {
        var hash = GetHashMap<T>(key);
        return hash.GetAll();
    }

    /// <summary>
    /// 删除HASH
    /// </summary>
    /// <param name="key"></param>
    /// <param name="fields"></param>
    /// <returns></returns>
    public int HashDel<T>(string key, params string[] fields)
    {
        var hash = GetHashMap<T>(key);
        return hash.HDel(fields);
    }
}