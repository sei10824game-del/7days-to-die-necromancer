using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEngine;

namespace AllyOnKill
{
    /// <summary>
    /// Settings.xml から読み込む設定。要素が無い / 壊れている場合は既定値を使います。
    /// </summary>
    public class AllySettings
    {
        // ---- 味方化 ----
        /// <summary>倒した敵を味方にする機能そのものの ON/OFF（トラブル時は false に）</summary>
        public bool ConvertOnKill = true;

        /// <summary>敵を倒した時に味方化する確率（0.0 ～ 1.0）</summary>
        public float ConvertChance = 1.0f;

        /// <summary>
        /// 味方をプレイヤーの Companions 一覧にも追加する。
        /// V3.x では HUD と連動しており、敵エンティティを入れると UI が null 参照で
        /// 毎フレームエラーを出すことがあるため、既定は false。
        /// </summary>
        public bool AddToCompanions = false;

        /// <summary>プレイヤーからこの距離(m)以内で倒れた敵だけ味方化する</summary>
        public float ConvertRadius = 45f;

        /// <summary>同時に抱えられる味方の上限</summary>
        public int MaxAllies = 8;

        /// <summary>true なら死体を残さず即座に消す（味方として蘇るので見た目がスッキリする）</summary>
        public bool RemoveCorpse = true;

        /// <summary>味方化しないエンティティ名（カンマ区切り・部分一致・大文字小文字無視）</summary>
        public List<string> Blacklist = new List<string>();

        /// <summary>true なら上のリストを「許可リスト」として扱う（書かれたものだけ味方化）</summary>
        public bool BlacklistIsWhitelist = false;

        // ---- 追従 ----
        /// <summary>追従の ON/OFF を切り替えるキー</summary>
        public KeyCode FollowKey = KeyCode.G;

        /// <summary>全員を解放するキー（追従をやめて通常の敵に戻す）。KeyCode.None で無効</summary>
        public KeyCode DismissKey = KeyCode.H;

        /// <summary>ゲーム開始直後の追従状態</summary>
        public bool FollowByDefault = true;

        /// <summary>この距離(m)より離れたら歩いて近づく</summary>
        public float FollowDistance = 4.0f;

        /// <summary>この距離(m)より離れたらプレイヤー側にテレポート（デスポーン対策）</summary>
        public float TeleportDistance = 70f;

        // ---- 戦闘 ----
        /// <summary>味方が自動で敵を攻撃する</summary>
        public bool AssistPlayer = true;

        /// <summary>敵を探す半径(m)</summary>
        public float AssistRadius = 20f;

        // ---- 直接攻撃（7DTD のゾンビ AI はゾンビを狙わないための回避策） ----

        /// <summary>
        /// AI を通さず直接ダメージを与えて戦わせる。
        /// ゾンビの AI はプレイヤー / NPC しか狙わないので、これを true にしないと
        /// 味方と敵ゾンビが戦いません。
        /// </summary>
        public bool DirectAssist = true;

        /// <summary>直接攻撃が届く距離(m)。近接間隔くらいが自然です</summary>
        public float DirectAssistRange = 3.2f;

        /// <summary>直接攻撃1回のダメージ</summary>
        public int DirectAssistDamage = 25;

        /// <summary>直接攻撃の間隔(秒)</summary>
        public float DirectAssistInterval = 1.1f;

        /// <summary>敵に「この味方を狙え」と指示する（効かない AI もあります）</summary>
        public bool TauntEnemies = true;

        /// <summary>敵ゾンビも味方ゾンビを追跡・攻撃する（相互敵対）</summary>
        public bool EnemyAttackAlly = true;

        /// <summary>敵を見つけたら追従より優先して突っ込ませる</summary>
        public bool EngageEnemies = true;

        /// <summary>
        /// 味方が倒した敵も味方にする（上限は MaxAllies）
        /// </summary>
        public bool ConvertOnAllyKill = true;

        /// <summary>動物（EntityAnimal）も攻撃対象に含める。通常のゾンビ以外を狙わせたい時だけ true</summary>
        public bool AttackAnimals = false;

        // ---- その他 ----
        /// <summary>更新間隔(秒)。小さくすると反応が良くなるが重くなる</summary>
        public float TickInterval = 0.35f;

        /// <summary>画面右下にツールチップを出す（OFF にするとログのみ）</summary>
        public bool ShowMessages = true;

        /// <summary>デバッグログを出力する</summary>
        public bool DebugLog = false;

