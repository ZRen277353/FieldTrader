using System.Diagnostics;
using System.Text;
using NAudio.CoreAudioApi;
namespace FieldTrader;
public static class AudioRouting {
 public static string? RecommendedId() {
  using var e=new MMDeviceEnumerator();var games=new List<(string Id,float Peak)>();
  foreach(var device in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))using(device)try{
   var sessions=device.AudioSessionManager?.Sessions;if(sessions==null)continue;
   for(int i=0;i<sessions.Count;i++)using(var session=sessions[i]){
    if(session==null)continue;
    if(session.State!=NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)continue;
    try{using var process=Process.GetProcessById((int)session.GetProcessID);if(process.ProcessName.Equals("UAGame",StringComparison.OrdinalIgnoreCase))games.Add((device.ID,session.AudioMeterInformation.MasterPeakValue));}catch(ArgumentException){}
   }
  }catch(System.Runtime.InteropServices.COMException){}
  if(games.Count>0)return games.OrderByDescending(g=>g.Peak).First().Id;
  try{using var device=e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);return device.ID;}catch{return null;}
 }
 public static string Report() {
  var text=new StringBuilder();text.AppendLine("建议监听 ID："+RecommendedId());using var e=new MMDeviceEnumerator();
  foreach(var role in new[]{Role.Multimedia,Role.Console,Role.Communications})try{using var d=e.GetDefaultAudioEndpoint(DataFlow.Render,role);text.AppendLine($"默认 {role}：{d.FriendlyName} [{d.ID}]");}catch(Exception ex){text.AppendLine(ex.Message);}
  foreach(var device in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))using(device){
   text.AppendLine($"\n输出：{device.FriendlyName}\nID：{device.ID}\n电平：{device.AudioMeterInformation.MasterPeakValue:F6}");
   try{var sessions=device.AudioSessionManager?.Sessions;if(sessions==null)continue;for(int i=0;i<sessions.Count;i++){using var session=sessions[i];string name;try{using var p=Process.GetProcessById((int)session.GetProcessID);name=p.ProcessName;}catch{name="系统/已退出";}text.AppendLine($"  {name}，状态={session.State}，电平={session.AudioMeterInformation.MasterPeakValue:F6}");}}catch(Exception ex){text.AppendLine(ex.Message);}
  }
  return text.ToString();
 }
}
