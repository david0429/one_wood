using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OneWood.LaunchMonitor.OpenConnect;

namespace OneWood.Tests
{
    /// <summary>Loopback tests: a TcpClient plays the role of FS Golf against the real server.</summary>
    public class OpenConnectServerTests
    {
        const int TimeoutMs = 5000;

        OpenConnectServer _server;
        BlockingCollection<ReceivedMessage> _received;

        [SetUp]
        public void SetUp()
        {
            _received = new BlockingCollection<ReceivedMessage>();
            _server = new OpenConnectServer(0, IPAddress.Loopback);
            _server.MessageReceived += _received.Add;
            _server.Start();
        }

        [TearDown]
        public void TearDown()
        {
            _server.Dispose();
            _received.Dispose();
        }

        TcpClient Connect()
        {
            var client = new TcpClient();
            client.Connect(IPAddress.Loopback, _server.Port);
            client.GetStream().ReadTimeout = TimeoutMs;
            return client;
        }

        static void Send(TcpClient client, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            client.GetStream().Write(bytes, 0, bytes.Length);
        }

        static JObject ReadResponse(TcpClient client)
        {
            var framer = new JsonMessageFramer();
            var messages = new List<string>();
            var buffer = new byte[1024];
            while (messages.Count == 0)
            {
                int read = client.GetStream().Read(buffer, 0, buffer.Length);
                Assert.That(read, Is.GreaterThan(0), "Server closed the connection");
                framer.Push(Encoding.UTF8.GetString(buffer, 0, read), messages);
            }
            return JObject.Parse(messages[0]);
        }

        ReceivedMessage Take()
        {
            Assert.That(_received.TryTake(out var message, TimeoutMs), Is.True, "No message received");
            return message;
        }

        [Test]
        public void Shot_IsReceivedParsedAndAcknowledged()
        {
            using (var client = Connect())
            {
                Send(client, TestShots.Driver + "\n");

                var message = Take();
                Assert.That(message.Parse.Success, Is.True);
                Assert.That(message.Parse.Message.BallData.Speed, Is.EqualTo(148.2));
                Assert.That(message.RawJson, Is.EqualTo(TestShots.Driver));
                Assert.That(message.ResponseCode, Is.EqualTo(200));
                Assert.That((int)ReadResponse(client)["Code"], Is.EqualTo(200));
                Assert.That(_server.IsClientConnected, Is.True);
            }
        }

        [Test]
        public void ShotSplitAcrossWrites_IsReassembled()
        {
            using (var client = Connect())
            {
                int half = TestShots.Driver.Length / 2;
                Send(client, TestShots.Driver.Substring(0, half));
                System.Threading.Thread.Sleep(50);
                Send(client, TestShots.Driver.Substring(half));

                Assert.That(Take().Parse.Message.ShotNumber, Is.EqualTo(1));
            }
        }

        [Test]
        public void BackToBackMessages_AreAllReceived()
        {
            using (var client = Connect())
            {
                Send(client, TestShots.Heartbeat + TestShots.Driver + TestShots.Putt);

                Assert.That(Take().Parse.Message.IsHeartbeat(), Is.True);
                Assert.That(Take().Parse.Message.ShotNumber, Is.EqualTo(1));
                Assert.That(Take().Parse.Message.ShotNumber, Is.EqualTo(2));
            }
        }

        [Test]
        public void Heartbeat_GetsNoResponseByDefault()
        {
            using (var client = Connect())
            {
                Send(client, TestShots.Heartbeat);
                Assert.That(Take().ResponseCode, Is.Null);
            }
        }

        [Test]
        public void Heartbeat_GetsResponseWhenEnabled()
        {
            _server.RespondToStatusMessages = true;
            using (var client = Connect())
            {
                Send(client, TestShots.Heartbeat);
                Assert.That(Take().ResponseCode, Is.EqualTo(200));
                Assert.That((int)ReadResponse(client)["Code"], Is.EqualTo(200));
            }
        }

        [Test]
        public void MalformedMessage_GetsFailureResponse()
        {
            using (var client = Connect())
            {
                Send(client, "{\"BallData\":{\"Speed\":\"fast\"}}");

                var message = Take();
                Assert.That(message.Parse.Success, Is.False);
                Assert.That(message.ResponseCode, Is.EqualTo(501));
                Assert.That((int)ReadResponse(client)["Code"], Is.EqualTo(501));
            }
        }

        [Test]
        public void PlayerInfo_IsSentToConnectedClient()
        {
            Assert.That(_server.SendPlayerInfo(new PlayerInfo(Handedness.Right, ClubCodes.Putter)), Is.False, "No client yet");

            using (var client = Connect())
            {
                // Ensure the server has registered the connection before sending.
                Send(client, TestShots.Heartbeat);
                Take();

                Assert.That(_server.SendPlayerInfo(new PlayerInfo(Handedness.Right, ClubCodes.Putter)), Is.True);
                var response = ReadResponse(client);
                Assert.That((int)response["Code"], Is.EqualTo(201));
                Assert.That((string)response["Player"]["Club"], Is.EqualTo("PT"));
            }
        }

        [Test]
        public void NewClient_ReplacesOldClient()
        {
            using (var first = Connect())
            using (var second = Connect())
            {
                Send(second, TestShots.Driver);
                var message = Take();
                Assert.That(message.RemoteEndpoint, Is.EqualTo(second.Client.LocalEndPoint.ToString()));

                var buffer = new byte[16];
                int read;
                try { read = first.GetStream().Read(buffer, 0, buffer.Length); }
                catch (System.IO.IOException) { read = 0; }
                Assert.That(read, Is.EqualTo(0), "First connection should have been closed by the server");
            }
        }

        [Test]
        public void Disconnect_IsDetected()
        {
            using (var client = Connect())
            {
                Send(client, TestShots.Heartbeat);
                Take();
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
            while (_server.IsClientConnected && DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(10);
            Assert.That(_server.IsClientConnected, Is.False);
        }
    }
}
