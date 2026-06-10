using ConDmsLockerCmd;
using System.IO.Ports;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== Locker Test ===");

Console.WriteLine("[DEBUG] Config path : " + LockerConfig.Instance.ConfigPath);
Console.WriteLine("[DEBUG] Mode        : " + LockerConfig.Instance.Mode);
Console.WriteLine("[DEBUG] Channels    : " + string.Join(", ", LockerConfig.Instance.Channels));

// ── Step 1: เช็คว่ามี RS-485 driver ติดตั้งอยู่ไหม ──
string[] allDriverPorts = SerialPort.GetPortNames();
if (allDriverPorts.Length == 0)
{
    Console.WriteLine("\nไม่พบ driver COM port ในเครื่องเลย");
    Console.WriteLine("→ ติดตั้ง driver USB-to-RS485 adapter ก่อน แล้วรันใหม่");
    Console.ReadKey();
    return;
}

// ── Step 2: แสดง port ที่พบให้เลือก ──
Console.WriteLine($"\nพบ {allDriverPorts.Length} port:");
for (int i = 0; i < allDriverPorts.Length; i++)
    Console.WriteLine($"  {i + 1} = {allDriverPorts[i]}");

Console.Write($"\nกด Enter เพื่อเลือก {allDriverPorts[0]} หรือพิมพ์หมายเลข/ชื่อ port: ");
string? portInput = Console.ReadLine()?.Trim();

string port;
if (string.IsNullOrWhiteSpace(portInput))
    port = allDriverPorts[0];
else if (int.TryParse(portInput, out int idx) && idx >= 1 && idx <= allDriverPorts.Length)
    port = allDriverPorts[idx - 1];
else
    port = portInput;

Console.WriteLine($"ใช้ port: {port}");

byte BOARD = LockerConfig.Instance.BoardAddr;
IReadOnlyList<int> CHANNELS = LockerConfig.Instance.Channels;
int MAX_CH = CHANNELS.Count;
const int READ_ALL_LEN = 11;

bool connected = LockerCommands.CmdConnectPort(port);
Console.WriteLine(connected
    ? $"Connected ({port})"
    : $"Failed — เช็ค COM port และสาย RS-485 ({port})");
if (!connected) { Console.ReadKey(); return; }

