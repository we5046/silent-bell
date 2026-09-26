# M1: 로비 서버 구현 계획

**Goal:** TCP로 접속한 클라이언트가 로그인하고, 방을 만들거나 방 코드로 참가하고, 클래스를 골라 준비한 뒤 방장이 게임을 시작할 수 있는 C# 로비 서버와, 이를 자동으로 검증하는 봇을 만든다.

**Architecture:** `proto/packets.proto`에서 Protobuf C# 코드를 생성해 `shared/`(.NET Standard 2.1)에 두고, 패킷 조립(길이 헤더)과 패킷 ID 표도 `shared/`에 둔다. 서버는 세션마다 async 수신 루프와 송신 큐를 두고, 받은 패킷을 로비 잡 큐에 넣는다. 로비 규칙(`LobbyService`)은 잡 큐 한 흐름에서만 실행되므로 락이 없고, 소켓 없이 단위 테스트한다.

**Tech Stack:** .NET 10 SDK(10.0.400), C#, `System.Net.Sockets`, `System.Threading.Channels`, Google.Protobuf 3.36.2, Grpc.Tools 2.84.0(protoc 코드 생성만), xUnit 2.9.3

**설계 문서:** `docs/specs/2026-09-26-coop-dungeon-design.md` (2절, 2-1절, 4-1절, 4-2절, 4-3절, 5절)

## Global Constraints

- 모든 명령은 저장소 루트 `D:\pixel\silent-bell`에서 실행한다.
- `shared/`는 `netstandard2.1`, `LangVersion 9.0`이다. M2에서 Unity가 같은 소스를 컴파일하기 때문이다. 따라서 `shared/`에서는 **블록 namespace(`namespace X { }`)만** 쓰고, file-scoped namespace, `global using`, `record`, `init` 접근자를 쓰지 않는다.
- `server/`, `server.tests/`, `tools/bot/`은 `net10.0`이다.
- 빌드 산출물은 루트의 `Directory.Build.props`가 지정한 `artifacts/` 아래에만 생긴다. `shared/` 안에 bin/obj가 생기면 안 된다.
- 패킷 형식: `[길이 u16 LE, 헤더 포함][패킷 ID u16 LE][Protobuf 본문]`. 길이 최소 4, 최대 4096.
- 닉네임은 앞뒤 공백을 제거한 뒤 2~12자이고, 접속 중인 닉네임과 같으면 거절한다.
- 방 코드는 `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`에서 뽑은 4글자다. 참가할 때는 대소문자를 구분하지 않는다.
- 방 최대 인원은 4명이다. 시작 조건은 2명 이상, 전원 클래스 선택과 준비 완료, 방장만 가능이다.
- 준비 상태에서는 클래스·성별을 바꿀 수 없다. 준비 상태에서 온 `C_PickClass`는 `ErrorCode.AlreadyReady`(proto 값 15)로 거절한다. (Task 4 리뷰 후 사용자 결정, 2026-09-26)
- M1의 proto에는 로비 패킷만 넣는다. 게임 패킷(`C_Input`, `S_Snapshot`, `S_Event`, `S_Result`)은 M2 이후에 추가한다.

## 파일 구조

| 파일 | 책임 |
|---|---|
| `Directory.Build.props` | 모든 프로젝트의 산출물을 `artifacts/`로 보냄 |
| `.gitignore` | `artifacts/` 등 제외 |
| `SilentBell.slnx` | 솔루션 |
| `proto/packets.proto` | 패킷 정의 단일 원본 |
| `shared/Shared.csproj` | Protobuf 코드 생성과 공유 코드 |
| `shared/Generated/Packets.cs` | protoc 생성물 (커밋함) |
| `shared/Net/PacketId.cs` | 패킷 ID enum |
| `shared/Net/PacketRegistry.cs` | ID ↔ 메시지 타입, 파싱 |
| `shared/Net/PacketCodec.cs` | 메시지 → 헤더가 붙은 바이트 배열 |
| `shared/Net/PacketAssembler.cs` | 수신 바이트 → 완성된 패킷 |
| `server/Server.csproj` | 서버 (Task 3에서는 라이브러리, Task 5에서 실행 파일로 전환) |
| `server/Lobby/Room.cs` | 로비 상태 자료형 (Player, Room, Member) |
| `server/Lobby/LobbyService.cs` | 로비와 대기실 규칙 |
| `server/Net/JobQueue.cs` | 잡 큐 |
| `server/Net/Session.cs` | 연결 하나의 수신 루프와 송신 큐 |
| `server/Net/GameServer.cs` | Accept 루프, 세션 목록, 로비 연결 |
| `server/Program.cs` | 진입점 |
| `tools/bot/Bot.csproj` | 봇 (Task 5에서는 라이브러리, Task 6에서 실행 파일로 전환) |
| `tools/bot/BotConnection.cs` | 테스트와 봇이 쓰는 최소 클라이언트 |
| `tools/bot/BotScenario.cs` | 봇 N개로 방 생성부터 게임 시작까지 진행 |
| `tools/bot/Program.cs` | 봇 진입점 |
| `server.tests/Server.Tests.csproj` | xUnit 테스트 |
| `server.tests/ProtocolTests.cs` | 코드 생성 확인 |
| `server.tests/PacketTests.cs` | 코덱, 조립기, 레지스트리 |
| `server.tests/LobbyServiceTests.cs` | 로비 규칙 |
| `server.tests/JobQueueTests.cs` | 잡 큐 |
| `server.tests/GameServerTests.cs` | 실제 TCP로 서버와 봇 통합 테스트 |

---

### Task 1: 저장소 뼈대, proto, 코드 생성, 테스트 프로젝트

**Files:**
- Create: `Directory.Build.props`, `.gitignore`, `SilentBell.slnx`
- Create: `proto/packets.proto`
- Create: `shared/Shared.csproj`, `shared/Generated/Packets.cs` (생성됨)
- Create: `server.tests/Server.Tests.csproj`, `server.tests/ProtocolTests.cs`

**Interfaces:**
- Produces: 네임스페이스 `SilentBell.Protocol`의 메시지 클래스 `C_Login`, `S_LoginResult`, `C_CreateRoom`, `C_JoinRoom`, `Slot`, `S_RoomState`, `S_Error`, `C_PickClass`, `C_Ready`, `C_StartGame`, `C_LeaveRoom`, `S_GameStart`와 enum `ClassType { None, Warrior, Mage, Archer, Bard }`, `Gender { Male, Female }`, `ErrorCode { None, NicknameInvalid, NicknameTaken, NotLoggedIn, AlreadyLoggedIn, RoomNotFound, RoomFull, RoomInGame, AlreadyInRoom, NotInRoom, InvalidRequest, ClassTaken, ClassNotPicked, NotHost, StartConditionNotMet }`
- 생성되는 C# 속성 이름: `C_Login.Nickname`, `S_LoginResult.Ok/PlayerId/Error`, `C_JoinRoom.Code`, `Slot.PlayerId/Nickname/ClassType/Gender/Ready`, `S_RoomState.Code/HostId/Slots`, `S_Error.Code`, `C_PickClass.ClassType/Gender`, `C_Ready.Ready`

- [ ] **Step 1: 루트 설정 파일 작성**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
  </PropertyGroup>
</Project>
```

`.gitignore`:

```
artifacts/
.vs/
*.user
```

- [ ] **Step 2: proto 작성**

`proto/packets.proto`:

```proto
syntax = "proto3";
option csharp_namespace = "SilentBell.Protocol";

enum ClassType {
  CLASS_TYPE_NONE = 0;
  CLASS_TYPE_WARRIOR = 1;
  CLASS_TYPE_MAGE = 2;
  CLASS_TYPE_ARCHER = 3;
  CLASS_TYPE_BARD = 4;
}

enum Gender {
  GENDER_MALE = 0;
  GENDER_FEMALE = 1;
}

