using System.Collections.Generic;
using NUnit.Framework;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// キーの組み合わせ（表示・比較・押下の判定）と、割り当て待ち（押したキーから組み合わせを決める）を検証する。
    /// </summary>
    public class KeyComboTests
    {
        // 検証に使う仮想キー（N・数字の 1・テンキーの 1・テンキーの + ・F12・Tab 以外の記号キー）
        private const int N = 0x4E;
        private const int Digit1 = 0x31;
        private const int Numpad1 = 0x61;
        private const int NumpadPlus = 0x6B;
        private const int F12 = 0x7B;
        private const int Oem1 = 0xBA;

        // 押されている仮想キー（テストごとに作り直す）
        private HashSet<int> _down;

        [SetUp]
        public void SetUp()
        {
            _down = new HashSet<int>();
        }

        private bool IsDown(int virtualKey) => _down.Contains(virtualKey);

        [Test]
        public void Describe_ShowsModifiersAndKeypad()
        {
            // 修飾キーは Ctrl → Alt → Shift の順、数字キーとテンキーは区別して表示すること
            Assert.That(new KeyCombo(N, true, true, false).Describe(), Is.EqualTo("Ctrl+Alt+N"));
            Assert.That(new KeyCombo(Digit1, false, false, true).Describe(), Is.EqualTo("Shift+1"));
            Assert.That(new KeyCombo(Numpad1, false, false, false).Describe(), Is.EqualTo("Num 1"));
            Assert.That(new KeyCombo(NumpadPlus, true, false, false).Describe(), Is.EqualTo("Ctrl+Num +"));
            Assert.That(new KeyCombo(F12, false, false, false).Describe(), Is.EqualTo("F12"));
        }

        [Test]
        public void Describe_UsesResolverOrNumber()
        {
            // 配列に依るキーは渡した関数で名前を引き、引けなければ番号で示すこと
            Assert.That(new KeyCombo(Oem1, false, false, false).Describe(_ => ";"), Is.EqualTo(";"));
            Assert.That(new KeyCombo(Oem1, false, false, false).Describe(), Is.EqualTo("Key 0xBA"));
        }

        [Test]
        public void Constructor_DropsOwnModifier()
        {
            // キー自体が修飾キーなら、その種類の条件は外れること（Right Ctrl に Ctrl+ は付かない）
            var rightCtrl = new KeyCombo(VirtualKeys.RightControl, true, false, true);
            Assert.That(rightCtrl.Ctrl, Is.False);
            Assert.That(rightCtrl.Shift, Is.True);
            Assert.That(rightCtrl.Describe(), Is.EqualTo("Shift+Right Ctrl"));
        }

        [Test]
        public void Equals_DistinguishesModifiers()
        {
            // 同じキーでも修飾キーが違えば別、テンキーと数字キーも別の組み合わせであること
            var ctrlN = new KeyCombo(N, true, false, false);
            Assert.That(ctrlN.Equals(new KeyCombo(N, true, false, false)), Is.True);
            Assert.That(ctrlN.Equals(new KeyCombo(N, false, false, false)), Is.False);
            Assert.That(new KeyCombo(Digit1, false, false, false).Equals(new KeyCombo(Numpad1, false, false, false)), Is.False);
        }

        [Test]
        public void IsDown_RequiresExactModifiers()
        {
            // Ctrl+N は Ctrl と N だけを押しているときに押下、Shift も押すと押下ではないこと
            var ctrlN = new KeyCombo(N, true, false, false);
            _down.Add(N);
            Assert.That(ctrlN.IsDown(IsDown), Is.False);
            _down.Add(VirtualKeys.RightControl);
            Assert.That(ctrlN.IsDown(IsDown), Is.True);
            _down.Add(VirtualKeys.LeftShift);
            Assert.That(ctrlN.IsDown(IsDown), Is.False);
        }

        [Test]
        public void IsDown_ModifierAloneIgnoresOwnKind()
        {
            // Right Ctrl 単独は、押していれば押下（Ctrl の条件は問わない）、左右は区別すること
            var rightCtrl = new KeyCombo(VirtualKeys.RightControl, false, false, false);
            _down.Add(VirtualKeys.LeftControl);
            Assert.That(rightCtrl.IsDown(IsDown), Is.False);
            _down.Add(VirtualKeys.RightControl);
            Assert.That(rightCtrl.IsDown(IsDown), Is.True);
        }

        [Test]
        public void Capture_IgnoresKeysHeldAtStart()
        {
            // 開始時から押しているキーでは決まらず、押し直すと決まること
            var capture = new KeyCapture();
            _down.Add(N);
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Waiting));
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Waiting));
            _down.Remove(N);
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Waiting));
            _down.Add(N);
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Captured));
            Assert.That(capture.Result, Is.EqualTo(new KeyCombo(N, false, false, false)));
        }

        [Test]
        public void Capture_CombinesHeldModifiers()
        {
            // Ctrl と Alt を押してからテンキーの 1 を押すと Ctrl+Alt+Num 1 に決まること
            var capture = new KeyCapture();
            capture.Poll(IsDown);
            _down.Add(VirtualKeys.LeftControl);
            _down.Add(VirtualKeys.LeftAlt);
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Waiting));
            _down.Add(Numpad1);
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Captured));
            Assert.That(capture.Result, Is.EqualTo(new KeyCombo(Numpad1, true, true, false)));
        }

        [Test]
        public void Capture_ModifierAloneOnRelease()
        {
            // Right Ctrl を押して離すと、Right Ctrl 単独に決まること
            var capture = new KeyCapture();
            capture.Poll(IsDown);
            _down.Add(VirtualKeys.RightControl);
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Waiting));
            _down.Remove(VirtualKeys.RightControl);
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Captured));
            Assert.That(capture.Result, Is.EqualTo(new KeyCombo(VirtualKeys.RightControl, false, false, false)));
        }

        [Test]
        public void Capture_TabCancelsPendingModifier()
        {
            // Alt を押したまま Tab を押してから Alt を離しても、Alt 単独にはならないこと
            var capture = new KeyCapture();
            capture.Poll(IsDown);
            _down.Add(VirtualKeys.LeftAlt);
            capture.Poll(IsDown);
            _down.Add(VirtualKeys.Tab);
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Waiting));
            _down.Clear();
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Waiting));
        }

        [Test]
        public void Capture_EscapeCancels()
        {
            // Esc を押すとやめること
            var capture = new KeyCapture();
            capture.Poll(IsDown);
            _down.Add(VirtualKeys.Escape);
            Assert.That(capture.Poll(IsDown), Is.EqualTo(KeyCapture.Status.Cancelled));
        }
    }
}
