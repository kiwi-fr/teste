using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;

namespace Coclico.Services;

internal enum PacketVerdict
{
    Allow,
    Inspect,
    Block
}
internal record PacketFilterStats(
    bool IsRunning,
    long PacketsSeen,
    long BytesSeen,
    long PacketsAnalyzed,
    long Inspected,
    long TrackersBlocked,
    long HeuristicBlocked,
    string? LastBlockReason);
internal static class PacketParser
{
    public const byte ProtocolIcmp = 1;
    public const byte ProtocolTcp = 6;
    public const byte ProtocolUdp = 17;

    public static bool TryParseIPv4Header(ReadOnlySpan<byte> packet, out int ihl, out byte protocol, out IPAddress src, out IPAddress dst)
    {
        ihl = 0; protocol = 0; src = IPAddress.Any; dst = IPAddress.Any;
        if (packet.Length < 20) return false;
        if ((packet[0] >> 4) != 4) return false;
        ihl = (packet[0] & 0x0F) * 4;
        if (ihl < 20 || packet.Length < ihl) return false;
        protocol = packet[9];
        src = new IPAddress(packet.Slice(12, 4).ToArray());
        dst = new IPAddress(packet.Slice(16, 4).ToArray());
        return true;
    }

    public static void TryParsePorts(ReadOnlySpan<byte> packet, int ihl, byte protocol, out int srcPort, out int dstPort)
    {
        srcPort = 0; dstPort = 0;
        if (protocol != ProtocolTcp && protocol != ProtocolUdp) return;
        if (packet.Length < ihl + 4) return;
        srcPort = (packet[ihl] << 8) | packet[ihl + 1];
        dstPort = (packet[ihl + 2] << 8) | packet[ihl + 3];
    }

    private static int DnsOffset(bool tcp) => tcp ? 2 : 0;

    public static bool TryParseDnsQueryName(ReadOnlySpan<byte> payload, bool tcp, out string qname)
    {
        qname = string.Empty;
        int start = DnsOffset(tcp);
        if (payload.Length < start + 12 + 5) return false;
        int qd = (payload[start + 4] << 8) | payload[start + 5];
        if (qd < 1) return false;
        int pos = start + 12;
        var parts = new List<string>();
        while (pos < payload.Length)
        {
            int len = payload[pos];
            if (len == 0) { pos++; break; }
            if ((len & 0xC0) == 0xC0) { pos += 2; break; }
            if (pos + 1 + len > payload.Length) return false;
            parts.Add(System.Text.Encoding.ASCII.GetString(payload.Slice(pos + 1, len)));
            pos += 1 + len;
            if (parts.Count > 16) return false;
        }
        if (parts.Count == 0) return false;
        qname = string.Join('.', parts).ToLowerInvariant();
        return qname.Length > 0;
    }

    public static List<IPAddress> ParseDnsAnswers(ReadOnlySpan<byte> payload, bool tcp)
    {
        var result = new List<IPAddress>();
        int start = DnsOffset(tcp);
        if (payload.Length < start + 12) return result;
        int qd = (payload[start + 4] << 8) | payload[start + 5];
        int an = (payload[start + 6] << 8) | payload[start + 7];
        int pos = start + 12;
        for (int i = 0; i < qd && pos < payload.Length; i++)
        {
            while (pos < payload.Length)
            {
                int len = payload[pos];
                if (len == 0) { pos++; break; }
                if ((len & 0xC0) == 0xC0) { pos += 2; break; }
                pos += 1 + len;
            }
            pos += 4; 
        }
        for (int i = 0; i < an && pos + 10 <= payload.Length; i++)
        {
            int len = payload[pos];
            if ((len & 0xC0) == 0xC0) pos += 2;
            else { while (pos < payload.Length && payload[pos] != 0) pos += 1 + payload[pos]; pos++; }
            if (pos + 10 > payload.Length) break;
            int type = (payload[pos] << 8) | payload[pos + 1];
            int rdlen = (payload[pos + 8] << 8) | payload[pos + 9];
            pos += 10;
            if (type == 1 && rdlen == 4 && pos + 4 <= payload.Length)
                result.Add(new IPAddress(payload.Slice(pos, 4).ToArray()));
            pos += rdlen;
        }
        return result;
    }

