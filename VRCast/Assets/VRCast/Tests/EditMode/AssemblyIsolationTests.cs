using System.Linq;
using NUnit.Framework;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// Runtime アセンブリに Editor 専用依存が混入していないことを検証する。
    /// </summary>
    public class AssemblyIsolationTests
    {
        // Runtime から参照してはならないアセンブリ名の接頭辞
        private static readonly string[] ForbiddenPrefixes =
        {
            "UnityEditor",
            "VRCast.Editor",
            "VRC.",
            "VRCSDK",
        };

        [Test]
        public void RuntimeAssembly_DoesNotReferenceEditorOrVrcSdk()
        {
            // Runtime アセンブリが実際に参照しているアセンブリ名を列挙
            var referenced = typeof(AppSettings).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToArray();

            // 禁止接頭辞に一致するものを抽出
            var violations = referenced
                .Where(name => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix)))
                .ToArray();

            // 1 件でもあれば違反として名前を表示
            Assert.That(violations, Is.Empty, "VRCast.Runtime references forbidden assemblies: " + string.Join(", ", violations));
        }
    }
}
