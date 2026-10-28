using NUnit.Framework;
using VRCast.Dynamics;

namespace VRCast.Tests.EditMode
{
    public class PhysBoneChainTests
    {
        [Test]
        public void GravityFalloffFactor_AtRest_RemovesGravityWhenFalloffIsOne()
        {
            Assert.That(PhysBoneChain.GravityFalloffFactor(1f, 0f), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(PhysBoneChain.GravityFalloffFactor(0.5f, 0f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(PhysBoneChain.GravityFalloffFactor(0f, 0f), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void GravityFalloffFactor_AtNinetyDegrees_AppliesFullGravity()
        {
            Assert.That(PhysBoneChain.GravityFalloffFactor(1f, 90f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(PhysBoneChain.GravityFalloffFactor(1f, 180f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(PhysBoneChain.GravityFalloffFactor(0.2f, 90f), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void GravityFalloffFactor_Halfway_BlendsTowardFull()
        {
            Assert.That(PhysBoneChain.GravityFalloffFactor(1f, 45f), Is.EqualTo(0.5f).Within(1e-5f));
        }
    }
}
