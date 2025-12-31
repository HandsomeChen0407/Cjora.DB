namespace Cjora.DB.Options;

/// <summary>
/// 缓存配置选项
/// </summary>
public sealed class CacheOptions : IConfigurableOptions<CacheOptions>
{
    /// <summary>
    /// 缓存前缀
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// 缓存类型
    /// </summary>
    public string CacheType { get; set; } = string.Empty;

    /// <summary>
    /// Redis缓存
    /// </summary>
    public RedisOption Redis { get; set; } = new RedisOption();

    public void PostConfigure(CacheOptions options, IConfiguration configuration)
    {
        options.Prefix = string.IsNullOrWhiteSpace(options.Prefix) ? "" : options.Prefix.Trim();
    }
}

/// <summary>
/// Redis缓存
/// </summary>
public sealed class RedisOption : RedisOptions
{
    /// <summary>
    /// 最大消息大小
    /// </summary>
    public int MaxMessageSize { get; set; }

    /// <summary>
    /// 自动检测集群节点
    /// </summary>
    public bool AutoDetect { get; set; } = false;
}