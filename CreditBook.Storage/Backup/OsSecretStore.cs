using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CreditBook.Storage.Backup;

/// <summary>No plaintext fallback. macOS login Keychain, Windows Credential Manager, or Linux Secret Service.</summary>
public sealed class OsSecretStore : ISecretStore
{
    const string Service="CreditBook.Backup";
    public string? Read(string name)
    {
        if(OperatingSystem.IsMacOS()) {
            var result=Find(name,out var length,out var data,out var item);
            if(result==-25300) return null; CheckMac(result);
            try { return Marshal.PtrToStringUTF8(data,(int)length); }
            finally { SecKeychainItemFreeContent(IntPtr.Zero,data); if(item!=IntPtr.Zero) CFRelease(item); }
        }
        if(OperatingSystem.IsWindows()) {
            if(!CredRead(Service+":"+name,1,0,out var pointer)) { var error=Marshal.GetLastWin32Error();if(error==1168)return null;throw new Win32Exception(error); }
            try { var c=Marshal.PtrToStructure<Credential>(pointer);return Marshal.PtrToStringUni(c.Blob,(int)c.BlobSize/2); } finally { CredFree(pointer); }
        }
        return SecretTool("lookup",name,null);
    }
    public void Write(string name,string value)
    {
        if(OperatingSystem.IsMacOS()) {
            var result=Find(name,out _,out var old,out var item);
            if(result==0) {
                SecKeychainItemFreeContent(IntPtr.Zero,old);
                try { var data=Encoding.UTF8.GetBytes(value);CheckMac(SecKeychainItemModifyAttributesAndData(item,IntPtr.Zero,(uint)data.Length,data)); }
                finally { CFRelease(item); }
            } else { CheckMac(result==-25300?0:result); var data=Encoding.UTF8.GetBytes(value);CheckMac(SecKeychainAddGenericPassword(IntPtr.Zero,(uint)Encoding.UTF8.GetByteCount(Service),Service,(uint)Encoding.UTF8.GetByteCount(name),name,(uint)data.Length,data,out item));if(item!=IntPtr.Zero)CFRelease(item); }
            return;
        }
        if(OperatingSystem.IsWindows()) {
            var pointer=Marshal.StringToCoTaskMemUni(value);
            try { var c=new Credential { Type=1,Target=Service+":"+name,UserName="CreditBook",Blob=pointer,BlobSize=(uint)Encoding.Unicode.GetByteCount(value),Persist=2 };if(!CredWrite(ref c,0)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
            finally { Marshal.ZeroFreeCoTaskMemUnicode(pointer); }return;
        }
        SecretTool("store",name,value);
    }
    public void Delete(string name)
    {
        if(OperatingSystem.IsMacOS()) { var r=Find(name,out _,out var data,out var item);if(r==-25300)return;CheckMac(r);SecKeychainItemFreeContent(IntPtr.Zero,data);try { CheckMac(SecKeychainItemDelete(item)); } finally {CFRelease(item);}return; }
        if(OperatingSystem.IsWindows()) { if(!CredDelete(Service+":"+name,1,0)&&Marshal.GetLastWin32Error()!=1168)throw new Win32Exception(Marshal.GetLastWin32Error());return; }
        SecretTool("clear",name,null);
    }
    static string? SecretTool(string operation,string name,string? value)
    {
        if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException("Bu sistemde güvenli kimlik kasası desteklenmiyor.");
        var start=new ProcessStartInfo("secret-tool") {RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add(operation); if(operation=="store")start.ArgumentList.Add("--label=CreditBook yedek anahtarı");
        start.ArgumentList.Add("service");start.ArgumentList.Add(Service);start.ArgumentList.Add("account");start.ArgumentList.Add(name);
        try {
            using var process=Process.Start(start)??throw new InvalidOperationException("Güvenli kasa başlatılamadı.");
            if(value!=null)process.StandardInput.Write(value);process.StandardInput.Close();
            var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(15000)) {process.Kill();throw new InvalidOperationException("Güvenli kasa yanıt vermedi.");}
            var text=output.GetAwaiter().GetResult();var errorText=error.GetAwaiter().GetResult();
            if(process.ExitCode==0)return text.TrimEnd('\r','\n');
            if(operation!="store"&&process.ExitCode==1&&string.IsNullOrWhiteSpace(errorText))return null;
            throw new InvalidOperationException("Secret Service güvenli kasası kullanılamıyor.");
        } catch(Win32Exception ex) {throw new PlatformNotSupportedException("Linux için secret-tool ve çalışan Secret Service gerekir; düz metin saklama kullanılmaz.",ex);}
    }
    static int Find(string name,out uint length,out IntPtr data,out IntPtr item)=>SecKeychainFindGenericPassword(IntPtr.Zero,(uint)Encoding.UTF8.GetByteCount(Service),Service,(uint)Encoding.UTF8.GetByteCount(name),name,out length,out data,out item);
    static void CheckMac(int status) {if(status!=0)throw new InvalidOperationException("macOS Keychain hatası: "+status);}
    const string Security="/System/Library/Frameworks/Security.framework/Security";
    [DllImport(Security)] static extern int SecKeychainFindGenericPassword(IntPtr chain,uint serviceLength,[MarshalAs(UnmanagedType.LPUTF8Str)]string service,uint accountLength,[MarshalAs(UnmanagedType.LPUTF8Str)]string account,out uint length,out IntPtr data,out IntPtr item);
    [DllImport(Security)] static extern int SecKeychainAddGenericPassword(IntPtr chain,uint serviceLength,[MarshalAs(UnmanagedType.LPUTF8Str)]string service,uint accountLength,[MarshalAs(UnmanagedType.LPUTF8Str)]string account,uint length,byte[] data,out IntPtr item);
    [DllImport(Security)] static extern int SecKeychainItemModifyAttributesAndData(IntPtr item,IntPtr attributes,uint length,byte[] data);
    [DllImport(Security)] static extern int SecKeychainItemFreeContent(IntPtr attributes,IntPtr data);
    [DllImport(Security)] static extern int SecKeychainItemDelete(IntPtr item);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] static extern void CFRelease(IntPtr obj);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Credential { public uint Flags;public uint Type; public string? Target;public string? Comment;public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;public uint BlobSize;public IntPtr Blob;public uint Persist;public uint AttributeCount;public IntPtr Attributes;public string? TargetAlias;public string? UserName; }
    [DllImport("advapi32",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool CredWrite(ref Credential credential,uint flags);
    [DllImport("advapi32",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool CredDelete(string target,uint type,uint flags);
    [DllImport("advapi32")] static extern void CredFree(IntPtr buffer);
}
