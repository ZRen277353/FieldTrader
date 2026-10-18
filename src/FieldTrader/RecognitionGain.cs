using System.IO;
using System.Text.Json;
namespace FieldTrader;
public static class RecognitionGain {
 public static AudioFrame Apply(AudioFrame frame,double gain){
  gain=double.IsFinite(gain)?Math.Clamp(gain,1,8):1;
  if(gain==1)return frame;
  // Internal floats may exceed 1: do not clip or distort loud samples. Never send to speakers.
  return frame with {Mono=frame.Mono.Select(v=>(float)(v*gain)).ToArray(),BackgroundMono=frame.BackgroundMono?.Select(v=>(float)(v*gain)).ToArray()};
 }
 public static double Load(string root){try{double gain=JsonSerializer.Deserialize<double>(File.ReadAllText(Path.Combine(root,"recognition-gain.json")));return double.IsFinite(gain)?Math.Clamp(gain,1,8):4;}catch{return 4;}}
 public static void Save(string root,double gain){var path=Path.Combine(root,"recognition-gain.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(gain));File.Move(path+".tmp",path,true);}
}
