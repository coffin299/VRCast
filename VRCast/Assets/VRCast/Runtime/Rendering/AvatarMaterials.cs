using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// 表示中アバターのマテリアルの色に倍率を掛けて明るさを変え、輪郭線（アウトライン）の太さにも倍率を掛ける。
    /// lilToon 等はライトの明るさをテクスチャの色までに制限するため、ライトを強くしても明るくならない分をここで補う。
    /// マテリアルは AssetBundle のものを直接書き換える（アバターごとに読み込み、破棄時に bundle と一緒に解放される）。
    /// </summary>
    public sealed class AvatarMaterials
    {
        // 主色のプロパティ（lilToon / Poiyomi / Standard は _Color、UTS 等は _BaseColor）
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // 輪郭線の太さのプロパティ（lilToon / MToon は _OutlineWidth、Poiyomi は _LineWidth、UTS は _Outline_Width）
        private static readonly int[] OutlineWidthIds =
        {
            Shader.PropertyToID("_OutlineWidth"),
            Shader.PropertyToID("_LineWidth"),
            Shader.PropertyToID("_Outline_Width"),
        };

        // 明るさに関わる lilToon のプロパティ（原因調査用にログへ出す）
        private static readonly string[] DiagnosticProperties =
        {
            "_LightMinLimit", "_LightMaxLimit", "_MonochromeLighting", "_AsUnlit", "_ShadowStrength",
        };

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<OutlineEntry> _outlines = new List<OutlineEntry>();

        /// <summary>
        /// 表示中のアバターに太さを変えられる輪郭線があるか（元の太さが 0 より大きいマテリアルがあるか）。
        /// </summary>
        public bool HasOutline => _outlines.Count > 0;

        /// <summary>
        /// アバターのマテリアルを登録し直す（元の色・輪郭線の太さを記録）。null で登録解除。
        /// </summary>
        public void SetAvatar(GameObject instance)
        {
            _entries.Clear();
            _outlines.Clear();

            // アバター無し
            if (instance == null)
            {
                return;
            }

            // 共有されているマテリアルは 1 度だけ登録する
            var seen = new HashSet<Material>();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    // 空スロット・登録済みは飛ばす
                    if (material == null || !seen.Add(material))
                    {
                        continue;
                    }

                    // 輪郭線の元の太さを記録（主色の有無とは別に調べる）
                    RegisterOutline(material);

                    // 主色を持たないシェーダーは対象外
                    int id = material.HasProperty(ColorId) ? ColorId : material.HasProperty(BaseColorId) ? BaseColorId : 0;
                    if (id == 0)
                    {
                        continue;
                    }

                    _entries.Add(new Entry(material, id, material.GetColor(id)));
                }
            }

            LogSummary(seen);
            VRCastLog.Info("Materials", $"Outline materials: {_outlines.Count}");
        }

        /// <summary>
        /// 元の輪郭線の太さに倍率を掛けて反映する（1 で元に戻る、0 で輪郭線が消える）。
        /// </summary>
        public void ApplyOutline(float widthScale)
        {
            foreach (OutlineEntry entry in _outlines)
            {
                // アバター破棄後に呼ばれても安全にする
                if (entry.Material == null)
                {
                    continue;
                }

                entry.Material.SetFloat(entry.PropertyId, entry.Original * widthScale);
            }
        }

        private void RegisterOutline(Material material)
        {
            foreach (int id in OutlineWidthIds)
            {
                // 太さ 0（輪郭線を使っていない）は倍率を掛けても変わらないので登録しない
                if (material.HasProperty(id) && material.GetFloat(id) > 0f)
                {
                    _outlines.Add(new OutlineEntry(material, id, material.GetFloat(id)));
                }
            }
        }

        /// <summary>
        /// 元の色に倍率を掛けて反映する（1 で元に戻る）。
        /// </summary>
        public void Apply(float brightness)
        {
            foreach (Entry entry in _entries)
            {
                // アバター破棄後に呼ばれても安全にする
                if (entry.Material == null)
                {
                    continue;
                }

                // 倍率が見た目どおり効くよう Linear で掛ける（SetColor / GetColor は sRGB の値を扱う）
                Color linear = entry.Original.linear;
                linear.r *= brightness;
                linear.g *= brightness;
                linear.b *= brightness;
                Color color = linear.gamma;
                // 透明度は元のまま
                color.a = entry.Original.a;
                entry.Material.SetColor(entry.PropertyId, color);
            }
        }

        private static void LogSummary(HashSet<Material> materials)
        {
            // シェーダーごとのマテリアル数と、最初のマテリアルの明るさ関連の値を出す
            foreach (IGrouping<string, Material> group in materials.GroupBy(m => m.shader != null ? m.shader.name : "(none)"))
            {
                var line = new StringBuilder();
                line.Append(group.Key).Append(" x").Append(group.Count());
                Material first = group.First();
                foreach (string property in DiagnosticProperties)
                {
                    // 持っているプロパティだけ出す
                    if (first.HasProperty(property))
                    {
                        line.Append(' ').Append(property).Append('=')
                            .Append(first.GetFloat(property).ToString("0.###", CultureInfo.InvariantCulture));
                    }
                }

                VRCastLog.Info("Materials", line.ToString());
            }
        }

        private readonly struct Entry
        {
            public readonly Material Material;
            public readonly int PropertyId;
            public readonly Color Original;

            public Entry(Material material, int propertyId, Color original)
            {
                Material = material;
                PropertyId = propertyId;
                Original = original;
            }
        }

        private readonly struct OutlineEntry
        {
            public readonly Material Material;
            public readonly int PropertyId;
            public readonly float Original;

            public OutlineEntry(Material material, int propertyId, float original)
            {
                Material = material;
                PropertyId = propertyId;
                Original = original;
            }
        }
    }
}
