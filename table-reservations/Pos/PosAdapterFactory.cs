using System;
using System.Collections.Generic;
using System.Linq;

namespace table_reservations.Pos;

public class PosAdapterFactory
{
   
    private readonly IEnumerable<IPosAdapter> _adapters;

    public PosAdapterFactory(IEnumerable<IPosAdapter> adapters)
    {
        _adapters = adapters;
    }

    public IPosAdapter Get(string providerName)
    {
        var adapter = _adapters.FirstOrDefault(a =>
            a.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase));

        return adapter ?? throw new ArgumentException(
            $"Провайдер '{providerName}' не зарегистрирован. " +
            $"Доступные: {string.Join(", ", _adapters.Select(a => a.ProviderName))}");
    }
}