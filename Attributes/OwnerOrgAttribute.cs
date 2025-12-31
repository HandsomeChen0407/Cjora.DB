namespace Cjora.DB.Attributes;

/// <summary>
/// 所属机构数据权限
/// </summary>
[SuppressSniffer]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public class OwnerOrgAttribute : Attribute
{
}