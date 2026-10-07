using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using VRCast.Remote;

namespace VRCast.Tests
{
    /// <summary>
    /// 外部操作（OSC パケットの読み取り・HTTP リクエストの読み取り・操作への変換・表情の指定）を検証する。
    /// </summary>
    public class RemoteControlTests
    {
        // 表情の一覧（Pose タブの並び順）
        private static readonly string[] Names = { "Smile", "Angry", "にっこり" };

        [Test]
        public void Osc_ReadsStringIntAndFloat()
        {
            // 文字列・整数・小数の引数を読めること
            Assert.That(ParseSingle(Message("/vrcast/expression", ",s", Str("Smile"))).String, Is.EqualTo("Smile"));
            Assert.That(ParseSingle(Message("/vrcast/expression", ",i", Int(2))).Int, Is.EqualTo(2));
            Assert.That(ParseSingle(Message("/vrcast/neutral", ",f", Float(1f))).Float, Is.EqualTo(1f));
        }

        [Test]
        public void Osc_ReadsBundle()
        {
            // バンドルの中の複数のメッセージを順に読めること
            byte[] first = Message("/vrcast/auto", ",", new byte[0]);
            byte[] second = Message("/vrcast/expression", ",i", Int(1));
            var bundle = new List<byte>();
            bundle.AddRange(Str("#bundle"));
            bundle.AddRange(new byte[8]);
            bundle.AddRange(Int(first.Length));
            bundle.AddRange(first);
            bundle.AddRange(Int(second.Length));
            bundle.AddRange(second);

            var messages = new List<OscMessage>();
            OscPacket.Parse(bundle.ToArray(), 0, bundle.Count, messages);
            Assert.That(messages.Count, Is.EqualTo(2));
            Assert.That(messages[0].Address, Is.EqualTo("/vrcast/auto"));
            Assert.That(messages[1].Int, Is.EqualTo(1));
        }

        [Test]
        public void Osc_IgnoresBrokenPacket()
        {
            // 終端の無い文字列・"/" で始まらないアドレスは読まないこと
            var messages = new List<OscMessage>();
            byte[] broken = Encoding.ASCII.GetBytes("/vrcast");
            OscPacket.Parse(broken, 0, broken.Length, messages);
            byte[] noSlash = Message("vrcast", ",", new byte[0]);
            OscPacket.Parse(noSlash, 0, noSlash.Length, messages);
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void OscCommand_MapsAddresses()
        {
            // 名前・番号・アドレス末尾の名前・toggle・自動検出へ戻すを読めること
            Assert.That(OscCommand(Message("/vrcast/expression", ",s", Str("Smile"))).ResolvePreset(Names), Is.EqualTo(0));
            Assert.That(OscCommand(Message("/vrcast/expression", ",i", Int(0))).ResolvePreset(Names), Is.EqualTo(-1));
            RemoteCommand named = OscCommand(Message("/vrcast/toggle/Angry", ",f", Float(1f)));
            Assert.That(named.Toggle, Is.True);
            Assert.That(named.ResolvePreset(Names), Is.EqualTo(1));
            Assert.That(OscCommand(Message("/vrcast/auto", ",", new byte[0])).Action, Is.EqualTo(RemoteAction.Auto));
        }

        [Test]
        public void OscCommand_IgnoresButtonRelease()
        {
            // ボタンを離したときの 0.0 は無視すること
            var messages = new List<OscMessage>();
            byte[] release = Message("/vrcast/expression/Smile", ",f", Float(0f));
            OscPacket.Parse(release, 0, release.Length, messages);
            Assert.That(RemoteCommand.TryParseOsc(messages[0], out _), Is.False);
        }

        [Test]
        public void Http_ParsesRequestLineAndQuery()
        {
            // メソッド・パス・URL エンコードされた日本語の名前を読めること
            Assert.That(HttpRequest.TryParse("GET /expression?name=%E3%81%AB%E3%81%A3%E3%81%93%E3%82%8A&toggle=1 HTTP/1.1\r\nHost: 127.0.0.1",
                out HttpRequest request), Is.True);
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo("/expression"));
            Assert.That(request.QueryValue("name"), Is.EqualTo("にっこり"));
            Assert.That(request.FromWebPage, Is.False);

            Assert.That(RemoteCommand.TryParseHttp(request, out RemoteCommand command), Is.True);
            Assert.That(command.Toggle, Is.True);
            Assert.That(command.ResolvePreset(Names), Is.EqualTo(2));
        }

        [Test]
        public void Http_DetectsWebPageRequests()
        {
            // Origin や別サイトの Sec-Fetch-Site があればページからの送信、アドレス欄からの直接入力は許すこと
            HttpRequest.TryParse("GET /neutral HTTP/1.1\r\nOrigin: https://example.com", out HttpRequest withOrigin);
            HttpRequest.TryParse("GET /neutral HTTP/1.1\r\nSec-Fetch-Site: cross-site", out HttpRequest crossSite);
            HttpRequest.TryParse("GET /neutral HTTP/1.1\r\nSec-Fetch-Site: none", out HttpRequest typed);
            Assert.That(withOrigin.FromWebPage, Is.True);
            Assert.That(crossSite.FromWebPage, Is.True);
            Assert.That(typed.FromWebPage, Is.False);
        }

