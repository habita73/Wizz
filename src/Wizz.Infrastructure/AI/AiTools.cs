namespace Wizz.Infrastructure.AI;

using System.Text.Json;
using Wizz.Application.Abstractions;

public sealed class AiToolRegistry : IAiToolRegistry
{
    private readonly Dictionary<string, IAiTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IAiTool tool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));
        _tools[tool.Name] = tool;
    }

    public IAiTool? GetTool(string name) => _tools.TryGetValue(name, out var tool) ? tool : null;

    public IReadOnlyList<IAiTool> GetAllTools() => _tools.Values.ToList();
}

public sealed class GetSalesSummaryTool : IAiTool
{
    private readonly ISaleRepository _sales;
    public GetSalesSummaryTool(ISaleRepository sales) => _sales = sales;
    public string Name => "get_sales_summary";
    public string Description => "الحصول على ملخص المبيعات لفترة محددة.";
    public string ParametersJsonSchema => """{"type":"object","properties":{"from":{"type":"string","format":"date-time"},"to":{"type":"string","format":"date-time"}},"required":["from","to"]}""";

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonSerializer.Deserialize<SalesSummaryArgs>(argumentsJson);
        if (args == null) return "{\"error\":\"معاملات غير صالحة\"}";
        var total = await _sales.GetTotalSalesAsync(args.From, args.To, ct);
        var paid = await _sales.GetTotalPaidAsync(args.From, args.To, ct);
        var remaining = await _sales.GetTotalRemainingAsync(ct);
        var count = await _sales.CountAsync(args.From, args.To, ct);
        return JsonSerializer.Serialize(new { totalSales = total, totalPaid = paid, totalRemaining = remaining, salesCount = count, period = new { from = args.From, to = args.To } });
    }
    private sealed record SalesSummaryArgs(DateTime From, DateTime To);
}

public sealed class SearchProductTool : IAiTool
{
    private readonly IProductRepository _products;
    public SearchProductTool(IProductRepository products) => _products = products;
    public string Name => "search_product";
    public string Description => "البحث عن منتج بالاسم أو الباركود.";
    public string ParametersJsonSchema => """{"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer","default":10}},"required":["query"]}""";

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonSerializer.Deserialize<SearchArgs>(argumentsJson);
        if (args == null) return "{\"error\":\"معاملات غير صالحة\"}";
        var products = await _products.SearchAsync(args.Query, args.Limit, ct);
        var result = products.Select(p => new { id = p.Id, name = p.Name, barcode = p.Barcode, retailPrice = p.RetailPrice, wholesalePrice = p.WholesalePrice, isService = p.IsService });
        return JsonSerializer.Serialize(new { products = result });
    }
    private sealed record SearchArgs(string Query, int Limit = 10);
}

public sealed class GetStoreInfoTool : IAiTool
{
    private readonly IStoreRepository _store;
    public GetStoreInfoTool(IStoreRepository store) => _store = store;
    public string Name => "get_store_info";
    public string Description => "الحصول على معلومات المتجر.";
    public string ParametersJsonSchema => """{"type":"object","properties":{}}""";

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        var store = await _store.GetAsync(ct);
        if (store == null) return "{\"error\":\"لم يتم إعداد المتجر\"}";
        return JsonSerializer.Serialize(new { name = store.Name, address = store.Address, phone = store.Phone, taxNumber = store.TaxNumber, commercialRegister = store.CommercialRegister, allowCreditSales = store.AllowCreditSales, currency = store.Currency });
    }
}