enum ErrorCode {
  ERROR_CODE_NONE = 0;
  ERROR_CODE_NICKNAME_INVALID = 1;
  ERROR_CODE_NICKNAME_TAKEN = 2;
  ERROR_CODE_NOT_LOGGED_IN = 3;
  ERROR_CODE_ALREADY_LOGGED_IN = 4;
  ERROR_CODE_ROOM_NOT_FOUND = 5;
  ERROR_CODE_ROOM_FULL = 6;
  ERROR_CODE_ROOM_IN_GAME = 7;
  ERROR_CODE_ALREADY_IN_ROOM = 8;
  ERROR_CODE_NOT_IN_ROOM = 9;
  ERROR_CODE_INVALID_REQUEST = 10;
  ERROR_CODE_CLASS_TAKEN = 11;
  ERROR_CODE_CLASS_NOT_PICKED = 12;
  ERROR_CODE_NOT_HOST = 13;
  ERROR_CODE_START_CONDITION_NOT_MET = 14;
}

message C_Login { string nickname = 1; }
message S_LoginResult { bool ok = 1; int32 player_id = 2; ErrorCode error = 3; }

message C_CreateRoom {}
message C_JoinRoom { string code = 1; }
message C_LeaveRoom {}

message Slot {
  int32 player_id = 1;
  string nickname = 2;
  ClassType class_type = 3;
  Gender gender = 4;
  bool ready = 5;
}

// code가 빈 문자열이면 "방에 없음"을 뜻한다 (방에서 나간 직후 받음)
message S_RoomState { string code = 1; int32 host_id = 2; repeated Slot slots = 3; }
message S_Error { ErrorCode code = 1; }

message C_PickClass { ClassType class_type = 1; Gender gender = 2; }
message C_Ready { bool ready = 1; }
message C_StartGame {}
message S_GameStart {}
```

- [ ] **Step 3: shared 프로젝트 작성**

`shared/Shared.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <RootNamespace>SilentBell.Shared</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Google.Protobuf" Version="3.36.2" />
    <PackageReference Include="Grpc.Tools" Version="2.84.0" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <!-- 생성 파일을 shared/Generated에 두고 커밋한다. Unity(M2)가 같은 소스를 쓰기 때문이다. -->
    <Protobuf Include="../proto/packets.proto" OutputDir="Generated" CompileOutputs="false" GrpcServices="None" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: shared 빌드로 코드 생성 확인**

Run: `dotnet build shared`
Expected: `빌드했습니다.` (또는 `Build succeeded.`), 오류 0개. `shared/Generated/Packets.cs`가 생기고, `shared/` 안에 `bin`, `obj` 폴더가 **없어야** 한다.

Run: `ls shared`
Expected: `Generated  Shared.csproj`

- [ ] **Step 5: 테스트 프로젝트 생성**

```bash
dotnet new xunit -n Server.Tests -o server.tests -f net10.0
rm server.tests/UnitTest1.cs
dotnet add server.tests reference shared/Shared.csproj
dotnet new sln -n SilentBell
dotnet sln SilentBell.slnx add shared/Shared.csproj server.tests/Server.Tests.csproj
```

- [ ] **Step 6: 코드 생성 확인 테스트 작성**

`server.tests/ProtocolTests.cs`:

```csharp
using Google.Protobuf;
using SilentBell.Protocol;

namespace SilentBell.Server.Tests;

public class ProtocolTests
{
    [Fact]
    public void Generated_message_round_trips()
    {
        var bytes = new C_Login { Nickname = "로엔" }.ToByteArray();
        Assert.Equal("로엔", C_Login.Parser.ParseFrom(bytes).Nickname);
    }

    [Fact]
    public void Enum_prefix_is_stripped()
    {
        Assert.Equal(1, (int)ClassType.Warrior);
        Assert.Equal(14, (int)ErrorCode.StartConditionNotMet);
    }
}
```

- [ ] **Step 7: 테스트 실행**

Run: `dotnet test server.tests`
Expected: 통과 2, 실패 0

- [ ] **Step 8: 커밋**

```bash
git add Directory.Build.props .gitignore SilentBell.slnx proto shared server.tests
git commit -m "chore: scaffold solution with protobuf codegen and test project"
```

---

### Task 2: 패킷 ID, 레지스트리, 코덱, 조립기

**Files:**
- Create: `shared/Net/PacketId.cs`, `shared/Net/PacketRegistry.cs`, `shared/Net/PacketCodec.cs`, `shared/Net/PacketAssembler.cs`
- Test: `server.tests/PacketTests.cs`

**Interfaces:**
- Consumes: Task 1의 `SilentBell.Protocol` 메시지 클래스
- Produces (네임스페이스 `SilentBell.Shared.Net`):
  - `enum PacketId : ushort { C_Login = 1, S_LoginResult = 2, C_CreateRoom = 3, C_JoinRoom = 4, S_RoomState = 5, S_Error = 6, C_PickClass = 7, C_Ready = 8, C_StartGame = 9, C_LeaveRoom = 10, S_GameStart = 11 }`
  - `static class PacketRegistry { static PacketId IdOf(IMessage message); static bool TryParse(PacketId id, byte[] body, out IMessage message); }`
  - `static class PacketCodec { const int HeaderSize = 4; const int MaxPacketSize = 4096; static byte[] Encode(IMessage message); }` (최대 크기를 넘으면 `InvalidOperationException`)
  - `sealed class PacketAssembler { void Append(ReadOnlySpan<byte> data); bool TryRead(out PacketId id, out byte[] body); }` (길이가 4 미만이거나 4096 초과면 `InvalidDataException`. 내부 버퍼는 16384바이트이므로 한 번에 넣는 데이터는 8192바이트 이하로 한다)

- [ ] **Step 1: 실패하는 테스트 작성**

`server.tests/PacketTests.cs`:

```csharp
using System.Buffers.Binary;
using SilentBell.Protocol;
using SilentBell.Shared.Net;

namespace SilentBell.Server.Tests;

public class PacketTests
{
    static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void Encode_writes_total_length_and_id_little_endian()
    {
        var packet = PacketCodec.Encode(new C_Login { Nickname = "로엔" });
        Assert.Equal(packet.Length, BinaryPrimitives.ReadUInt16LittleEndian(packet));
        Assert.Equal((ushort)PacketId.C_Login, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)));
    }

    [Fact]
    public void Encode_rejects_packet_over_max_size()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PacketCodec.Encode(new C_Login { Nickname = new string('a', PacketCodec.MaxPacketSize) }));
    }

    [Fact]
    public void Assembler_reads_packet_arriving_one_byte_at_a_time()
    {
        var packet = PacketCodec.Encode(new C_Login { Nickname = "로엔" });
        var assembler = new PacketAssembler();
        for (int i = 0; i < packet.Length - 1; i++)
        {
            assembler.Append(packet.AsSpan(i, 1));
            Assert.False(assembler.TryRead(out _, out _));
        }
        assembler.Append(packet.AsSpan(packet.Length - 1, 1));

        Assert.True(assembler.TryRead(out var id, out var body));
        Assert.True(PacketRegistry.TryParse(id, body, out var message));
        Assert.Equal("로엔", ((C_Login)message).Nickname);
    }

    [Fact]
    public void Assembler_reads_several_packets_from_one_chunk()
    {
        var assembler = new PacketAssembler();
        assembler.Append(Concat(
            PacketCodec.Encode(new C_CreateRoom()),
            PacketCodec.Encode(new C_JoinRoom { Code = "AB23" })));

        Assert.True(assembler.TryRead(out var first, out _));
        Assert.True(assembler.TryRead(out var second, out var body));
        Assert.False(assembler.TryRead(out _, out _));
        Assert.Equal(PacketId.C_CreateRoom, first);
        Assert.Equal(PacketId.C_JoinRoom, second);
        Assert.True(PacketRegistry.TryParse(second, body, out var join));
        Assert.Equal("AB23", ((C_JoinRoom)join).Code);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4097)]
    public void Assembler_rejects_invalid_length(int size)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)size);
        var assembler = new PacketAssembler();
        assembler.Append(header);
        Assert.Throws<InvalidDataException>(() => assembler.TryRead(out _, out _));
    }

    [Fact]
    public void Registry_rejects_unknown_id()
    {
        Assert.False(PacketRegistry.TryParse((PacketId)999, Array.Empty<byte>(), out _));
    }

    [Fact]
    public void Every_packet_id_has_a_parser()
    {
        foreach (var id in Enum.GetValues<PacketId>())
            Assert.True(PacketRegistry.TryParse(id, Array.Empty<byte>(), out _), id.ToString());
    }

    [Fact]
    public void IdOf_matches_registered_id()
    {
        Assert.Equal(PacketId.S_RoomState, PacketRegistry.IdOf(new S_RoomState()));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server.tests --filter "FullyQualifiedName~PacketTests"`