        [Test]
        public void HttpCommand_RejectsUnknownOrIncomplete()
        {
            // 未知のパス・名前も番号も無い /expression は操作にならないこと、壊れたリクエスト行は読まないこと
            HttpRequest.TryParse("GET /unknown HTTP/1.1", out HttpRequest unknown);
            HttpRequest.TryParse("GET /expression HTTP/1.1", out HttpRequest incomplete);
            Assert.That(RemoteCommand.TryParseHttp(unknown, out _), Is.False);
            Assert.That(RemoteCommand.TryParseHttp(incomplete, out _), Is.False);
            Assert.That(HttpRequest.TryParse("hello", out _), Is.False);
        }

        [Test]
        public void ResolvePreset_HandlesCaseAndRange()
        {
            // 大文字・小文字を区別しない一致、範囲外の番号・無い名前は見つからないこと
            Assert.That(RemoteCommand.SelectName("smile", false).ResolvePreset(Names), Is.EqualTo(0));
            Assert.That(RemoteCommand.SelectName("Sad", false).ResolvePreset(Names), Is.EqualTo(RemoteCommand.NotFound));
            Assert.That(RemoteCommand.SelectIndex(3, false).ResolvePreset(Names), Is.EqualTo(2));
            Assert.That(RemoteCommand.SelectIndex(4, false).ResolvePreset(Names), Is.EqualTo(RemoteCommand.NotFound));
        }

        [Test]
        public void CommandText_OscAddressSafety()
        {
            // ASCII の名前はアドレスに入れられ、日本語・空白・予約文字を含む名前は入れられないこと
            Assert.That(RemoteCommandText.IsOscAddressSafe("Smile_2"), Is.True);
            Assert.That(RemoteCommandText.IsOscAddressSafe("にっこり"), Is.False);
            Assert.That(RemoteCommandText.IsOscAddressSafe("Big Smile"), Is.False);
            Assert.That(RemoteCommandText.IsOscAddressSafe("a/b"), Is.False);
        }

        [Test]
        public void CommandText_HttpRoundTrips()
        {
            // 一覧に出す URL を読み直すと、同じ表情を toggle 付きで指すこと（日本語の名前も）
            foreach (string name in Names)
            {
                string url = RemoteCommandText.HttpExpression(39571, name, true);
                string target = url.Substring("http://127.0.0.1:39571".Length);
                Assert.That(HttpRequest.TryParse($"GET {target} HTTP/1.1", out HttpRequest request), Is.True);
                Assert.That(RemoteCommand.TryParseHttp(request, out RemoteCommand command), Is.True);
                Assert.That(command.Toggle, Is.True);
                Assert.That(command.ResolvePreset(Names), Is.EqualTo(Array.IndexOf(Names, name)));
            }

            Assert.That(RemoteCommandText.HttpNeutral(39571, true), Is.EqualTo("http://127.0.0.1:39571/neutral?toggle=1"));
        }

        [Test]
        public void CommandText_OscRoundTrips()
        {
            // アドレス末尾の形と、文字列の引数の形のどちらも、読み直すと同じ表情を指すこと
            RemoteCommandText.OscText ascii = RemoteCommandText.OscExpression("Angry", true);
            Assert.That(ascii.Address, Is.EqualTo("/vrcast/toggle/Angry"));
            Assert.That(ascii.Argument, Is.Null);
            Assert.That(OscCommand(Message(ascii.Address, ",", new byte[0])).ResolvePreset(Names), Is.EqualTo(1));

            RemoteCommandText.OscText japanese = RemoteCommandText.OscExpression("にっこり", false);
            Assert.That(japanese.Address, Is.EqualTo("/vrcast/expression"));
            Assert.That(OscCommand(Message(japanese.Address, ",s", Str("にっこり"))).ResolvePreset(Names), Is.EqualTo(2));
        }

        private static OscMessage ParseSingle(byte[] packet)
        {
            // 1 つだけメッセージが読めること
            var messages = new List<OscMessage>();
            OscPacket.Parse(packet, 0, packet.Length, messages);
            Assert.That(messages.Count, Is.EqualTo(1));
            return messages[0];
        }

        private static RemoteCommand OscCommand(byte[] packet)
        {
            // メッセージを操作として読めること
            Assert.That(RemoteCommand.TryParseOsc(ParseSingle(packet), out RemoteCommand command), Is.True);
            return command;
        }

        private static byte[] Message(string address, string tags, byte[] argument)
        {
            // アドレス・型タグ・引数を並べた OSC メッセージ
            var bytes = new List<byte>();
            bytes.AddRange(Str(address));
            bytes.AddRange(Str(tags));
            bytes.AddRange(argument);
            return bytes.ToArray();
        }

        private static byte[] Str(string text)
        {
            // 終端の 0 を含めて 4 バイト境界まで埋めた OSC 文字列
            byte[] raw = Encoding.UTF8.GetBytes(text);
            var padded = new byte[(raw.Length + 4) & ~3];
            Array.Copy(raw, padded, raw.Length);
            return padded;
        }

        private static byte[] Int(int value)
        {
            // ビッグエンディアンの 32bit 整数
            return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        private static byte[] Float(float value)
        {
            // ビッグエンディアンの 32bit 浮動小数
            return Int(BitConverter.SingleToInt32Bits(value));
        }
    }
}
