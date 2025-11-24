using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#if USE_UNITASK
using Cysharp.Threading.Tasks;
#endif

namespace work.ctrl3d
{
    public class StationPacket
    {
        public string Channel;
        public object Payload;
    }

    [DefaultExecutionOrder(-100)]
    public class RadioStation : MonoBehaviour
    {
        // 채널별 리스너 관리
        private readonly Dictionary<string, Action> _channelDictionary = new();
        private readonly Dictionary<string, Action<object>> _typedChannelDictionary = new();
        private readonly Dictionary<string, Type> _channelTypeMap = new();
#if UNITY_EDITOR
        public Dictionary<string, Action> DebugChannels => _channelDictionary;
        public Dictionary<string, Action<object>> DebugTypedChannels => _typedChannelDictionary;
        public Dictionary<string, Type> DebugChannelTypes => _channelTypeMap;
#endif

        // 중복 구독 방지용 트래킹
        private readonly Dictionary<string, HashSet<Delegate>> _listenerTracking = new();

        // 제네릭 리스너(T)와 내부 래퍼(object) 매핑 관리
        private readonly Dictionary<Delegate, Action<object>> _decoderMap = new();

        // 메인 스레드 실행 큐
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        private static RadioStation _instance;
        private static readonly object Lock = new();
        private static int _mainThreadId;

        public static RadioStation Instance
        {
            get
            {
                lock (Lock)
                {
                    if (_instance != null) return _instance;

                    _instance = FindAnyObjectByType<RadioStation>();
                    if (_instance == null)
                    {
                        if (System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId)
                        {
                            var go = new GameObject("@RadioStation");
                            _instance = go.AddComponent<RadioStation>();
                            DontDestroyOnLoad(go);
                        }
                        else
                        {
                            Debug.LogError("[RadioStation] 초기화 실패: 메인 스레드에서 미리 생성되어야 합니다.");
                        }
                    }
                    else
                    {
                        _instance.Init();
                    }

                    return _instance;
                }
            }
        }

