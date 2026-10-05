using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace OneWood.LaunchMonitor.OpenConnect
{
    public sealed class ReceivedMessage
    {
        public DateTime ReceivedUtc { get; }
        public string RemoteEndpoint { get; }
        public string RawJson { get; }
        public OpenConnectParseResult Parse { get; }

        /// <summary>Code sent back to the client, or null if no response was sent.</summary>
        public int? ResponseCode { get; }

        public ReceivedMessage(DateTime receivedUtc, string remoteEndpoint, string rawJson,
            OpenConnectParseResult parse, int? responseCode)
        {
            ReceivedUtc = receivedUtc;
            RemoteEndpoint = remoteEndpoint;
            RawJson = rawJson;
            Parse = parse;
            ResponseCode = responseCode;
        }
    }

    /// <summary>
    /// TCP server speaking GSPro Open Connect v1. FS Golf connects as the client and
    /// streams JSON messages; one client is served at a time and a new connection
    /// replaces the old one. Events are raised on background threads.
    /// </summary>
    public sealed class OpenConnectServer : IDisposable
    {
        public const int DefaultPort = 921;

        static readonly Encoding Utf8 = new UTF8Encoding(false);

        readonly IPAddress _address;
        readonly int _requestedPort;
        readonly object _clientLock = new object();
        readonly object _writeLock = new object();

        TcpListener _listener;
        Thread _acceptThread;
        TcpClient _client;
        volatile bool _running;

        /// <summary>Raised on a background thread for every framed message.</summary>
        public event Action<ReceivedMessage> MessageReceived;

        /// <summary>Raised on a background thread with connection and framing diagnostics.</summary>
        public event Action<string> Diagnostic;

        /// <summary>
        /// Whether heartbeats and status-only messages get a 200 reply. Shots always do.
        /// Off by default until captures show what FS Golf expects.
        /// </summary>
        public bool RespondToStatusMessages { get; set; }

        /// <summary>The bound port; differs from the requested port when 0 (ephemeral) was requested.</summary>
        public int Port { get; private set; }

        public bool IsRunning => _running;

        public bool IsClientConnected
        {
            get { lock (_clientLock) return _client != null; }
        }

        public string ClientEndpoint { get; private set; }

        public OpenConnectServer(int port = DefaultPort, IPAddress address = null)
        {
            _requestedPort = port;
            _address = address ?? IPAddress.Any;
        }

        public void Start()
        {
            if (_running) return;
            _listener = new TcpListener(_address, _requestedPort);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "OpenConnect accept" };
            _acceptThread.Start();
            Diagnostic?.Invoke($"Listening for Open Connect clients on {_address}:{Port}.");
        }

        /// <summary>Sends player info (code 201) to the connected client. Returns false if no client is connected.</summary>
        public bool SendPlayerInfo(PlayerInfo player)
        {
            TcpClient client;
            lock (_clientLock) client = _client;
            return client != null && Send(client, OpenConnectResponse.Player(player));
        }

        public void Dispose()
        {
            if (!_running) return;
            _running = false;
            try { _listener?.Stop(); }
            catch (SocketException) { }
            lock (_clientLock)
            {
                _client?.Close();
                _client = null;
            }
            _acceptThread?.Join(1000);
        }

        void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception e) when (e is SocketException || e is ObjectDisposedException || e is InvalidOperationException)
                {
                    if (!_running) return;
                    Diagnostic?.Invoke($"Accept failed: {e.Message}");
                    Thread.Sleep(100);
                    continue;
                }

                client.NoDelay = true;
                string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                TcpClient previous;
                lock (_clientLock)
                {
                    previous = _client;
                    _client = client;
                    ClientEndpoint = endpoint;
                }
                if (previous != null)
                {
                    Diagnostic?.Invoke("New client connected; closing the previous connection.");
                    previous.Close();
                }
                Diagnostic?.Invoke($"Client connected from {endpoint}.");

                var reader = new Thread(() => ReadLoop(client, endpoint)) { IsBackground = true, Name = "OpenConnect read" };
                reader.Start();
            }
        }

        void ReadLoop(TcpClient client, string endpoint)
        {
            var framer = new JsonMessageFramer();
            var decoder = Utf8.GetDecoder();
            var buffer = new byte[4096];
            var chars = new char[Utf8.GetMaxCharCount(buffer.Length)];
            var messages = new List<string>();
            var errors = new List<string>();

            try
            {
                var stream = client.GetStream();
                while (_running)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    int charCount = decoder.GetChars(buffer, 0, read, chars, 0);
                    framer.Push(new string(chars, 0, charCount), messages, errors);

                    foreach (var error in errors)
                        Diagnostic?.Invoke(error);
                    foreach (var json in messages)
                        Handle(client, endpoint, json);
                    messages.Clear();
                    errors.Clear();
                }
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                // Socket closed by us (shutdown or replacement) or by the client.
            }
            finally
            {
                bool wasCurrent;
                lock (_clientLock)
                {
                    wasCurrent = _client == client;
                    if (wasCurrent)
                    {
                        _client = null;
                        ClientEndpoint = null;
                    }
                }
                client.Close();
                if (wasCurrent && _running)
                    Diagnostic?.Invoke($"Client {endpoint} disconnected.");
            }
        }

        void Handle(TcpClient client, string endpoint, string json)
        {
            var receivedUtc = DateTime.UtcNow;
            var parse = OpenConnectParser.Parse(json);

            string response = null;
            if (!parse.Success)
                response = OpenConnectResponse.Failure(parse.Error);
            else if (parse.Message.ClaimsBallData())
                response = OpenConnectResponse.ShotReceived();
            else if (RespondToStatusMessages)
                response = OpenConnectResponse.ShotReceived("Status received");

            int? responseCode = null;
            if (response != null && Send(client, response))
                responseCode = parse.Success ? OpenConnectResponse.ShotReceivedCode : OpenConnectResponse.FailureCode;

            var received = new ReceivedMessage(receivedUtc, endpoint, json, parse, responseCode);
            try
            {
                MessageReceived?.Invoke(received);
            }
            catch (Exception e)
            {
                Diagnostic?.Invoke($"MessageReceived handler threw: {e}");
            }
        }

        bool Send(TcpClient client, string json)
        {
            var bytes = Utf8.GetBytes(json);
            try
            {
                lock (_writeLock)
                    client.GetStream().Write(bytes, 0, bytes.Length);
                return true;
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                Diagnostic?.Invoke($"Send failed: {e.Message}");
                return false;
            }
        }
    }
}
