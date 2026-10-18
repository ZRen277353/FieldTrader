using System.IO;
using NAudio.Wave;
namespace FieldTrader;
public sealed class CaptureSampleDecoder {
 private readonly int bytes,channels,align;private readonly bool floating;
 public CaptureSampleDecoder(WaveFormat f){
  channels=f.Channels;bytes=f.BitsPerSample/8;align=f.BlockAlign;
  var encoding=f.Encoding;
  if(f is WaveFormatExtensible ext){encoding=ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71")?WaveFormatEncoding.IeeeFloat:ext.SubFormat==new Guid("00000001-0000-0010-8000-00aa00389b71")?WaveFormatEncoding.Pcm:WaveFormatEncoding.Unknown;}
  floating=encoding==WaveFormatEncoding.IeeeFloat;
  if(channels<1||f.SampleRate<1||align!=channels*bytes||f.BitsPerSample%8!=0||!(floating?bytes is 4 or 8:encoding==WaveFormatEncoding.Pcm&&bytes is 1 or 2 or 3 or 4))throw new NotSupportedException("不支持的设备原生格式："+f);
 }
 public (float[] Mono,float[] Raw,float[] Peaks) Decode(byte[] buffer,int count){
  if(count<0||count>buffer.Length||count%align!=0)throw new InvalidDataException("音频回调不是完整采样帧");
  var mono=new float[count/align];var raw=new float[mono.Length*channels];var peaks=new float[channels];
  for(int i=0;i<mono.Length;i++){double sum=0;for(int ch=0;ch<channels;ch++){
   int p=i*align+ch*bytes;double v;
   if(floating)v=bytes==4?BitConverter.ToSingle(buffer,p):BitConverter.ToDouble(buffer,p);
   else v=bytes switch {1=>(buffer[p]-128)/128d,2=>BitConverter.ToInt16(buffer,p)/32768d,3=>((buffer[p]|buffer[p+1]<<8|buffer[p+2]<<16)<<8>>8)/8388608d,4=>BitConverter.ToInt32(buffer,p)/2147483648d,_=>0};
   float value=double.IsFinite(v)?(float)Math.Clamp(v,-1,1):0;raw[i*channels+ch]=value;sum+=value;peaks[ch]=Math.Max(peaks[ch],Math.Abs(value));
  }mono[i]=(float)(sum/channels);}
  return(mono,raw,peaks);
 }
}
