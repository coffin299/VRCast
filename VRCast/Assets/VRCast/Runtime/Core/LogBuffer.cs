using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// ログの重要度（デバッグログタブの絞り込み単位）。Debug は「詳細ログ」が ON のときだけ記録する。
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    /// <summary>
    /// アプリ内のデバッグログタブ用に、起動直後からのログを上限付きで保持する。
    /// Unity のログ（VRCastLog・Unity 自身のエラー・例外）を全スレッドから受け取り、
    /// Player.log に書かない情報（詳細ログ・トラッカーの出力）も Add で追加できる。
    /// 直前と同じ内容が続いたときは 1 件にまとめて回数を数える（同じ警告の連発でログが流れないように）。
    /// </summary>
    public static class LogBuffer
    {
        // 保持する最大件数（超えたら古いものから捨てる）
        public const int Capacity = 2000;

        // 重要度の数（LogLevel の要素数）
        public const int LevelCount = 4;

        // VRCastLog の接頭辞（"[VRCast][Category] message"）
        private const string VRCastPrefix = "[VRCast][";

        // VRCastLog を経由しない Unity 自身のログのカテゴリ名
        private const string UnityCategory = "Unity";

        /// <summary>
        /// 1 件のログ。Count は同じ内容が続いた回数（Time は最後の時刻）。
        /// </summary>
        public readonly struct Entry
        {
            public readonly DateTime Time;
            public readonly LogLevel Level;
            public readonly string Category;
            public readonly string Message;
            public readonly string StackTrace;
            public readonly int Count;

            public Entry(DateTime time, LogLevel level, string category, string message, string stackTrace, int count)
            {
                Time = time;
                Level = level;
                Category = category;
                Message = message;
                StackTrace = stackTrace;
                Count = count;
            }

            /// <summary>
            /// 重要度・カテゴリ・本文が同じなら true（まとめる対象）。
            /// </summary>
            public bool SameAs(LogLevel level, string category, string message)
            {
                return Level == level && Category == category && Message == message;
            }
        }

        // ログは別スレッド（トラッカーの出力・Unity のワーカー）からも届くため排他する
        private static readonly object Lock = new object();

        // 古い順の環状バッファ（_start が最古、_count 件）
        private static readonly Entry[] Entries = new Entry[Capacity];
        private static int _start;
        private static int _count;

        // 重要度ごとの保持件数
        private static readonly int[] LevelCounts = new int[LevelCount];

        /// <summary>
        /// 追加・消去のたびに増える番号（UI が変化を検出して表示を作り直すため）。
        /// </summary>
        public static int Version { get; private set; }

        /// <summary>
        /// 詳細ログ（Debug）を記録するか。OFF の間は記録処理も文字列の組み立ても省く（負荷対策）。
        /// </summary>
        public static bool DetailEnabled { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            // 起動時の設定読み込みのログより前に受け取り始める。Editor の Domain Reload 無効時の二重登録を防ぐため一度外す
            Application.logMessageReceivedThreaded -= Receive;
            Application.logMessageReceivedThreaded += Receive;

            // 配布版では通常ログ・警告のスタックトレースを取らない（ログ 1 行ごとの取得が重いため。エラー・例外は残す）
            if (!Application.isEditor)
            {
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            }
        }

        /// <summary>
        /// ログを追加する（Player.log には書かない。どのスレッドからでも呼べる）。
        /// </summary>
        public static void Add(LogLevel level, string category, string message, string stackTrace = null)
        {
            // 詳細ログが OFF なら Debug は捨てる
            if (level == LogLevel.Debug && !DetailEnabled)
            {
                return;
            }

            message ??= string.Empty;
            DateTime now = DateTime.Now;
            lock (Lock)
            {
                // 直前と同じ内容なら回数を増やして時刻を更新するだけにする
                if (_count > 0)
                {
                    int last = (_start + _count - 1) % Capacity;
                    Entry previous = Entries[last];
                    if (previous.SameAs(level, category, message))
                    {
                        Entries[last] = new Entry(
                            now, level, category, message, previous.StackTrace, previous.Count + 1);
                        Version++;
                        return;
                    }
                }

                var entry = new Entry(now, level, category, message, stackTrace ?? string.Empty, 1);

                // 満杯なら最古を捨てて、その位置に書く
                if (_count == Capacity)
                {
                    LevelCounts[(int)Entries[_start].Level]--;
                    Entries[_start] = entry;
                    _start = (_start + 1) % Capacity;
                }
                else
                {
                    Entries[(_start + _count) % Capacity] = entry;
                    _count++;
                }

                LevelCounts[(int)level]++;
                Version++;
            }
        }

        /// <summary>
        /// 保持中のログを古い順に result へ書き出す（result は先に空にする）。
        /// </summary>
        public static void CopyTo(List<Entry> result)
        {
            result.Clear();
            lock (Lock)
            {
                for (int i = 0; i < _count; i++)
                {
                    result.Add(Entries[(_start + i) % Capacity]);
                }
            }
        }

        /// <summary>
        /// 重要度ごとの保持件数（まとめた行は 1 件と数える）。
        /// </summary>
        public static int CountOf(LogLevel level)
        {
            lock (Lock)
            {
                return LevelCounts[(int)level];
            }
        }

        /// <summary>
        /// 保持中のログを消す（Player.log はそのまま）。
        /// </summary>
        public static void Clear()
        {
            lock (Lock)
            {
                Array.Clear(Entries, 0, Capacity);
                Array.Clear(LevelCounts, 0, LevelCounts.Length);
                _start = 0;
                _count = 0;
                Version++;
            }
        }

        /// <summary>
        /// Unity のログを 1 件取り込む（Application.logMessageReceivedThreaded の受け口）。
        /// </summary>
        public static void Receive(string condition, string stackTrace, LogType type)
        {
            // 重要度に変換（Assert・例外はエラー扱い）
            LogLevel level = type == LogType.Log ? LogLevel.Info
                : type == LogType.Warning ? LogLevel.Warning
                : LogLevel.Error;

            // VRCastLog の "[VRCast][Category] message" はカテゴリと本文に分け、それ以外は Unity のログとする
            string category = UnityCategory;
            string message = condition ?? string.Empty;
            if (message.StartsWith(VRCastPrefix, StringComparison.Ordinal))
            {
                int end = message.IndexOf(']', VRCastPrefix.Length);
                if (end > VRCastPrefix.Length)
                {
                    category = message.Substring(VRCastPrefix.Length, end - VRCastPrefix.Length);
                    message = message.Substring(end + 1).TrimStart();
                }
            }

            // スタックトレースは原因の追跡に要るエラーだけ残す（通常ログの分まで持つとメモリを食うため）
            Add(level, category, message, level == LogLevel.Error ? stackTrace : null);
        }
    }
}
