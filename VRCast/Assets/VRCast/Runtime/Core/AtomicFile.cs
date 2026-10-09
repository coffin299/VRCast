using System.IO;

namespace VRCast.Core
{
    /// <summary>
    /// 書き込み途中で落ちても既存ファイルを壊さないよう、一時ファイルに書いてから置き換える。
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>
        /// テキストを UTF-8（BOM なし）で書く。保存先フォルダが無ければ作る。失敗時は IOException 等をそのまま投げる。
        /// </summary>
        public static void WriteAllText(string path, string text)
        {
            // 保存先ディレクトリが無ければ作成
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 一時ファイルへ書き切ってから置き換える
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, text);

            // 既存ファイルがあれば置換、無ければ移動
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
    }
}
