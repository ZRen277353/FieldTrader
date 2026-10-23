using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

// .NET Framework bootstrapper for the already provisioned private .NET 8 runtime.
// Does not install software, change system settings or invoke a command shell.
internal static class PortableLauncher {
    [STAThread]
    private static void Main() {
        try {
            string output=AppDomain.CurrentDomain.BaseDirectory;
            string runtime=Path.GetFullPath(Path.Combine(output,"..","work","dotnet"));
            string app=Path.Combine(output,"FieldTrader-market-cd","FieldTrader.dll");
            string executable=Path.Combine(runtime,"dotnet.exe");
            if(!File.Exists(app)||!File.Exists(executable))throw new FileNotFoundException("找不到配套应用或本地 .NET 8 运行时，请保留 outputs 与 work 的目录关系。");
            var start=new ProcessStartInfo(executable,"\""+app+"\"") {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(app)};
            start.EnvironmentVariables["DOTNET_ROOT"]=runtime;
            Process.Start(start);
        }catch(Exception ex){MessageBox.Show(ex.Message,"战地行商启动失败",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
}
