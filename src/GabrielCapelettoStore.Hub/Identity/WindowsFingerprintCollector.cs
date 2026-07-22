using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace GabrielCapelettoStore.Hub.Identity;

/// <summary>
/// Windows implementation of the fingerprint collector. Reads SMBIOS/board/CPU via WMI,
/// the physical NIC MAC via NetworkInformation, and TPM presence via the TBS API (all
/// non-admin). Every read is best-effort — a failing signal becomes null, never a crash.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFingerprintCollector : IDeviceFingerprintCollector
{
    public DeviceFingerprint Collect()
    {
        var (tpmPresent, tpmVersion) = ReadTpm();
        var macs = ReadPhysicalMacs();

        return new DeviceFingerprint
        {
            SystemUuid = WmiFirst("Win32_ComputerSystemProduct", "UUID"),
            BaseboardSerial = WmiFirst("Win32_BaseBoard", "SerialNumber"),
            ProcessorId = WmiFirst("Win32_Processor", "ProcessorId"),
            Manufacturer = WmiFirst("Win32_ComputerSystem", "Manufacturer"),
            Model = WmiFirst("Win32_ComputerSystem", "Model"),
            PermanentMac = macs.Count > 0 ? macs[0] : null,
            PhysicalMacs = macs,
            TpmPresent = tpmPresent,
            TpmVersion = tpmVersion,
            TpmEkPublicHash = ReadTpmEkPublicHash(),
        };
    }

    private static string? WmiFirst(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    var value = item[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }
        }
        catch
        {
            // Best-effort: a signal that cannot be read is simply absent.
        }

        return null;
    }

    private static List<string> ReadPhysicalMacs()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                                                    or NetworkInterfaceType.Wireless80211)
                .Where(n => !IsVirtual(n))
                // Wired first (its MAC is the burned-in one; Wi-Fi may be randomized).
                .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0)
                .Select(n => n.GetPhysicalAddress().GetAddressBytes())
                .Where(b => b.Length == 6)
                .Select(b => string.Join(":", b.Select(x => x.ToString("X2"))))
                .Where(FingerprintHygiene.IsDistinctiveMac)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsVirtual(NetworkInterface nic)
    {
        var text = (nic.Description + " " + nic.Name).ToLowerInvariant();
        return text.Contains("virtual") || text.Contains("vmware") || text.Contains("hyper-v")
            || text.Contains("vethernet") || text.Contains("vpn") || text.Contains("tap")
            || text.Contains("bluetooth") || text.Contains("loopback") || text.Contains("tunnel")
            || text.Contains("wan miniport") || text.Contains("pseudo");
    }

    // --- TPM presence via the TPM Base Services (TBS) API — works without elevation. ---

    [StructLayout(LayoutKind.Sequential)]
    private struct TbsDeviceInfo
    {
        public uint StructVersion;
        public uint TpmVersion;
        public uint TpmInterfaceType;
        public uint TpmImpRevision;
    }

    [DllImport("tbs.dll")]
    private static extern uint Tbsi_GetDeviceInfo(uint size, ref TbsDeviceInfo info);

    private static (bool Present, string? Version) ReadTpm()
    {
        try
        {
            var info = default(TbsDeviceInfo);
            var size = (uint)Marshal.SizeOf<TbsDeviceInfo>();
            var result = Tbsi_GetDeviceInfo(size, ref info); // 0 == TBS_SUCCESS (TPM present)

            if (result == 0)
            {
                var version = info.TpmVersion switch
                {
                    1 => "TPM 1.2",
                    2 => "TPM 2.0",
                    _ => "TPM",
                };
                return (true, version);
            }
        }
        catch
        {
            // No TPM / TBS unavailable.
        }

        return (false, null);
    }

    // --- TPM endorsement-key public via the Platform Crypto Provider — non-admin, per-device unique. ---

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int NCryptOpenStorageProvider(out IntPtr phProvider, string pszProviderName, uint dwFlags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int NCryptGetProperty(
        IntPtr hObject, string pszProperty, byte[]? pbOutput, int cbOutput, out int pcbResult, uint dwFlags);

    [DllImport("ncrypt.dll")]
    private static extern int NCryptFreeObject(IntPtr hObject);

    private static string? ReadTpmEkPublicHash()
    {
        var provider = IntPtr.Zero;
        try
        {
            if (NCryptOpenStorageProvider(out provider, "Microsoft Platform Crypto Provider", 0) != 0)
            {
                return null;
            }

            if (NCryptGetProperty(provider, "PCP_EKPUB", null, 0, out var size, 0) != 0 || size <= 0)
            {
                return null;
            }

            var buffer = new byte[size];
            if (NCryptGetProperty(provider, "PCP_EKPUB", buffer, size, out size, 0) != 0)
            {
                return null;
            }

            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, size)));
        }
        catch
        {
            return null;
        }
        finally
        {
            if (provider != IntPtr.Zero)
            {
                NCryptFreeObject(provider);
            }
        }
    }
}
