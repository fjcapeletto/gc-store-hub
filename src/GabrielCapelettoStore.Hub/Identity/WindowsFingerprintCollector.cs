using System;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

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

        return new DeviceFingerprint
        {
            SystemUuid = WmiFirst("Win32_ComputerSystemProduct", "UUID"),
            BaseboardSerial = WmiFirst("Win32_BaseBoard", "SerialNumber"),
            ProcessorId = WmiFirst("Win32_Processor", "ProcessorId"),
            Manufacturer = WmiFirst("Win32_ComputerSystem", "Manufacturer"),
            Model = WmiFirst("Win32_ComputerSystem", "Model"),
            PermanentMac = ReadPhysicalMac(),
            TpmPresent = tpmPresent,
            TpmVersion = tpmVersion,
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

    private static string? ReadPhysicalMac()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                                                    or NetworkInterfaceType.Wireless80211)
                .Where(n => !IsVirtual(n))
                // Prefer wired (its MAC is the burned-in one; Wi-Fi may be randomized).
                .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0);

            foreach (var nic in candidates)
            {
                var bytes = nic.GetPhysicalAddress().GetAddressBytes();
                if (bytes.Length == 6)
                {
                    return string.Join(":", bytes.Select(b => b.ToString("X2")));
                }
            }
        }
        catch
        {
            // Best-effort.
        }

        return null;
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
}
