using System;
using System.Linq;
using NUnit.Framework;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// Runtime に含まれるアセンブリに Editor 専用依存が混入していないことを検証する。
    /// </summary>
    public class AssemblyIsolationTests
    {
        // Runtime から参照してはならないアセンブリ名の接頭辞
        private static readonly string[] ForbiddenPrefixes =
        {
            "UnityEditor",
            "VRCast.Editor",
            "VRCast.Converter.Editor",
            "VRC.",
            "VRCSDK",
        };

        // 検査対象: スタンドアロンに含まれるアセンブリの代表型
        private static readonly Type[] RuntimeAssemblyTypes =
        {
            typeof(AppSettings),
            typeof(AvatarManifest),
        };

        [TestCaseSource(nameof(RuntimeAssemblyTypes))]
        public void RuntimeAssembly_DoesNotReferenceEditorOrVrcSdk(Type representative)
        {
            // 対象アセンブリが実際に参照しているアセンブリ名を列挙
            var referenced = representative.Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToArray();

            // 禁止接頭辞に一致するものを抽出
            var violations = referenced
                .Where(name => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                .ToArray();

            // 1 件でもあれば違反として名前を表示
            Assert.That(violations, Is.Empty,
                $"{representative.Assembly.GetName().Name} references forbidden assemblies: " + string.Join(", ", violations));
        }
    }
}
