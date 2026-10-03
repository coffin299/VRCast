using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Dynamics
{
    /// <summary>
    /// アバターの全 Constraint を毎フレーム評価する。トラッキング適用後・揺れもの計算前に動かす。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ConstraintSolver : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Constraint";

        private List<ConstraintEvaluator> _evaluators = new List<ConstraintEvaluator>();

        public int Count => _evaluators.Count;

        public void Initialize(ConstraintSet set)
        {
            // 解決できたものだけを残す
            var evaluators = new List<ConstraintEvaluator>();
            foreach (ConstraintData data in set.constraints)
            {
                ConstraintEvaluator evaluator = ConstraintEvaluator.Create(transform, data);
                if (evaluator != null)
                {
                    evaluators.Add(evaluator);
                }
            }

            // 他の Constraint の結果を参照するものを後にする
            _evaluators = SortByDependency(evaluators);
            VRCastLog.Info(LogCategory, $"Resolved {_evaluators.Count}/{set.constraints.Length} constraints.");

            // 揺れものが静止姿勢を記録する前に一度適用しておく
            EvaluateAll();
        }

        /// <summary>
        /// 参照先（ソース・上方向）を動かす Constraint を先に並べる。循環は元の順序のまま打ち切る。
        /// </summary>
        public static List<ConstraintEvaluator> SortByDependency(List<ConstraintEvaluator> evaluators)
        {
            var sorted = new List<ConstraintEvaluator>(evaluators.Count);
            var visiting = new HashSet<ConstraintEvaluator>();
            var done = new HashSet<ConstraintEvaluator>();
            foreach (ConstraintEvaluator evaluator in evaluators)
            {
                Visit(evaluator, evaluators, visiting, done, sorted);
            }

            return sorted;
        }

        private static void Visit(
            ConstraintEvaluator evaluator, List<ConstraintEvaluator> all, HashSet<ConstraintEvaluator> visiting,
            HashSet<ConstraintEvaluator> done, List<ConstraintEvaluator> sorted)
        {
            // 処理済み・循環中は飛ばす
            if (done.Contains(evaluator) || !visiting.Add(evaluator))
            {
                return;
            }

            // 依存先を先に追加
            foreach (ConstraintEvaluator other in all)
            {
                if (other != evaluator && evaluator.DependsOn(other))
                {
                    Visit(other, all, visiting, done, sorted);
                }
            }

            visiting.Remove(evaluator);
            done.Add(evaluator);
            sorted.Add(evaluator);
        }

        private void LateUpdate()
        {
            EvaluateAll();
        }

        private void EvaluateAll()
        {
            foreach (ConstraintEvaluator evaluator in _evaluators)
            {
                evaluator.Evaluate();
            }
        }
    }
}
