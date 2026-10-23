using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;

namespace FieldTrader.Market;
public static class MarketIntegrationValidation {
    private sealed class Tone : ISampleProvider {
        private long cursor;
        public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
        public int Read(float[] buffer,int offset,int count){for(int i=0;i<count;i++)buffer[offset+i]=.1f*(float)Math.Sin(2*Math.PI*440*(cursor++/2)/48000);return count;}
    }
    public static async Task Run(string config,string images,string output) {
        Directory.CreateDirectory(output);
        var uiGaps=new List<double>();var uiClock=Stopwatch.StartNew();double previous=0;
        var timer=new DispatcherTimer(TimeSpan.FromMilliseconds(80),DispatcherPriority.Background,(_,_)=>{
            var current=uiClock.Elapsed.TotalMilliseconds;uiGaps.Add(current-previous);previous=current;
        },Application.Current.Dispatcher);timer.Start();
        int decisions=0,starts=0;var submitMs=new List<double>();
        using var pipeline=new RecognitionPipeline(()=>frame=>new(null,Array.Empty<GroupMatch>(),"synthetic matcher"));
        pipeline.AnalysisStarted+=()=>Interlocked.Increment(ref starts);pipeline.Decision+=_=>Interlocked.Increment(ref decisions);
        var audio=Task.Run(async()=>{
            using var mix=new LiveVoiceMix(new Tone());var mixBuffer=new float[960];var random=new Random(75);var clock=Stopwatch.StartNew();
            for(int block=0;block<1000;block++) {
                bool burst=block>=60&&(block-60)%70<12;
                var samples=Enumerable.Range(0,160).Select(_=>burst?(float)((random.NextDouble()*2-1)*.12):0).ToArray();
                var begin=Stopwatch.GetTimestamp();pipeline.Submit(new(samples,16000,DateTimeOffset.Now));submitMs.Add(Stopwatch.GetElapsedTime(begin).TotalMilliseconds);
                if(mix.Read(mixBuffer,0,mixBuffer.Length)!=mixBuffer.Length||mixBuffer.Any(v=>!float.IsFinite(v)))throw new InvalidOperationException("混音未返回完整有效采样");
                int wait=(int)((block+1)*10-clock.ElapsedMilliseconds);if(wait>0)await Task.Delay(wait);
            }
            await Task.Delay(1000);pipeline.Dispose();await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            VoiceMixValidation.Run(Path.Combine(output,"mix-regression"));
            await VoicePlaybackValidation.Run(Path.Combine(output,"playback-regression"));
        });
        try {await Task.WhenAll(audio,Task.Run(()=>MarketStageValidation.AnalyzeFolder(config,images,Path.Combine(output,"vision"))));}
        finally{timer.Stop();}
        if(decisions<3||starts!=decisions)throw new InvalidOperationException($"音频流水线未闭合 starts={starts},decisions={decisions}");
        if(submitMs.Max()>100)throw new InvalidOperationException("音频提交阻塞超过100ms");
        await File.WriteAllTextAsync(Path.Combine(output,"concurrency.json"),JsonSerializer.Serialize(new{
            Passed=true,AudioMode="synthetic samples through real RecognitionPipeline and LiveVoiceMix; synthetic matcher",
            Starts=starts,Decisions=decisions,MaximumSubmitMs=submitMs.Max(),MeanSubmitMs=submitMs.Average(),
            UiTimerTicks=uiGaps.Count,MaximumUiTickIntervalMs=uiGaps.DefaultIfEmpty(0).Max(),
            PhysicalAudioDevicesTested=false,GameInputTested=false
        },new JsonSerializerOptions{WriteIndented=true}));
        var evidence=Directory.EnumerateFiles(Path.Combine(output,"vision","evidence"),"*.json").FirstOrDefault(p=>File.ReadAllText(p).Contains("market-card"));
        if(evidence is not null) await Preview(evidence,output);
    }
    public static async Task Preview(string evidence,string output) {
            Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Directory.CreateDirectory(output);
            using var json=JsonDocument.Parse(File.ReadAllText(evidence));
            var result=json.RootElement.GetProperty("Result").Deserialize<PerceptionResult>()!;
            var screen=json.RootElement.GetProperty("Screen").Deserialize<CapturedScreen>()!;
            var window=new MarketWindow(Path.Combine(output,"ui-data")){Left=-20000,Top=-20000,ShowInTaskbar=false};
            window.Show();await window.Initialization.WaitAsync(TimeSpan.FromSeconds(10));
            var coverage=new MarketDatabase(Path.Combine(output,"ui-data","market","market.sqlite")).Coverage(DateTimeOffset.UtcNow.AddDays(-1));
            var facts=json.RootElement.TryGetProperty("TradeFacts",out var factsJson)?factsJson.Deserialize<TradeScreenFacts>():TradeScreenReader.Read(result,[]);
            window.Show(new(result,0,coverage,null,evidence,screen.Frame.ScreenshotPath,facts),screen.Frame.ScreenshotPath);
            window.UpdateLayout();
            var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(output,"market-window.png")))encoder.Save(stream);
            window.SelectCatalogPreview();await Task.Delay(200);window.UpdateLayout();
            var catalogImage=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);catalogImage.Render(window);
            var catalogEncoder=new PngBitmapEncoder();catalogEncoder.Frames.Add(BitmapFrame.Create(catalogImage));using(var stream=File.Create(Path.Combine(output,"market-catalog.png")))catalogEncoder.Save(stream);
            window.SelectTradingPreview();await Task.Delay(100);window.UpdateLayout();
            var tradingImage=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);tradingImage.Render(window);
            var tradingEncoder=new PngBitmapEncoder();tradingEncoder.Frames.Add(BitmapFrame.Create(tradingImage));using(var stream=File.Create(Path.Combine(output,"market-trading.png")))tradingEncoder.Save(stream);
            window.Close();
            using var session=new SessionState(new CatalogStore(Path.Combine(output,"main-ui-data")),Application.Current.Dispatcher);
            var main=new MainWindow(session){Left=-20000,Top=-20000,ShowInTaskbar=false};main.Show();await Task.Delay(200);main.UpdateLayout();
            var mainImage=new RenderTargetBitmap((int)main.ActualWidth,(int)main.ActualHeight,96,96,PixelFormats.Pbgra32);mainImage.Render(main);
            var mainEncoder=new PngBitmapEncoder();mainEncoder.Frames.Add(BitmapFrame.Create(mainImage));using(var stream=File.Create(Path.Combine(output,"main-window.png")))mainEncoder.Save(stream);main.Close();
    }
}