Expected: 빌드 실패, `The type or namespace name 'Net' does not exist in the namespace 'SilentBell.Shared'` 류의 오류

- [ ] **Step 3: PacketId 작성**

`shared/Net/PacketId.cs`:

```csharp
namespace SilentBell.Shared.Net
{
    // 헤더의 패킷 ID. 번호는 한 번 정하면 바꾸지 않는다 (배포된 클라이언트와의 호환).
    public enum PacketId : ushort
    {
        C_Login = 1,
        S_LoginResult = 2,
        C_CreateRoom = 3,
        C_JoinRoom = 4,
        S_RoomState = 5,
        S_Error = 6,
        C_PickClass = 7,
        C_Ready = 8,
        C_StartGame = 9,
        C_LeaveRoom = 10,
        S_GameStart = 11,
    }
}
```

- [ ] **Step 4: PacketRegistry 작성**

`shared/Net/PacketRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using Google.Protobuf;
using SilentBell.Protocol;

namespace SilentBell.Shared.Net
{
    // 패킷 ID와 메시지 타입을 잇는 표. 새 패킷은 PacketId와 여기 두 곳에 추가한다.
    public static class PacketRegistry
    {
        static readonly Dictionary<PacketId, MessageParser> Parsers = new Dictionary<PacketId, MessageParser>();
        static readonly Dictionary<Type, PacketId> Ids = new Dictionary<Type, PacketId>();

        static PacketRegistry()
        {
            Add(PacketId.C_Login, C_Login.Parser);
            Add(PacketId.S_LoginResult, S_LoginResult.Parser);
            Add(PacketId.C_CreateRoom, C_CreateRoom.Parser);
            Add(PacketId.C_JoinRoom, C_JoinRoom.Parser);
            Add(PacketId.S_RoomState, S_RoomState.Parser);
            Add(PacketId.S_Error, S_Error.Parser);
            Add(PacketId.C_PickClass, C_PickClass.Parser);
            Add(PacketId.C_Ready, C_Ready.Parser);
            Add(PacketId.C_StartGame, C_StartGame.Parser);
            Add(PacketId.C_LeaveRoom, C_LeaveRoom.Parser);
            Add(PacketId.S_GameStart, S_GameStart.Parser);
        }

        static void Add<T>(PacketId id, MessageParser<T> parser) where T : IMessage<T>
        {
            Parsers[id] = parser;
            Ids[typeof(T)] = id;
        }

        public static PacketId IdOf(IMessage message)
        {
            return Ids[message.GetType()];
        }

        // 모르는 ID이거나 본문이 깨졌으면 false
        public static bool TryParse(PacketId id, byte[] body, out IMessage message)
        {
            message = null;
            if (!Parsers.TryGetValue(id, out var parser)) return false;
            try
            {
                message = parser.ParseFrom(body);
                return true;
            }
            catch (InvalidProtocolBufferException)
            {
                return false;
            }
        }
    }
}
```

- [ ] **Step 5: PacketCodec 작성**

`shared/Net/PacketCodec.cs`:

```csharp
using System;
using System.Buffers.Binary;
using Google.Protobuf;

namespace SilentBell.Shared.Net
{
    // [길이 u16 LE, 헤더 포함][ID u16 LE][Protobuf 본문]
    public static class PacketCodec
    {
        public const int HeaderSize = 4;
        public const int MaxPacketSize = 4096;

        public static byte[] Encode(IMessage message)
        {
            var id = PacketRegistry.IdOf(message);
            var body = message.ToByteArray();
            int size = HeaderSize + body.Length;
            if (size > MaxPacketSize)
                throw new InvalidOperationException($"packet too large: {id} {size} bytes");

            var packet = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), (ushort)size);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)id);
            Buffer.BlockCopy(body, 0, packet, HeaderSize, body.Length);
            return packet;
        }
    }
}
```

- [ ] **Step 6: PacketAssembler 작성**

`shared/Net/PacketAssembler.cs`:

```csharp
using System;
using System.Buffers.Binary;
using System.IO;

namespace SilentBell.Shared.Net
{
    // TCP는 경계가 없는 스트림이라, 받은 바이트를 모았다가 길이 헤더 기준으로 패킷을 잘라 낸다.
    // 한 번에 Append하는 양은 8192바이트 이하여야 한다 (남은 조각 최대 4095 + 8192 < 16384).
    public sealed class PacketAssembler
    {
        readonly byte[] _buffer = new byte[PacketCodec.MaxPacketSize * 4];
        int _length;

        public void Append(ReadOnlySpan<byte> data)
        {
            if (_length + data.Length > _buffer.Length)
                throw new InvalidDataException("receive buffer overflow");
            data.CopyTo(_buffer.AsSpan(_length));
            _length += data.Length;
        }

        // 완성된 패킷이 있으면 꺼내고 true. 길이 헤더가 규약 밖이면 InvalidDataException.
        public bool TryRead(out PacketId id, out byte[] body)
        {
            id = default;
            body = null;
            if (_length < 2) return false;

            int size = BinaryPrimitives.ReadUInt16LittleEndian(_buffer);
            if (size < PacketCodec.HeaderSize || size > PacketCodec.MaxPacketSize)
                throw new InvalidDataException($"invalid packet size: {size}");
            if (_length < size) return false;

            id = (PacketId)BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(2));
            body = _buffer.AsSpan(PacketCodec.HeaderSize, size - PacketCodec.HeaderSize).ToArray();
            Buffer.BlockCopy(_buffer, size, _buffer, 0, _length - size);
            _length -= size;
            return true;
        }
    }
}
```

- [ ] **Step 7: 테스트 통과 확인**

Run: `dotnet test server.tests --filter "FullyQualifiedName~PacketTests"`
Expected: 통과 9, 실패 0

- [ ] **Step 8: 커밋**

```bash
git add shared/Net server.tests/PacketTests.cs
git commit -m "feat(shared): add packet id registry, codec and stream assembler"
```

---

### Task 3: 로비 규칙 1 — 로그인, 방 생성·참가·나가기, 방장 승계, 연결 끊김

**Files:**
- Create: `server/Server.csproj`, `server/Lobby/Room.cs`, `server/Lobby/LobbyService.cs`
- Modify: `server.tests/Server.Tests.csproj` (server 참조 추가), `SilentBell.slnx`
- Test: `server.tests/LobbyServiceTests.cs`

**Interfaces:**
- Consumes: Task 1의 메시지와 enum
- Produces (네임스페이스 `SilentBell.Server.Lobby`):
  - `sealed class LobbyService(Action<int, IMessage> send, Random rng)` — 생성자 인자 `send(대상 세션 ID, 메시지)`
  - `void Handle(int sessionId, IMessage message)`
  - `void OnDisconnect(int sessionId)`
  - `const int MaxRoomSize = 4`, `const int MinStartPlayers = 2`
  - 플레이어 ID는 세션 ID와 같다.

