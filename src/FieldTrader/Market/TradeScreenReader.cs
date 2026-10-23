using System.Globalization;
using System.Text.RegularExpressions;

namespace FieldTrader.Market;
public sealed record PurchaseReceipt(string ItemName,int? Quantity,decimal? UnitPrice,decimal? Total,bool Complete);
public sealed record SaleTerms(string ItemName,int? Quantity,decimal? DisplayedUnitPrice,decimal? Gross,decimal? ManagementFee,decimal? SaleFee,decimal? NetAfterSaleFee,decimal? NetAfterAllFees);
public sealed record PresenceCandidate(string ItemName,Region TextBounds,double Confidence,bool ItemBoxVerified=false);
public sealed record ListedObservation(string Name,int? Remaining,int? Original,decimal? Price,string RegionKey);
public sealed record TradeScreenFacts(string FrameId,string ContentHash,DateTimeOffset At,MarketPage Page,bool PriceChanged,
    ListingQuota? Quota,PurchaseReceipt? Receipt,SaleTerms? Sale,PresenceCandidate[] Presence,ListedObservation[] Listings,string[] Issues);

public static class TradeScreenReader {
    public static TradeScreenFacts Read(PerceptionResult p,IReadOnlyList<CatalogItem> catalog) {
        var v=p.Vision;var text=v.Texts.Where(t=>t.Confidence>=.9&&t.Bounds.Valid).ToArray();var issues=new List<string>();
        string N(string s)=>Geometry.Normalize(s);
        bool changed=text.Any(t=>t.Bounds.Y<.25&&N(t.Label).Contains("物品价格发生变化")&&N(t.Label).Contains("交易失败"));
        ListingQuota? quota=null;
        var quotaTokens=text.Where(t=>t.Bounds.Y<.17&&N(t.Label).StartsWith("本周上架")).ToArray();
        if(quotaTokens.Length==1) {
            var m=Regex.Match(N(quotaTokens[0].Label),@"^本周上架[:：]?(\d{1,4})/(600|1200)$");
            if(m.Success){var q=new ListingQuota(int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value),ListingQuotaManager.Week(v.CapturedAt),v.CapturedAt,v.ContentHash);if(q.Valid)quota=q;}
        }
        PurchaseReceipt? receipt=null;SaleTerms? sale=null;
        if(p.Page==MarketPage.Receipt) {
            var result=v.Regions.Where(r=>r.Label=="result_region"&&r.Confidence>=.8).ToArray();
            if(result.Length==1){var inside=text.Where(t=>Geometry.Contains(result[0].Bounds,t.Bounds)).ToArray();
                // Receipt names may be absent from the bootstrap catalog. Require the name below the one product card.
                var cards=v.Regions.Where(r=>r.Label=="product_card"&&r.Confidence>=.8&&Geometry.Contains(result[0].Bounds,r.Bounds)).ToArray();
                var names=inside.Where(t=>catalog.Any(i=>i.Confirmed&&N(i.Name)==N(t.Label))).ToArray();
                if(names.Length==0&&cards.Length==1) {
                    var b=cards[0].Bounds;
                    names=inside.Where(t=>t.Bounds.Y>=b.Y+b.Height&&t.Bounds.Y<b.Y+b.Height+.055
                        &&Math.Abs(Geometry.CenterX(t.Bounds)-Geometry.CenterX(b))<.07&&Regex.IsMatch(t.Label,@"[\u4e00-\u9fff]")&&!t.Label.Contains('价')).ToArray();
                }
                decimal? unit=InlineNumber(inside,"均价");decimal? total=InlineNumber(inside,"总价");
                var quantities=inside.Select(t=>Regex.Match(N(t.Label),@"^[x×](\d{1,5})$")).Where(m=>m.Success).ToArray();
                int? qty=quantities.Length==1?int.Parse(quantities[0].Groups[1].Value):null;
                if(names.Length==1)receipt=new(names[0].Label,qty,unit,total,qty>0&&unit>0&&total==unit*qty);
                if(receipt?.Complete!=true)issues.Add("购买结果数量/金额证据不完整；不得仅凭结果页或总价推算数量并入账");
            }
        }
        if(p.Page==MarketPage.Sell) {
            var panels=v.Regions.Where(r=>r.Label=="trade_panel"&&r.Confidence>=.8).ToArray();var quote=p.Quotes.SingleOrDefault();
            if(panels.Length==1&&quote is not null) {
                var inside=text.Where(t=>Geometry.Contains(panels[0].Bounds,t.Bounds)).ToArray();
                var gross=RowNumber(inside,"上架金额");var management=RowNumber(inside,"管理费");var fee=RowNumber(inside,"手续费");var net=RowNumber(inside,"成交后预计获得");
                sale=new(quote.ItemName,quote.Quantity,quote.Price,gross,management,fee,net,gross is {} g&&management is {} a&&fee is {} b?g-a-b:null);
                if(gross is null||management is null||fee is null||net is null||gross-fee!=net||quote.Quantity is null||gross!=quote.Quantity*quote.Price)issues.Add("出售金额、数量、费用未完全核对；不能提交上架");
                issues.Add("显示售价可能被人工修改；未验证页面刚打开，不能单凭截图称为自动推荐价");
            }
        }
        var presence=new List<PresenceCandidate>();var listings=new List<ListedObservation>();
        if(p.Page==MarketPage.Listings) {
            foreach(var token in text.Where(t=>t.Bounds.X>.58&&t.Bounds.Y>.15&&t.Bounds.Y<.9)) {
                var found=catalog.Where(i=>i.Confirmed&&N(i.Name)==N(token.Label)).ToArray();
                if(found.Length==1)presence.Add(new(found[0].Name,token.Bounds,token.Confidence));
            }
            if(presence.Count>0)issues.Add("仓库仅取得OCR名称候选；现有14类模型没有逐商品识别类别，不能声称已定位可点击物品边界");
            foreach(var card in v.Regions.Where(r=>r.Label=="listed_item"&&r.Confidence>=.8&&r.Bounds.X<.55)) {
                var inside=text.Where(t=>Geometry.Contains(card.Bounds,t.Bounds)).ToArray();
                var names=inside.Where(t=>t.Bounds.Y<card.Bounds.Y+card.Bounds.Height*.3&&catalog.Any(i=>i.Confirmed&&N(i.Name)==N(t.Label))).ToArray();
                var counts=inside.Select(t=>Regex.Match(N(t.Label),@"^(\d{1,5})/(\d{1,5})$")).Where(m=>m.Success).ToArray();
                if(names.Length!=1)continue;
                listings.Add(new(names[0].Label,counts.Length==1?int.Parse(counts[0].Groups[1].Value):null,counts.Length==1?int.Parse(counts[0].Groups[2].Value):null,RowNumber(inside,"售价"),$"{card.Bounds.X:F3}:{card.Bounds.Y:F3}"));
            }
        }
        return new(v.FrameId,v.ContentHash,v.CapturedAt,p.Page,changed,quota,receipt,sale,presence.ToArray(),listings.ToArray(),issues.ToArray());
    }
    private static decimal? InlineNumber(VisualToken[] texts,string label) {
        var matches=texts.Select(t=>Regex.Match(Geometry.Normalize(t.Label),"^"+label+@"[:：]([\d,]+)$")).Where(m=>m.Success).ToArray();
        return matches.Length==1&&MarketPerception.TryInteger(matches[0].Groups[1].Value,out var value)?value:null;
    }
    private static decimal? RowNumber(VisualToken[] texts,string label) {
        var labels=texts.Where(t=>Geometry.Normalize(t.Label).TrimEnd(':','：')==label).ToArray();if(labels.Length!=1)return null;
        var numbers=texts.Where(t=>t.Bounds.X>labels[0].Bounds.X+labels[0].Bounds.Width&&Math.Abs(Geometry.CenterY(t.Bounds)-Geometry.CenterY(labels[0].Bounds))<Math.Max(t.Bounds.Height,labels[0].Bounds.Height)*.7)
            .Select(t=>Geometry.Normalize(t.Label).TrimStart('-','−')).Where(s=>Regex.IsMatch(s,@"^(0|[1-9]\d*|[1-9]\d{0,2}(,\d{3})+)$")).ToArray();
        return numbers.Length==1&&decimal.TryParse(numbers[0],NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out var value)?value:null;
    }
}