while (true)
{
    Console.WriteLine("\n================================");
    Console.WriteLine("เลือกคำสั่ง:");
    Console.WriteLine($"  1 = Unlock ช่องเดียว ({CHANNELS.First()}-{CHANNELS.Last()})");
    Console.WriteLine($"  2 = Check สถานะช่องเดียว ({CHANNELS.First()}-{CHANNELS.Last()})");
    Console.WriteLine($"  3 = Read All Status");
    Console.WriteLine($"  4 = Unlock ALL ({MAX_CH} ช่องพร้อมกัน)");
    Console.WriteLine($"  5 = Unlock หลายช่อง (ระบุเอง)");
    Console.WriteLine("  0 = ออก");
    Console.Write("เลือก: ");

    string? cmd = Console.ReadLine();
    if (cmd == "0") break;

    // ─────────────────────────────────────────────────────────
    // CMD 4 — Unlock ALL channels พร้อมกัน
    // ─────────────────────────────────────────────────────────
    if (cmd == "4")
    {
        Console.WriteLine($"\nUnlocking ALL {MAX_CH} channels...");
        string result = LockerCommands.CmdUnlockAll(BOARD);
        Console.WriteLine(result == "ok"
            ? $"ok — Unlock {MAX_CH} ช่องสำเร็จ"
            : $"Error: {result}");
        continue;
    }

    // ─────────────────────────────────────────────────────────
    // CMD 5 — Unlock หลายช่องที่ระบุ
    // ─────────────────────────────────────────────────────────
    if (cmd == "5")
    {
        Console.Write($"ระบุ channel (คั่นด้วย comma เช่น {CHANNELS.First()},{CHANNELS.Skip(1).First()}): ");
        string? input = Console.ReadLine();
        var channels = input?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .Where(n => CHANNELS.Contains(n))
            .Distinct()
            .ToArray() ?? Array.Empty<int>();

        if (channels.Length == 0)
        {
            Console.WriteLine("ไม่มี channel ที่ถูกต้อง");
            Console.WriteLine($"  CH ที่ใช้ได้: {string.Join(", ", CHANNELS)}");
            continue;
        }

        Console.WriteLine($"\nUnlocking CH: {string.Join(", ", channels)}...");
        string result = LockerCommands.CmdUnlockChannels(BOARD, channels);
        Console.WriteLine(result == "ok"
            ? "ok — Unlock สำเร็จ"
            : $"Error: {result}");
        continue;
    }

    // ─────────────────────────────────────────────────────────
    // CMD 3 — Read All Status
    // ─────────────────────────────────────────────────────────
    if (cmd == "3")
    {
        byte[]? response = LockerCommands.ReadAllStatus(BOARD);
        if (response == null || response.Length < READ_ALL_LEN)
        {
            Console.WriteLine($"ไม่มีการตอบกลับ (ได้ {response?.Length ?? 0} bytes, ต้องการ {READ_ALL_LEN})");
            continue;
        }

        Console.Write("[DEBUG] Raw S1-S7: ");
        for (int b = 2; b <= 8; b++) Console.Write($"0x{response[b]:X2} ");
        Console.WriteLine();

        var statusMap = LockerCommands.ParseAllStatusOwned(response);

        Console.WriteLine($"\nBoard {BOARD} — สถานะเฉพาะ CH ที่เครื่องนี้ควบคุม (Mode={LockerConfig.Instance.Mode})");
        Console.WriteLine("─────────────────────────────────────────");
        foreach (var (ch, locked) in statusMap.OrderBy(x => x.Key))
            Console.WriteLine($"  CH {ch:D2} → {(locked ? "Locked (ปิด)" : "Unlocked (เปิด)")}");
        Console.WriteLine("─────────────────────────────────────────");
        continue;
    }

    // ─────────────────────────────────────────────────────────
    // CMD 1 & 2 — ต้องการ channel number
    // ─────────────────────────────────────────────────────────
    if (cmd == "1" || cmd == "2")
    {
        Console.Write($"Channel ({string.Join(", ", CHANNELS)}): ");
        string? chInput = Console.ReadLine();

        if (!byte.TryParse(chInput, out byte ch) || !CHANNELS.Contains(ch))
        {
            Console.WriteLine("Channel ไม่ถูกต้อง หรือไม่ใช่ของเครื่องนี้");
            Console.WriteLine($"  CH ที่ใช้ได้: {string.Join(", ", CHANNELS)}");
            continue;
        }

        if (cmd == "1")
        {
            Console.WriteLine($"\nUnlocking CH {ch}...");
            string result = LockerCommands.CmdUnlock(BOARD, ch);
            Console.WriteLine(result == "ok" ? "ok — ฟังเสียงรีเลย์คลิก" : $"Error: {result}");
        }
        else // cmd == "2"
        {
            Console.WriteLine($"\nChecking CH {ch}...");

            string rawHex = LockerCommands.ReadRawHex(BOARD, ch);
            Console.WriteLine($"[DEBUG] Raw bytes: [{rawHex}]");

            byte rawByte3 = LockerCommands.CmdCheckLockedRaw(BOARD, ch);
            Console.WriteLine($"[DEBUG] Byte[3] = 0x{rawByte3:X2}  (0x11=Locked, 0x00=Unlocked)");

            bool locked = rawByte3 == 0x11;
            Console.WriteLine(locked ? "Locked (ปิดอยู่)" : "Unlocked (เปิดอยู่)");

            if (rawByte3 != 0x00 && rawByte3 != 0x11)
                Console.WriteLine($"Warning: Byte[3]=0x{rawByte3:X2} ไม่ใช่ค่ามาตรฐาน ตรวจสอบสาย feedback");
        }
        continue;
    }

    Console.WriteLine("ไม่รู้จักคำสั่ง");
}

LockerCommands.Disconnect();
Console.WriteLine("\nDisconnected.");