        // ------------------------------------------------------------------

        public static AllySettings Load(string _path)
        {
            var s = new AllySettings();
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
            {
                Log.Warning("[AllyOnKill] Settings.xml が見つかりません（" + _path + "）。既定値を使います。");
                return s;
            }

            var doc = XDocument.Load(_path);
            var root = doc.Root;
            if (root == null) return s;

            s.ConvertOnKill = GetBool(root, "ConvertOnKill", s.ConvertOnKill);
            s.ConvertChance = GetFloat(root, "ConvertChance", s.ConvertChance);
            s.AddToCompanions = GetBool(root, "AddToCompanions", s.AddToCompanions);
            s.ConvertRadius = GetFloat(root, "ConvertRadius", s.ConvertRadius);
            s.MaxAllies = GetInt(root, "MaxAllies", s.MaxAllies);
            s.RemoveCorpse = GetBool(root, "RemoveCorpse", s.RemoveCorpse);

            var list = GetString(root, "Blacklist", "");
            s.Blacklist = Split(list);
            s.BlacklistIsWhitelist = GetBool(root, "BlacklistIsWhitelist", s.BlacklistIsWhitelist);

            s.FollowKey = ParseKey(GetString(root, "FollowKey", "G"), KeyCode.G);
            s.DismissKey = ParseKey(GetString(root, "DismissKey", "H"), KeyCode.H);
            s.FollowByDefault = GetBool(root, "FollowByDefault", s.FollowByDefault);
            s.FollowDistance = GetFloat(root, "FollowDistance", s.FollowDistance);
            s.TeleportDistance = GetFloat(root, "TeleportDistance", s.TeleportDistance);

            s.AssistPlayer = GetBool(root, "AssistPlayer", s.AssistPlayer);
            s.AssistRadius = GetFloat(root, "AssistRadius", s.AssistRadius);
            s.AttackAnimals = GetBool(root, "AttackAnimals", s.AttackAnimals);

            s.DirectAssist = GetBool(root, "DirectAssist", s.DirectAssist);
            s.DirectAssistRange = GetFloat(root, "DirectAssistRange", s.DirectAssistRange);
            s.DirectAssistDamage = GetInt(root, "DirectAssistDamage", s.DirectAssistDamage);
            s.DirectAssistInterval = GetFloat(root, "DirectAssistInterval", s.DirectAssistInterval);
            s.TauntEnemies = GetBool(root, "TauntEnemies", s.TauntEnemies);
            s.EnemyAttackAlly = GetBool(root, "EnemyAttackAlly", s.EnemyAttackAlly);
            s.EngageEnemies = GetBool(root, "EngageEnemies", s.EngageEnemies);
            s.ConvertOnAllyKill = GetBool(root, "ConvertOnAllyKill", s.ConvertOnAllyKill);

            s.TickInterval = GetFloat(root, "TickInterval", s.TickInterval);
            s.ShowMessages = GetBool(root, "ShowMessages", s.ShowMessages);
            s.DebugLog = GetBool(root, "DebugLog", s.DebugLog);

            Log.Out("[AllyOnKill] Settings.xml を読み込みました");
            return s;
        }

        // ---- XML ヘルパー（要素が無ければ既定値） ----
        private static string GetString(XElement root, string name, string def)
        {
            var el = root.Element(name);
            var v = el?.Attribute("value")?.Value;
            return string.IsNullOrEmpty(v) ? def : v.Trim();
        }

        private static float GetFloat(XElement root, string name, float def)
        {
            float v;
            return float.TryParse(GetString(root, name, def.ToString()), out v) ? v : def;
        }

        private static int GetInt(XElement root, string name, int def)
        {
            int v;
            return int.TryParse(GetString(root, name, def.ToString()), out v) ? v : def;
        }

        private static bool GetBool(XElement root, string name, bool def)
        {
            bool v;
            return bool.TryParse(GetString(root, name, def.ToString()), out v) ? v : def;
        }

        private static List<string> Split(string csv)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(csv)) return result;
            foreach (var part in csv.Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0) result.Add(t.ToLowerInvariant());
            }
            return result;
        }

        private static KeyCode ParseKey(string name, KeyCode def)
        {
            try
            {
                var code = (KeyCode)Enum.Parse(typeof(KeyCode), name, true);
                return code;
            }
            catch
            {
                Log.Warning("[AllyOnKill] キー名 '" + name + "' を解釈できません。既定値 " + def + " を使います。");
                return def;
            }
        }
    }
}