- [ ] **Step 1: server 프로젝트 생성 (이 단계에서는 라이브러리)**

`server/Server.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>SilentBell.Server</RootNamespace>
    <AssemblyName>silent-bell-server</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\shared\Shared.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet add server.tests reference server/Server.csproj
dotnet sln SilentBell.slnx add server/Server.csproj
```

- [ ] **Step 2: 실패하는 테스트 작성**

`server.tests/LobbyServiceTests.cs`:

```csharp
using Google.Protobuf;
using SilentBell.Protocol;
using SilentBell.Server.Lobby;

namespace SilentBell.Server.Tests;

public class LobbyServiceTests
{
    readonly List<(int To, IMessage Message)> _sent = new();
    readonly LobbyService _lobby;

    public LobbyServiceTests()
    {
        _lobby = new LobbyService((to, message) => _sent.Add((to, message)), new Random(1));
    }

    T Last<T>(int to) where T : IMessage =>
        _sent.Where(s => s.To == to).Select(s => s.Message).OfType<T>().Last();

    void Login(int id, string? nickname = null) =>
        _lobby.Handle(id, new C_Login { Nickname = nickname ?? $"player{id}" });

    string CreateRoom(int hostId)
    {
        Login(hostId);
        _lobby.Handle(hostId, new C_CreateRoom());
        return Last<S_RoomState>(hostId).Code;
    }

    void Join(int id, string code)
    {
        Login(id);
        _lobby.Handle(id, new C_JoinRoom { Code = code });
    }

    [Fact]
    public void Login_succeeds_with_trimmed_nickname()
    {
        Login(1, "  로엔  ");
        var result = Last<S_LoginResult>(1);
        Assert.True(result.Ok);
        Assert.Equal(1, result.PlayerId);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("   ")]
    [InlineData("1234567890123")]
    public void Login_rejects_invalid_nickname_length(string nickname)
    {
        Login(1, nickname);
        var result = Last<S_LoginResult>(1);
        Assert.False(result.Ok);
        Assert.Equal(ErrorCode.NicknameInvalid, result.Error);
    }

    [Fact]
    public void Login_rejects_nickname_in_use()
    {
        Login(1, "로엔");
        Login(2, "로엔");
        Assert.Equal(ErrorCode.NicknameTaken, Last<S_LoginResult>(2).Error);
    }

    [Fact]
    public void Login_twice_returns_error()
    {
        Login(1);
        Login(1);
        Assert.Equal(ErrorCode.AlreadyLoggedIn, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Request_before_login_returns_error()
    {
        _lobby.Handle(1, new C_CreateRoom());
        Assert.Equal(ErrorCode.NotLoggedIn, Last<S_Error>(1).Code);
    }

    [Fact]
    public void CreateRoom_makes_creator_host_with_readable_code()
    {
        var code = CreateRoom(1);
        var state = Last<S_RoomState>(1);
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}$", code);
        Assert.Equal(1, state.HostId);
        Assert.Single(state.Slots);
    }

    [Fact]
    public void CreateRoom_while_in_room_returns_error()
    {
        CreateRoom(1);
        _lobby.Handle(1, new C_CreateRoom());
        Assert.Equal(ErrorCode.AlreadyInRoom, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Join_is_case_insensitive_and_broadcasts_to_every_member()
    {
        var code = CreateRoom(1);
        Join(2, code.ToLowerInvariant());
        Assert.Equal(2, Last<S_RoomState>(1).Slots.Count);
        Assert.Equal(new[] { 1, 2 }, Last<S_RoomState>(2).Slots.Select(s => s.PlayerId));
    }

    [Fact]
    public void Join_unknown_code_returns_error()
    {
        Join(1, "ZZZZ");
        Assert.Equal(ErrorCode.RoomNotFound, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Join_full_room_returns_error()
    {
        var code = CreateRoom(1);
        for (int id = 2; id <= 4; id++) Join(id, code);
        Join(5, code);
        Assert.Equal(ErrorCode.RoomFull, Last<S_Error>(5).Code);
    }

    [Fact]
    public void Host_leaving_passes_host_to_next_member()
    {
        var code = CreateRoom(1);
        Join(2, code);
        Join(3, code);
        _lobby.Handle(1, new C_LeaveRoom());

        var state = Last<S_RoomState>(2);
        Assert.Equal(2, state.HostId);
        Assert.Equal(new[] { 2, 3 }, state.Slots.Select(s => s.PlayerId));
        Assert.Equal("", Last<S_RoomState>(1).Code);
    }

    [Fact]
    public void Last_member_leaving_deletes_room()
    {
        var code = CreateRoom(1);
        _lobby.Handle(1, new C_LeaveRoom());
        Join(2, code);
        Assert.Equal(ErrorCode.RoomNotFound, Last<S_Error>(2).Code);
    }

    [Fact]
    public void Leave_when_not_in_room_returns_error()
    {
        Login(1);
        _lobby.Handle(1, new C_LeaveRoom());
        Assert.Equal(ErrorCode.NotInRoom, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Disconnect_frees_nickname_and_slot()
    {
        var code = CreateRoom(1);
        Join(2, code);
        _lobby.OnDisconnect(1);

        Assert.Equal(new[] { 2 }, Last<S_RoomState>(2).Slots.Select(s => s.PlayerId));
        Login(3, "player1");
        Assert.True(Last<S_LoginResult>(3).Ok);
    }
}
```

- [ ] **Step 3: 실패 확인**

Run: `dotnet test server.tests --filter "FullyQualifiedName~LobbyServiceTests"`
Expected: 빌드 실패, `The type or namespace name 'Lobby' does not exist` 류의 오류

- [ ] **Step 4: 로비 자료형 작성**

`server/Lobby/Room.cs`:

```csharp
using SilentBell.Protocol;

namespace SilentBell.Server.Lobby;

sealed class Player
{
    public required int Id { get; init; }
    public required string Nickname { get; init; }
    public Room? Room { get; set; }
}

sealed class Room
{
    public required string Code { get; init; }
    public int HostId { get; set; }
    public bool InGame { get; set; }
    public List<Member> Members { get; } = new(); // 입장 순서. 방장 승계에 쓴다.
}

sealed class Member
{
    public required int PlayerId { get; init; }
    public required string Nickname { get; init; }
    public ClassType ClassType { get; set; }
    public Gender Gender { get; set; }
    public bool Ready { get; set; }
}
```

- [ ] **Step 5: LobbyService 작성**

`server/Lobby/LobbyService.cs`:

