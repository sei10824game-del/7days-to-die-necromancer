using System;
using UnityEngine;

namespace AllyOnKill
{
    /// <summary>
    /// メインループ。キー入力と定期更新をここから回します。
    /// </summary>
    internal static class GameManagerUpdate
    {
        public static void Postfix()
        {
            try
            {
                AllyController.Tick();
            }
            catch (Exception e)
            {
                // 例外を外に出すとゲーム側のループごと止まりかねないので、ここで食い止める
                Log.Error("[AllyOnKill] Tick 例外: " + e);
            }
        }
    }

    /// <summary>
    /// 敵が倒れた瞬間のフック。
    /// </summary>
    internal static class EntityDeath
    {
        public static void Postfix(EntityAlive __instance)
        {
            try
            {
                AllyController.OnEntityDeath(__instance);
            }
            catch (Exception e)
            {
                Log.Error("[AllyOnKill] OnEntityDeath 例外: " + e);
            }
        }
    }

    /// <summary>
    /// エンティティ生成直後のフック。
    /// ワールド読み込み直しで cvar 入りの味方が再生成された時に自動で再登録します。
    /// </summary>
    internal static class EntityPostInit
    {
        public static void Postfix(EntityAlive __instance)
        {
            try
            {
                AllyController.OnPostInit(__instance);
            }
            catch (Exception e)
            {
                Log.Error("[AllyOnKill] OnPostInit 例外: " + e);
            }
        }
    }

    /// <summary>
    /// ゲームが実際に使っている DamageSource のインスタンスを実行時に捕まえます。
    ///
    /// DamageSource の作り方はバージョン差が大きく、自前生成に失敗することがあります。
    /// ゲーム自身が DamageEntity() に渡してきた実物をそのまま使い回すのが一番確実なので、
    /// 何かがダメージを受けるタイミングでこっそり確保します。
    /// 副作用はありません（値を変えず、見るだけ）。
    /// </summary>
    internal static class DamageSourceCapture
    {
        public static void Prefix(object[] __args)
        {
            try
            {
                if (__args == null) return;

                for (var i = 0; i < __args.Length; i++)
                {
                    var arg = __args[i];
                    if (arg == null) continue;
                    if (arg.GetType().Name != "DamageSource") continue;

                    Combat.Capture(arg);
                    return;
                }
            }
            catch
            {
                // 取得できないなら自前生成に任せる
            }
        }
    }

    /// <summary>
    /// 味方がプレイヤーや他の味方を攻撃対象にするのを禁止します。
    /// 引数は名前ではなく位置（__args）で受け取るので、バージョンで引数名が
    /// 変わっても壊れにくいです。
    /// </summary>
    internal static class Targeting
    {
        public static bool SetAttackTarget_Prefix(EntityAlive __instance, object[] __args)
        {
            try
            {
                if (__instance == null || !AllyRegistry.IsAlly(__instance)) return true;
                if (__args == null || __args.Length < 1) return true;

                var target = __args[0] as EntityAlive;
                if (target == null) return true;

                // プレイヤー / 他の味方は狙わせない
                return !AllyController.IsProtected(target);
            }
            catch
            {
                return true; // 何かあればバニラの挙動に任せる
            }
        }

        public static bool SetRevengeTarget_Prefix(EntityAlive __instance, object[] __args)
        {
            try
            {
                if (__instance == null || !AllyRegistry.IsAlly(__instance)) return true;
                if (__args == null || __args.Length < 1) return true;

                var target = __args[0] as EntityAlive;
                if (target == null) return true;

                return !AllyController.IsProtected(target);
            }
            catch
            {
                return true;
            }
        }
    }
}
