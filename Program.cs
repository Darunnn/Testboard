using ConDmsLockerCmd;
using System.IO.Ports;

Console.WriteLine("=== Locker Test ===");

// 1. Connect
Console.WriteLine("\n[1] Connecting...");

// ── DEBUG ──────────────────────────────────────────
Console.WriteLine("[DEBUG] Config path : " + LockerConfig.Instance.ConfigPath);
Console.WriteLine("[DEBUG] Port จาก ini: " + LockerConfig.Instance.Port);

string[] ports = System.IO.Ports.SerialPort.GetPortNames();
Console.WriteLine("[DEBUG] Ports ในเครื่อง: " + string.Join(", ", ports));
// ───────────────────────────────────────────────────

bool connected = LockerCommands.CmdConnectPort("COM3");
Console.WriteLine(connected ? "✓ Connected" : "✗ Failed — เช็ค COM port และสาย RS-485");
if (!connected) { Console.ReadKey(); return; }

// fix board = 1
const byte BOARD = 0x01;

while (true)
{
    Console.WriteLine("\n================================");
    Console.WriteLine("เลือกคำสั่ง:");
    Console.WriteLine("  1 = Unlock ช่อง");
    Console.WriteLine("  2 = Check สถานะช่อง");
    Console.WriteLine("  3 = Read All Status (CH 1-24)");
    Console.WriteLine("  0 = ออก");
    Console.Write("เลือก: ");

    string? cmd = Console.ReadLine();

    if (cmd == "0") break;

    // ----------------------------------------------------------
    // Read All 24 CH
    // ----------------------------------------------------------
    if (cmd == "3")
    {
        byte[]? response = LockerCommands.ReadAllStatus(BOARD);

        if (response == null || response.Length < 7)
        {
            Console.WriteLine("✗ ไม่มีการตอบกลับจาก Board");
            continue;
        }

        byte s1 = response[2], s2 = response[3], s3 = response[4];

        Console.WriteLine("\nBoard 1 — สถานะทุกช่อง");
        Console.WriteLine("─────────────────────────────────────────");

        for (int i = 0; i < 8; i++)
        {
            int ch = i + 1;
            bool open = (s1 >> i & 1) == 1;
            Console.WriteLine($"  CH {ch:D2} → {(open ? "🔓 เปิดอยู่" : "🔒 ปิด")}");
        }
        for (int i = 0; i < 8; i++)
        {
            int ch = i + 9;
            bool open = (s2 >> i & 1) == 1;
            Console.WriteLine($"  CH {ch:D2} → {(open ? "🔓 เปิดอยู่" : "🔒 ปิด")}");
        }
        for (int i = 0; i < 8; i++)
        {
            int ch = i + 17;
            bool open = (s3 >> i & 1) == 1;
            Console.WriteLine($"  CH {ch:D2} → {(open ? "🔓 เปิดอยู่" : "🔒 ปิด")}");
        }
        Console.WriteLine("─────────────────────────────────────────");
    }

    // ----------------------------------------------------------
    // Unlock / Check single CH
    // ----------------------------------------------------------
    else if (cmd == "1" || cmd == "2")
    {
        Console.Write("Channel (1-24): ");
        string? chInput = Console.ReadLine();
        if (!byte.TryParse(chInput, out byte ch) || ch < 1 || ch > 24)
        {
            Console.WriteLine("✗ Channel ไม่ถูกต้อง (1-24)");
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