        private void Awake()
        {
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            lock (Lock)
            {
                if (_instance == null)
                {
                    _instance = this;
                    Init();
                    DontDestroyOnLoad(gameObject);
                }
                else if (_instance != this)
                {
                    Destroy(gameObject);
                }
            }
        }

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                action.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                ClearAllSubscriptions();
                _instance = null;
            }
        }

        private void Init()
        {
        }

        /// <summary>
        /// 모든 구독을 취소하고 상태를 초기화합니다.
        /// </summary>
        private void ClearAllSubscriptions()
        {
            lock (Lock)
            {
                _channelDictionary.Clear();
                _typedChannelDictionary.Clear();
                _listenerTracking.Clear();
                _decoderMap.Clear();
            }
        }

        // ------------------------------------------------------------------------
        // Helpers (Internal)
        // ------------------------------------------------------------------------
        private static bool IsSubscribed(string channel, Delegate listener)
        {
            if (Instance._listenerTracking.TryGetValue(channel, out var listeners)) return listeners.Contains(listener);
            return false;
        }

        private static void TrackListener(string channel, Delegate listener)
        {
            if (!Instance._listenerTracking.ContainsKey(channel))
                Instance._listenerTracking[channel] = new HashSet<Delegate>();
            Instance._listenerTracking[channel].Add(listener);
        }

        private static void UntrackListener(string channel, Delegate listener)
        {
            if (!Instance._listenerTracking.TryGetValue(channel, out var listeners)) return;
            listeners.Remove(listener);
            if (listeners.Count == 0) Instance._listenerTracking.Remove(channel);
        }

        // ------------------------------------------------------------------------
        // API: Subscribe (구독)
        // ------------------------------------------------------------------------

        /// <summary>
        /// 특정 채널을 구독합니다. (데이터 없음)
        /// </summary>
        public static void Subscribe(string channel, Action listener)
        {
            lock (Lock)
            {
                if (IsSubscribed(channel, listener)) return;
                if (!Instance._channelDictionary.ContainsKey(channel))
                    Instance._channelDictionary[channel] = delegate { };

                Instance._channelDictionary[channel] += listener;
                TrackListener(channel, listener);
            }
        }

        /// <summary>
        /// 특정 채널의 데이터(object)를 구독합니다.
        /// </summary>
        public static void Subscribe(string channel, Action<object> listener)
        {
            lock (Lock)
            {
                if (IsSubscribed(channel, listener)) return;
                if (!Instance._typedChannelDictionary.ContainsKey(channel))
                    Instance._typedChannelDictionary[channel] = delegate { };

                Instance._typedChannelDictionary[channel] += listener;
                TrackListener(channel, listener);
            }
        }

        /// <summary>
        /// 특정 채널의 데이터를 제네릭 타입 T로 변환하여 구독합니다.
        /// </summary>
        public static void Subscribe<T>(string channel, Action<T> listener)
        {
            lock (Lock)
            {
                if (IsSubscribed(channel, listener)) return;

                Instance._channelTypeMap[channel] = typeof(T);

                Action<object> decoder = obj =>
                {
                    try
                    {
                        T decodedData = default;

#if USE_NEWTONSOFT_JSON
                        decodedData = obj switch
                        {
                            null => default,
                            T directValue => directValue,
                            JToken jToken => jToken.ToObject<T>(),
                            string jsonString => JsonConvert.DeserializeObject<T>(jsonString),
                            _ => JToken.FromObject(obj).ToObject<T>()
                        };
#else
                        if (obj is T directValue) decodedData = directValue;
                        else if (obj != null)
                        {
                            Debug.LogWarning($"[RadioStation] Newtonsoft.Json이 없어 '{channel}' 데이터 변환을 제한적으로 수행합니다.");
                            if (obj is string jsonStr) decodedData = JsonUtility.FromJson<T>(jsonStr);
                        }
#endif

                        listener(decodedData);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[RadioStation] 수신 데이터 변환 실패 ({channel}): {e.Message}");
                    }
                };

                // 원본 리스너로 래퍼를 찾을 수 있게 맵에 저장
                Instance._decoderMap[listener] = decoder;

                if (!Instance._typedChannelDictionary.ContainsKey(channel))
                    Instance._typedChannelDictionary[channel] = delegate { };
                Instance._typedChannelDictionary[channel] += decoder;

                TrackListener(channel, listener);
            }
        }

        // ------------------------------------------------------------------------
        // API: Unsubscribe (구독 취소)
        // ------------------------------------------------------------------------

        public static void Unsubscribe(string channel, Action listener)
        {
            lock (Lock)
            {
                if (_instance == null) return;
                if (!Instance._channelDictionary.ContainsKey(channel)) return;
                Instance._channelDictionary[channel] -= listener;
                UntrackListener(channel, listener);
            }
        }

        public static void Unsubscribe(string channel, Action<object> listener)
        {
            lock (Lock)
            {
                if (_instance == null) return;
                if (!Instance._typedChannelDictionary.ContainsKey(channel)) return;
                Instance._typedChannelDictionary[channel] -= listener;
                UntrackListener(channel, listener);
            }
        }

        public static void Unsubscribe<T>(string channel, Action<T> listener)
        {
            lock (Lock)
            {
                if (_instance == null) return;

                if (!Instance._decoderMap.TryGetValue(listener, out var decoder)) return;
                if (Instance._typedChannelDictionary.ContainsKey(channel))
                {
                    Instance._typedChannelDictionary[channel] -= decoder;
                }

                Instance._decoderMap.Remove(listener);
                UntrackListener(channel, listener);
            }
        }

        // ------------------------------------------------------------------------
        // API: Send (송출)
        // ------------------------------------------------------------------------

        /// <summary>
        /// 해당 채널로 신호를 보냅니다.
        /// </summary>
        public static void Send(string channel)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId) Broadcast(channel);
            else Instance._mainThreadQueue.Enqueue(() => Broadcast(channel));
        }

        /// <summary>
        /// 해당 채널로 데이터(Payload)를 보냅니다.
        /// </summary>
        public static void Send(string channel, object payload)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId)
                BroadcastPayload(channel, payload);
            else Instance._mainThreadQueue.Enqueue(() => BroadcastPayload(channel, payload));
        }

        public static void SendPacket(string rawJson)
        {
#if USE_NEWTONSOFT_JSON
            try
            {
                var packet = JsonConvert.DeserializeObject<StationPacket>(rawJson);
                if (string.IsNullOrEmpty(packet.Channel)) return;
                
                if (packet.Payload != null) Send(packet.Channel, packet.Payload);
                else Send(packet.Channel);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RadioStation] 패킷 송출 오류: {e.Message}");
            }
#else
            Debug.LogError("[RadioStation] SendPacket 기능을 사용하려면 Newtonsoft.Json 패키지가 필요합니다.");
#endif
        }

        // ------------------------------------------------------------------------
        // Broadcast Logic (Internal)
        // ------------------------------------------------------------------------

        private static void Broadcast(string channel)
        {
            Action action = null;
            lock (Lock)
            {
                if (Instance._channelDictionary.TryGetValue(channel, out var a)) action = a;
            }

            action?.Invoke();
        }

        private static void BroadcastPayload(string channel, object payload)
        {
            Action<object> action = null;
            lock (Lock)
            {
                if (Instance._typedChannelDictionary.TryGetValue(channel, out var a)) action = a;
            }

            action?.Invoke(payload);
        }

        /// <summary>
        /// 모든 채널의 구독을 강제로 끊습니다.
        /// </summary>
        public static void UnsubscribeAll()
        {
            if (_instance != null) _instance.ClearAllSubscriptions();
        }

#if USE_UNITASK
        public static async UniTask SendAsync(string channel)
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
            Broadcast(channel);
        }

        public static async UniTask SendAsync(string channel, object payload)
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
            BroadcastPayload(channel, payload);
        }
#endif
    }
}