```csharp
using Google.Protobuf;
using SilentBell.Protocol;

namespace SilentBell.Server.Lobby;

// 로비와 대기실 규칙. 로비 잡 큐 한 흐름에서만 호출되므로 락이 없다.
public sealed class LobbyService
{
    public const int MaxRoomSize = 4;
    public const int MinStartPlayers = 2;
    const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 0/O/1/I 제외

    readonly Action<int, IMessage> _send;
    readonly Random _rng;
    readonly Dictionary<int, Player> _players = new();
    readonly Dictionary<string, Room> _rooms = new();

    public LobbyService(Action<int, IMessage> send, Random rng)
    {
        _send = send;
        _rng = rng;
    }

    public void Handle(int sessionId, IMessage message)
    {
        if (message is C_Login login)
        {
            Login(sessionId, login.Nickname);
            return;
        }
        if (!_players.TryGetValue(sessionId, out var player))
        {
            Error(sessionId, ErrorCode.NotLoggedIn);
            return;
        }
        switch (message)
        {
            case C_CreateRoom: CreateRoom(player); break;
            case C_JoinRoom join: JoinRoom(player, join.Code); break;
            case C_LeaveRoom: LeaveRoom(player); break;
        }
    }

    public void OnDisconnect(int sessionId)
    {
        if (!_players.Remove(sessionId, out var player)) return;
        if (player.Room != null) RemoveFromRoom(player);
    }

    void Login(int sessionId, string rawNickname)
    {
        if (_players.ContainsKey(sessionId))
        {
            Error(sessionId, ErrorCode.AlreadyLoggedIn);
            return;
        }
        var nickname = rawNickname.Trim();
        if (nickname.Length < 2 || nickname.Length > 12)
        {
            _send(sessionId, new S_LoginResult { Ok = false, Error = ErrorCode.NicknameInvalid });
            return;
        }
        // ponytail: 접속자 전체 선형 탐색. 동시 접속이 수천 명이 되면 HashSet으로 바꾼다.
        if (_players.Values.Any(p => p.Nickname == nickname))
        {
            _send(sessionId, new S_LoginResult { Ok = false, Error = ErrorCode.NicknameTaken });
            return;
        }
        _players[sessionId] = new Player { Id = sessionId, Nickname = nickname };
        _send(sessionId, new S_LoginResult { Ok = true, PlayerId = sessionId });
    }

    void CreateRoom(Player player)
    {
        if (player.Room != null)
        {
            Error(player.Id, ErrorCode.AlreadyInRoom);
            return;
        }
        var room = new Room { Code = NewCode(), HostId = player.Id };
        _rooms[room.Code] = room;
        AddMember(room, player);
    }

    void JoinRoom(Player player, string rawCode)
    {
        if (player.Room != null)
        {
            Error(player.Id, ErrorCode.AlreadyInRoom);
            return;
        }
        if (!_rooms.TryGetValue(rawCode.Trim().ToUpperInvariant(), out var room))
        {
            Error(player.Id, ErrorCode.RoomNotFound);
            return;
        }
        if (room.InGame)
        {
            Error(player.Id, ErrorCode.RoomInGame);
            return;
        }
        if (room.Members.Count >= MaxRoomSize)
        {
            Error(player.Id, ErrorCode.RoomFull);
            return;
        }
        AddMember(room, player);
    }

    void LeaveRoom(Player player)
    {
        if (player.Room == null)
        {
            Error(player.Id, ErrorCode.NotInRoom);
            return;
        }
        RemoveFromRoom(player);
        _send(player.Id, new S_RoomState()); // 빈 코드 = 방에 없음
    }

    void AddMember(Room room, Player player)
    {
        room.Members.Add(new Member { PlayerId = player.Id, Nickname = player.Nickname });
        player.Room = room;
        Broadcast(room);
    }

    void RemoveFromRoom(Player player)
    {
        var room = player.Room!;
        room.Members.RemoveAll(m => m.PlayerId == player.Id);
        player.Room = null;
        if (room.Members.Count == 0)
        {
            _rooms.Remove(room.Code);
            return;
        }
        if (room.HostId == player.Id) room.HostId = room.Members[0].PlayerId;
        Broadcast(room);
    }

    string NewCode()
    {
        while (true)
        {
            var chars = new char[4];
            for (int i = 0; i < chars.Length; i++) chars[i] = CodeChars[_rng.Next(CodeChars.Length)];
            var code = new string(chars);
            if (!_rooms.ContainsKey(code)) return code;
        }
    }

    void Broadcast(Room room)
    {
        var state = new S_RoomState { Code = room.Code, HostId = room.HostId };
        foreach (var m in room.Members)
        {
            state.Slots.Add(new Slot
            {
                PlayerId = m.PlayerId,
                Nickname = m.Nickname,
                ClassType = m.ClassType,
                Gender = m.Gender,
                Ready = m.Ready,
            });
        }
        // 같은 인스턴스를 여러 번 보내도 된다. Session.Send가 즉시 바이트로 인코딩한다.
        foreach (var m in room.Members) _send(m.PlayerId, state);
    }

    void Error(int to, ErrorCode code) => _send(to, new S_Error { Code = code });
}
```

- [ ] **Step 6: 테스트 통과 확인**

Run: `dotnet test server.tests --filter "FullyQualifiedName~LobbyServiceTests"`
Expected: 통과 16, 실패 0

- [ ] **Step 7: 커밋**

```bash
git add server SilentBell.slnx server.tests/Server.Tests.csproj server.tests/LobbyServiceTests.cs
git commit -m "feat(server): add lobby login, room create/join/leave and host handover"
```

---

### Task 4: 로비 규칙 2 — 클래스 선택, 준비, 게임 시작

**Files:**
- Modify: `server/Lobby/LobbyService.cs` (`Handle`의 switch, 새 메서드 3개)
- Test: `server.tests/LobbyServiceTests.cs` (클래스 끝에 추가)

**Interfaces:**
- Consumes: Task 3의 `LobbyService`, `Room`, `Member`, `Broadcast`, `Error`
- Produces: `C_PickClass`, `C_Ready`, `C_StartGame` 처리. 시작에 성공하면 방의 모든 멤버에게 `S_GameStart`를 보내고 `Room.InGame = true`가 된다.

- [ ] **Step 1: 실패하는 테스트 추가**

`server.tests/LobbyServiceTests.cs`의 `LobbyServiceTests` 클래스 **마지막 `}` 바로 앞**에 추가:

```csharp
    void Pick(int id, ClassType classType, Gender gender = Gender.Male) =>
        _lobby.Handle(id, new C_PickClass { ClassType = classType, Gender = gender });

    void Ready(int id, bool ready = true) =>
        _lobby.Handle(id, new C_Ready { Ready = ready });

    string ReadyRoomOfTwo()
    {
        var code = CreateRoom(1);
        Join(2, code);
        Pick(1, ClassType.Warrior);
        Ready(1);
        Pick(2, ClassType.Bard);
        Ready(2);
        return code;
    }

    [Fact]
    public void PickClass_sets_class_and_gender()
    {
        CreateRoom(1);
        Pick(1, ClassType.Bard, Gender.Female);
        var slot = Last<S_RoomState>(1).Slots.Single();
        Assert.Equal(ClassType.Bard, slot.ClassType);
        Assert.Equal(Gender.Female, slot.Gender);
    }

    [Fact]
    public void PickClass_taken_by_other_member_returns_error()
    {
        var code = CreateRoom(1);
        Join(2, code);
        Pick(1, ClassType.Warrior);
        Pick(2, ClassType.Warrior);
        Assert.Equal(ErrorCode.ClassTaken, Last<S_Error>(2).Code);
    }

    [Fact]
    public void PickClass_none_returns_error()
    {
        CreateRoom(1);
        Pick(1, ClassType.None);
        Assert.Equal(ErrorCode.InvalidRequest, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Changing_class_clears_ready()
    {
        CreateRoom(1);
        Pick(1, ClassType.Mage);
        Ready(1);
        Pick(1, ClassType.Archer);
        Assert.False(Last<S_RoomState>(1).Slots.Single().Ready);
    }

    [Fact]
    public void Ready_without_class_returns_error()
    {
        CreateRoom(1);
        Ready(1);
        Assert.Equal(ErrorCode.ClassNotPicked, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Start_by_non_host_returns_error()
    {
        ReadyRoomOfTwo();
        _lobby.Handle(2, new C_StartGame());
        Assert.Equal(ErrorCode.NotHost, Last<S_Error>(2).Code);
    }

    [Fact]
    public void Start_alone_returns_error()
    {
        CreateRoom(1);
        Pick(1, ClassType.Warrior);
        Ready(1);
        _lobby.Handle(1, new C_StartGame());
        Assert.Equal(ErrorCode.StartConditionNotMet, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Start_with_unready_member_returns_error()
    {
        ReadyRoomOfTwo();
        Ready(2, false);
        _lobby.Handle(1, new C_StartGame());
        Assert.Equal(ErrorCode.StartConditionNotMet, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Start_sends_game_start_to_all_and_locks_room()
    {
        var code = ReadyRoomOfTwo();
        _lobby.Handle(1, new C_StartGame());

        Assert.NotNull(Last<S_GameStart>(1));
        Assert.NotNull(Last<S_GameStart>(2));
        Join(3, code);
        Assert.Equal(ErrorCode.RoomInGame, Last<S_Error>(3).Code);
    }

    [Fact]
    public void Disconnect_releases_class()
    {
        var code = CreateRoom(1);
        Join(2, code);
        Pick(2, ClassType.Archer);
        _lobby.OnDisconnect(2);
        Pick(1, ClassType.Archer);
        Assert.Equal(ClassType.Archer, Last<S_RoomState>(1).Slots.Single().ClassType);
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server.tests --filter "FullyQualifiedName~LobbyServiceTests"`
Expected: 새 테스트 10개 실패 (`Sequence contains no elements` 등), 기존 16개 통과

