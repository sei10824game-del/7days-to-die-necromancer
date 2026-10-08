using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AllyOnKill
{
    /// <summary>
    /// キーボード入力ヘルパー。
    ///
    /// 7DTD v3.x (Unity 2022.3) は新しい Input System を使っており、
    /// 従来の UnityEngine.Input は別アセンブリ (InputLegacyModule) に入っていて
    /// 参照できない／実行時に無効な場合があります。
    ///
    /// そこで「従来 Input」と「InputSystem」の両方をリフレクションで探索し、
    /// 最初に利用できた方を使います。どちらが有効でもビルドは通ります。
    /// </summary>
    internal static class KeyInput
    {
        // ---- legacy UnityEngine.Input ----
        private static MethodInfo _legacyGetKeyDown;
        private static bool _legacyResolved;

        // ---- UnityEngine.InputSystem ----
        private static PropertyInfo _kbCurrent;
        private static MethodInfo _kbIndexer;
        private static Type _keyType;
        private static readonly Dictionary<string, object> _keyCache = new Dictionary<string, object>();
        private static bool _isResolved;

        private static string _backend = "none";

        /// <summary>利用できた入力バックエンド名（ログ用）</summary>
        public static string Backend => _backend;

        public static bool GetKeyDown(KeyCode _key)
        {
            try
            {
                if (!_isResolved) Resolve();

                if (_legacyGetKeyDown != null)
                {
                    var result = _legacyGetKeyDown.Invoke(null, new object[] { _key });
                    return result is bool && (bool)result;
                }

                if (_kbCurrent != null && _kbIndexer != null)
                    return InputSystemKeyDown(_key);

                return false;
            }
            catch (Exception e)
            {
                Log.Warning("[AllyOnKill] KeyInput 例外: " + e.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------

        private static void Resolve()
        {
            _isResolved = true;

            // 1) 従来の UnityEngine.Input を探す
            var inputType = FindType("UnityEngine.Input",
                "UnityEngine.InputLegacyModule", "UnityEngine.CoreModule", "UnityEngine");

            if (inputType != null)
            {
                _legacyGetKeyDown = inputType.GetMethod(
                    "GetKeyDown",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(KeyCode) },
                    null);
            }

            if (_legacyGetKeyDown != null)
            {
                _backend = "UnityEngine.Input (legacy)";
                Log.Out("[AllyOnKill] 入力バックエンド: " + _backend);
                return;
            }

            // 2) 新しい Input System を探す
            var keyboardType = FindType("UnityEngine.InputSystem.Keyboard", "UnityEngine.InputSystem");
            _keyType = FindType("UnityEngine.InputSystem.Key", "UnityEngine.InputSystem");

            if (keyboardType != null && _keyType != null)
            {
                _kbCurrent = keyboardType.GetProperty("current",
                    BindingFlags.Public | BindingFlags.Static);

                _kbIndexer = keyboardType.GetMethod("get_Item",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { _keyType },
                    null);
            }

            if (_kbCurrent != null && _kbIndexer != null)
            {
                _backend = "UnityEngine.InputSystem";
                Log.Out("[AllyOnKill] 入力バックエンド: " + _backend);
                return;
            }

            _backend = "none";
            Log.Warning("[AllyOnKill] キーボード入力 API を検出できませんでした。G キーは動作しません。");
        }

        private static bool InputSystemKeyDown(KeyCode _key)
        {
            var keyboard = _kbCurrent.GetValue(null);
            if (keyboard == null) return false;

            var name = _key.ToString();
            object keyValue;
            if (!_keyCache.TryGetValue(name, out keyValue))
            {
                keyValue = ToInputSystemKey(name);
                _keyCache[name] = keyValue;
            }

            if (keyValue == null) return false;

            var control = _kbIndexer.Invoke(keyboard, new[] { keyValue });
            if (control == null) return false;

            var pi = control.GetType().GetProperty("wasPressedThisFrame",
                BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || pi.PropertyType != typeof(bool)) return false;

            return (bool)pi.GetValue(control);
        }

        /// <summary>
        /// UnityEngine.KeyCode の名前を UnityEngine.InputSystem.Key の名前に変換します。
        /// 変換できなければ null（= そのキーは新 Input System では扱わない）。
        /// </summary>
        private static object ToInputSystemKey(string _name)
        {
            var mapped = MapName(_name);
            try
            {
                return Enum.Parse(_keyType, mapped, true);
            }
            catch
            {
                Log.Warning("[AllyOnKill] キー '" + _name + "' を InputSystem.Key に変換できませんでした。");
                return null;
            }
        }

        private static string MapName(string _name)
        {
            if (string.IsNullOrEmpty(_name)) return _name;

            var lower = _name.ToLowerInvariant();

            switch (lower)
            {
                case "leftcontrol": return "LeftCtrl";
                case "rightcontrol": return "RightCtrl";
                case "leftshift": return "LeftShift";
                case "rightshift": return "RightShift";
                case "leftalt": return "LeftAlt";
                case "rightalt": return "RightAlt";
                case "leftcommand":
                case "leftwindows": return "LeftMeta";
                case "rightcommand":
                case "rightwindows": return "RightMeta";
                case "return": return "Enter";
                case "uparrow": return "UpArrow";
                case "downarrow": return "DownArrow";
                case "leftarrow": return "LeftArrow";
                case "rightarrow": return "RightArrow";
                case "backquote": return "Backquote";
                case "minus": return "Minus";
                case "equals": return "Equals";
                case "leftbracket": return "LeftBracket";
                case "rightbracket": return "RightBracket";
                case "backslash": return "Backslash";
                case "semicolon": return "Semicolon";
                case "quote": return "Quote";
                case "comma": return "Comma";
                case "period": return "Period";
                case "slash": return "Slash";
            }

            // Alpha0-9 / 数字 -> Digit0-9
            if (lower.StartsWith("alpha") && lower.Length == 6)
                return "Digit" + lower.Substring(5);

            if (lower.Length == 1 && char.IsDigit(lower[0]))
                return "Digit" + lower;

            return _name; // A-Z などはそのまま
        }

        /// <summary>
        /// 型を探します。まず指定アセンブリ名で Type.GetType を試し、
        /// だめなら読み込み済みアセンブリを総走査します。
        /// </summary>
        private static Type FindType(string _typeName, params string[] _assemblyNames)
        {
            foreach (var asm in _assemblyNames)
            {
                try
                {
                    var t = Type.GetType(_typeName + ", " + asm);
                    if (t != null) return t;
                }
                catch
                {
                    // 無視して次へ
                }
            }

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = null;
                    try { t = asm.GetType(_typeName, false, false); }
                    catch { t = null; }
                    if (t != null) return t;
                }
            }
            catch
            {
                // 無視
            }

            return null;
        }
    }
}
