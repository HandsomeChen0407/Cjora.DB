namespace Cjora.DB.Extensions;

/// <summary>
/// 枚举拓展
/// </summary>
public static class EnumExtension
{
    /// <summary>
    /// 获取枚举的Description
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static string GetDescription(this System.Enum value)
    {
        return value.GetType().GetMember(value.ToString()).FirstOrDefault()?.GetCustomAttribute<DescriptionAttribute>()
            ?.Description ?? string.Empty;
    }
}