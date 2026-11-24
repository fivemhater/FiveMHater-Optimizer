using System;
using System.Collections.Generic;
using System.Linq;

namespace FiveMHaterOptimizer.Services;

public sealed class VerificationItem
{
    public string Id { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Func<bool> Check { get; init; } = () => false;
}

/// <summary>Uygulanan her ayarı sonradan tek tek yeniden okuyup doğrulamak için kayıt listesi.</summary>
public sealed class VerificationList
{
    private readonly object _lock = new();
    private readonly Dictionary<string, VerificationItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = new();

    public int Count { get { lock (_lock) return _items.Count; } }

    public void Add(string id, string description, Func<bool> check)
    {
        lock (_lock)
        {
            if (!_items.ContainsKey(id)) _order.Add(id);
            _items[id] = new VerificationItem { Id = id, Description = description, Check = check };
        }
    }

    public (int ok, int total, List<string> failed) Run()
    {
        List<VerificationItem> list;
        lock (_lock) list = _order.Select(id => _items[id]).ToList();
        int ok = 0;
        var failed = new List<string>();
        foreach (var item in list)
        {
            bool pass;
            try { pass = item.Check(); }
            catch { pass = false; }
            if (pass) ok++;
            else failed.Add(item.Description);
        }
        return (ok, list.Count, failed);
    }
}
