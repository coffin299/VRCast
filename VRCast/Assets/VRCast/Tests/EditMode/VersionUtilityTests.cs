using NUnit.Framework;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// 更新確認に使うバージョン番号の解析と比較を検証する。
    /// </summary>
    public class VersionUtilityTests
    {
        [TestCase("1.2.1", "1.2.0")]
        [TestCase("1.3.0", "1.2.9")]
        [TestCase("2.0.0", "1.9.9")]
        [TestCase("1.10.0", "1.9.0")]
        [TestCase("v1.2.1", "1.2.0")]
        public void IsNewer_HigherVersion_ReturnsTrue(string candidate, string current)
        {
            // 上の桁から数値として比べること（"1.10" > "1.9"、先頭の v は無視）
            Assert.That(VersionUtility.IsNewer(candidate, current), Is.True);
        }

        [TestCase("1.2.0", "1.2.0")]
        [TestCase("1.2", "1.2.0")]
        [TestCase("1.1.9", "1.2.0")]
        [TestCase("0.9.0", "1.0.0")]
        public void IsNewer_SameOrOlderVersion_ReturnsFalse(string candidate, string current)
        {
            // 同じ（欠けた桁は 0）・古いバージョンでは通知しないこと
            Assert.That(VersionUtility.IsNewer(candidate, current), Is.False);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("abc")]
        [TestCase("1.2.0-beta")]
        [TestCase("1.2.3.4")]
        [TestCase("-1.0.0")]
        public void IsNewer_InvalidCandidate_ReturnsFalse(string candidate)
        {
            // 読めない番号は通知しない側に倒すこと
            Assert.That(VersionUtility.IsNewer(candidate, "1.0.0"), Is.False);
        }

        [Test]
        public void TryParse_FillsMissingPartsWithZero()
        {
            // 欠けた桁は 0 で埋めること
            Assert.That(VersionUtility.TryParse("2", out int[] parts), Is.True);
            Assert.That(parts, Is.EqualTo(new[] { 2, 0, 0 }));
        }
    }
}