    public static int GetDnsRcode(ReadOnlySpan<byte> payload, bool tcp)
    {
        int start = DnsOffset(tcp);
        if (payload.Length < start + 4) return -1;
        return payload[start + 3] & 0x0F;
    }

    public static bool TryExtractTlsSni(ReadOnlySpan<byte> payload, out string sni)
    {
        sni = string.Empty;
        if (payload.Length < 5 || payload[0] != 0x16) return false;
        int recLen = (payload[3] << 8) | payload[4];
        if (payload.Length < 5 + recLen || recLen < 4) return false;
        ReadOnlySpan<byte> hs = payload.Slice(5, recLen);
        if (hs.Length < 4 || hs[0] != 0x01) return false; 
        int pos = 4 + 2 + 32; 
        if (pos >= hs.Length) return false;
        int sidLen = hs[pos]; pos += 1 + sidLen;
        if (pos + 2 > hs.Length) return false;
        int csLen = (hs[pos] << 8) | hs[pos + 1]; pos += 2 + csLen;
        if (pos + 1 > hs.Length) return false;
        int cmLen = hs[pos]; pos += 1 + cmLen;
        if (pos + 2 > hs.Length) return false;
        int extEnd = pos + 2 + ((hs[pos] << 8) | hs[pos + 1]);
        pos += 2;
        while (pos + 4 <= extEnd && pos + 4 <= hs.Length)
        {
            int extType = (hs[pos] << 8) | hs[pos + 1];
            int extLen = (hs[pos + 2] << 8) | hs[pos + 3];
            int extStart = pos + 4;
            if (extType == 0 && extStart + 5 <= hs.Length) 
            {
                int p = extStart + 2; 
                int nameType = hs[p];
                int nameLen = (hs[p + 1] << 8) | hs[p + 2];
                if (nameType == 0 && p + 3 + nameLen <= hs.Length)
                {
                    sni = System.Text.Encoding.ASCII.GetString(hs.Slice(p + 3, nameLen)).ToLowerInvariant();
                    return sni.Length > 0;
                }
            }
            pos = extStart + extLen;
        }
        return false;
    }

    public static bool TryExtractHttpRequest(ReadOnlySpan<byte> payload, out string host, out string path)
    {
        host = string.Empty; path = string.Empty;
        if (payload.Length < 16) return false;
        string text = System.Text.Encoding.ASCII.GetString(payload);
        int eol = text.IndexOf("\r\n", StringComparison.Ordinal);
        if (eol < 0) return false;
        string firstLine = text.Substring(0, eol);
        int sp1 = firstLine.IndexOf(' ');
        if (sp1 < 0) return false;
        int sp2 = firstLine.IndexOf(' ', sp1 + 1);
        if (sp2 < 0) return false;
        path = firstLine.Substring(sp1 + 1, sp2 - sp1 - 1);
        int hi = text.IndexOf("\r\nHost:", StringComparison.OrdinalIgnoreCase);
        if (hi < 0) return false;
        int hs = hi + 7;
        int he = text.IndexOf("\r\n", hs, StringComparison.Ordinal);
        if (he < 0) he = text.Length;
        host = text.Substring(hs, he - hs).Trim().ToLowerInvariant();
        return host.Length > 0;
    }

    public static bool LooksLikeAscii(ReadOnlySpan<byte> payload)
    {
        int n = Math.Min(payload.Length, 256);
        if (n < 8) return false;
        int printable = 0;
        for (int i = 0; i < n; i++)
        {
            byte b = payload[i];
            if (b >= 0x20 && b < 0x7F) printable++;
        }
        return printable * 100 / n >= 90;
    }