- [ ] **Step 3: Handle의 switch에 세 경우 추가**

`server/Lobby/LobbyService.cs`의 switch를 다음으로 교체:

```csharp
        switch (message)
        {
            case C_CreateRoom: CreateRoom(player); break;
            case C_JoinRoom join: JoinRoom(player, join.Code); break;
            case C_LeaveRoom: LeaveRoom(player); break;
            case C_PickClass pick: PickClass(player, pick.ClassType, pick.Gender); break;
            case C_Ready ready: SetReady(player, ready.Ready); break;
            case C_StartGame: StartGame(player); break;
        }
```

- [ ] **Step 4: 메서드 추가**

`server/Lobby/LobbyService.cs`에서 `void AddMember(Room room, Player player)` 바로 **앞에** 추가:

```csharp
    void PickClass(Player player, ClassType classType, Gender gender)
    {
        var room = WaitingRoomOf(player);
        if (room == null) return;
        if (classType == ClassType.None || !Enum.IsDefined(classType) || !Enum.IsDefined(gender))
        {
            Error(player.Id, ErrorCode.InvalidRequest);
            return;
        }
        if (room.Members.Any(m => m.PlayerId != player.Id && m.ClassType == classType))
        {
            Error(player.Id, ErrorCode.ClassTaken);
            return;
        }
        var me = MemberOf(room, player);
        me.ClassType = classType;
        me.Gender = gender;
        me.Ready = false; // 클래스를 바꾸면 준비가 풀린다
        Broadcast(room);
    }

    void SetReady(Player player, bool ready)
    {
        var room = WaitingRoomOf(player);
        if (room == null) return;
        var me = MemberOf(room, player);
        if (ready && me.ClassType == ClassType.None)
        {
            Error(player.Id, ErrorCode.ClassNotPicked);
            return;
        }
        me.Ready = ready;
        Broadcast(room);
    }

    void StartGame(Player player)
    {
        var room = WaitingRoomOf(player);
        if (room == null) return;
        if (room.HostId != player.Id)
        {
            Error(player.Id, ErrorCode.NotHost);
            return;
        }
        if (room.Members.Count < MinStartPlayers || room.Members.Any(m => m.ClassType == ClassType.None || !m.Ready))
        {
            Error(player.Id, ErrorCode.StartConditionNotMet);
            return;
        }
        room.InGame = true;
        var start = new S_GameStart();
        foreach (var m in room.Members) _send(m.PlayerId, start);
    }

    // 대기 중인 방에 있으면 그 방을, 아니면 오류를 보내고 null
    Room? WaitingRoomOf(Player player)
    {
        if (player.Room == null)
        {
            Error(player.Id, ErrorCode.NotInRoom);
            return null;
        }
        if (player.Room.InGame)
        {
            Error(player.Id, ErrorCode.RoomInGame);
            return null;
        }
        return player.Room;
    }

    static Member MemberOf(Room room, Player player) => room.Members.First(m => m.PlayerId == player.Id);
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test server.tests --filter "FullyQualifiedName~LobbyServiceTests"`
Expected: 통과 26, 실패 0

- [ ] **Step 6: 커밋**

```bash
git add server/Lobby/LobbyService.cs server.tests/LobbyServiceTests.cs
git commit -m "feat(server): add class pick, ready and game start rules"
```

---

### Task 5: 네트워크 계층 — 잡 큐, 세션, 서버, 진입점

**Files:**
- Create: `server/Net/JobQueue.cs`, `server/Net/Session.cs`, `server/Net/GameServer.cs`, `server/Program.cs`
- Modify: `server/Server.csproj` (`OutputType Exe` 추가)
- Create: `tools/bot/Bot.csproj` (이 단계에서는 라이브러리), `tools/bot/BotConnection.cs`
- Modify: `server.tests/Server.Tests.csproj` (bot 참조 추가), `SilentBell.slnx`
- Test: `server.tests/JobQueueTests.cs`, `server.tests/GameServerTests.cs`

**Interfaces:**
- Consumes: Task 2의 `PacketCodec`, `PacketAssembler`, `PacketRegistry`, Task 3~4의 `LobbyService`
- Produces:
  - `SilentBell.Server.Net.JobQueue { void Push(Action job); Task RunAsync(CancellationToken ct); }`
  - `SilentBell.Server.Net.Session { int Id; void Send(IMessage message); Task RunAsync(Action<Session, IMessage> onPacket, CancellationToken ct); }`
  - `SilentBell.Server.Net.GameServer(int port) { void Start(); int Port; Task RunAsync(CancellationToken ct); }` (port 0이면 빈 포트를 자동 선택하고, `Start()` 뒤 `Port`로 확인)
  - `SilentBell.Bot.BotConnection { static Task<BotConnection> ConnectAsync(string host, int port); Task SendAsync(IMessage message); Task SendRawAsync(byte[] bytes); Task<T> ReceiveAsync<T>(Func<T, bool>? match = null, int timeoutMs = 3000) where T : class, IMessage; void Dispose(); }` (서버가 연결을 끊으면 `IOException`, 시간 초과면 `OperationCanceledException`)

- [ ] **Step 1: bot 프로젝트 생성 (이 단계에서는 라이브러리)**

`tools/bot/Bot.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>SilentBell.Bot</RootNamespace>
    <AssemblyName>silent-bell-bot</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\shared\Shared.csproj" />
  </ItemGroup>
</Project>
```

`tools/bot/BotConnection.cs`:

```csharp
using System.Net.Sockets;
using Google.Protobuf;
using SilentBell.Shared.Net;

namespace SilentBell.Bot;

// 테스트와 봇이 쓰는 최소 클라이언트. 원하는 타입의 패킷이 올 때까지 읽고, 그 사이의 다른 패킷은 버린다.
public sealed class BotConnection : IDisposable
{
    readonly TcpClient _client;
    readonly NetworkStream _stream;
    readonly PacketAssembler _assembler = new();
    readonly byte[] _buffer = new byte[8192];

    BotConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public static async Task<BotConnection> ConnectAsync(string host, int port)
    {
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(host, port);
        return new BotConnection(client);
    }

    public Task SendAsync(IMessage message) => SendRawAsync(PacketCodec.Encode(message));

    public Task SendRawAsync(byte[] bytes) => _stream.WriteAsync(bytes).AsTask();

    public async Task<T> ReceiveAsync<T>(Func<T, bool>? match = null, int timeoutMs = 3000) where T : class, IMessage
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (true)
        {
            while (_assembler.TryRead(out var id, out var body))
            {
                if (PacketRegistry.TryParse(id, body, out var message) && message is T typed && (match == null || match(typed)))
                    return typed;
            }
            int n = await _stream.ReadAsync(_buffer, cts.Token);
            if (n == 0) throw new IOException("server closed the connection");
            _assembler.Append(_buffer.AsSpan(0, n));
        }
    }

    public void Dispose() => _client.Dispose();
}
```

```bash
dotnet add server.tests reference tools/bot/Bot.csproj
dotnet sln SilentBell.slnx add tools/bot/Bot.csproj
```

