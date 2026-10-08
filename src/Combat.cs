using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace AllyOnKill
{
    /// <summary>
    /// 味方に「直接ダメージ」で戦わせるためのヘルパー。
    ///
    /// 7DTD のゾンビ AI は entityclasses.xml の AITask / AITarget で
    /// 攻撃対象クラスが決まっており、ゾンビは原則プレイヤー / NPC しか狙いません。
    /// SetAttackTarget() で別のゾンビを渡しても AI 側のクラスフィルタで弾かれるため、
    /// AI を通さず EntityAlive.DamageEntity() を直接呼びます。
    ///
    /// DamageSource の扱いはバージョン差が大きいので、3段構えです:
    ///   1) ゲームが実際に使っている DamageSource インスタンスを実行時に捕まえて使い回す
    ///      (DamageEntity の Prefix で取得。最も確実)
    ///   2) リフレクションで自前生成する
    ///   3) どちらも無理なら「使えない」と記録して静かに諦める（ビルドは壊れない）
    /// </summary>
    internal static class Combat
    {
        private static Type _dsType;
        private static MethodInfo _damageEntity;
        private static ParameterInfo[] _dmgParams;

        private static ConstructorInfo _entityCtor;
        private static object[] _entityCtorArgs;
        private static object _constructed;
        private static object _captured;
        private static bool _captureTried;

        private static FieldInfo _attackerField;
        private static PropertyInfo _attackerProp;

        private static bool _resolved;
        private static bool _available;

        /// <summary>新しい DamageSource を捕まえたので、もう一度解決し直す必要がある</summary>
        private static bool _dirty;

        /// <summary>一度でも利用可になったか（2回目以降のログ文言切り替え用）</summary>
        private static bool _wasAvailable;

        private static readonly StringBuilder _detail = new StringBuilder();

        /// <summary>直接攻撃が使えるか</summary>
        public static bool Available
        {
            get
            {
                // 初回、または捕まえた後は解決し直す
                if (!_resolved || _dirty)
                {
                    _dirty = false;
                    Resolve();
                }
                return _available;
            }
        }

        /// <summary>使えない場合の理由</summary>
        public static string Reason { get; private set; } = "not resolved yet";

        /// <summary>解決できた内容の詳細（ログ用）</summary>
        public static string Detail => _detail.ToString();

        /// <summary>
        /// ゲームが使った DamageSource の実インスタンスを受け取ります。
        /// （DamageEntity の Prefix から呼ばれます）
        /// </summary>
        public static void Capture(object _damageSource)
        {
            if (_damageSource == null) return;
            if (_captured != null) return;

            _captured = _damageSource;
            _captureTried = true;

            // 起動時に一度失敗していても、ここで再挑戦できるようにする
            _dirty = true;

            Log.Out("[AllyOnKill] DamageSource の実インスタンスを取得しました: " + _damageSource.GetType().FullName);
        }

        /// <summary>直接ダメージを与えます。成功したら true</summary>
        public static bool Hurt(EntityAlive attacker, EntityAlive victim, int strength)
        {
            if (attacker == null || victim == null || strength <= 0) return false;
            if (victim.IsDead()) return false;

            if (!_resolved) Resolve();
            if (!_available) return false;

            // 捕まえた実インスタンスを優先し、無ければ自前生成したものを使う
            // 攻撃者ID入りの DamageSourceEntity(External, Bashing, id) を毎回新規生成（通常の近接攻撃と同じ）
            object ds = null;
            try
            {
                if (_entityCtor != null)
                {
                    var a = (object[])_entityCtorArgs.Clone();
                    a[2] = attacker.entityId;
                    ds = _entityCtor.Invoke(a);
                }
            }
            catch { ds = null; }
            if (ds == null) ds = _captured ?? _constructed;
            if (ds == null) return false;

            try
            {
                if (_entityCtor == null) SetAttacker(ds, attacker.entityId);

                var args = BuildArgs(_dmgParams, ds, strength);
                _damageEntity.Invoke(victim, args);
                return true;
            }
            catch (Exception e)
            {
                if (Safe.OnError("Combat", e.Message)) _available = false;
                return false;
            }
        }

        // ------------------------------------------------------------------

        private static void Resolve()
        {
            _resolved = true;
            _detail.Length = 0;

            try
            {
                if (_dsType == null) _dsType = FindType("DamageSource");

                if (_dsType == null)
                {
                    Reason = "DamageSource 型が見つかりません";
                    Log.Warning("[AllyOnKill] 直接攻撃は利用できません: " + Reason);
                    return;
                }
                _detail.Append("DamageSource=").Append(_dsType.FullName).Append("; ");

                // DamageEntity を探す。第1引数が DamageSource のものを優先。
                MethodInfo fallback = null;
                foreach (var mi in typeof(EntityAlive).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (mi.Name != "DamageEntity") continue;
                    var ps = mi.GetParameters();
                    if (ps.Length == 0) continue;

                    if (ps[0].ParameterType != _dsType)
                    {
                        if (fallback == null && ps.Length >= 2) fallback = mi;
                        continue;
                    }

                    // 4引数 (DamageSource,int,bool,float) を最優先
                    if (_damageEntity == null || ps.Length == 4) _damageEntity = mi;
                }

                if (_damageEntity == null) _damageEntity = fallback;

                if (_damageEntity == null)
                {
                    Reason = "EntityAlive.DamageEntity(DamageSource, ...) が見つかりません";
                    Log.Warning("[AllyOnKill] 直接攻撃は利用できません: " + Reason);
                    return;
                }

                _dmgParams = _damageEntity.GetParameters();
                _detail.Append("DamageEntity(").Append(_dmgParams.Length).Append("引数); ");

                // 自前生成にも挑戦しておく（捕まえられなかった時の保険）
                TryEntityCtor();
                _constructed = _entityCtor != null ? _entityCtor.Invoke(_entityCtorArgs) : TryConstruct();
                _detail.Append(_constructed != null ? "自前生成=可; " : "自前生成=不可; ");
                _detail.Append(_captureTried ? "実行時取得=済; " : "実行時取得=未; ");

                if (_constructed == null && _captured == null)
                {
                    Reason = "DamageSource を用意できません（生成も取得も失敗）";
                    Log.Warning("[AllyOnKill] 直接攻撃は利用できません: " + Reason);
                    return;
                }

                var ds = _captured ?? _constructed;
                _attackerField = FindField(ds.GetType(),
                    "EntityId", "entityId", "AttackerEntityId", "attackerEntityId", "SourceEntityId");
                _attackerProp = FindProperty(ds.GetType(),
                    "EntityId", "entityId", "AttackerEntityId", "attackerEntityId", "SourceEntityId");

                _available = true;
                Reason = "ok";
                Log.Out(_wasAvailable
                    ? "[AllyOnKill] 直接攻撃の状態を更新: " + Detail
                    : "[AllyOnKill] 直接攻撃が利用できます: " + Detail);
                _wasAvailable = true;
            }
            catch (Exception e)
            {
                Reason = e.Message;
                Log.Warning("[AllyOnKill] 直接攻撃の初期化に失敗: " + e.Message);
            }
        }

        /// <summary>バニラの近接攻撃と同じ DamageSourceEntity(External, Bashing, id) を探す</summary>
        private static void TryEntityCtor()
        {
            _entityCtor = null;
            try
            {
                var t = FindType("DamageSourceEntity");
                var eSrc = FindType("EnumDamageSource");
                var eTyp = FindType("EnumDamageTypes");
                if (t == null || eSrc == null || eTyp == null) return;
                foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                {
                    var ps = c.GetParameters();
                    if (ps.Length < 3 || ps[0].ParameterType != eSrc || ps[1].ParameterType != eTyp || ps[2].ParameterType != typeof(int)) continue;
                    var args = new object[ps.Length];
                    for (var i = 0; i < ps.Length; i++) args[i] = DefaultValue(ps[i].ParameterType);
                    args[0] = EnumValue(eSrc, "External");
                    args[1] = EnumValue(eTyp, "Bashing");
                    args[2] = -1;
                    _entityCtor = c;
                    _entityCtorArgs = args;
                    _detail.Append("DamageSourceEntity=可; ");
                    return;
                }
            }
            catch { _entityCtor = null; }
        }

        /// <summary>
        /// DamageSource を自前で生成します。
        /// コンストラクタの引数の型を決め打ちするとバージョン差で外れるので、
        /// 全コンストラクタを「引数の型から適当な値を埋めて」片っ端から試します。
        /// </summary>
        private static object TryConstruct()
        {
            // 引数が少ない順に試す（シンプルな方が安全）
            var ctors = new List<ConstructorInfo>();
            try { ctors.AddRange(_dsType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)); }
            catch { /* 無視 */ }
            try { ctors.AddRange(_dsType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)); }
            catch { /* 無視 */ }

            ctors.Sort((a, b) => a.GetParameters().Length.CompareTo(b.GetParameters().Length));

            foreach (var c in ctors)
            {
                try
                {
                    var ps = c.GetParameters();
                    var args = new object[ps.Length];
                    for (var i = 0; i < ps.Length; i++)
                        args[i] = DefaultValue(ps[i].ParameterType);

                    var obj = c.Invoke(args);
                    if (obj != null)
                    {
                        _detail.Append("生成=").Append(ps.Length).Append("引数ctor; ");
                        return obj;
                    }
                }
                catch (Exception e)
                {
                    _detail.Append("ctor失敗=").Append(e.GetType().Name).Append("; ");
                }
            }

            // コンストラクタが全部ダメなら、初期化せずに作る（フィールドは後から埋める）
            try
            {
                var obj = System.Runtime.Serialization.FormatterServices
                    .GetUninitializedObject(_dsType);
                if (obj != null)
                {
                    _detail.Append("生成=未初期化; ");
                    return obj;
                }
            }
            catch (Exception e)
            {
                _detail.Append("未初期化生成失敗=").Append(e.Message).Append("; ");
            }

            return null;
        }

        /// <summary>型から「とりあえず入れておく値」を返します</summary>
        private static object DefaultValue(Type _type)
        {
            if (_type == typeof(int)) return -1;
            if (_type == typeof(float)) return 0f;
            if (_type == typeof(double)) return 0.0;
            if (_type == typeof(bool)) return false;
            if (_type == typeof(string)) return "";
            if (_type == typeof(byte)) return (byte)0;
            if (_type == typeof(long)) return (long)-1;

            if (_type.IsEnum) return FirstEnumValue(_type);
            if (_type.IsValueType)
            {
                try { return Activator.CreateInstance(_type); }
                catch { return null; }
            }
            return null;
        }

        /// <summary>DamageEntity の引数を、型から推測して組み立てます</summary>
        private static object[] BuildArgs(ParameterInfo[] _params, object _damageSource, int _strength)
        {
            var args = new object[_params.Length];

            for (var i = 0; i < _params.Length; i++)
            {
                var t = _params[i].ParameterType;

                if (t == _dsType || (t.IsClass && t.IsInstanceOfType(_damageSource)))
                    args[i] = _damageSource;
                else if (t == typeof(int))
                    args[i] = _strength;
                else if (t == typeof(bool))
                    args[i] = false;
                else if (t == typeof(float))
                    args[i] = 1f;
                else if (t == typeof(double))
                    args[i] = 1.0;
                else if (t.IsEnum)
                    args[i] = FirstEnumValue(t);
                else if (t.IsValueType)
                    args[i] = Activator.CreateInstance(t);
                else
                    args[i] = null;
            }

            return args;
        }

        private static void SetAttacker(object _damageSource, int _entityId)
        {
            if (_damageSource == null) return;

            try
            {
                if (_attackerField != null && _attackerField.FieldType == typeof(int))
                    _attackerField.SetValue(_damageSource, _entityId);
                else if (_attackerProp != null && _attackerProp.PropertyType == typeof(int) && _attackerProp.CanWrite)
                    _attackerProp.SetValue(_damageSource, _entityId, null);
            }
            catch
            {
                // 攻撃者の設定は必須ではないので無視
            }
        }

        private static object FirstEnumValue(Type _enumType)
        {
            try
            {
                var values = Enum.GetValues(_enumType);
                return values.Length > 0 ? values.GetValue(0) : null;
            }
            catch
            {
                return null;
            }
        }

        private static object EnumValue(Type _enumType, params string[] _names)
        {
            if (_enumType == null) return null;

            foreach (var n in _names)
            {
                try { return Enum.Parse(_enumType, n, true); }
                catch { /* 次へ */ }
            }
            return FirstEnumValue(_enumType);
        }

        private static FieldInfo FindField(Type _type, params string[] _names)
        {
            foreach (var n in _names)
            {
                try
                {
                    var f = _type.GetField(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (f != null) return f;
                }
                catch { /* 次へ */ }
            }
            return null;
        }

        private static PropertyInfo FindProperty(Type _type, params string[] _names)
        {
            foreach (var n in _names)
            {
                try
                {
                    var p = _type.GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (p != null) return p;
                }
                catch { /* 次へ */ }
            }
            return null;
        }

        private static Type FindType(string _name)
        {
            try
            {
                var t = Type.GetType(_name + ", Assembly-CSharp");
                if (t != null) return t;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type found = null;
                    try { found = asm.GetType(_name, false, false); }
                    catch { found = null; }
                    if (found != null) return found;
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