    private static readonly string[] AdTokens =
    [
        "/ads/", "/adserver", "/doubleclick", "/track", "/pixel", "/beacon",
        "adserver", "doubleclick", "/prebid", "/affiliate"
    ];

    public static bool ContainsAdToken(ReadOnlySpan<byte> payload)
    {
        if (!LooksLikeAscii(payload)) return false;
        string s = System.Text.Encoding.ASCII.GetString(payload).ToLowerInvariant();
        foreach (string t in AdTokens)
            if (s.Contains(t, StringComparison.Ordinal)) return true;
        return false;
    }

    public static bool IsPrivateIp(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || IPAddress.IsLoopback(ip);
    }
}

internal static class PacketHeuristics
{
    public static double ShannonEntropy(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var freq = new Dictionary<char, int>();
        foreach (char c in s) freq[c] = freq.TryGetValue(c, out int n) ? n + 1 : 1;
        double h = 0;
        foreach (var kv in freq)
        {
            double p = (double)kv.Value / s.Length;
            h -= p * Math.Log2(p);
        }
        return h;
    }
    public static double DomainScore(string host)
    {
        if (string.IsNullOrEmpty(host)) return 0;
        string name = host;
        int dot = name.IndexOf('.');
        if (dot > 0) name = name.Substring(0, dot); 
        return ShannonEntropy(name) / Math.Log2(36);
    }

    public static bool IsDgaLike(string host) => DomainScore(host) > 0.60;


    public static double BeaconRegularity(double[] intervals)
    {
        if (intervals == null || intervals.Length < 3) return 0;
        double mean = intervals.Average();
        if (mean <= 0) return 0;
        double var = intervals.Average(d => (d - mean) * (d - mean));
        double std = Math.Sqrt(var);
        double score = 1.0 - Math.Min(1.0, std / mean);
        return score;
    }

    public static (PacketVerdict Verdict, string Reason) HeuristicVerdict(string? hostOrSni, double beaconScore)
    {
        double ds = hostOrSni != null ? DomainScore(hostOrSni) : 0;
        if (IsDgaLike(hostOrSni ?? string.Empty))
            return (PacketVerdict.Block, $"Heuristique DGA : '{hostOrSni}' score={ds:F2}");
        if (beaconScore >= 0.80 && ds >= 0.55)
            return (PacketVerdict.Block, $"Beaconing régulier (score={beaconScore:F2}) vers '{hostOrSni}' score domaine={ds:F2}");
        if (beaconScore >= 0.65 || ds >= 0.55)
            return (PacketVerdict.Inspect, $"Comportement suspect : domaine={ds:F2} beacon={beaconScore:F2} ('{hostOrSni}')");
        return (PacketVerdict.Allow, "Comportement normal");
    }
}

internal sealed class PacketFilterService : IDisposable
{
    public const string FirewallRulePrefix = "Coclico-PacketShield-";
    private static readonly HashSet<int> KnownTcpPorts = [21, 22, 25, 53, 80, 110, 143, 443, 465, 587, 993, 995, 3389, 8080];
    private static readonly HashSet<int> KnownUdpPorts = [53, 67, 68, 123, 443, 1900, 5353];
    private static readonly string[] AdPathTokens =
    [
        "/ads/", "/ad/", "/adserver", "/banner", "/banners/", "/sponsor", "/popunder",
        "/popunder/", "/track?", "/tracking/", "/pixel?", "/pixel/", "/beacon",
        "/affiliate", "/doubleclick", "/ads.js", "/adframe", "/prebid", "/analytics.js", "/collect?",
        "/adblock.js", "/adblock-detector", "/noadblock", "/blockadblock", "/detect-adblock", "/fingerprint2"
    ];

