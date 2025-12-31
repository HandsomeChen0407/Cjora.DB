using Microsoft.Extensions.Options;

namespace Cjora.DB.Services;

public class SysGeoCacheService : ISingleton
{
    private readonly FullRedis _redis;
    private readonly CacheOptions _cacheOptions;

    public SysGeoCacheService(ICacheProvider cacheProvider, IOptions<CacheOptions> cacheOptions)
    {
        _redis = cacheProvider.Cache as FullRedis ?? throw new ArgumentException("当前缓存未使用Redis实现");
        _cacheOptions = cacheOptions.Value;
    }

    /// <summary>
    /// 添加或更新地理坐标（GEOADD）
    /// </summary>
    /// <param name="key">GEO key</param>
    /// <param name="longitude">经度</param>
    /// <param name="latitude">维度</param>
    /// <param name="member">成员</param>
    /// <returns></returns>
    public bool GeoAdd(string key, decimal longitude, decimal latitude, string member)
    {
        if (string.IsNullOrWhiteSpace(key) || longitude <= 0 || latitude <= 0 || string.IsNullOrWhiteSpace(member)) return false;
        var fullKey = $"{_cacheOptions.Prefix}{key}";
        _redis.Execute(fullKey, (client, k) => client.Execute("GEOADD", k, longitude, latitude, member), true);
        return true;
    }

    /// <summary>
    /// 批量添加地理坐标
    /// </summary>
    /// <param name="key">GEO key</param>
    /// <param name="locations">经纬度+成员</param>
    public bool GeoAddRange(string key, IEnumerable<(decimal longitude, decimal latitude, string member)> locations)
    {
        if (string.IsNullOrWhiteSpace(key) || locations == null || !locations.Any()) return false;
        var fullKey = $"{_cacheOptions.Prefix}{key}";
        var args = new List<object> { fullKey };
        foreach (var (lng, lat, sId) in locations)
        {
            args.Add(lng);
            args.Add(lat);
            args.Add(sId);
        }
        _redis.Execute(fullKey, (client, k) => client.Execute("GEOADD", args.ToArray()), true);
        return true;
    }

    /// <summary>
    /// 分页安全地获取 ZSet 全部成员（ZRANGE 分段取出，避免性能问题）
    /// 并合并为一个 char[] 返回。
    /// </summary>
    public string[]? ZRangeAllPaged(string key, int pageSize = 1000)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var fullKey = $"{_cacheOptions.Prefix}{key}";
        long start = 0;

        var result = new List<string>();

        while (true)
        {
            try 
            {
                // 每次分段从 Redis 拉取
                var chunk = _redis.Execute(fullKey, (client, k) =>
                    client.Execute<string[]>("ZRANGE", k, start, start + pageSize - 1)
                );

                if (chunk == null || chunk.Length == 0)
                    break;

                result.AddRange(chunk);

                // 若少于 pageSize，说明已到结尾
                if (chunk.Length < pageSize)
                    break;

                start += pageSize; // 移动分页游标，防止死循环
            }
            catch (Exception ex)
            {
                Furion.Logging.Log.Error($"分页获取 ZSet 全部成员异常：{ex.ToJson()}");
                return null;
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// 删除 GEO 成员（ZREM）
    /// </summary>
    /// <param name="key">GEO key</param>
    /// <param name="members">成员</param>
    public bool GeoRemove(string key, params string[] members)
    {
        if (string.IsNullOrWhiteSpace(key) || members == null || members.Length == 0) return false;
        var fullKey = $"{_cacheOptions.Prefix}{key}";
        var args = new List<object> { fullKey };
        args.AddRange(members);
        var result = _redis.Execute(fullKey, (client, k) => client.Execute("ZREM", args.ToArray()), true);
        return Convert.ToInt32(result) > 0;
    }

    /// <summary>
    /// 获取两个成员之间的距离（单位：米）
    /// </summary>
    /// <param name="key">GEO key</param>
    /// <param name="member1">成员1</param>
    /// <param name="member2">成员2</param>
    /// <returns></returns>
    public decimal GeoDistance(string key, string member1, string member2)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(member1) || string.IsNullOrWhiteSpace(member2)) return 0;
        var fullKey = $"{_cacheOptions.Prefix}{key}";
        var result = _redis.Execute(fullKey, (client, k) => client.Execute("GEODIST", k, member1, member2, "m"));
        if (result == null) return 0;
        return decimal.TryParse(result.ToString(), out var dist) ? dist : 0;
    }

    /// <summary>
    /// 查询指定经纬度半径内的成员，兼容低版本 Redis
    /// </summary>
    /// <param name="key">GEO key</param>
    /// <param name="longitude">经度</param>
    /// <param name="latitude">维度</param>
    /// <param name="radiusMeters">半径，单位米</param>
    /// <param name="count">返回个数</param>
    /// <returns></returns>
    public char[]? GeoSearch(string key, decimal longitude, decimal latitude, double radiusMeters, int count = 50)
    {
        if (string.IsNullOrWhiteSpace(key) || longitude <= 0 || latitude <= 0 || radiusMeters <= 0 || count <= 0)
            return null;

        var fullKey = $"{_cacheOptions.Prefix}{key}";

        // 尝试使用 Redis 6.2+ GEOSEARCH
        try
        {
            return _redis.Execute(fullKey, (client, k) =>
                client.Execute<char[]>("GEOSEARCH", k,
                    "FROMLONLAT", longitude, latitude,
                    "BYRADIUS", radiusMeters, "m",
                    "ASC", "COUNT", count
                )
            );
        }
        catch (Exception ex) when (ex.Message.Contains("unknown command `GEOSEARCH`"))
        {
            // Redis 版本低，不支持 GEOSEARCH，降级使用 GEORADIUS
            return _redis.Execute(fullKey, (client, k) =>
                client.Execute<char[]>("GEORADIUS", k,
                    longitude, latitude,
                    radiusMeters, "m",
                    "ASC", "COUNT", count
                )
            );
        }
    }
}
