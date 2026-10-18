using System.IO;
using System.Text.Json;
using System.Diagnostics;
namespace FieldTrader;
public static class PipelineValidation {
 public static async Task Run(string directory){
  Directory.CreateDirectory(directory);var records=new List<object>();
  // Synthetic bursts trigger the real detector; matcher deliberately takes 1.2s.
  var lags=new List<double>();int starts=0,decisions=0;
  var pipeline=new RecognitionPipeline(()=>frame=>{Thread.Sleep(1200);return new(null,Array.Empty<GroupMatch>(),frame.Timestamp.ToString("O"));});
  pipeline.AnalysisStarted+=()=>Interlocked.Increment(ref starts);
  pipeline.Decision+=d=>{lock(lags)lags.Add((DateTimeOffset.Now-DateTimeOffset.Parse(d.Reason)).TotalSeconds);Interlocked.Increment(ref decisions);};
  var clock=Stopwatch.StartNew();var rng=new Random(75);
  for(int block=0;block<800;block++){
   bool burst=block>=60&&(block-60)%70<12;
   var x=Enumerable.Range(0,160).Select(_=>burst?(float)((rng.NextDouble()*2-1)*.12):0).ToArray();pipeline.Submit(new(x,16000,DateTimeOffset.Now));
   int wait=(int)((block+1)*10-clock.ElapsedMilliseconds);if(wait>0)await Task.Delay(wait);
  }
  await Task.Delay(2000);pipeline.Dispose();await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(5));
  double max;lock(lags)max=lags.DefaultIfEmpty(0).Max();if(decisions<3||max>3.2||starts!=decisions)throw new Exception($"积压/状态不闭合：{decisions}, {max:F2}s, starts={starts}");
  records.Add(new{Scenario="连续声音，匹配器人为减速至1.2秒",Starts=starts,Decisions=decisions,MaxOnsetToDecisionSeconds=max});
  // Match exceptions must finish analyzing with an explicit unknown decision.
  int failures=0;var broken=new RecognitionPipeline(()=>_=>throw new InvalidOperationException("测试错误"));broken.Decision+=d=>{if(d.Result==null&&d.Reason.Contains("测试错误"))Interlocked.Increment(ref failures);};
  for(int b=0;b<120;b++){var x=Enumerable.Range(0,160).Select(_=>b is >=50 and <62?(float)((rng.NextDouble()*2-1)*.12):0).ToArray();broken.Submit(new(x,16000,DateTimeOffset.Now));await Task.Delay(10);}
  await broken.Completion.WaitAsync(TimeSpan.FromSeconds(5));broken.Dispose();if(failures!=1)throw new Exception("异常未结束分析状态");records.Add(new{Scenario="匹配器异常显示明确不确定",Passed=true});
  var seed=Path.Combine(AppContext.BaseDirectory,"SeedData");var catalog=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(seed,"catalog.json")))!;
  using var real=new RecognitionPipeline(catalog,seed);var output=new List<object>();real.Decision+=d=>{lock(output)output.Add(new{Accepted=d.Result!=null,d.Reason,Group=d.Result?.SoundGroupId,AgeSeconds=d.Result==null?(double?)null:(DateTimeOffset.Now-d.Result.Timestamp).TotalSeconds});};
  var audio=AudioFiles.Read(Path.Combine("work","outdoor0927","audio.wav"));clock.Restart();int step=audio.SampleRate/100;
  for(int i=0;i<audio.Mono.Length;i+=step){real.Submit(new(audio.Mono[i..Math.Min(audio.Mono.Length,i+step)],audio.SampleRate,DateTimeOffset.Now));int wait=(int)((i+step)*1000L/audio.SampleRate-clock.ElapsedMilliseconds);if(wait>0)await Task.Delay(wait);}
  await Task.Delay(2500);real.Dispose();await real.Completion.WaitAsync(TimeSpan.FromSeconds(5));
  File.WriteAllText(Path.Combine(directory,"pipeline.json"),JsonSerializer.Serialize(new{Checks=records,RealReplay=output},new JsonSerializerOptions{WriteIndented=true}));
 }
}