    private readonly ConcurrentDictionary<IPAddress, byte> _trackerIps = new();
    private volatile IPAddress[] _trackerIpsSnapshot = Array.Empty<IPAddress>();
    private readonly ConcurrentDictionary<string, byte> _blockedRemotes = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxFirewallRules = 256;
    private readonly ConcurrentQueue<string> _pendingFwIps = new();
    private readonly SemaphoreSlim _fwLock = new(1, 1);
    private readonly ConcurrentQueue<string> _recentBlocks = new();
    private readonly Lock _statsLock = new();
    private readonly CancellationTokenSource _cts = new();

    private Socket? _socket;
    private Task? _receiveLoop;
    private Task? _resolverLoop;
    private long _packetsSeen;
    private long _bytesSeen;
    private long _analyzed;
    private long _inspected;
    private long _blocked;
    private string? _lastBlockReason;
    private DateTime _lastStatsPush = DateTime.MinValue;
    private bool _disposed;

    public bool IsRunning => _socket != null;
    public bool IsAdmin => IsAdministrator();

    public event Action<PacketFilterStats>? StatsChanged;

    public static bool IsAdministrator()
    {
        try
        {
            using WindowsIdentity id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
    public bool Strict { get; set; } = true;
    public bool HeuristicsEnabled { get; set; } = true;

    private sealed class FlowBeacon
    {
        public long LastTicks;
        public readonly Queue<double> Intervals = new();
    }

    private readonly ConcurrentDictionary<string, FlowBeacon> _flows = new(StringComparer.Ordinal);
    private long _heuristicBlocked;
    public static (PacketVerdict Verdict, string Reason) Classify(
        byte protocol, IPAddress remoteIp, int remotePort, string? sni,
        IReadOnlyCollection<IPAddress> trackerIps, IReadOnlyCollection<string> trackerDomains,
        bool strict = false)
    {
        if (protocol == PacketParser.ProtocolIcmp) return (PacketVerdict.Allow, "ICMP autorisé");
        if (protocol != PacketParser.ProtocolTcp && protocol != PacketParser.ProtocolUdp)
            return (PacketVerdict.Allow, "Protocole connu autorisé (n°" + protocol + ")");

        bool ipHit = trackerIps.Any(ip => ip.Equals(remoteIp));
        if (ipHit) return (PacketVerdict.Block, "IP de traqueur connue : " + remoteIp);

        if (!string.IsNullOrEmpty(sni))
        {
            string host = sni.ToLowerInvariant();
            bool sniHit = trackerDomains.Any(d => string.Equals(host, d, StringComparison.Ordinal) || host.EndsWith("." + d, StringComparison.Ordinal));
            if (sniHit) return (PacketVerdict.Block, "SNI traqueur : " + sni);
        }

        bool known = protocol == PacketParser.ProtocolTcp ? KnownTcpPorts.Contains(remotePort) : KnownUdpPorts.Contains(remotePort);
        if (known) return (PacketVerdict.Allow, "Port " + remotePort + " connu");
        if (strict && !PacketParser.IsPrivateIp(remoteIp))
            return (PacketVerdict.Block, "Mode strict : port " + remotePort + " inconnu vers IP publique " + remoteIp + " bloqué");
        return (PacketVerdict.Inspect, "Port " + remotePort + " inhabituel (protocole " + protocol + ") — inspecté");
    }

    public static (PacketVerdict Verdict, string Reason) ClassifyHttpRequest(
        string host, string path, IReadOnlyCollection<string> trackerDomains)
    {
        if (!string.IsNullOrEmpty(host))
        {
            bool hostHit = trackerDomains.Any(d => string.Equals(host, d, StringComparison.Ordinal) || host.EndsWith("." + d, StringComparison.Ordinal));
            if (hostHit) return (PacketVerdict.Block, "HTTP host traqueur : " + host + path);
        }

        string p = path.ToLowerInvariant();
        foreach (string token in AdPathTokens)
        {
            if (p.Contains(token)) return (PacketVerdict.Block, "HTTP chemin de pub détecté (" + token + ") : " + host + path);
        }

        return (PacketVerdict.Allow, "HTTP standard : " + host + path);
    }

    public void Start()
    {
        if (_socket != null) return;
        if (!IsAdministrator())
            throw new InvalidOperationException("Droits administrateur requis pour le bouclier paquets.");

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP)
        {
            ReceiveBufferSize = 4 * 1024 * 1024
        };

        byte[] on = BitConverter.GetBytes(1);
        _socket.IOControl(IOControlCode.ReceiveAll, on, null);
        _socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        _receiveLoop = Task.Run(() => ReceiveLoop(_cts.Token));
        _resolverLoop = Task.Run(() => ResolverLoop(_cts.Token));
        PushStats(force: true);
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _socket?.Close(); } catch { }
        _socket = null;
        PushStats(force: true);
    }

