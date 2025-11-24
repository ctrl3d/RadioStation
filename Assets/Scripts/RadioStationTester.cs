using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using work.ctrl3d;

public class RadioStationTester : MonoBehaviour
{
    // 구독 해제 테스트를 위해 델리게이트를 변수로 저장
    private Action<UserProfile> _onProfileUpdate;

    private void Start()
    {
        Debug.Log("<color=cyan>=== RadioStation 테스트 시작 ===</color>");

        TestBasicSubscription();
        TestTypedSubscription();
        TestJsonPacket();
        TestUnsubscribe();
        TestMultithreading();
    }

    // 1. 기본 이벤트 (데이터 없음) 테스트
    private void TestBasicSubscription()
    {
        var channel = "GameStart";
        
        // 구독
        RadioStation.Subscribe(channel, OnGameStart);
        
        // 송출
        Debug.Log($"[Test 1] '{channel}' 채널 송출");
        RadioStation.Send(channel);
    }

    private void OnGameStart()
    {
        Debug.Log("<color=green> -> [Pass] GameStart 수신 성공!</color>");
        // 테스트 후 바로 해제
        RadioStation.Unsubscribe("GameStart", OnGameStart);
    }

    // 2. 제네릭 타입 데이터 전송 테스트
    private void TestTypedSubscription()
    {
        var channel = "UpdateProfile";
        
        // 리스너 정의 (변수에 저장)
        _onProfileUpdate = profile =>
        {
            Debug.Log($"<color=green> -> [Pass] 프로필 수신: {profile.username} (Lv.{profile.level})</color>");
        };

        RadioStation.Subscribe(channel, _onProfileUpdate);

        var myData = new UserProfile 
        { 
            username = "TestPlayer", 
            level = 10, 
            items = new List<string> { "Sword" } 
        };

        Debug.Log($"[Test 2] '{channel}' 채널로 객체 송출");
        RadioStation.Send(channel, myData);
    }

    // 3. JSON 패킷 송신 및 자동 변환 테스트
    private void TestJsonPacket()
    {
        // 서버에서 이런 JSON이 왔다고 가정
        // 주의: Payload 내부가 UserProfile 구조와 일치해야 함
        var jsonPacket = @"
        {
            'Channel': 'UpdateProfile',
            'Payload': {
                'username': 'JsonUser',
                'level': 99,
                'items': ['Gold', 'Gem']
            }
        }";

        Debug.Log($"[Test 3] JSON 패킷 송출 (이미 구독 중인 UpdateProfile 채널)");
        RadioStation.SendPacket(jsonPacket);
    }

    // 4. 구독 취소 테스트
    private void TestUnsubscribe()
    {
        var channel = "UpdateProfile";

        Debug.Log($"[Test 4] '{channel}' 구독 취소 시도");
        
        // 저장해둔 델리게이트로 구독 취소
        RadioStation.Unsubscribe(channel, _onProfileUpdate);

        // 다시 보내봄 (로그가 안 찍혀야 정상)
        RadioStation.Send(channel, new UserProfile { username = "Ghost", level = 0 });
        
        Debug.Log("<color=yellow> -> [Check] 위에서 'Ghost' 유저 로그가 안 찍혔다면 성공입니다.</color>");
    }

    // 5. 멀티스레드 안정성 테스트
    private void TestMultithreading()
    {
        var channel = "BackgroundSignal";

        // 메인 스레드에서 구독
        RadioStation.Subscribe<string>(channel, (msg) =>
        {
            Debug.Log($"<color=green> -> [Pass] 백그라운드 신호 수신: {msg} (Frame: {Time.frameCount})</color>");
            // Unity API 사용 가능 확인
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ThreadCube";
            Destroy(cube, 0.1f);
        });

        Debug.Log("[Test 5] 백그라운드 스레드 작업 시작...");

        // 별도 스레드 실행
        Task.Run(async () =>
        {
            await Task.Delay(1000); // 1초 대기
            
            // 백그라운드에서 Send 호출
            await RadioStation.SendAsync(channel, "Hello from Thread!");
        });
    }
    
    // ContextMenu를 통해 에디터에서 버튼으로 전체 초기화 테스트 가능
    [ContextMenu("Test Unsubscribe All")]
    public void TestClearAll()
    {
        RadioStation.UnsubscribeAll();
        Debug.Log("모든 채널 초기화 완료");
    }
}