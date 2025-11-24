#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

namespace work.ctrl3d.Editor
{
    [CustomEditor(typeof(RadioStation))]
    public class RadioStationEditor : UnityEditor.Editor
    {
        private RadioStation _target;
        private string _testChannelName = "GameStart";
        private string _testPayloadJson = "{\"id\": 1}";
        private bool _showChannels = true;
        private bool _showTypedChannels = true;

        private void OnEnable()
        {
            _target = (RadioStation)target;
        }

        public override void OnInspectorGUI()
        {
            // 스타일 설정
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                margin = new RectOffset(0, 0, 10, 10),
                normal = { textColor = new Color(0.3f, 0.8f, 1f) }
            };

            var boxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 10, 10)
            };

            // --- 1. 헤더 ---
            GUILayout.Label("📡 Radio Station", headerStyle);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("실시간 모니터링은 플레이 모드에서만 가능합니다.", MessageType.Info);
                return;
            }

            // --- 2. 테스트 송출 패널 ---
            EditorGUILayout.BeginVertical(boxStyle);
            GUILayout.Label("Test Broadcast", EditorStyles.boldLabel);

            // [입력 영역] ------------------------------------------
            
            // 1. 채널 이름 입력
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Channel:", GUILayout.Width(60));
            _testChannelName = EditorGUILayout.TextField(_testChannelName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // 2. JSON 입력 (여러 줄)
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("JSON:", GUILayout.Width(60));
            _testPayloadJson = EditorGUILayout.TextArea(_testPayloadJson, GUILayout.Height(60));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10); // 입력부와 버튼 사이 간격

            // [실행 영역] ------------------------------------------
            
            EditorGUILayout.BeginHorizontal();
            
            // 버튼 1: 단순 신호 (왼쪽)
            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f); // 연한 초록
            if (GUILayout.Button("⚡ Signal Only", GUILayout.Height(30)))
            {
                RadioStation.Send(_testChannelName);
                Debug.Log($"[Editor] '{_testChannelName}' 신호 송출함 (Payload 없음)");
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(5); // 버튼 사이 간격

            // 버튼 2: 데이터 포함 전송 (오른쪽)
            if (GUILayout.Button("📦 Signal + Payload", GUILayout.Height(30)))
            {
                string cleanJson = _testPayloadJson.Trim();
                if (string.IsNullOrEmpty(cleanJson)) cleanJson = "{}";

                // 패킷 조립
                string packet = $"{{\"Channel\":\"{_testChannelName}\", \"Payload\":{cleanJson}}}";
                RadioStation.SendPacket(packet);
                Debug.Log($"[Editor] '{_testChannelName}' 패킷 송출함:\n{cleanJson}");
            }
            
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // --- 3. 실시간 채널 현황 ---
            DrawChannelList("Active Channels (Signal Only)", _target.DebugChannels, ref _showChannels);
            EditorGUILayout.Space(5);
            DrawChannelList("Active Channels (Data Payload)", _target.DebugTypedChannels, ref _showTypedChannels);

            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawChannelList<T>(string title, System.Collections.Generic.Dictionary<string, T> dict,
            ref bool foldout)
        {
            var count = dict.Count;
            var titleText = $"{title} [{count}]";

            foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, titleText);
            if (foldout)
            {
                if (count == 0)
                {
                    EditorGUILayout.LabelField("  (No active listeners)", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUI.indentLevel++;
                    foreach (var kvp in dict)
                    {
                        var listeners = kvp.Value as System.Delegate;
                        var listenerCount = listeners?.GetInvocationList().Length ?? 0;

                        EditorGUILayout.BeginHorizontal();
                        
                        if (GUILayout.Button($"📻 {kvp.Key}", EditorStyles.label, GUILayout.Height(20)))
                        {
                            _testChannelName = kvp.Key; // 1. 채널명 복사
                        
                            // 2. 해당 채널의 타입 정보를 찾아서 샘플 JSON 생성
                            if (_target.DebugChannelTypes.TryGetValue(kvp.Key, out System.Type payloadType))
                            {
                                _testPayloadJson = GenerateSampleJson(payloadType);
                                Debug.Log($"[Editor] 채널 '{kvp.Key}'의 타입({payloadType.Name})으로 JSON 생성함");
                            }
                            else
                            {
                                // 타입 정보가 없으면(Non-generic) 빈 JSON
                                _testPayloadJson = "{}";
                            }
                        
                            GUI.FocusControl(null); // 포커스 해제
                        }

                        // 리스너 수 표시
                        var originalColor = GUI.color;
                        GUI.color = Color.green;
                        // 버튼 옆에 리스너 수를 표시하기 위해 유연한 공간 사용
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.LabelField($"Listeners: {listenerCount}", EditorStyles.miniLabel,
                            GUILayout.Width(80));
                        GUI.color = originalColor;

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }
        
        /// <summary>
        /// 특정 타입의 인스턴스를 생성하여 JSON 문자열로 반환합니다.
        /// </summary>
        private string GenerateSampleJson(System.Type type)
        {
            try
            {
                // 1. 문자열인 경우
                if (type == typeof(string)) return "\"Sample Text\"";
                
                // 2. 원시 타입(int, float 등)인 경우
                if (type.IsPrimitive) return System.Activator.CreateInstance(type).ToString();

                // 3. 클래스/구조체인 경우 (생성자 호출 시도)
                object instance = null;
                try
                {
                    // 파라미터 없는 생성자로 인스턴스 생성 시도
                    instance = System.Activator.CreateInstance(type);
                }
                catch
                {
                    // 생성 실패 시(생성자가 없거나 private 등) null 처리 -> 빈 JSON이 됨
                }

                // Newtonsoft.Json을 이용해 이쁘게(Formatting.Indented) 직렬화
                return JsonConvert.SerializeObject(instance, Formatting.Indented);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"JSON 생성 실패: {e.Message}");
                return "{}";
            }
        }
        
    }
}
#endif