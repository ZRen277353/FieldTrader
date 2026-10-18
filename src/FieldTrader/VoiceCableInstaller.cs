using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
namespace FieldTrader;
public static class VoiceCableInstaller {
 public const string SetupHash="734C35DFA6D98F48782A451633CEB471166EC70D60482FD89A1123D0EE3C4F41";
 public static string PackageDirectory=>Path.Combine(AppContext.BaseDirectory,"ThirdParty","VB-CABLE","original-package");
 public static bool IsInstalled(){using var service=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\VBAudioVACMME");return service!=null||AudioCaptureService.EnumerateDevices().Any(d=>d.Name.Contains("CABLE Input",StringComparison.OrdinalIgnoreCase)&&d.Name.Contains("VB-Audio",StringComparison.OrdinalIgnoreCase));}
 public static ProcessStartInfo CreateStartInfo(){string file=Path.Combine(PackageDirectory,"VBCABLE_Setup_x64.exe");if(!File.Exists(file))throw new FileNotFoundException("随附的虚拟麦克风组件缺失，请用完整版安装包覆盖安装。",file);using var stream=File.OpenRead(file);if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(SetupHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("虚拟麦克风安装组件校验失败，请重新下载安装包。");return new(file){WorkingDirectory=PackageDirectory,UseShellExecute=true,Verb="runas"};}
 public static void Launch(){if(IsInstalled())return;using var process=Process.Start(CreateStartInfo());}
}
