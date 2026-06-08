using ConDmsLockerCmd;
using System.IO.Ports;

Console.WriteLine("=== Locker Test ===");

// ── DEBUG ──────────────────────────────────────────
Console.WriteLine("[DEBUG] Config path : " + LockerConfig.Instance.ConfigPath);
Console.WriteLine("[DEBUG] Port จาก ini: " + LockerConfig.Instance.Port);
Console.WriteLine("[DEBUG] MaxChannels  : " + LockerConfig.Instance.MaxChannels);

string[] ports = SerialPort.GetPortNames();
Console.WriteLine("[DEBUG] Ports ในเครื่อง: " + string.Join(", ", ports));
// ───────────────────────────────────────────────────

// อ่านค่าจาก .ini ทั้งหมด
string port = LockerConfig.Instance.Port;
byte BOARD = LockerConfig.Instance.BoardAddr;
int MAX_CH = LockerConfig.Instance.MaxChannels;
const int STATUS_LEN = 11;   // 80 [board] S1..S7 33 [BCC] = 11 bytes

bool connected = LockerCommands.CmdConnectPort(port);
Console.WriteLine(connected ? $"✓ Connected ({port})" : $"✗ Failed — เช็ค COM port และสาย RS-485 ({port})");
if (!connected) { Console.ReadKey(); return; }

while (true)
{
    Console.WriteLine("\n================================");
    Console.WriteLine("เลือกคำสั่ง:");
    Console.WriteLine($"  1 = Unlock ช่อง (1-{MAX_CH})");
    Console.WriteLine($"  2 = Check สถานะช่องเดียว (1-{MAX_CH})");
    Console.WriteLine($"  3 = Read All Status (CH 1-{MAX_CH})");
    Console.WriteLine("  0 = ออก");
    Console.Write("เลือก: ");

    string? cmd = Console.ReadLine();
    if (cmd == "0") break;

    if (cmd == "3")
    {
        byte[]? response = LockerCommands.ReadAllStatus(BOARD);

        if (response == null || response.Length < STATUS_LEN)
        {
            Console.WriteLine($"✗ ไม่มีการตอบกลับจาก Board (ได้ {response?.Length ?? 0} bytes, ต้องการ {STATUS_LEN})");
            continue;
        }

        byte[] s = new byte[7];
        for (int b = 0; b < 7; b++)
            s[b] = response[2 + b];

        Console.WriteLine($"\nBoard {BOARD} — สถานะทุกช่อง (CH 1–{MAX_CH})");
        Console.Write("[DEBUG] Raw S1-S7: ");
        for (int b = 0; b < 7; b++) Console.Write($"0x{s[b]:X2} ");
        Console.WriteLine();
        Console.WriteLine("─────────────────────────────────────────");

        for (int byteIdx = 0; byteIdx < 6; byteIdx++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                int ch = byteIdx * 8 + bit + 1;
                if (ch > MAX_CH) break;
                bool open = (s[byteIdx] >> bit & 1) == 0;
                Console.WriteLine($"  CH {ch:D2} → {(open ? "🔓 เปิดอยู่" : "🔒 ปิด")}");
            }
        }
        // CH 49–50
        for (int bit = 0; bit < 2; bit++)
        {
            int ch = 49 + bit;
            if (ch > MAX_CH) break;
            bool open = (s[6] >> bit & 1) == 0;
            Console.WriteLine($"  CH {ch:D2} → {(open ? "🔓 เปิดอยู่" : "🔒 ปิด")}");
        }
        Console.WriteLine("─────────────────────────────────────────");
    }

    else if (cmd == "1" || cmd == "2")
    {
        Console.Write($"Channel (1-{MAX_CH}): ");
        string? chInput = Console.ReadLine();
        if (!byte.TryParse(chInput, out byte ch) || ch < 1 || ch > MAX_CH)
        {
            Console.WriteLine($"✗ Channel ไม่ถูกต้อง (1-{MAX_CH})");
            continue;
        }

        if (cmd == "1")
        {
            Console.WriteLine($"\nUnlocking CH {ch}...");
            string result = LockerCommands.CmdUnlock(BOARD, ch);
            Console.WriteLine(result == "ok" ? "✓ ok — ฟังเสียงรีเลย์คลิก" : $"✗ {result}");
        }
        else
        {
            Console.WriteLine($"\nChecking CH {ch}...");
            bool locked = LockerCommands.CmdCheckLocked(BOARD, ch);
            Console.WriteLine(locked ? "🔒 Locked (ปิดอยู่)" : "🔓 Unlocked (เปิดอยู่)");
        }
    }
    else
    {
        Console.WriteLine("✗ ไม่รู้จักคำสั่ง");
    }
}

LockerCommands.Disconnect();
Console.WriteLine("\nDisconnected — bye");