using ConDmsLockerCmd;
using System.IO.Ports;

Console.WriteLine("=== Locker Test ===");

Console.WriteLine("[DEBUG] Config path : " + LockerConfig.Instance.ConfigPath);
Console.WriteLine("[DEBUG] Port จาก ini: " + LockerConfig.Instance.Port);
Console.WriteLine("[DEBUG] MaxChannels  : " + LockerConfig.Instance.MaxChannels);

string[] ports = SerialPort.GetPortNames();
Console.WriteLine("[DEBUG] Ports ในเครื่อง: " + string.Join(", ", ports));

string port = LockerConfig.Instance.Port;
byte BOARD = LockerConfig.Instance.BoardAddr;
int MAX_CH = LockerConfig.Instance.MaxChannels;
const int STATUS_LEN = 11;

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
    Console.WriteLine($"  4 = Debug Raw — ดู raw bytes ทั้งล็อคและไม่ล็อค");
    Console.WriteLine("  0 = ออก");
    Console.Write("เลือก: ");

    string? cmd = Console.ReadLine();
    if (cmd == "0") break;

    // ─────────────────────────────────────────────────────────
    // CMD 4 — Debug Raw: กด Enter ค้างไว้แล้วเปลี่ยนสถานะล็อค
    // ดูว่า raw bytes เปลี่ยนไหมเมื่อล็อค/ไม่ล็อค
    // ─────────────────────────────────────────────────────────
    if (cmd == "4")
    {
        Console.Write($"Channel (1-{MAX_CH}): ");
        string? chInput = Console.ReadLine();
        if (!byte.TryParse(chInput, out byte ch) || ch < 1 || ch > MAX_CH)
        {
            Console.WriteLine($"✗ Channel ไม่ถูกต้อง (1-{MAX_CH})");
            continue;
        }

        Console.WriteLine($"\n[DEBUG MODE] CH {ch} — กด Enter เพื่อ query ซ้ำ, กด Q แล้ว Enter เพื่อออก");
        Console.WriteLine("ลองล็อคและไม่ล็อคสลับกัน แล้วดูว่า raw bytes เปลี่ยนไหม\n");

        while (true)
        {
            string rawHex = LockerCommands.ReadRawHex(BOARD, ch);
            byte rawByte3 = 0xFF;

            try { rawByte3 = LockerCommands.CmdCheckLockedRaw(BOARD, ch); }
            catch { }

            string status = rawByte3 == 0x11 ? "🔒 Locked (0x11)"
                          : rawByte3 == 0x00 ? "🔓 Unlocked (0x00)"
                          : $"❓ Unknown (0x{rawByte3:X2})";

            Console.WriteLine($"  Raw: [{rawHex}]  →  Byte[3] = 0x{rawByte3:X2}  →  {status}");

            Console.Write("  Enter=query อีกครั้ง, Q=ออก: ");
            string? k = Console.ReadLine();
            if (k?.Trim().ToUpper() == "Q") break;
        }
        continue;
    }

    // ─────────────────────────────────────────────────────────
    // CMD 3 — Read All Status
    // ─────────────────────────────────────────────────────────
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

        // ตรวจว่ามี feedback wiring หรือเปล่า
        bool allFF = s.Take(7).All(b => b == 0xFF);
        if (allFF)
            Console.WriteLine("⚠ ได้ 0xFF ทุก byte — อาจไม่ได้ต่อสาย Feedback (pin 6/7)");

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

            // แสดง raw bytes ด้วยทุกครั้ง
            string rawHex = LockerCommands.ReadRawHex(BOARD, ch);
            Console.WriteLine($"[DEBUG] Raw bytes: [{rawHex}]");

            byte rawByte3 = LockerCommands.CmdCheckLockedRaw(BOARD, ch);
            Console.WriteLine($"[DEBUG] Byte[3] = 0x{rawByte3:X2}  (0x11=Locked, 0x00=Unlocked)");

            bool locked = rawByte3 == 0x00;
            Console.WriteLine(locked ? "🔒 Locked (ปิดอยู่)" : "🔓 Unlocked (เปิดอยู่)");

            if (rawByte3 != 0x00 && rawByte3 != 0x11)
                Console.WriteLine($"⚠ Byte[3] = 0x{rawByte3:X2} — ไม่ใช่ค่ามาตรฐาน ตรวจสอบสาย feedback");
        }
    }
    else
    {
        Console.WriteLine("✗ ไม่รู้จักคำสั่ง");
    }
}

LockerCommands.Disconnect();
Console.WriteLine("\nDisconnected — bye");