- [ ] **Step 2: 실패하는 테스트 작성**

`server.tests/JobQueueTests.cs`:

```csharp
using SilentBell.Server.Net;

namespace SilentBell.Server.Tests;

public class JobQueueTests
{
    [Fact]
    public async Task Runs_jobs_in_order_and_survives_exceptions()
    {
        var queue = new JobQueue();
        var log = new List<int>();
        var done = new TaskCompletionSource();
        queue.Push(() => log.Add(1));
        queue.Push(() => throw new InvalidOperationException("boom"));
        queue.Push(() => log.Add(2));
        queue.Push(() => done.SetResult());

        using var cts = new CancellationTokenSource();
        var run = queue.RunAsync(cts.Token);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cts.Cancel();
        await run;

        Assert.Equal(new[] { 1, 2 }, log);
    }
}
```

`server.tests/GameServerTests.cs`:

```csharp
using SilentBell.Bot;
using SilentBell.Protocol;
using SilentBell.Server.Net;

namespace SilentBell.Server.Tests;

public class GameServerTests : IAsyncLifetime
{
    readonly GameServer _server = new(0);
    readonly CancellationTokenSource _cts = new();
    Task _run = Task.CompletedTask;

    public Task InitializeAsync()
    {
        _server.Start();
        _run = _server.RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        await _run;
    }

    [Fact]
    public async Task Clients_login_and_share_a_room_over_tcp()
    {
        using var host = await BotConnection.ConnectAsync("127.0.0.1", _server.Port);
        using var guest = await BotConnection.ConnectAsync("127.0.0.1", _server.Port);

        await host.SendAsync(new C_Login { Nickname = "host" });
        Assert.True((await host.ReceiveAsync<S_LoginResult>()).Ok);
        await guest.SendAsync(new C_Login { Nickname = "guest" });
        Assert.True((await guest.ReceiveAsync<S_LoginResult>()).Ok);

        await host.SendAsync(new C_CreateRoom());
        var code = (await host.ReceiveAsync<S_RoomState>()).Code;
        await guest.SendAsync(new C_JoinRoom { Code = code });

        var state = await host.ReceiveAsync<S_RoomState>(s => s.Slots.Count == 2);
        Assert.Equal(new[] { "host", "guest" }, state.Slots.Select(s => s.Nickname));
    }

    [Fact]
    public async Task Invalid_length_header_closes_connection()
    {
        using var bot = await BotConnection.ConnectAsync("127.0.0.1", _server.Port);
        await bot.SendRawAsync(new byte[] { 0x02, 0x00, 0x01, 0x00 }); // 길이 2 < 최소 4
        await Assert.ThrowsAsync<IOException>(() => bot.ReceiveAsync<S_Error>());
    }
}
```

- [ ] **Step 3: 실패 확인**

Run: `dotnet test server.tests --filter "FullyQualifiedName~JobQueueTests|FullyQualifiedName~GameServerTests"`
Expected: 빌드 실패, `The type or namespace name 'Net' does not exist in the namespace 'SilentBell.Server'` 류의 오류

- [ ] **Step 4: JobQueue 작성**

`server/Net/JobQueue.cs`:

```csharp
using System.Threading.Channels;

namespace SilentBell.Server.Net;

// 여러 스레드가 Push하고, 한 흐름이 넣은 순서대로 하나씩 실행한다.
// 실행되는 쪽(로비, 방)의 로직은 동시에 돌지 않으므로 락이 필요 없다.
public sealed class JobQueue
{
    readonly Channel<Action> _jobs = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });

    public void Push(Action job) => _jobs.Writer.TryWrite(job);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var job in _jobs.Reader.ReadAllAsync(ct))
            {
                try
                {
                    job();
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"[job] {e}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
```

- [ ] **Step 5: Session 작성**

`server/Net/Session.cs`:

```csharp
using System.Net.Sockets;
using System.Threading.Channels;
using Google.Protobuf;
using SilentBell.Shared.Net;

namespace SilentBell.Server.Net;

// 연결 하나. 수신 루프는 패킷을 조립해 onPacket으로 넘기고,
// 송신은 큐를 거쳐 한 번에 하나씩만 진행한다 (여러 스레드가 동시에 Send해도 바이트가 섞이지 않음).
public sealed class Session
{
    const int ReceiveChunkSize = 8192; // PacketAssembler 한 번 Append 상한과 같음

    readonly Socket _socket;
    readonly Channel<byte[]> _sendQueue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    public int Id { get; }

    public Session(int id, Socket socket)
    {
        Id = id;
        _socket = socket;
    }

    // 호출한 스레드에서 바로 인코딩하므로, 같은 메시지 인스턴스를 여러 세션에 보내도 안전하다.
    public void Send(IMessage message) => _sendQueue.Writer.TryWrite(PacketCodec.Encode(message));

    // 연결이 끊기거나 규약 위반이 생기면 반환한다.
    public async Task RunAsync(Action<Session, IMessage> onPacket, CancellationToken ct)
    {
        var sendTask = SendLoopAsync(ct);
        try
        {
            await ReceiveLoopAsync(onPacket, ct);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[session {Id}] closed: {e.GetType().Name} {e.Message}");
        }
        finally
        {
            _sendQueue.Writer.TryComplete();
            _socket.Close();
            try
            {
                await sendTask;
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or OperationCanceledException)
            {
                // 소켓을 방금 닫았으므로 남아 있던 송신이 이 예외들로 끝나는 것은 정상이다
            }
        }
    }

    async Task ReceiveLoopAsync(Action<Session, IMessage> onPacket, CancellationToken ct)
    {
        var assembler = new PacketAssembler();
        var buffer = new byte[ReceiveChunkSize];
        while (true)
        {
            int n = await _socket.ReceiveAsync(buffer, SocketFlags.None, ct);
            if (n == 0) return; // 상대가 정상 종료
            assembler.Append(buffer.AsSpan(0, n));
            while (assembler.TryRead(out var id, out var body))
            {
                if (!PacketRegistry.TryParse(id, body, out var message))
                    throw new InvalidDataException($"unknown or broken packet: {id}");
                onPacket(this, message);
            }
        }
    }

    async Task SendLoopAsync(CancellationToken ct)
    {
        await foreach (var packet in _sendQueue.Reader.ReadAllAsync(ct))
        {
            // TCP 송신은 일부만 나갈 수 있으므로 다 나갈 때까지 반복한다.
            var remaining = packet.AsMemory();
            while (!remaining.IsEmpty)
            {
                int sent = await _socket.SendAsync(remaining, SocketFlags.None, ct);
                remaining = remaining[sent..];
            }
        }
    }
}
```

- [ ] **Step 6: GameServer 작성**

`server/Net/GameServer.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using SilentBell.Server.Lobby;

namespace SilentBell.Server.Net;

// Accept 루프와 세션 목록. 받은 패킷은 전부 로비 잡 큐로 보낸다.
public sealed class GameServer
{
    readonly TcpListener _listener;
    readonly ConcurrentDictionary<int, Session> _sessions = new();
    readonly JobQueue _lobbyQueue = new();
    readonly LobbyService _lobby;
    int _nextSessionId;

    public GameServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _lobby = new LobbyService(SendTo, new Random());
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start() => _listener.Start();

    public async Task RunAsync(CancellationToken ct)
    {
        var lobbyTask = _lobbyQueue.RunAsync(ct);
        try
        {
            while (true)
            {
                var socket = await _listener.AcceptSocketAsync(ct);
                socket.NoDelay = true; // 작은 패킷을 모아 늦게 보내는 Nagle 알고리즘을 끈다
                var session = new Session(Interlocked.Increment(ref _nextSessionId), socket);
                _sessions[session.Id] = session;
                Console.WriteLine($"[session {session.Id}] connected {socket.RemoteEndPoint}");
                _ = RunSessionAsync(session, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _listener.Stop();
            await lobbyTask;
        }
    }

    async Task RunSessionAsync(Session session, CancellationToken ct)
    {
        await session.RunAsync((s, message) => _lobbyQueue.Push(() => _lobby.Handle(s.Id, message)), ct);
        _sessions.TryRemove(session.Id, out _);
        _lobbyQueue.Push(() => _lobby.OnDisconnect(session.Id));
    }

    void SendTo(int sessionId, IMessage message)
    {
        if (_sessions.TryGetValue(sessionId, out var session)) session.Send(message);
    }
}
```

