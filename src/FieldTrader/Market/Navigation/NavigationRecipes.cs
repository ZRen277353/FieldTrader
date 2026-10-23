using FieldTrader.Market;

namespace FieldTrader.Navigation;

/// <summary>Business adapters build navigation intents, never trade submissions.</summary>
public static class NavigationRecipes {
    public static NavigationTask OpenMarketProduct(CatalogItem item, TargetQuery calibratedProduct,
        ExecutionMode mode = ExecutionMode.RecognizeOnly) {
        if (!item.Confirmed || calibratedProduct.VisualClass != "product_card"
            || Geometry.Normalize(calibratedProduct.Name) != Geometry.Normalize(item.Name))
            throw new ArgumentException("Confirmed catalog item and matching calibrated product-card query required");
        return new("patrol:" + item.Id, [new("open:" + item.Id, MarketPage.Market.ToString(), calibratedProduct,
            InputOperation.Click, MarketPage.Purchase.ToString(), TimeSpan.FromSeconds(10),
            FailurePolicy.Pause, Effect: LayoutEffect.MayChange, Critical: true, RequiredAfterText: [item.Name],
            ExpectedResultTarget: new(item.Name, "detail_panel", new(0,0,1,1), "购买数量", new(0,0,1,1)))], mode);
    }
    // The caller supplies calibrated contact/warehouse page anchors. The current market classifier
    // does not reliably identify all of those pages: provide an independently tested IPageClassifier.
    public static NavigationTask BrowseContact(string contactId, NavigationStep[] calibratedRoute,
        ExecutionMode mode = ExecutionMode.RecognizeOnly) {
        if (calibratedRoute.Length == 0 || calibratedRoute[^1].ExpectedAfter != MarketPage.Contact.ToString())
            throw new ArgumentException("Contact browse route must end at Contact, before any exchange submission");
        return new("contact:" + contactId, calibratedRoute.ToArray(), mode, BatchMode.DynamicPage);
    }
}
