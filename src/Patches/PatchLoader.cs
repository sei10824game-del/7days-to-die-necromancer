using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AllyOnKill
{
    /// <summary>
    /// Harmony パッチをまとめて適用します。
    ///
    /// バージョン差でメソッド名やシグネチャが変わっても MOD 全体が落ちないように、
    /// ここではリフレクションで対象を探し、見つからないパッチは「警告を出して飛ばす」
    /// 方針をとっています（必須パッチが無い場合はログで気づけます）。
    /// </summary>
    internal static class PatchLoader
    {
        public static int AttemptedCount;
        public static int AppliedCount;

        public static void Apply(Harmony harmony)
        {
            // ---- 必須 ----
            Apply(harmony, typeof(GameManager), "Update",
                typeof(GameManagerUpdate), nameof(GameManagerUpdate.Postfix), true);

            Apply(harmony, typeof(EntityAlive), "OnEntityDeath",
                typeof(EntityDeath), nameof(EntityDeath.Postfix), true);

            // ---- 任意（無くても動くが、あると良くなる） ----
            Apply(harmony, typeof(EntityAlive), "PostInit",
                typeof(EntityPostInit), nameof(EntityPostInit.Postfix), false);

            Apply(harmony, typeof(EntityAlive), "SetAttackTarget",
                new[] { typeof(EntityAlive), typeof(int) },
                typeof(Targeting), nameof(Targeting.SetAttackTarget_Prefix), false);

            Apply(harmony, typeof(EntityAlive), "SetRevengeTarget",
                new[] { typeof(EntityAlive) },
                typeof(Targeting), nameof(Targeting.SetRevengeTarget_Prefix), false);

            // DamageSource の実インスタンスを捕まえる（直接攻撃用・任意）
            ApplyFirstDamageEntity(harmony);
        }

        private static void Apply(Harmony harmony, Type target, string method,
            Type patchClass, string patchName, bool required)
        {
            Apply(harmony, target, method, null, patchClass, patchName, required);
        }

        /// <summary>
        /// EntityAlive.DamageEntity の「引数に DamageSource を含む」全オーバーロードに
        /// DamageSource 捕獲用の Prefix を当てます。
        ///
        /// 1つだけに当てると、ゲームが実際に呼ぶオーバーロードとズレて
        /// 全然捕まらないことがあるため、全部に当てます。
        /// </summary>
        private static void ApplyFirstDamageEntity(Harmony harmony)
        {
            AttemptedCount++;

            var patch = AccessTools.Method(typeof(DamageSourceCapture), nameof(DamageSourceCapture.Prefix));
            if (patch == null)
            {
                Log.Error("[AllyOnKill] パッチメソッドが見つかりません: DamageSourceCapture.Prefix");
                return;
            }

            var targets = new List<MethodInfo>();
            foreach (var mi in AccessTools.GetDeclaredMethods(typeof(EntityAlive)))
            {
                if (mi.Name != "DamageEntity") continue;

                var hasDs = false;
                foreach (var p in mi.GetParameters())
                {
                    if (p.ParameterType.Name == "DamageSource") { hasDs = true; break; }
                }
                if (hasDs) targets.Add(mi);
            }

            // 念のため基底クラス側も見る
            foreach (var mi in AccessTools.GetDeclaredMethods(typeof(EntityAlive).BaseType ?? typeof(EntityAlive)))
            {
                if (mi.Name != "DamageEntity") continue;

                var hasDs = false;
                foreach (var p in mi.GetParameters())
                {
                    if (p.ParameterType.Name == "DamageSource") { hasDs = true; break; }
                }
                if (hasDs && !targets.Contains(mi)) targets.Add(mi);
            }

            if (targets.Count == 0)
            {
                Log.Warning("[AllyOnKill] [WARN] パッチ対象が見つかりません: "
                            + "EntityAlive.DamageEntity(DamageSource...) → スキップ"
                            + "（直接攻撃が使えない可能性があります）");
                return;
            }

            var ok = 0;
            foreach (var mi in targets)
            {
                try
                {
                    harmony.Patch(mi, new HarmonyMethod(patch));
                    ok++;
                    Log.Out("[AllyOnKill] パッチ適用: DamageEntity ("
                            + mi.GetParameters().Length + "引数) (Prefix)");
                }
                catch (Exception e)
                {
                    Log.Error("[AllyOnKill] パッチ適用失敗: DamageEntity ("
                              + mi.GetParameters().Length + "引数) → " + e.Message);
                }
            }

            if (ok > 0) AppliedCount++;
        }

        private static void Apply(Harmony harmony, Type target, string method, Type[] argTypes,
            Type patchClass, string patchName, bool required)
        {
            AttemptedCount++;

            MethodInfo original = null;

            // まずは引数型つきで探す（オーバーロード対策）
            if (argTypes != null)
            {
                try { original = AccessTools.Method(target, method, argTypes); }
                catch { original = null; }
            }

            // だめなら名前だけで探す
            if (original == null)
            {
                try { original = AccessTools.Method(target, method); }
                catch (AmbiguousMatchException)
                {
                    // オーバーロードが複数ある場合は引数が EntityAlive のものを優先
                    foreach (var mi in AccessTools.GetDeclaredMethods(target))
                    {
                        if (mi.Name != method) continue;
                        var ps = mi.GetParameters();
                        if (ps.Length > 0 && ps[0].ParameterType == typeof(EntityAlive))
                        {
                            original = mi;
                            break;
                        }
                    }
                }
                catch { original = null; }
            }

            if (original == null)
            {
                var level = required ? "ERROR" : "WARN";
                Log.Warning("[AllyOnKill] [" + level + "] パッチ対象が見つかりません: "
                            + target.Name + "." + method + " → スキップ"
                            + (required ? "（この機能は動作しません）" : string.Empty));
                return;
            }

            var patch = AccessTools.Method(patchClass, patchName);
            if (patch == null)
            {
                Log.Error("[AllyOnKill] パッチメソッドが見つかりません: " + patchClass.Name + "." + patchName);
                return;
            }

            var isPrefix = patchName.IndexOf("Prefix", StringComparison.OrdinalIgnoreCase) >= 0;
            var hm = new HarmonyMethod(patch);

            try
            {
                harmony.Patch(original, isPrefix ? hm : null, isPrefix ? null : hm);
                AppliedCount++;
                Log.Out("[AllyOnKill] パッチ適用: " + target.Name + "." + method + " (" + patchName + ")");
            }
            catch (Exception e)
            {
                Log.Error("[AllyOnKill] パッチ適用失敗: " + target.Name + "." + method + " → " + e.Message);
            }
        }
    }
}