- [ ] **Step 7: 진입점 작성과 실행 파일 전환**

`server/Server.csproj`의 `<PropertyGroup>` 안에 한 줄 추가:

```xml
    <OutputType>Exe</OutputType>
```

`server/Program.cs`:

```csharp
using SilentBell.Server.Net;

int port = args.Length > 0 ? int.Parse(args[0]) : 7777;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new GameServer(port);
server.Start();
Console.WriteLine($"silent-bell server listening on {server.Port}");
await server.RunAsync(cts.Token);
```

- [ ] **Step 8: 테스트 통과 확인**

Run: `dotnet test server.tests`
Expected: 통과 40, 실패 0 (Protocol 2 + Packet 9 + Lobby 26 + JobQueue 1 + GameServer 2)

- [ ] **Step 9: 수동 실행 확인**

Run: `dotnet run --project server`
Expected: `silent-bell server listening on 7777` 출력 후 대기. Ctrl+C로 종료된다.

- [ ] **Step 10: 커밋**

```bash
git add server tools/bot SilentBell.slnx server.tests
git commit -m "feat(server): add job queue, socket sessions and tcp game server"
```

---

### Task 6: 봇 시나리오 — 방 생성부터 게임 시작까지

**Files:**
- Create: `tools/bot/BotScenario.cs`, `tools/bot/Program.cs`
- Modify: `tools/bot/Bot.csproj` (`OutputType Exe` 추가)
- Test: `server.tests/GameServerTests.cs` (테스트 1개 추가)

**Interfaces:**
- Consumes: Task 5의 `BotConnection`, `GameServer`
- Produces: `SilentBell.Bot.BotScenario.RunAsync(string host, int port, int count) : Task<string>` — 봇 `count`명(2~4)이 로그인, 방 생성·참가, 서로 다른 클래스 선택, 준비를 마치고 방장이 게임을 시작한다. 전원이 `S_GameStart`를 받으면 방 코드를 반환하고, 중간에 응답이 없으면 `OperationCanceledException`, 로그인이 실패하면 `InvalidOperationException`을 던진다.
- 실행: `silent-bell-bot [host=127.0.0.1] [port=7777] [count=4]`. 성공 시 종료 코드 0과 `OK: room XXXX started with N bots`, 실패 시 종료 코드 1과 `FAIL: ...`

- [ ] **Step 1: 실패하는 테스트 추가**

`server.tests/GameServerTests.cs`의 `GameServerTests` 클래스 **마지막 `}` 바로 앞**에 추가:

```csharp
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Bot_scenario_starts_game(int count)
    {
        var code = await BotScenario.RunAsync("127.0.0.1", _server.Port, count);
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}$", code);
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test server.tests --filter "FullyQualifiedName~GameServerTests"`
Expected: 빌드 실패, `The name 'BotScenario' does not exist in the current context`

- [ ] **Step 3: BotScenario 작성**

`tools/bot/BotScenario.cs`:

```csharp
using SilentBell.Protocol;

namespace SilentBell.Bot;

public static class BotScenario
{
    static readonly ClassType[] Classes = { ClassType.Warrior, ClassType.Mage, ClassType.Archer, ClassType.Bard };

    // 봇 count명이 방을 만들고 참가해 서로 다른 클래스를 고르고 게임을 시작한다. 방 코드를 반환한다.
    public static async Task<string> RunAsync(string host, int port, int count)
    {
        if (count < 2 || count > Classes.Length) throw new ArgumentOutOfRangeException(nameof(count), "2~4");

        var bots = new List<BotConnection>();
        try
        {
            int tag = Random.Shared.Next(1000, 10000); // 여러 번 실행해도 닉네임이 겹치지 않게
            for (int i = 0; i < count; i++)
            {
                var bot = await BotConnection.ConnectAsync(host, port);
                bots.Add(bot);
                await bot.SendAsync(new C_Login { Nickname = $"bot{i}_{tag}" });
                var login = await bot.ReceiveAsync<S_LoginResult>();
                if (!login.Ok) throw new InvalidOperationException($"bot{i} login failed: {login.Error}");
            }

            await bots[0].SendAsync(new C_CreateRoom());
            var code = (await bots[0].ReceiveAsync<S_RoomState>()).Code;
            for (int i = 1; i < count; i++)
            {
                await bots[i].SendAsync(new C_JoinRoom { Code = code });
                await bots[i].ReceiveAsync<S_RoomState>(s => s.Code == code);
            }

            for (int i = 0; i < count; i++)
            {
                await bots[i].SendAsync(new C_PickClass { ClassType = Classes[i], Gender = i % 2 == 0 ? Gender.Male : Gender.Female });
                await bots[i].SendAsync(new C_Ready { Ready = true });
            }
            await bots[0].ReceiveAsync<S_RoomState>(s => s.Slots.Count == count && s.Slots.All(slot => slot.Ready));

            await bots[0].SendAsync(new C_StartGame());
            foreach (var bot in bots) await bot.ReceiveAsync<S_GameStart>();
            return code;
        }
        finally
        {
            foreach (var bot in bots) bot.Dispose();
        }
    }
}
```

- [ ] **Step 4: 봇 진입점 작성과 실행 파일 전환**

`tools/bot/Bot.csproj`의 `<PropertyGroup>` 안에 한 줄 추가:

```xml
    <OutputType>Exe</OutputType>
```

`tools/bot/Program.cs`:

```csharp
using SilentBell.Bot;

string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 ? int.Parse(args[1]) : 7777;
int count = args.Length > 2 ? int.Parse(args[2]) : 4;

try
{
    var code = await BotScenario.RunAsync(host, port, count);
    Console.WriteLine($"OK: room {code} started with {count} bots");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"FAIL: {e.GetType().Name} {e.Message}");
    return 1;
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test server.tests`
Expected: 통과 42, 실패 0

- [ ] **Step 6: 두 터미널로 수동 확인**

터미널 1:

```bash
dotnet run --project server
```

Expected: `silent-bell server listening on 7777`

터미널 2:

```bash
dotnet run --project tools/bot -- 127.0.0.1 7777 4
```

Expected: `OK: room XXXX started with 4 bots` (XXXX는 4글자 코드), 종료 코드 0. 터미널 1에는 `[session 1] connected ...`부터 `[session 4] ...`까지 접속 로그와, 봇이 끝난 뒤의 종료 로그가 찍힌다.

- [ ] **Step 7: 커밋**

```bash
git add tools/bot server.tests/GameServerTests.cs
git commit -m "feat(bot): add bot scenario that creates, joins and starts a room"
```

---

## M1 완료 기준

- `dotnet test server.tests` 전체 통과 (42개)
- 서버를 켠 상태에서 `dotnet run --project tools/bot -- 127.0.0.1 7777 4`가 `OK: ...`를 출력
- `shared/` 안에 bin/obj가 없음 (`ls shared` → `Generated  Net  Shared.csproj`)

## M1에서 다루지 않는 것 (M2 이후)

- 방 틱 루프, 게임 패킷(`C_Input`, `S_Snapshot`, `S_Event`, `S_Result`), 결과 화면 후 대기실 복귀와 준비 초기화
- Unity 클라이언트와 `shared/`의 Unity 패키지화(`package.json`, asmdef)
- AWS 배포, systemd, SIGTERM 처리
