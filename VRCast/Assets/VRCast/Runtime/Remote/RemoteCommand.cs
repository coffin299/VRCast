using System;
using System.Collections.Generic;
using VRCast.Animations;

namespace VRCast.Remote
{
    /// <summary>
    /// 外部操作の種類（表情を選ぶ / 自動検出へ戻す / 状態を返すだけ）。
    /// </summary>
    public enum RemoteAction
    {
        Select,
        Auto,
        Status,
    }

    /// <summary>
    /// OSC・HTTP から読んだ 1 つの操作。表情は名前か番号（0 = ニュートラル、1 以降 = Pose タブの並び順）で指す。
    /// </summary>
    public readonly struct RemoteCommand
    {
        // 対象の表情を見つけられなかったことを表す値
        public const int NotFound = -2;

        public readonly RemoteAction Action;
        public readonly string Name;
        public readonly int Index;
        public readonly bool Toggle;

        private RemoteCommand(RemoteAction action, string name, int index, bool toggle)
        {
            Action = action;
            Name = name;
            Index = index;
            Toggle = toggle;
        }

        public static RemoteCommand Auto => new RemoteCommand(RemoteAction.Auto, null, -1, false);
        public static RemoteCommand Status => new RemoteCommand(RemoteAction.Status, null, -1, false);

        /// <summary>
        /// 名前で表情を選ぶ操作。
        /// </summary>
        public static RemoteCommand SelectName(string name, bool toggle)
        {
            return new RemoteCommand(RemoteAction.Select, name, -1, toggle);
        }

        /// <summary>
        /// 番号（0 = ニュートラル）で表情を選ぶ操作。
        /// </summary>
        public static RemoteCommand SelectIndex(int index, bool toggle)
        {
            return new RemoteCommand(RemoteAction.Select, null, index, toggle);
        }

        /// <summary>
        /// 対象の表情プリセットの位置を返す（-1 = ニュートラル、NotFound = 見つからない）。
        /// 名前は完全一致を優先し、無ければ大文字・小文字を区別せずに探す。
        /// </summary>
        public int ResolvePreset(IReadOnlyList<string> names)
        {
            // 番号指定（0 = ニュートラル、1 以降がプリセット）
            if (Name == null)
            {
                return Index == 0 ? -1 : Index >= 1 && Index <= names.Count ? Index - 1 : NotFound;
            }

            // 完全一致、次に大文字・小文字を区別しない一致
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == Name)
                {
                    return i;
                }
            }

            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], Name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return NotFound;
        }

        /// <summary>
        /// 表情に対して操作を行う。対象の表情が無ければ false と理由を返す（状態を返すだけの操作は常に true）。
        /// </summary>
        public bool Execute(ExpressionController expressions, out string error)
        {
            error = null;

            // 状態を返すだけなら何もしない
            if (Action == RemoteAction.Status)
            {
                return true;
            }

            // アバター・表情データが無ければ操作できない
            if (expressions == null || expressions.Names.Count == 0)
            {
                error = "no avatar with expressions is loaded";
                return false;
            }

            // 自動検出へ戻す
            if (Action == RemoteAction.Auto)
            {
                expressions.ReleaseManual();
                return true;
            }

            // 表情を探して固定する（toggle なら固定中の同じ表情で自動検出へ戻す）
            int preset = ResolvePreset(expressions.Names);
            if (preset == NotFound)
            {
                error = Name != null ? $"expression not found: {Name}" : $"expression index out of range: {Index}";
                return false;
            }

            expressions.Select(preset, Toggle);
            return true;
        }

        /// <summary>
        /// HTTP の要求を操作として読む（未知のパス・引数不足なら false）。
        /// /expression?name=名前 または ?index=番号（toggle=1 で切り替え）、/neutral、/auto、/status。
        /// </summary>
        public static bool TryParseHttp(HttpRequest request, out RemoteCommand command)
        {
            command = default;
            bool toggle = IsTrue(request.QueryValue("toggle"));

            // 末尾の "/" の有無を問わない
            switch (request.Path.TrimEnd('/'))
            {
                case "/expression":
                    // 名前を優先し、無ければ番号
                    string name = request.QueryValue("name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        command = SelectName(name, toggle);
                        return true;
                    }

                    if (int.TryParse(request.QueryValue("index"), out int index))
                    {
                        command = SelectIndex(index, toggle);
                        return true;
                    }

                    return false;
                case "/neutral":
                    command = SelectIndex(0, toggle);
                    return true;
                case "/auto":
                    command = Auto;
                    return true;
                case "":
                case "/status":
                    command = Status;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// OSC メッセージを操作として読む（未知のアドレス・ボタンを離した値なら false）。
        /// /vrcast/expression（文字列 = 名前、数値 = 番号）、/vrcast/expression/名前、/vrcast/neutral、/vrcast/auto。
        /// expression の代わりに toggle を使うと、固定中の同じ表情で自動検出へ戻す。
        /// </summary>
        public static bool TryParseOsc(OscMessage message, out RemoteCommand command)
        {
            command = default;

            // ボタンを離したときの 0.0 / false は無視する（押下の 1 で 1 回だけ動かす。整数の 0 は番号のニュートラルとして読む）
            if (message.Address == null || (message.IsRelease && message.Kind != OscValueKind.Int))
            {
                return false;
            }

            switch (message.Address)
            {
                case "/vrcast/neutral":
                    // 引数なし・押下の値のときだけ
                    return !message.IsRelease && Assign(SelectIndex(0, false), out command);
                case "/vrcast/auto":
                    return !message.IsRelease && Assign(Auto, out command);
                case "/vrcast/expression":
                case "/vrcast/toggle":
                    // 引数で対象を指す（文字列 = 名前、整数・整数値の小数 = 番号）
                    return TryTarget(message, message.Address == "/vrcast/toggle", out command);
            }

            // アドレスの末尾で名前を指す形（引数は押下の値か無し）
            return TryNamedAddress(message, "/vrcast/expression/", false, out command)
                || TryNamedAddress(message, "/vrcast/toggle/", true, out command);
        }

        private static bool TryTarget(OscMessage message, bool toggle, out RemoteCommand command)
        {
            command = default;
            switch (message.Kind)
            {
                case OscValueKind.String when !string.IsNullOrEmpty(message.String):
                    command = SelectName(message.String, toggle);
                    return true;
                case OscValueKind.Int:
                    command = SelectIndex(message.Int, toggle);
                    return true;
                case OscValueKind.Float when message.Float == (float)Math.Round(message.Float):
                    command = SelectIndex((int)message.Float, toggle);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryNamedAddress(OscMessage message, string prefix, bool toggle, out RemoteCommand command)
        {
            command = default;

            // 接頭辞の後ろに名前があり、ボタンを離した値でないこと
            if (!message.Address.StartsWith(prefix, StringComparison.Ordinal) || message.Address.Length == prefix.Length
                || message.IsRelease)
            {
                return false;
            }

            command = SelectName(message.Address.Substring(prefix.Length), toggle);
            return true;
        }

        private static bool Assign(RemoteCommand value, out RemoteCommand command)
        {
            command = value;
            return true;
        }

        private static bool IsTrue(string value)
        {
            // "1" / "true" / "yes" / "on"（大文字・小文字を問わない）
            return value != null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase));
        }
    }
}
