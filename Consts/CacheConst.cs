namespace Cjora.DB.Consts;

/// <summary>
/// 缓存相关常量
/// </summary>
public class CacheConst
{
    /// <summary>
    /// 用户机构缓存
    /// </summary>
    public const string KeyUserOrg = "sys_user_org:";

    /// <summary>
    /// 用户选择机构缓存（根据产品）
    /// </summary>
    public const string KeyUserOrgSelectProduct = "sys_user_org:select:";

    /// <summary>
    /// 角色最大数据范围缓存
    /// </summary>
    public const string KeyRoleMaxDataScope = "sys_role_maxDataScope:";

    /// <summary>
    /// 租户缓存（AES 加密）
    /// </summary>
    public const string KeyTenantAES = "sys_tenant_aes";

    /// <summary>
    /// 设备归属租户id
    /// </summary>
    public const string KeyDeviceTenantId = "device_tid:";

    /// <summary>
    /// 用户脱敏角色
    /// </summary>
    public const string KeySysUserTMRole = "sensitive_data_viewer";
}