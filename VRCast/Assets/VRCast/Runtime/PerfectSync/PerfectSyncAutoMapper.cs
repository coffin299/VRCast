using System;
using System.Collections.Generic;
using VRCast.AvatarFormat;
using VRCast.Tracking;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// アバターに元からある BlendShape（Viseme・まばたき・笑顔・眉・視線の「上を見る」など）の名前から、ARKit 名へ割り当てる候補を推定する。
    /// 作者が作った形状は生成より品質が良いことが多いため、見つかった種類は既定で「既存」を使う。
    /// ARKit 名そのものの BlendShape は PerfectSyncBlendShapes が名前で見つけるので、ここでは扱わない。
    /// </summary>
    public static class PerfectSyncAutoMapper
    {
        /// <summary>
        /// 割り当ての候補になる BlendShape 1 つ（メッシュのパスと名前）。
        /// </summary>
        public readonly struct Candidate
        {
            public readonly string Path;
            public readonly string Name;

            public Candidate(string path, string name)
            {
                Path = path ?? string.Empty;
                Name = name ?? string.Empty;
            }
        }

        // ARKit 名 → 当てはまる BlendShape 名（正規化済み・優先順）と強さ。両目・両側の名前は左右の両方に入れる
        private static readonly (string Arkit, string[] Names, float Weight)[] Rules =
        {
            ("eyeBlinkLeft", new[] { "blinkl", "blinkleft", "eyeblinkl", "winkl", "winkleft", "まばたき左", "ウィンク左",
                "blink", "eyeclose", "eyesclosed", "まばたき", "目閉じ", "vrcblink" }, 1f),
            ("eyeBlinkRight", new[] { "blinkr", "blinkright", "eyeblinkr", "winkr", "winkright", "まばたき右", "ウィンク右",
                "blink", "eyeclose", "eyesclosed", "まばたき", "目閉じ", "vrcblink" }, 1f),
            // 両目まとめて動かす視線（アッシュ等）。本人の右向き = 左目の内寄せ + 右目の外寄せ
            ("eyeLookUpLeft", new[] { "上を見る", "lookup", "eyeslookup", "eyelookup" }, 1f),
            ("eyeLookUpRight", new[] { "上を見る", "lookup", "eyeslookup", "eyelookup" }, 1f),
            ("eyeLookDownLeft", new[] { "下を見る", "lookdown", "eyeslookdown", "eyelookdown" }, 1f),
            ("eyeLookDownRight", new[] { "下を見る", "lookdown", "eyeslookdown", "eyelookdown" }, 1f),
            ("eyeLookInLeft", new[] { "右を見る", "lookright", "eyeslookright", "eyelookright" }, 1f),
            ("eyeLookOutRight", new[] { "右を見る", "lookright", "eyeslookright", "eyelookright" }, 1f),
            ("eyeLookOutLeft", new[] { "左を見る", "lookleft", "eyeslookleft", "eyelookleft" }, 1f),
            ("eyeLookInRight", new[] { "左を見る", "lookleft", "eyeslookleft", "eyelookleft" }, 1f),
            ("jawOpen", new[] { "vrcvaa", "vaa", "あ", "mtha", "moutha", "aa" }, 1f),
            ("mouthFunnel", new[] { "vrcvoh", "voh", "お", "mtho", "moutho", "oh" }, 1f),
            ("mouthPucker", new[] { "vrcvou", "vou", "う", "mthu", "mouthu", "ou" }, 1f),
            ("mouthStretchLeft", new[] { "vrcvih", "vih", "い", "mthi", "mouthi", "ih" }, 0.7f),
            ("mouthStretchRight", new[] { "vrcvih", "vih", "い", "mthi", "mouthi", "ih" }, 0.7f),
            ("mouthSmileLeft", new[] { "mouthsmilel", "smilel", "smile", "mouthsmile", "にこ", "口角上げ", "笑い", "にっこり" }, 1f),
            ("mouthSmileRight", new[] { "mouthsmiler", "smiler", "smile", "mouthsmile", "にこ", "口角上げ", "笑い", "にっこり" },
                1f),
            ("mouthFrownLeft", new[] { "mouthfrownl", "frownl", "frown", "mouthfrown", "口角下げ", "への字" }, 1f),
            ("mouthFrownRight", new[] { "mouthfrownr", "frownr", "frown", "mouthfrown", "口角下げ", "への字" }, 1f),
            ("browDownLeft", new[] { "browdownl", "browangryl", "browdown", "browangry", "angry", "眉怒り", "怒り" }, 1f),
            ("browDownRight", new[] { "browdownr", "browangryr", "browdown", "browangry", "angry", "眉怒り", "怒り" }, 1f),
            ("browInnerUp", new[] { "browinnerup", "browsad", "browtrouble", "trouble", "困る", "眉困り", "困り" }, 1f),
            ("browOuterUpLeft", new[] { "browouterupl", "browupl", "browup", "眉上", "眉上げ" }, 1f),
            ("browOuterUpRight", new[] { "browouterupr", "browupr", "browup", "眉上", "眉上げ" }, 1f),
            ("eyeWideLeft", new[] { "eyewidel", "eyewide", "びっくり", "見開き", "目見開き" }, 1f),
            ("eyeWideRight", new[] { "eyewider", "eyewide", "びっくり", "見開き", "目見開き" }, 1f),
            // eyeSquint は下まぶたを上げる動きで、目を開けていても 0.3〜0.5 ほど出る。上まぶたを下げるジト目・なごみを
            // 当てると常に半目になるので、名前が eyeSquint のものだけを割り当てる
            ("eyeSquintLeft", new[] { "eyesquintl", "eyesquint" }, 1f),
            ("eyeSquintRight", new[] { "eyesquintr", "eyesquint" }, 1f),
            ("cheekPuff", new[] { "cheekpuff", "ぷくー", "ぷく", "puff" }, 1f),
            ("tongueOut", new[] { "tongueout", "舌出し", "あかんべ", "tongue" }, 1f),
        };

        /// <summary>
        /// 候補（顔メッシュのものを先に並べる）から割り当てを推定する。ARKit 名ごとに最初に当てはまった 1 つ。
        /// </summary>
        public static List<PerfectSyncBinding> Map(IReadOnlyList<Candidate> candidates)
        {
            // 正規化した名前 → 最初に見つかった候補
            var byName = new Dictionary<string, Candidate>();
            foreach (Candidate candidate in candidates)
            {
                // ARKit 名そのものは名前の照合で使われるので候補にしない
                if (PerfectSyncBlendShapes.Match(candidate.Name) >= 0)
                {
                    continue;
                }

                string key = Normalize(candidate.Name);
                if (key.Length > 0 && !byName.ContainsKey(key))
                {
                    byName[key] = candidate;
                }
            }

            var bindings = new List<PerfectSyncBinding>();
            foreach ((string arkit, string[] names, float weight) in Rules)
            {
                // 優先順に、名前が完全に一致するものを探す
                foreach (string name in names)
                {
                    if (!byName.TryGetValue(name, out Candidate found))
                    {
                        continue;
                    }

                    bindings.Add(new PerfectSyncBinding
                    {
                        name = arkit, path = found.Path, blendShape = found.Name, weight = weight,
                    });
                    break;
                }
            }

            return bindings;
        }

        /// <summary>
        /// 照合用の名前（FBX の接頭辞を外し、英数字・かな・漢字だけを小文字で残す）。
        /// </summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            // "blendShape1.smile" のような接頭辞を外す（"vrc.v_aa" は外すと "v_aa" になるが、規則に両方の形がある）
            int dot = name.LastIndexOf('.');
            string body = dot >= 0 && !name.StartsWith("vrc.", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(dot + 1)
                : name;

            // 記号・空白を除いて小文字にする
            var chars = new char[body.Length];
            int length = 0;
            foreach (char c in body)
            {
                if (char.IsLetterOrDigit(c))
                {
                    chars[length++] = char.ToLowerInvariant(c);
                }
            }

            return new string(chars, 0, length);
        }
    }
}
