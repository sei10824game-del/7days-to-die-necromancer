using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace AllyOnKill
{
    /// <summary>
    /// 7DTD の Mod エントリポイント。
    /// ModInfo.xml と同じフォルダに置いた DLL は、ゲーム起動時に自動で読み込まれ
    /// IModApi を実装したクラスの InitMod() が呼ばれます。
    /// </summary>
    public class AllyApi : IModApi
    {
        public const string Version = "1.1.0";

        /// <summary>ModInfo.xml があるフォルダ（設定ファイル等の置き場）</summary>
        public static string ModPath = string.Empty;

        /// <summary>Settings.xml から読み込んだ設定</summary>
        public static AllySettings Settings = new AllySettings();

        public void InitMod(Mod _modInstance)
        {
            try
            {
                ModPath = _modInstance?.Path ?? string.Empty;
            }
            catch (Exception e)
            {
                Log.Warning("[AllyOnKill] Mod パスの取得に失敗: " + e.Message);
            }

            try
            {
                var file = Path.Combine(ModPath, "Settings.xml");
                Settings = AllySettings.Load(file);
            }
            catch (Exception e)
            {
                Log.Error("[AllyOnKill] Settings.xml の読み込みに失敗。既定値で動作します: " + e.Message);
                Settings = new AllySettings();
            }

            AllyRegistry.Following = Settings.FollowByDefault;

            var harmony = new Harmony("jp.allyonkill.mod");
            PatchLoader.Apply(harmony);

            // 直接攻撃が使えるか先に確認してログに残す（後から切り分けやすくするため）
            var combat = Combat.Available
                ? "利用可 (" + Combat.Detail + ")"
                : "利用不可 (" + Combat.Reason + ")";
            Log.Out("[AllyOnKill] 直接攻撃: " + combat);

            Log.Out("[AllyOnKill] v" + Version + " ロード完了"
                    + " / 追従キー=" + Settings.FollowKey
                    + " / 上限=" + Settings.MaxAllies
                    + " / 味方化率=" + (Settings.ConvertChance * 100f).ToString("0.#") + "%"
                    + " / パッチ=" + PatchLoader.AppliedCount + "/" + PatchLoader.AttemptedCount);
        }
    }
}
