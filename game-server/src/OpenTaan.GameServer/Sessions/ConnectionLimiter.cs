using System.Net;

namespace OpenTaan.GameServer.Sessions;

/// <summary>Whether a new connection may be held open.</summary>
public enum Admission
{
    /// <summary>Admitted. It must be released when it closes.</summary>
    Admitted,

    /// <summary>The server holds as many connections as it allows.</summary>
    ServerFull,

    /// <summary>The address holds as many connections as one address may.</summary>
    AddressFull,
}

/// <summary>Counts open connections, in total and per address, against their limits.</summary>
public sealed class ConnectionLimiter(int maxConnections, int maxConnectionsPerAddress)
{
    private readonly Dictionary<IPAddress, int> _perAddress = [];
    private readonly Lock _lock = new();
    private int _open;

    /// <summary>Connections admitted and not yet released.</summary>
    public int Open
    {
        get
        {
            lock (_lock)
            {
                return _open;
            }
        }
    }

    /// <summary>Admits a connection from <paramref name="address"/> if both limits allow it.</summary>
    public Admission TryAdmit(IPAddress address)
    {
        lock (_lock)
        {
            if (_open >= maxConnections)
            {
                return Admission.ServerFull;
            }

            var fromAddress = _perAddress.GetValueOrDefault(address);
            if (fromAddress >= maxConnectionsPerAddress)
            {
                return Admission.AddressFull;
            }

            _perAddress[address] = fromAddress + 1;
            _open++;
            return Admission.Admitted;
        }
    }

    /// <summary>Releases a connection <see cref="TryAdmit"/> admitted.</summary>
    public void Release(IPAddress address)
    {
        lock (_lock)
        {
            var remaining = _perAddress[address] - 1;
            if (remaining == 0)
            {
                _perAddress.Remove(address);
            }
            else
            {
                _perAddress[address] = remaining;
            }

            _open--;
        }
    }
}
