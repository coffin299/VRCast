using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Avatars;

namespace VRCast.UI
{
    /// <summary>
    /// 表示中アバターに付いたコンポーネントを、アバターが替わるまでキャッシュする（OnGUI 毎の GetComponent を避ける）。
    /// </summary>
    public class AvatarComponentCache
    {
        private readonly AvatarSession _session;
        private readonly Dictionary<Type, Component> _components = new Dictionary<Type, Component>();
        private GameObject _instance;

        public AvatarComponentCache(AvatarSession session)
        {
            _session = session;
        }

        /// <summary>
        /// 表示中アバターの有無。
        /// </summary>
        public bool HasAvatar => _instance != null;

        /// <summary>
        /// アバターの切替を検出してキャッシュを破棄する。切り替わった場合は true。
        /// </summary>
        public bool Refresh()
        {
            GameObject instance = _session.Current?.Instance;

            // 同じアバター（破棄済み同士も含む）なら何もしない
            if (instance == _instance)
            {
                return false;
            }

            _instance = instance;
            _components.Clear();
            return true;
        }

        /// <summary>
        /// 表示中アバターのコンポーネントを返す。無ければ null。
        /// </summary>
        public T Get<T>() where T : Component
        {
            // アバター無し
            if (_instance == null)
            {
                return null;
            }

            // 初回だけ GetComponent し、結果（null 含む）を保持
            if (!_components.TryGetValue(typeof(T), out Component component))
            {
                component = _instance.GetComponent<T>();
                _components[typeof(T)] = component;
            }

            return component as T;
        }
    }
}
