using ConDmsLockerCmd;
using System.IO.Ports;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== Locker Test ===");

Console.WriteLine("[DEBUG] Config path : " + LockerConfig.Instance.ConfigPath);
Console.WriteLine("[DEBUG] Port จาก ini: " + LockerConfig.Instance.Port);
Console.WriteLine("[DEBUG] MaxChannels  : " + LockerConfig.Instance.MaxChannels);

string[] ports = SerialPort.GetPortNames();
Console.WriteLine("[DEBUG] Ports ในเครื่อง: " + string.Join(", ", ports));

string port = LockerConfig.Instance.Port;
byte BOARD = LockerConfig.Instance.BoardAddr;
int MAX_CH = LockerConfig.Instance.MaxChannels;
const int READ_ALL_LEN = 11; // 80 board S1..S7 33 BCC

bool connected = LockerCommands.CmdConnectPort(port);
Console.WriteLine(connected
    ? $"Connected ({port})"
    : $"Failed — เช็ค COM port และสาย RS-485 ({port})");
if (!connected) { Console.ReadKey(); return; }

while (true)
{
    Console.WriteLine("\n================================");
    Console.WriteLine("เลือกคำสั่ง:");
    Console.WriteLine($"  1 = Unlock ช่องเดียว (1-{MAX_CH})");
    Console.WriteLine($"  2 = Check สถานะช่องเดียว (1-{MAX_CH})");
    Console.WriteLine($"  3 = Read All Status (CH 1-{MAX_CH})");
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
    // CMD5 — Unlock หลายช่องที่ระบุ
    // ─────────────────────────────────────────────────────────
    if (cmd == "5")
    {
        Console.Write($"ระบุ channel (คั่นด้วย comma เช่น 1,3,5): ");
        string? input = Console.ReadLine();
        var channels = input?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .Where(n => n >= 1 && n <= MAX_CH)
            .Distinct()
            .ToArray() ?? Array.Empty<int>();

        if (channels.Length == 0)
        {
            Console.WriteLine("ไม่มี channel ที่ถูกต้อง");
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
    // Send: 80 [board] 00 33 [BCC]
    // Reply: 80 [board] S1..S7 33 [BCC] = 11 bytes
    //   bit=0 → Open (เปิด), bit=1 → Closed (ปิด)
    // ─────────────────────────────────────────────────────────
    if (cmd == "3")
    {
        byte[]? response = LockerCommands.ReadAllStatus(BOARD);

        if (response == null || response.Length < READ_ALL_LEN)
        {
            Console.WriteLine($"ไม่มีการตอบกลับ (ได้ {response?.Length ?? 0} bytes, ต้องการ {READ_ALL_LEN})");
            continue;
        }

        // S1–S7 อยู่ที่ index 2–8
        byte[] s = new byte[7];
        for (int b = 0; b < 7; b++)
            s[b] = response[2 + b];

        Console.WriteLine($"\nBoard {BOARD} — สถานะทุกช่อง (CH 1–{MAX_CH})");
        Console.Write("[DEBUG] Raw S1-S7: ");
        for (int b = 0; b < 7; b++) Console.Write($"0x{s[b]:X2} ");
        Console.WriteLine();

        bool allFF = s.Take(7).All(b => b == 0xFF);
        if (allFF)
            Console.WriteLine("Warning: ได้ 0xFF ทุก byte — อาจไม่ได้ต่อสาย Feedback");

        Console.WriteLine("─────────────────────────────────────────");

        // CH 1–48 (S1–S6)
        for (int byteIdx = 0; byteIdx < 6; byteIdx++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                int ch = byteIdx * 8 + bit + 1;
                if (ch > MAX_CH) break;
                // bit=0 → Open, bit=1 → Closed
                int reversedBit = 7 - bit;
                bool closed = (s[byteIdx] >> reversedBit & 1) == 1;
                Console.WriteLine($"  CH {ch:D2} → {(closed ? "Locked (ปิด)" : "Unlocked (เปิด)")}");
            }
        }

        // CH 49–50 (S7 bit 0,1)
        for (int bit = 0; bit < 2; bit++)
        {
            int ch = 49 + bit;
            if (ch > MAX_CH) break;
            bool closed = (s[6] >> bit & 1) == 1;
            Console.WriteLine($"  CH {ch:D2} → {(closed ? "Locked (ปิด)" : "Unlocked (เปิด)")}");
        }
        Console.WriteLine("─────────────────────────────────────────");
        continue;
    }

    // ─────────────────────────────────────────────────────────
    // CMD 1 & 2 — ต้องการ channel number
    // ─────────────────────────────────────────────────────────
    if (cmd == "1" || cmd == "2")
    {
        Console.Write($"Channel (1-{MAX_CH}): ");
        string? chInput = Console.ReadLine();
        if (!byte.TryParse(chInput, out byte ch) || ch < 1 || ch > MAX_CH)
        {
            Console.WriteLine($"Channel ไม่ถูกต้อง (1-{MAX_CH})");
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

            // ─────────────────────────────────────────────
            // FIX: 0x11 = Locked, 0x00 = Unlocked
            // ก่อนหน้าผิดเป็น "locked = rawByte3 == 0x00"
            // ─────────────────────────────────────────────
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