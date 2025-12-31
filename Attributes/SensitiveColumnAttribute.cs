namespace Cjora.DB.Attributes;

/// <summary>
/// 敏感列特性
/// </summary>
[SuppressSniffer]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public class SensitiveColumnAttribute : Attribute
{
    public SensitiveType Type { get; set; }

    /// <summary>
    /// 可选自定义脱敏规则
    /// </summary>
    public Func<string, string>? CustomMaskFunc { get; set; }

    public SensitiveColumnAttribute(SensitiveType type)
    {
        Type = type;
    }
}

/// <summary>
/// 脱敏规则类型
/// </summary>
public enum SensitiveType
{
    /// <summary>
    /// 手机号
    /// </summary>
    Phone,
    /// <summary>
    /// 身份证
    /// </summary>
    IdCard,
    /// <summary>
    /// 姓名
    /// </summary>
    Name,
    /// <summary>
    /// 邮箱
    /// </summary>
    Email,
    /// <summary>
    /// 银行卡号
    /// </summary>
    BankCard,
    /// <summary>
    /// 合同链接
    /// </summary>
    ContractUrl,
    /// <summary>
    /// 密码
    /// </summary>
    Password,
    /// <summary>
    /// 自定义脱敏规则
    /// </summary>
    Custom = 99
}
