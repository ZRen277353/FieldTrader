using NAudio.Wave;
namespace FieldTrader;
public static class CaptureDecoderValidation {
 public static IEnumerable<string> Run(){
  foreach(int bits in new[]{8,16,24,32})foreach(int channels in new[]{1,2,6}){
   var f=new WaveFormat(channels==1?16000:48000,bits,channels);var data=new byte[f.BlockAlign*3];
   for(int frame=0;frame<3;frame++)for(int ch=0;ch<channels;ch++){
    int p=frame*f.BlockAlign+ch*bits/8;long value=frame==0?0:frame==1?1L<<(bits-2):-(1L<<(bits-2));
    if(bits==8)data[p]=(byte)(value+128);else for(int b=0;b<bits/8;b++)data[p+b]=(byte)(value>>(8*b));
   }
   Check(f,data);if(bits==24)Check(new WaveFormatExtensible(f.SampleRate,bits,channels),data);
   yield return $"PASS PCM{bits} / {channels}声道 原生采样解码";
  }
  foreach(var f in new WaveFormat[]{WaveFormat.CreateIeeeFloatWaveFormat(48000,2),new WaveFormatExtensible(48000,32,2),WaveFormat.CreateCustomFormat(WaveFormatEncoding.IeeeFloat,48000,2,48000*16,16,64)}){
   var data=new byte[f.BlockAlign*3];for(int i=0;i<6;i++){double value=i/2==0?0:i/2==1?.5:-.5;var bytes=f.BitsPerSample==32?BitConverter.GetBytes((float)value):BitConverter.GetBytes(value);bytes.CopyTo(data,i*f.BitsPerSample/8);}Check(f,data);yield return $"PASS {f} 浮点解码";
  }
  var decoder=new CaptureSampleDecoder(WaveFormat.CreateIeeeFloatWaveFormat(48000,1));if(decoder.Decode(BitConverter.GetBytes(float.NaN),4).Mono[0]!=0)throw new Exception("非有限采样未清理");
  bool rejected=false;try{decoder.Decode(new byte[3],3);}catch(System.IO.InvalidDataException){rejected=true;}if(!rejected)throw new Exception("错误帧边界未拒绝");yield return "PASS 非有限值与不完整帧检查";
 }
 private static void Check(WaveFormat f,byte[] data){var (mono,raw,peaks)=new CaptureSampleDecoder(f).Decode(data,data.Length);if(mono.Length!=3||Math.Abs(mono[0])>1e-6||Math.Abs(mono[1]-.5)>1e-6||Math.Abs(mono[2]+.5)>1e-6||raw.Length!=3*f.Channels||peaks.Any(v=>Math.Abs(v-.5)>1e-6))throw new Exception("解码验证失败："+f);}
}