    public IReadOnlyList<string> RecentBlocks => _recentBlocks.ToArray();

    private void ReceiveLoop(CancellationToken ct)
    {
        byte[] buffer = new byte[65536];
        while (!ct.IsCancellationRequested && _socket != null)
        {
            int len;
            try
            {
                len = _socket.Receive(buffer, 0, buffer.Length, SocketFlags.None);
            }
            catch
            {
                break;
            }
            if (len <= 0) continue;
            try
            {
                AnalyzePacket(buffer.AsSpan(0, len));
            }
            catch
            {
            }
        }
    }

    private void AnalyzePacket(ReadOnlySpan<byte> packet)
    {
        Interlocked.Increment(ref _packetsSeen);
        Interlocked.Add(ref _bytesSeen, packet.Length);

        if (!PacketParser.TryParseIPv4Header(packet, out int ihl, out byte protocol, out IPAddress src, out IPAddress dst))
            return;
        PacketParser.TryParsePorts(packet, ihl, protocol, out int srcPort, out int dstPort);

        string? dnsQname = null;
        if ((protocol == PacketParser.ProtocolUdp || protocol == PacketParser.ProtocolTcp) && (srcPort == 53 || dstPort == 53))
        {
            int transportHeader = protocol == PacketParser.ProtocolUdp
                ? 8
                : (packet.Length > ihl + 12 ? (packet[ihl + 12] >> 4) * 4 : 20);
            int dnsStart = ihl + transportHeader;
            if (dnsStart < packet.Length)
            {
                ReadOnlySpan<byte> dns = packet.Slice(dnsStart);
                bool tcp = protocol == PacketParser.ProtocolTcp;
                if (PacketParser.TryParseDnsQueryName(dns, tcp, out string qname))
                {
                    dnsQname = qname;
                    bool isTracker = TrackerDomainCatalog.Domains.Any(d => string.Equals(qname, d, StringComparison.Ordinal) || qname.EndsWith("." + d, StringComparison.Ordinal));
                    if (isTracker)
                    {
                        var answers = PacketParser.ParseDnsAnswers(dns, tcp);
                        Interlocked.Increment(ref _analyzed);
                        Interlocked.Increment(ref _blocked);
                        _lastBlockReason = "DNS traqueur : " + qname + (answers.Count > 0 ? " -> " + string.Join(",", answers.Select(a => a.ToString())) : string.Empty);
                        EnqueueBlock(dst, _lastBlockReason);
                        foreach (IPAddress ip in answers)
                        {
                            _trackerIps[ip] = 0;
                            BlockRemoteIp(ip, _lastBlockReason);
                        }
                        UpdateTrackerSnapshot();
                        PushStats();
                        return;
                    }
                    else if (HeuristicsEnabled && PacketParser.GetDnsRcode(dns, tcp) == 3 && PacketHeuristics.IsDgaLike(qname))
                    {
                        Interlocked.Increment(ref _analyzed);
                        Interlocked.Increment(ref _inspected);
                        _lastBlockReason = "DNS NXDOMAIN + nom DGA : " + qname + " (poste potentiellement infecté)";
                        EnqueueBlock(dst, _lastBlockReason);
                        PushStats();
                    }
                }
            }
        }

        string? sni = null;
        string? httpHost = null;
        string? httpPath = null;
        if (protocol == PacketParser.ProtocolTcp && packet.Length > ihl + 20)
        {
            int dataOffset = (packet[ihl + 12] >> 4) * 4;
            int payloadStart = ihl + dataOffset;
            if (payloadStart >= packet.Length) payloadStart = packet.Length;
            ReadOnlySpan<byte> payload = packet.Slice(payloadStart);
            if (dstPort == 443 && payload.Length > 0 && payload[0] == 0x16)
            {
                PacketParser.TryExtractTlsSni(payload, out sni);
            }
            else if (dstPort == 80)
            {
                if (PacketParser.TryExtractHttpRequest(payload, out httpHost, out httpPath))
                {
                    var (hv, hr) = ClassifyHttpRequest(httpHost, httpPath, TrackerDomainCatalog.Domains);
                    if (hv == PacketVerdict.Block)
                    {
                        Interlocked.Increment(ref _analyzed);
                        Interlocked.Increment(ref _blocked);
                        _lastBlockReason = hr + " (" + dst + ":80)";
                        EnqueueBlock(dst, _lastBlockReason);
                        BlockRemoteIp(dst, _lastBlockReason);
                        PushStats();
                        return;
                    }
                }
            }
            else if (payload.Length > 8 && PacketParser.LooksLikeAscii(payload) && PacketParser.ContainsAdToken(payload))
            {
                Interlocked.Increment(ref _analyzed);
                Interlocked.Increment(ref _blocked);
                Interlocked.Increment(ref _heuristicBlocked);
                _lastBlockReason = "Token pub/tracking dans la trame (" + dst + ":" + dstPort + ")";
                EnqueueBlock(dst, _lastBlockReason);
                BlockRemoteIp(dst, _lastBlockReason);
                PushStats();
                return;
            }
        }

        Interlocked.Increment(ref _analyzed);
        bool dstTracked = _trackerIps.ContainsKey(dst);
        bool needClassify = dstTracked || sni != null || httpHost != null || dnsQname != null
                            || (Strict && !PacketParser.IsPrivateIp(dst));
        PacketVerdict verdict = PacketVerdict.Allow;
        string reason = "Hors périmètre (port connu / flux non inspecté)";
        if (needClassify)
            (verdict, reason) = Classify(protocol, dst, dstPort, sni, _trackerIpsSnapshot, TrackerDomainCatalog.Domains, Strict);
        if (HeuristicsEnabled && verdict != PacketVerdict.Block && (protocol == PacketParser.ProtocolTcp || protocol == PacketParser.ProtocolUdp))
        {
            double beacon = UpdateFlowBeacon(src, dst, dstPort);
            string? host = sni ?? httpHost ?? dnsQname;
            var (hv, hr) = PacketHeuristics.HeuristicVerdict(host, beacon);
            if (hv == PacketVerdict.Block)
            {
                verdict = PacketVerdict.Block;
                reason = hr;
                Interlocked.Increment(ref _heuristicBlocked);
            }
            else if (hv == PacketVerdict.Inspect && verdict == PacketVerdict.Allow)
            {
                verdict = PacketVerdict.Inspect;
                reason = hr;
            }
        }

        if (verdict == PacketVerdict.Inspect) Interlocked.Increment(ref _inspected);
        if (verdict == PacketVerdict.Block)
        {
            Interlocked.Increment(ref _blocked);
            _lastBlockReason = reason + " (" + dst + ":" + dstPort + (sni != null ? " SNI=" + sni : "") + ")";
            EnqueueBlock(dst, _lastBlockReason);
            BlockRemoteIp(dst, _lastBlockReason);
        }
        PushStats();
    }
    private double UpdateFlowBeacon(IPAddress src, IPAddress dst, int dstPort)
    {
        if (PacketParser.IsPrivateIp(dst)) return 0;
        string key = src + ">" + dst + ":" + dstPort;
        long now = Environment.TickCount64;
        var flow = _flows.GetOrAdd(key, _ => new FlowBeacon());
        lock (flow)
        {
            if (flow.LastTicks != 0)
            {
                double interval = now - flow.LastTicks;
                flow.Intervals.Enqueue(interval);
                if (flow.Intervals.Count > 8) flow.Intervals.Dequeue();
            }
            flow.LastTicks = now;
            if (_flows.Count > 8192) _flows.Clear(); 
            return PacketHeuristics.BeaconRegularity(flow.Intervals.ToArray());
        }
    }

