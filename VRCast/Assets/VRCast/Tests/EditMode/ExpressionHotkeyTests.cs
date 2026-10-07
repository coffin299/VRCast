using System.Collections.Generic;
using NUnit.Framework;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// 表情のショートカットキー（使えるキー・保存値との変換・アバターごとの記録）を検証する。
    /// </summary>
    public class ExpressionHotkeyTests
    {
        private const string AvatarPath = @"C:\Avatars\Test.vrcaster";

        // 検証に使う仮想キー（F1・F2・テンキーの 1・マウスの左ボタン・左右を区別しない Ctrl・記号キー）
        private const int F1 = 0x70;
        private const int F2 = 0x71;
        private const int Numpad1 = 0x61;
        private const int LeftButton = 0x01;
        private const int AnyControl = 0x11;
        private const int Oem1 = 0xBA;

        [Test]
        public void IsAssignable_RejectsReservedKeys()
        {
            // 未割り当て・Esc・Tab・マウスの左ボタン・左右を区別しない Ctrl は使えないこと
            Assert.That(VirtualKeys.IsAssignable(0), Is.False);
            Assert.That(VirtualKeys.IsAssignable(VirtualKeys.Escape), Is.False);
            Assert.That(VirtualKeys.IsAssignable(VirtualKeys.Tab), Is.False);
            Assert.That(VirtualKeys.IsAssignable(LeftButton), Is.False);
            Assert.That(VirtualKeys.IsAssignable(AnyControl), Is.False);
        }

        [Test]
        public void IsAssignable_AcceptsAnyOtherKey()
        {
            // ファンクションキー・テンキー・記号キー・左右の修飾キー単独は使えること
            Assert.That(VirtualKeys.IsAssignable(F1), Is.True);
            Assert.That(VirtualKeys.IsAssignable(Numpad1), Is.True);
            Assert.That(VirtualKeys.IsAssignable(Oem1), Is.True);
            Assert.That(VirtualKeys.IsAssignable(VirtualKeys.RightControl), Is.True);
            Assert.That(VirtualKeys.IsAssignable(VirtualKeys.LeftShift), Is.True);
        }

        [Test]
        public void From_RoundTripsCombo()
        {
            // 保存値へ変換して読み直しても同じ組み合わせになること
            var combo = new KeyCombo(F2, true, false, true);
            ExpressionHotkey saved = ExpressionHotkey.From("Smile", combo);
            Assert.That(saved.IsValid, Is.True);
            Assert.That(saved.Combo, Is.EqualTo(combo));
        }

        [Test]
        public void SetExpressionHotkeys_StoresPerAvatarAndDropsInvalid()
        {
            // 読込時と同じく視点を記録してから、割り当てを記録する
            var settings = new AppSettings();
            settings.SetAvatarCamera(AvatarPath, new CameraPose());
            settings.SetExpressionHotkeys(AvatarPath, new List<ExpressionHotkey>
            {
                new ExpressionHotkey { preset = "Smile", virtualKey = F1 },
                new ExpressionHotkey { preset = "Angry", virtualKey = VirtualKeys.Escape },
            });

            // 使えるキーの割り当てだけが残り、別のアバターには無いこと
            List<ExpressionHotkey> stored = settings.GetExpressionHotkeys(AvatarPath);
            Assert.That(stored.Count, Is.EqualTo(1));
            Assert.That(stored[0].preset, Is.EqualTo("Smile"));
            Assert.That(settings.GetExpressionHotkeys(@"C:\Avatars\Other.vrcaster"), Is.Empty);
        }
    }
}
