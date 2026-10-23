using System.IO;
using System.Text.Json;

namespace FieldTrader.Market;
public static class MarketStageValidation {
    public static async Task Run(string directory) {
        Directory.CreateDirectory(directory);var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        var now=new DateTimeOffset(2026,10,21,12,0,0,TimeSpan.Zero);
        VisualToken T(string label,double x,double y,double w,double h,double confidence=.99)=>new(label,confidence,new(x,y,w,h));
        var regions=new[]{T("trade_panel",.55,.2,.25,.6),T("detail_panel",.2,.2,.33,.4),T("quantity_field",.65,.63,.13,.04),T("primary_button",.67,.75,.1,.04),T("secondary_button",.55,.75,.1,.04)};
        var texts=new[]{T("测试材料",.21,.21,.1,.02),T("购买数量",.56,.64,.06,.02),T("2",.72,.64,.02,.02),T("平均单价",.56,.69,.06,.02),T("1,200",.72,.69,.05,.02),T("购买",.7,.76,.03,.02),T("关闭",.58,.76,.03,.02)};
        var raw=new VisionResult("f1",now,"hash-one",2560,1440,regions,texts,10,20,25);
        var fused=MarketPerception.Fuse(raw,[]);
        Check(fused.Page==MarketPage.Purchase&&fused.Quotes.Length==1&&fused.Quotes[0].Price==1200&&fused.Quotes[0].Quantity==2,"弹窗商品、平均单价、数量区域关联");
        Check(!fused.Quotes[0].CatalogMatched,"未知商品不得视为已知目录");
        Check(MarketPerception.Fuse(raw with{Texts=texts.Append(T("1500",.65,.69,.05,.02)).ToArray()},[]).Quotes.Length==0,"同一行两个价格拒绝关联");
        Check(MarketPerception.Fuse(raw with{Texts=texts.Where(t=>t.Label!="测试材料").ToArray()},[]).Quotes.Length==0,"无标题禁止价格关联");
        var missingQuantity=MarketPerception.Fuse(raw with{Texts=texts.Where(t=>t.Label!="2").ToArray()},[]);
        Check(missingQuantity.Quotes.Single().Quantity is null,"数量漏读时保留单价观察但不猜测数量");
        Check(MarketPerception.Fuse(raw with{Texts=texts.Append(T("验证码",.2,.1,.1,.02)).ToArray()},[]).Paused,"安全验证提示暂停");
        Check(!MarketPerception.TryInteger("12.800",out _)&&!MarketPerception.TryInteger("12K",out _)&&!MarketPerception.TryInteger("O12",out _),"歧义金额不猜测");
        var cards=raw with{Regions=[T("product_card",.2,.2,.15,.3),T("product_card",.36,.2,.15,.3)],Texts=[T("购买",.05,.03,.04,.02),T("我的交易",.16,.03,.06,.02),T("材料甲",.21,.21,.1,.02),T("100",.25,.45,.04,.02),T("材料乙",.37,.21,.1,.02),T("200",.41,.45,.04,.02)]};
        var cardResult=MarketPerception.Fuse(cards,[]);
        Check(cardResult.Quotes.Length==2&&cardResult.Quotes.Single(q=>q.ItemName=="材料甲").Price==100&&cardResult.Quotes.Single(q=>q.ItemName=="材料乙").Price==200,"相邻商品卡片价格不串联");
        Check(MarketPerception.Fuse(cards with{Regions=cards.Regions.Append(T("confirm_dialog",.3,.3,.3,.3)).ToArray()},[]).Quotes.Length==0,"未知弹窗屏蔽背景报价");
        var path=Path.Combine(directory,"test-"+Guid.NewGuid().ToString("N")+".sqlite");var db=new MarketDatabase(path);
        db.UpsertItem(new("material","测试材料","测试类别",Material:true,Confirmed:true));
        Check(db.Store(fused,true)==1&&db.Store(fused with{Vision=raw with{FrameId="same-image-new-id"}},true)==0,"同截图不同ID去重");
        Check(new MarketDatabase(path).Store(fused,true)==0,"数据库重启去重");
        Check(db.Store(fused with{Vision=raw with{FrameId="f2",ContentHash="hash-two",CapturedAt=now.AddSeconds(30)}},true)==0,"五分钟内同报价避免重复权重");
        Check(db.Statistics("material","purchase-unit").Samples==0&&db.Statistics("material","purchase-unit",replay:true).Samples==1,"回放隔离实时统计");
        Check(db.Coverage(now.AddDays(-1)).Visited==0,"回放不增加实时覆盖率");
        Check(db.Store(fused with{Vision=raw with{FrameId="live"}},false)==1,"实时观察独立保存");
        Check(db.Coverage(now.AddDays(-1)).Visited==1,"有效实时观察计入覆盖率");
        var hourly=db.Hourly("material","purchase-unit");Check(hourly.Length==1&&hourly[0].Hour==20&&hourly[0].Weekday==3,"国服小时与星期统计");
        Check(db.Statistics("material","purchase-unit",PriceSource.ActualFill).Samples==0,"挂单/报价不能成为成交数据");
        Check(db.ImportHistorical(new("reference-1","material",900,null,now,PriceSource.Reference,"purchase-unit"))
            &&db.Statistics("material","purchase-unit",PriceSource.Reference).Samples==1&&db.Statistics("material","purchase-unit").Samples==1,"初始参考价格与OCR观察隔离");
        var summary=MarketDatabase.Summarize([100,200,300,400]);Check(summary.Median==250&&summary.P25==175&&summary.P75==325,"分位数和中位数");
        db.UpsertItem(new("never","未巡查商品",Confirmed:true));
        var scheduler=new MarketScheduler(db);Check(scheduler.Next(now.AddHours(7),new Dictionary<string,double>{{"material",10}})?.Item.Id=="never","最长未巡查时间优先于动态加权");
        db.FailedAttempt("never",now.AddHours(7),"测试失败");Check(scheduler.Next(now.AddHours(7))?.Item.Id=="material","失败退避不阻塞其他商品");
        var action=NavigationPlanner.Plan(fused,new("other","其他商品"));Check(action?.Kind==NavigationKind.CloseDialog,"购买弹窗只允许关闭导航");
        var navigation=NavigationPlanner.Plan(cardResult,new("a","材料甲"));Check(navigation?.Kind==NavigationKind.Product,"OCR名称与YOLO卡片共同定位");
        var search=NavigationPlanner.Plan(MarketPerception.Fuse(cards with{Texts=cards.Texts.Append(T("输入物品名称搜索",.05,.12,.1,.02)).ToArray()},[]),new("absent","未显示商品"));
        Check(search?.Kind==NavigationKind.Search&&search.SearchText=="未显示商品","OCR搜索框导航计划");
        var vision=new FixedVision(raw);var work=new MarketWorkflow(vision,db,Path.Combine(directory,"evidence"));
        work.Stop();bool cancelled=false;try{await work.CollectAsync(new FixedCapture(raw),default);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"紧急停止取消后续采集");
        var sequence=new[]{cards,cards with{FrameId="after-click",ContentHash="after-click",Regions=[],Texts=[]}};
        var sequenced=new SequenceVision(sequence);
        var navWork=new MarketWorkflow(sequenced,db,Path.Combine(directory,"nav-evidence"));var fakeMouse=new ValidationMouse();bool unknown=false;
        try{await navWork.NavigateAsync(new("a","材料甲"),new SequenceCapture(sequence),fakeMouse,default);}catch(InvalidOperationException){unknown=true;}
        Check(unknown&&navWork.Paused&&fakeMouse.Count==1,"导航后未知页面暂停且不重复点击（模拟鼠标）");
        await MarketValidation.Run();checks.Add("原模拟核心回归通过");
        MarketSeedValidation.Run(directory,Check);
        await File.WriteAllTextAsync(Path.Combine(directory,"stage-ab-tests.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,GameInputTested=false,AudioHardwareTested=false},new JsonSerializerOptions{WriteIndented=true}));
    }
    public static async Task AnalyzeFolder(string config,string images,string output,CancellationToken token=default) {
        Directory.CreateDirectory(output);var db=new MarketDatabase(Path.Combine(output,"market.sqlite"));
        await using var vision=new PythonVisionPipeline(config);
        var workflow=new MarketWorkflow(vision,db,Path.Combine(output,"evidence"));var results=new List<object>();
        foreach(var file in Directory.EnumerateFiles(images).Where(p=>new[]{".png",".jpg",".jpeg"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Order()) {
            var update=await workflow.CollectAsync(new ImageFileCapture(file),token);var v=update.Perception.Vision;
            results.Add(new{Image=Path.GetFileName(file),v.FrameId,v.Width,v.Height,v.YoloMs,v.OcrMs,v.TotalMs,RegionCount=v.Regions.Length,TextCount=v.Texts.Length,
                Page=update.Perception.Page.ToString(),update.Perception.Quotes,update.Perception.Issues,update.Inserted,update.ResultPath});
            if(workflow.Paused)break;
        }
        await File.WriteAllTextAsync(Path.Combine(output,"inference-report.json"),JsonSerializer.Serialize(new{Mode="HistoricalReplay",vision.StartupMs,Frames=results,
            Coverage=db.Coverage(DateTimeOffset.UtcNow.AddDays(-1)),Catalog=db.Catalog(),NativeInput=false},new JsonSerializerOptions{WriteIndented=true}),token);
    }
    private sealed class FixedVision(VisionResult value):IVisionPipeline {public Task<VisionResult> AnalyzeAsync(CapturedScreen screen,CancellationToken token)=>Task.FromResult(value);}
    private sealed class FixedCapture(VisionResult value):IScreenSource {public Task<CapturedScreen> CaptureAsync(CancellationToken token)=>Task.FromResult(new CapturedScreen(new(value.FrameId,value.CapturedAt,"fixture.png"),value.ContentHash));}
    private sealed class SequenceVision(VisionResult[] values):IVisionPipeline {private int index;public Task<VisionResult> AnalyzeAsync(CapturedScreen screen,CancellationToken token)=>Task.FromResult(values[Math.Min(index++,values.Length-1)]);}
    private sealed class SequenceCapture(VisionResult[] values):IScreenSource {private int index;public Task<CapturedScreen> CaptureAsync(CancellationToken token){var v=values[Math.Min(index++,values.Length-1)];return Task.FromResult(new CapturedScreen(new(v.FrameId,v.CapturedAt,"fixture.png"),v.ContentHash));}}
    private sealed class ValidationMouse:IMouseExecutor {public bool IsSimulation=>false;public int Count;public Task ExecuteAsync(NavigationAction action,CapturedScreen screen,CancellationToken token){Count++;return Task.CompletedTask;}}
}
