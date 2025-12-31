namespace Cjora.DB.Services;

/// <summary>
/// 租户
/// </summary>
public class TenantDto
{
    /// <summary>
    /// Id
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 数据库类型
    /// </summary>
    public SqlSugar.DbType DbType { get; set; }

    /// <summary>
    /// 数据库连接
    /// </summary>
    public string Connection { get; set; } = string.Empty;
}