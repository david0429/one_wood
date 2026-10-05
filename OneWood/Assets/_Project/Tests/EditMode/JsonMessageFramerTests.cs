using System.Collections.Generic;
using NUnit.Framework;
using OneWood.LaunchMonitor.OpenConnect;

namespace OneWood.Tests
{
    public class JsonMessageFramerTests
    {
        JsonMessageFramer _framer;
        List<string> _messages;
        List<string> _errors;

        [SetUp]
        public void SetUp()
        {
            _framer = new JsonMessageFramer();
            _messages = new List<string>();
            _errors = new List<string>();
        }

        void Push(string chunk) => _framer.Push(chunk, _messages, _errors);

        [Test]
        public void SingleObject_IsEmitted()
        {
            Push("{\"a\":1}");
            Assert.That(_messages, Is.EqualTo(new[] { "{\"a\":1}" }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void NewlineDelimitedObjects_AreSplit()
        {
            Push("{\"a\":1}\n{\"b\":2}\r\n");
            Assert.That(_messages, Is.EqualTo(new[] { "{\"a\":1}", "{\"b\":2}" }));
        }

        [Test]
        public void BackToBackObjects_AreSplit()
        {
            Push("{\"a\":1}{\"b\":{\"c\":[1,2]}}");
            Assert.That(_messages, Is.EqualTo(new[] { "{\"a\":1}", "{\"b\":{\"c\":[1,2]}}" }));
        }

        [Test]
        public void ObjectSplitAcrossChunks_IsReassembled()
        {
            Push("{\"BallData\":{\"Sp");
            Assert.That(_messages, Is.Empty);
            Assert.That(_framer.HasPartialMessage, Is.True);
            Push("eed\":148.2}}");
            Assert.That(_messages, Is.EqualTo(new[] { "{\"BallData\":{\"Speed\":148.2}}" }));
            Assert.That(_framer.HasPartialMessage, Is.False);
        }

        [Test]
        public void ByteByByteDelivery_Works()
        {
            foreach (char c in TestShots.Driver)
                Push(c.ToString());
            Assert.That(_messages, Is.EqualTo(new[] { TestShots.Driver }));
        }

        [Test]
        public void BracesInsideStrings_AreIgnored()
        {
            Push("{\"DeviceID\":\"weird } { name\"}");
            Assert.That(_messages, Is.EqualTo(new[] { "{\"DeviceID\":\"weird } { name\"}" }));
        }

        [Test]
        public void EscapedQuotesInsideStrings_AreHandled()
        {
            Push("{\"DeviceID\":\"say \\\"}\\\" \\\\\"}{\"x\":1}");
            Assert.That(_messages.Count, Is.EqualTo(2));
            Assert.That(_messages[1], Is.EqualTo("{\"x\":1}"));
        }

        [Test]
        public void JunkBetweenObjects_IsReportedAndSkipped()
        {
            Push("garbage{\"a\":1}");
            Assert.That(_messages, Is.EqualTo(new[] { "{\"a\":1}" }));
            Assert.That(_errors.Count, Is.EqualTo(1));
            StringAssert.Contains("garbage", _errors[0]);
        }

        [Test]
        public void OversizeMessage_IsDiscardedAndFramerRecovers()
        {
            var framer = new JsonMessageFramer(maxMessageLength: 16);
            framer.Push("{\"a\":\"0123456789abcdef\"}", _messages, _errors);
            Assert.That(_messages, Is.Empty);
            Assert.That(_errors, Is.Not.Empty);

            framer.Push("{\"b\":2}", _messages, _errors);
            Assert.That(_messages, Is.EqualTo(new[] { "{\"b\":2}" }));
        }
    }
}
