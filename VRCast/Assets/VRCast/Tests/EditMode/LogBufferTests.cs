using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// デバッグログタブ用のログ保持（上限・件数・VRCastLog のカテゴリ分け）を検証する。
    /// </summary>
    public class LogBufferTests
    {
        [SetUp]
        public void SetUp()
        {
            // 他のテストや Editor のログの影響を受けないよう空にする
            LogBuffer.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            LogBuffer.Clear();
            LogBuffer.DetailEnabled = false;
        }

        [Test]
        public void Add_SameMessageInARow_IsCollapsedWithCount()
        {
            // 直前と同じ内容は 1 件にまとめて回数を数え、重要度の件数も 1 件のままであること
            LogBuffer.Add(LogLevel.Warning, "Test", "repeated");
            LogBuffer.Add(LogLevel.Warning, "Test", "repeated");
            LogBuffer.Add(LogLevel.Warning, "Test", "repeated");

            LogBuffer.Entry entry = FindByMessage("repeated");
            Assert.That(entry.Count, Is.EqualTo(3));
            Assert.That(LogBuffer.CountOf(LogLevel.Warning), Is.EqualTo(1));
        }

        [Test]
        public void Add_Debug_IsRecordedOnlyWhenDetailEnabled()
        {
            // 詳細ログ OFF の間は DEBUG を捨て、ON にすると記録すること
            LogBuffer.DetailEnabled = false;
            LogBuffer.Add(LogLevel.Debug, "Test", "hidden");
            Assert.That(LogBuffer.CountOf(LogLevel.Debug), Is.EqualTo(0));

            LogBuffer.DetailEnabled = true;
            LogBuffer.Add(LogLevel.Debug, "Test", "shown");
            Assert.That(FindByMessage("shown").Level, Is.EqualTo(LogLevel.Debug));
        }

        [Test]
        public void Add_OverCapacity_DropsOldestAndKeepsCounts()
        {
            // 上限 + 1 件（先頭だけ警告）を入れると、最古の警告が捨てられること
            LogBuffer.Add(LogLevel.Warning, "Test", "first");
            for (int i = 0; i < LogBuffer.Capacity; i++)
            {
                LogBuffer.Add(LogLevel.Info, "Test", "info " + i);
            }

            var entries = new List<LogBuffer.Entry>();
            LogBuffer.CopyTo(entries);
            Assert.That(entries.Count, Is.EqualTo(LogBuffer.Capacity));
            Assert.That(entries[0].Message, Is.EqualTo("info 0"));
            Assert.That(entries[entries.Count - 1].Message, Is.EqualTo("info " + (LogBuffer.Capacity - 1)));
            Assert.That(LogBuffer.CountOf(LogLevel.Warning), Is.EqualTo(0));
            Assert.That(LogBuffer.CountOf(LogLevel.Info), Is.EqualTo(LogBuffer.Capacity));
        }

        [Test]
        public void Clear_RemovesAllAndChangesVersion()
        {
            // 消去で件数が 0 になり、UI が気付けるよう番号が変わること
            LogBuffer.Add(LogLevel.Error, "Test", "error");
            int version = LogBuffer.Version;
            LogBuffer.Clear();

            var entries = new List<LogBuffer.Entry>();
            LogBuffer.CopyTo(entries);
            Assert.That(entries, Is.Empty);
            Assert.That(LogBuffer.CountOf(LogLevel.Error), Is.EqualTo(0));
            Assert.That(LogBuffer.Version, Is.Not.EqualTo(version));
        }

        [Test]
        public void Receive_VRCastLog_IsSplitIntoCategoryAndMessage()
        {
            // "[VRCast][Category] message" はカテゴリと本文に分け、通常ログのスタックトレースは持たないこと
            LogBuffer.Receive("[VRCast][Camera] not found", "trace", LogType.Warning);

            LogBuffer.Entry entry = FindByMessage("not found");
            Assert.That(entry.Category, Is.EqualTo("Camera"));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.StackTrace, Is.Empty);
        }

        [Test]
        public void Receive_UnityException_IsErrorWithStackTrace()
        {
            // VRCastLog 以外は Unity カテゴリ、例外はエラー扱いでスタックトレースを残すこと
            LogBuffer.Receive("NullReferenceException", "at Foo()", LogType.Exception);

            LogBuffer.Entry entry = FindByMessage("NullReferenceException");
            Assert.That(entry.Category, Is.EqualTo("Unity"));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(entry.StackTrace, Is.EqualTo("at Foo()"));
        }

        private static LogBuffer.Entry FindByMessage(string message)
        {
            // Editor 自身のログが混ざっても対象を取り出せるよう、本文で探す
            var entries = new List<LogBuffer.Entry>();
            LogBuffer.CopyTo(entries);
            int index = entries.FindIndex(e => e.Message == message);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Entry not found: " + message);
            return entries[index];
        }
    }
}
