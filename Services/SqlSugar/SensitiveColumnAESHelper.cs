using System.Security.Cryptography;

namespace Cjora.DB.Services;

public static class SensitiveColumnAESHelper
{
    private static string AESKey;

    public static void Init(IConfiguration configuration)
    {
        var dbConnectionOptions = App.GetConfig<DbConnectionOptions>("DbConnection", true);
        AESKey = dbConnectionOptions.SensitiveAesKey ?? throw new Exception("DbConnection SensitiveAesKey 未配置");
    }

    // 默认密钥向量 
    private static byte[] Keys = { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };

    /// <summary>
    /// 加密
    /// </summary>
    /// <param name="encryptString"></param>
    /// <returns></returns>
    public static string Encrypt(string encryptString)
    {
        return Encrypt(encryptString, AESKey);
    }

    /// <summary>
    /// 加密数据
    /// </summary>
    /// <param name="encryptString"></param>
    /// <param name="encryptKey"></param>
    /// <returns></returns>
    private static string Encrypt(string encryptString, string encryptKey)
    {
        byte[] rgbKey = Encoding.UTF8.GetBytes(encryptKey.Substring(0, 16));
        byte[] rgbIV = Keys;
        byte[] inputByteArray = Encoding.UTF8.GetBytes(encryptString);
        var DCSP = Aes.Create();
        DCSP.Key = rgbKey;
        DCSP.Mode = CipherMode.ECB;
        DCSP.Padding = PaddingMode.PKCS7;
        System.IO.MemoryStream mStream = new System.IO.MemoryStream();
        CryptoStream cStream = new CryptoStream(mStream, DCSP.CreateEncryptor(rgbKey, rgbIV), CryptoStreamMode.Write);
        cStream.Write(inputByteArray, 0, inputByteArray.Length);
        cStream.FlushFinalBlock();
        return Convert.ToBase64String(mStream.ToArray());
    }

    /// <summary>
    /// 解密
    /// </summary>
    /// <param name="Text"></param>
    /// <returns></returns>
    public static string Decrypt(string decryptString)
    {
        return Decrypt(decryptString, AESKey);
    }

    /// <summary> 
    /// 解密数据 
    /// </summary> 
    /// <param name="Text"></param> 
    /// <param name="sKey"></param> 
    /// <returns></returns> 
    private static string Decrypt(string decryptString, string decryptKey)
    {
        byte[] rgbKey = Encoding.UTF8.GetBytes(decryptKey.Substring(0, 16));
        byte[] rgbIV = Keys;
        byte[] inputByteArray = Convert.FromBase64String(decryptString);
        var DCSP = Aes.Create();
        DCSP.Key = rgbKey;
        DCSP.Mode = CipherMode.ECB;
        DCSP.Padding = PaddingMode.PKCS7;
        System.IO.MemoryStream mStream = new System.IO.MemoryStream();
        CryptoStream cStream = new CryptoStream(mStream, DCSP.CreateDecryptor(rgbKey, rgbIV), CryptoStreamMode.Write);
        Byte[] inputByteArrays = new byte[inputByteArray.Length];
        cStream.Write(inputByteArray, 0, inputByteArray.Length);
        cStream.FlushFinalBlock();
        return Encoding.UTF8.GetString(mStream.ToArray());
    }
}
