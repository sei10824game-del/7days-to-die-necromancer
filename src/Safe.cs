using System;
using System.Collections.Generic;

namespace AllyOnKill
{
    /// <summary>
    /// 設定への安全なアクセスと、「例外が連続したらその機能を止める」回路ブレーカー。
    ///
    /// 7DTD の更新ループの中で毎フレーム例外を投げ続けると、ログが埋め尽くされ、
    /// UI の更新も失敗してゲームが操作不能になります。
    /// それを防ぐため、同じ箇所で規定回数続けて例外が出たら以降は実行しません。
    /// </summary>
    internal static class Safe
    {
        private const int MaxFailures = 20;

        private static readonly AllySettings Fallback = new AllySettings();
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private static readonly HashSet<string> TrippedKeys = new HashSet<string>();

        /// <summary>Settings.xml が読めていない場合でも null を返さない</summary>
        public static AllySettings Settings
        {
            get
            {
                try
                {
                    return AllyApi.Settings ?? Fallback;
                }
                catch
                {
                    return Fallback;
                }
            }
        }

        /// <summary>この機能は停止済みか</summary>
        public static bool Tripped(string _key)
        {
            return TrippedKeys.Contains(_key);
        }

        /// <summary>
        /// 例外を記録します。規定回数を超えたら true（＝以降は実行しない）を返します。
        /// </summary>
        public static bool OnError(string _key, string _message)
        {
            int count;
            if (!Counts.TryGetValue(_key, out count)) count = 0;
            count++;
            Counts[_key] = count;

            if (count == 1 || count == MaxFailures / 2)
                Log.Warning("[AllyOnKill] " + _key + " で例外 (" + count + "回目): " + _message);

            if (count < MaxFailures) return false;

            if (TrippedKeys.Add(_key))
                Log.Error("[AllyOnKill] '" + _key + "' で例外が " + count +
                          " 回続いたため、この機能を無効にしました。最後の例外: " + _message);

            return true;
        }

        /// <summary>正常に通ったことを記録（カウンタをリセット）</summary>
        public static void Success(string _key)
        {
            Counts[_key] = 0;
        }
    }
}