    private void EnqueueBlock(IPAddress ip, string reason)
    {
        _recentBlocks.Enqueue($"{DateTime.Now:HH:mm:ss} BLOCK {ip} — {reason}");
        while (_recentBlocks.Count > 100 && _recentBlocks.TryDequeue(out _)) { }
    }

    private void UpdateTrackerSnapshot() => _trackerIpsSnapshot = _trackerIps.Keys.ToArray();

    private void BlockRemoteIp(IPAddress ip, string reason)
    {
        string key = ip.ToString();
        if (!_blockedRemotes.TryAdd(key, 0)) return; 
        if (_blockedRemotes.Count > MaxFirewallRules) return;
        _pendingFwIps.Enqueue(key);
        _ = Task.Run(FlushFirewallQueue);
    }

    private async Task FlushFirewallQueue()
    {
        await _fwLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var batch = new List<string>();
            while (batch.Count < 32 && _pendingFwIps.TryDequeue(out string? ip))
                batch.Add(ip);
            if (batch.Count == 0) return;
            var cmds = new List<string>();
            foreach (string ip in batch)
            {
                string rule = FirewallRulePrefix + ip.Replace('.', '_');
                cmds.Add($"add rule name=\"{rule}\" dir=out action=block remoteip={ip} protocol=any profile=any");
                cmds.Add($"add rule name=\"{rule}\" dir=in action=block remoteip={ip} protocol=any profile=any");
            }
            RunNetshContext("advfirewall firewall", cmds);
        }
        catch
        {
        }
        finally
        {
            _fwLock.Release();
        }
    }

    private async Task ResolverLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshTrackerIps(ct).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshTrackerIps(CancellationToken ct)
    {
        var resolved = new List<IPAddress>();
        foreach (string domain in TrackerDomainCatalog.Domains)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                IPAddress[] addrs = await Dns.GetHostAddressesAsync(domain).ConfigureAwait(false);
                foreach (IPAddress a in addrs)
                {
                    if (a.AddressFamily == AddressFamily.InterNetwork) resolved.Add(a);
                }
            }
            catch
            {
            }
        }

        foreach (IPAddress ip in resolved) _trackerIps[ip] = 0;
        UpdateTrackerSnapshot();
    }

    private void PushStats(bool force = false)
    {
        if (!force && (DateTime.UtcNow - _lastStatsPush).TotalSeconds < 1) return;
        _lastStatsPush = DateTime.UtcNow;
        PacketFilterStats stats;
        lock (_statsLock)
        {
            stats = new PacketFilterStats(
                IsRunning,
                Interlocked.Read(ref _packetsSeen),
                Interlocked.Read(ref _bytesSeen),
                Interlocked.Read(ref _analyzed),
                Interlocked.Read(ref _inspected),
                Interlocked.Read(ref _blocked),
                Interlocked.Read(ref _heuristicBlocked),
                _lastBlockReason);
        }
        StatsChanged?.Invoke(stats);
    }

    private static (int Code, string Output) RunNetshContext(string context, IReadOnlyList<string> commands)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = context,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return (-1, "process null");
            foreach (string c in commands)
                p.StandardInput.WriteLine(c);
            p.StandardInput.WriteLine("exit");
            p.StandardInput.Close();
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts.Dispose();
        _fwLock.Dispose();
    }
}
