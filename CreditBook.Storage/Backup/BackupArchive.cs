using System.Security.Cryptography;
using System.Text;

namespace CreditBook.Storage.Backup;

/// <summary>Versioned AES-256-GCM envelope. The authenticated header contains a non-secret key fingerprint.</summary>
public static class BackupArchive
{
    const int HeaderLength=8+16+12;
    static readonly byte[] Magic=Encoding.ASCII.GetBytes("CBKUP001");
    public static string GenerateRecoveryKey()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public static string KeyId(string recoveryKey)=>Convert.ToHexString(SHA256.HashData(ParseKey(recoveryKey)).AsSpan(0,16));
    public static byte[] Encrypt(byte[] database, string recoveryKey)
    {
        var key=ParseKey(recoveryKey);
        try {
            var archive=new byte[HeaderLength+16+database.Length]; Magic.CopyTo(archive,0);
            SHA256.HashData(key).AsSpan(0,16).CopyTo(archive.AsSpan(8,16)); RandomNumberGenerator.Fill(archive.AsSpan(24,12));
            using var aes=new AesGcm(key,16);
            aes.Encrypt(archive.AsSpan(24,12),database,archive.AsSpan(HeaderLength+16),archive.AsSpan(HeaderLength,16),archive.AsSpan(0,HeaderLength));
            return archive;
        } finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static byte[] Decrypt(byte[] archive,string recoveryKey)
    {
        if(archive.Length<HeaderLength+16 || !archive.AsSpan(0,8).SequenceEqual(Magic)) throw new CryptographicException("Geçersiz yedek biçimi.");
        var key=ParseKey(recoveryKey);
        try {
            if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(key).AsSpan(0,16),archive.AsSpan(8,16))) throw new CryptographicException("Bu yedek için farklı bir kurtarma anahtarı gerekiyor.");
            var plain=new byte[archive.Length-HeaderLength-16]; using var aes=new AesGcm(key,16);
            aes.Decrypt(archive.AsSpan(24,12),archive.AsSpan(HeaderLength+16),archive.AsSpan(HeaderLength,16),plain,archive.AsSpan(0,HeaderLength));return plain;
        } finally { CryptographicOperations.ZeroMemory(key); }
    }
    static byte[] ParseKey(string key)
    {
        try { var bytes=Convert.FromBase64String(key.Trim()); if(bytes.Length==32) return bytes; }
        catch(FormatException) { }
        throw new CryptographicException("Kurtarma anahtarı 32 baytlık Base64 olmalı.");
    }
}
