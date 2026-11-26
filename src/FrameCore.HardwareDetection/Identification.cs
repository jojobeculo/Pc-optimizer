using System.Text.RegularExpressions;
using FrameCore.Core.Hardware;
using FrameCore.Core.Models;

namespace FrameCore.HardwareDetection;

/// <summary>Pure identification rules (unit-tested). Unknown inputs yield null, never a guess presented as fact.</summary>
public static class Identification
{
    public static CpuVendor CpuVendorFrom(string? vendorString, string? name)
    {
        var s = (vendorString ?? "") + " " + (name ?? "");
        if (s.Contains("GenuineIntel", StringComparison.OrdinalIgnoreCase) || s.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return CpuVendor.Intel;
        if (s.Contains("AuthenticAMD", StringComparison.OrdinalIgnoreCase) || s.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return CpuVendor.Amd;
        return string.IsNullOrWhiteSpace(s) ? CpuVendor.Unknown : CpuVendor.Other;
    }

    public static string? IntelMicroarchitecture(int family, int model)
    {
        if (family != 6) return null;
        return model switch
        {
            0x9E or 0xA5 or 0xA6 => model == 0x9E ? "Coffee Lake / Kaby Lake" : "Comet Lake",
            0x8E => "Kaby/Whiskey/Amber Lake (mobile)",
            0x7D or 0x7E => "Ice Lake",
            0x8C or 0x8D => "Tiger Lake",
            0xA7 => "Rocket Lake",
            0x97 or 0x9A => "Alder Lake",
            0xB7 or 0xBA or 0xBF => "Raptor Lake",
            0xAA or 0xAC => "Meteor Lake",
            0xBD => "Lunar Lake",
            0xC5 or 0xC6 or 0xB5 => "Arrow Lake",
            0xCC => "Panther Lake",
            0x5E or 0x4E => "Skylake",
            _ => null,
        };
    }

    public static string? AmdMicroarchitecture(int family, int model)
    {
        return family switch
        {
            0x17 when model < 0x10 => "Zen / Zen+",
            0x17 when model is >= 0x10 and < 0x20 => "Zen / Zen+ (APU)",
            0x17 => "Zen 2",
            0x19 when model is >= 0x60 and < 0x80 => "Zen 4",
            0x19 when model is >= 0x10 and < 0x20 => "Zen 4",
            0x19 when model is >= 0xA0 and < 0xB0 => "Zen 4c",
            0x19 when model is >= 0x40 and < 0x50 => "Zen 3+",
            0x19 => "Zen 3",
            0x1A => "Zen 5",
            0x1B => "Zen 6",
            _ => null,
        };
    }

    /// <summary>"12e génération", "Core Ultra (Série 2)", "Ryzen 7000"…</summary>
    public static string? Generation(CpuVendor vendor, string name)
    {
        if (vendor == CpuVendor.Intel)
        {
            var ultra = Regex.Match(name, @"Core\(?T?M?\)?\s*Ultra\s*\d\s*(\d)(\d{2})", RegexOptions.IgnoreCase);
            if (ultra.Success) return $"Core Ultra Series {ultra.Groups[1].Value}";
            var m = Regex.Match(name, @"i[3579]-(\d{4,5})", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var digits = m.Groups[1].Value;
                var gen = digits.Length == 5 ? digits[..2] : digits[..1];
                return $"Core {gen}th gen";
            }
            var core3 = Regex.Match(name, @"Core\(?T?M?\)?\s*[3579]\s*(\d)\d{2}", RegexOptions.IgnoreCase);
            if (core3.Success) return $"Core Series {core3.Groups[1].Value}";
            return null;
        }
        if (vendor == CpuVendor.Amd)
        {
            var m = Regex.Match(name, @"Ryzen\s*(?:AI\s*)?(?:[3579]|Threadripper)\s*(?:PRO\s*)?(\d)\d{2,3}", RegexOptions.IgnoreCase);
            if (m.Success) return $"Ryzen {m.Groups[1].Value}000";
            var ai = Regex.Match(name, @"Ryzen\s*AI\s*(?:Max\+?\s*)?(\d{3})", RegexOptions.IgnoreCase);
            if (ai.Success) return "Ryzen AI " + ai.Groups[1].Value;
        }
        return null;
    }

    public static bool IsX3D(string name) => name.Contains("X3D", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dual-CCD X3D parts (only one CCD has 3D V-Cache): 7900X3D, 7950X3D, 9900X3D, 9950X3D…</summary>
    public static bool IsDualCcdX3D(string name, int l3Groups, int cores)
    {
        if (!IsX3D(name)) return false;
        if (l3Groups >= 2) return true;
        return Regex.IsMatch(name, @"\b(79[05]0X3D|99[05]0X3D2?|59[05]0X3D)\b", RegexOptions.IgnoreCase) || cores >= 12;
    }

    public static TriState IsUnlocked(CpuVendor vendor, string name, bool mobile)
    {
        if (vendor == CpuVendor.Intel)
        {
            if (Regex.IsMatch(name, @"\d{3,5}K[FS]?\b", RegexOptions.IgnoreCase)) return TriState.Yes;
            if (Regex.IsMatch(name, @"\d{3,5}HX\b", RegexOptions.IgnoreCase)) return TriState.Unknown; // partially unlocked depending on SKU/OEM
            return TriState.No;
        }
        if (vendor == CpuVendor.Amd)
        {
            if (name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) && !mobile) return TriState.Yes;
            return mobile ? TriState.No : TriState.Unknown;
        }
        return TriState.Unknown;
    }

    public static bool IsMobileCpu(string name) =>
        Regex.IsMatch(name, @"\d{3,5}(U|H|HS|HX|HK|P|V|Y|G\d?)\b", RegexOptions.IgnoreCase)
        || name.Contains("Ryzen AI", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(name, @"Core\(?T?M?\)?\s*Ultra\s*\d\s*\d{3}[UHVP]", RegexOptions.IgnoreCase);

    public static GpuVendor GpuVendorFrom(int vendorId, string? name)
    {
        return vendorId switch
        {
            0x10DE => GpuVendor.Nvidia,
            0x1002 or 0x1022 => GpuVendor.Amd,
            0x8086 => GpuVendor.Intel,
            0x1414 => GpuVendor.Microsoft,
            _ => name switch
            {
                null => GpuVendor.Unknown,
                _ when name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) => GpuVendor.Nvidia,
                _ when name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) => GpuVendor.Amd,
                _ when name.Contains("Intel", StringComparison.OrdinalIgnoreCase) => GpuVendor.Intel,
                _ => GpuVendor.Other,
            },
        };
    }

    public static string? GpuArchitecture(GpuVendor vendor, string name)
    {
        var n = name.ToUpperInvariant();
        switch (vendor)
        {
            case GpuVendor.Nvidia:
                if (Regex.IsMatch(n, @"RTX\s*50\d{2}")) return "Blackwell (RTX 50)";
                if (Regex.IsMatch(n, @"RTX\s*40\d{2}")) return "Ada Lovelace (RTX 40)";
                if (Regex.IsMatch(n, @"RTX\s*30\d{2}")) return "Ampere (RTX 30)";
                if (Regex.IsMatch(n, @"RTX\s*20\d{2}|GTX\s*16\d{2}")) return "Turing";
                if (Regex.IsMatch(n, @"GTX\s*10\d{2}")) return "Pascal";
                if (Regex.IsMatch(n, @"GTX\s*9\d{2}")) return "Maxwell";
                return null;
            case GpuVendor.Amd:
                if (Regex.IsMatch(n, @"RX\s*9\d{3}")) return "RDNA 4 (RX 9000)";
                if (Regex.IsMatch(n, @"RX\s*7\d{3}")) return "RDNA 3 (RX 7000)";
                if (Regex.IsMatch(n, @"RX\s*6\d{3}")) return "RDNA 2 (RX 6000)";
                if (Regex.IsMatch(n, @"RX\s*5\d{3}")) return "RDNA 1 (RX 5000)";
                if (n.Contains("VEGA")) return "GCN 5 (Vega)";
                if (Regex.IsMatch(n, @"RADEON\(?T?M?\)?\s*8\d0M")) return "RDNA 3.5 (iGPU)";
                return null;
            case GpuVendor.Intel:
                if (Regex.IsMatch(n, @"ARC.*\bB\d{3}")) return "Xe2 Battlemage (Arc B)";
                if (Regex.IsMatch(n, @"ARC.*\bA\d{3}")) return "Xe-HPG Alchemist (Arc A)";
                if (n.Contains("ARC")) return "Xe (Arc)";
                if (n.Contains("IRIS XE") || n.Contains("UHD")) return "Xe-LP / Gen (iGPU)";
                return null;
        }
        return null;
    }

    /// <summary>Resizable BAR is expected to be usable from these generations on (vendor requirement).</summary>
    public static bool ResizableBarCapableGpu(GpuVendor vendor, string name)
    {
        var n = name.ToUpperInvariant();
        return vendor switch
        {
            GpuVendor.Nvidia => Regex.IsMatch(n, @"RTX\s*(30|40|50)\d{2}"),
            GpuVendor.Amd => Regex.IsMatch(n, @"RX\s*(6|7|9)\d{3}"),
            GpuVendor.Intel => n.Contains("ARC"),
            _ => false,
        };
    }

    /// <summary>Extracts a chipset token from a motherboard product name ("ROG STRIX B650E-F" → "B650E").</summary>
    public static string? ChipsetFromBoard(string? product)
    {
        if (string.IsNullOrWhiteSpace(product)) return null;
        var m = Regex.Match(product.ToUpperInvariant(), @"(?<![A-Z0-9])([ABHXZQW]\d{3}[EM]?)(?![0-9])");
        if (!m.Success) return null;
        var token = m.Groups[1].Value;
        return token.EndsWith('M') ? token[..^1] : token;
    }

    public static bool IsLaptopChassis(IEnumerable<int> chassisTypes) =>
        chassisTypes.Any(t => t is 8 or 9 or 10 or 11 or 12 or 14 or 18 or 21 or 30 or 31 or 32);

    public static string? MemoryType(int? smbiosType) => smbiosType switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2", 29 => "LPDDR3",
        30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => null,
    };
}
