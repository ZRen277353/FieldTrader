using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
namespace FieldTrader;
public static class VoiceCableValidation {
 public static void Run(string directory){
  Directory.CreateDirectory(directory);var start=VoiceCableInstaller.CreateStartInfo();if(!start.UseShellExecute||start.Verb!="runas"||start.WorkingDirectory!=VoiceCableInstaller.PackageDirectory||start.Arguments.Length!=0)throw new Exception("组件安装启动配置不正确");
  using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"ThirdParty","vbcable-manifest.json")));int count=0;
  foreach(var item in manifest.RootElement.GetProperty("Files").EnumerateArray()){var path=Path.Combine(VoiceCableInstaller.PackageDirectory,item.GetProperty("File").GetString()!);using var stream=File.OpenRead(path);if(Convert.ToHexString(SHA256.HashData(stream))!=item.GetProperty("Sha256").GetString()||stream.Length!=item.GetProperty("Bytes").GetInt64())throw new Exception("原始组件文件缺失或改动："+path);count++;}
  string notice=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"ThirdParty","第三方组件说明.txt"));if(count!=31||!notice.Contains("Donationware")||!notice.Contains("https://vb-audio.com/")||!File.Exists(Path.Combine(VoiceCableInstaller.PackageDirectory,"readme.txt")))throw new Exception("完整原始包或第三方署名缺失");
  File.WriteAllText(Path.Combine(directory,"bundle-validation.json"),JsonSerializer.Serialize(new{Passed=true,OriginalFiles=count,HashesMatch=true,AdministratorPromptConfigured=true,DriverExecuted=false,DriverInstalled=false},new JsonSerializerOptions{WriteIndented=true}));
 }
}
