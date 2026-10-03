using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// 表示中アバターのマテリアルの色に倍率を掛けて明るさを変える。
    /// lilToon 等はライトの明るさをテクスチャの色までに制限するため、ライトを強くしても明るくならない分をここで補う。
    /// マテリアルは AssetBundle のものを直接書き換える（アバターごとに読み込み、破棄時に bundle と一緒に解放される）。
    /// </summary>
    public sealed class AvatarMaterials
    {
        // 主色のプロパティ（lilToon / Poiyomi / Standard は _Color、UTS 等は _BaseColor）
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // 明るさに関わる lilToon のプロパティ（原因調査用にログへ出す）
        private static readonly string[] DiagnosticProperties =
        {
            "_LightMinLimit", "_LightMaxLimit", "_MonochromeLighting", "_AsUnlit", "_ShadowStrength",
        };

        private readonly List<Entry> _entries = new List<Entry>();

        /// <summary>
        /// アバターのマテリアルを登録し直す（元の色を記録）。null で登録解除。
        /// </summary>
        public void SetAvatar(GameObject instance)
        {
            _entries.Clear();

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
    }
}
