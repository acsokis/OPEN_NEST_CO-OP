using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using OpenNestCoop.Core;

namespace OpenNestCoop.Net;

/// <summary>
/// 本地 TCP 回环传输（双开测试用，不经 Steam）。
/// - host：StartHost(port) 监听 127.0.0.1，接受 client 连接，peerId=1。
/// - client：Connect(port) 连 127.0.0.1，peerId=2。
/// - 每条消息：[4B 大端长度][payload]。
/// - 后台线程读 TCP → 入队；Poll 从队列取（主线程）。
/// - TCP 天然可靠（reliable 标志忽略，仅接口兼容）。
/// </summary>
public class LocalTransport : ITransport, IDisposable
{
    private TcpListener _listener;
    private sealed class Connection : IDisposable
    {
        internal readonly TcpClient Client;
        internal readonly NetworkStream Stream;
        internal readonly object SendGate = new();
        internal readonly long Generation;
        internal Connection(TcpClient client, long generation) { Client = client; Stream = client.GetStream(); Generation = generation; }
        public void Dispose() { try { Client.Close(); } catch { } }
    }
    private Connection _connection;
    private long _generation;
    private volatile bool _running;

    /// <summary>本端 peerId（host=1, client=2）。</summary>
    public ulong LocalPeerId = 1;

    // 接收队列：peerId → 数据
    private readonly ConcurrentQueue<(long, ulong, byte[])> _incoming = new();

    /// <summary>是否有客户端连上（host 用）。</summary>
    public event Action ClientConnected;

    /// <summary>启动 TCP 服务器（host）。</summary>
    public bool StartHost(int port)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            LocalPeerId = 1;
            _running = true;
            var t = new Thread(AcceptLoop);
            t.IsBackground = true;
            t.Start();
            CoopRuntime.LogSource?.LogInfo($"[LocalTransport] host listening on 127.0.0.1:{port}");
            return true;
        }
        catch (Exception ex) { CoopRuntime.LogSource?.LogWarning($"[LocalTransport] StartHost failed: {ex.Message}"); return false; }
    }

    private void AcceptLoop()
    {
        try
        {
            while (_running)
            {
                var client = _listener.AcceptTcpClient();
                client.NoDelay = true;
                // 只接受一个 client（双开测试）
                Attach(client);
                CoopRuntime.LogSource?.LogInfo("[LocalTransport] client connected");
                try { ClientConnected?.Invoke(); } catch { }
            }
        }
        catch { }
    }

    /// <summary>连接 host（client）。失败返回 false（可重试）。</summary>
    public bool Connect(int port)
    {
        try
        {
            var client = new TcpClient();
            client.NoDelay = true;
            var iar = client.BeginConnect(IPAddress.Loopback, port, null, null);
            if (!iar.AsyncWaitHandle.WaitOne(300)) // 300ms 超时，避免 host 未就绪时长时间卡住
            {
                try { client.Close(); } catch { }
                return false;
            }
            client.EndConnect(iar);
            LocalPeerId = 2;
            _running = true;
            Attach(client);
            CoopRuntime.LogSource?.LogInfo($"[LocalTransport] client connected to host 127.0.0.1:{port}");
            return true;
        }
        catch (Exception ex) { CoopRuntime.LogSource?.LogWarning($"[LocalTransport] Connect failed: {ex.Message}"); return false; }
    }

    private void Attach(TcpClient client)
    {
        var connection = new Connection(client, Interlocked.Increment(ref _generation));
        var previous = Interlocked.Exchange(ref _connection, connection);
        previous?.Dispose();
        var reader = new Thread(() => ReadLoop(connection)) { IsBackground = true };
        reader.Start();
    }

    private void ReadLoop(Connection connection)
    {
        try
        {
            var lenBuf = new byte[4];
            while (_running && ReferenceEquals(Volatile.Read(ref _connection), connection))
            {
                if (!ReadExactly(connection.Stream, lenBuf, 4)) break;
                int len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
                if (len <= 0 || len > 1024 * 1024) break;
                var payload = new byte[len];
                if (!ReadExactly(connection.Stream, payload, len)) break;
                if (ReferenceEquals(Volatile.Read(ref _connection), connection))
                    _incoming.Enqueue((connection.Generation, LocalPeerId == 1 ? 2UL : 1UL, payload));
            }
        }
        catch { }
        finally
        {
            Interlocked.CompareExchange(ref _connection, null, connection);
            connection.Dispose();
        }
    }

    private static bool ReadExactly(NetworkStream stream, byte[] buf, int count)
    {
        int off = 0;
        while (off < count)
        {
            int n = stream.Read(buf, off, count - off);
            if (n <= 0) return false;
            off += n;
        }
        return true;
    }

    public void Send(ulong peerId, byte[] data, bool reliable)
    {
        Send(peerId, data, data?.Length ?? 0, reliable);
    }

    public void Send(ulong peerId, byte[] data, int len, bool reliable)
    {
        var connection = Volatile.Read(ref _connection);
        if (data == null || len <= 0 || len > data.Length || len > 1024 * 1024 || connection == null) return;
        try
        {
            var lenBuf = new byte[4];
            lenBuf[0] = (byte)(len >> 24);
            lenBuf[1] = (byte)(len >> 16);
            lenBuf[2] = (byte)(len >> 8);
            lenBuf[3] = (byte)len;
            lock (connection.SendGate)
            {
                if (!ReferenceEquals(Volatile.Read(ref _connection), connection)) return;
                connection.Stream.Write(lenBuf, 0, 4);
                connection.Stream.Write(data, 0, len);
                connection.Stream.Flush();
            }
        }
        catch (Exception ex) { CoopRuntime.LogSource?.LogWarning($"[LocalTransport] Send: {ex.Message}"); }
    }

    public bool Poll(out ulong sender, out byte[] data)
    {
        while (_incoming.TryDequeue(out var item))
        {
            var connection = Volatile.Read(ref _connection);
            if (connection == null || item.Item1 != connection.Generation) continue;
            sender = item.Item2;
            data = item.Item3;
            return true;
        }
        sender = 0;
        data = Array.Empty<byte>();
        return false;
    }

    public void Dispose()
    {
        _running = false;
        Interlocked.Exchange(ref _connection, null)?.Dispose();
        while (_incoming.TryDequeue(out _)) { }
        try { _listener?.Stop(); } catch { }
    }
}
