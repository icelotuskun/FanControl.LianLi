using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace FanControl.LianLi.Hid;

/// <summary>
/// Resolves the Windows <em>ContainerId</em> of a HID device-interface path. The ContainerId is the
/// GUID Windows assigns to one physical device: every HID interface a single controller exposes
/// shares it, and it differs across physical controllers. That makes it the reliable
/// de-duplication key for the Lian Li Uni family, whose units all report the same firmware-fixed USB
/// serial ("6243168001" on the SL-Infinity) - the serial cannot tell two interfaces of one
/// controller from two separate controllers, but the ContainerId can (see
/// <see cref="HidDeviceDeduplicator"/>).
/// </summary>
// Excluded from coverage: this calls the Windows configuration manager (cfgmgr32) against real
// device nodes and is verified on hardware. The de-dup logic that consumes the resolved id is
// unit-tested through HidDeviceInfo; this mirrors HidSharpEnumerator's hardware-only seam.
[ExcludeFromCodeCoverage]
internal static class ContainerIdResolver {
    private const int CrSuccess = 0;
    private const int CrBufferSmall = 26; // CR_BUFFER_SMALL: returned by the sizing call below.
    private const uint LocateDevNodeNormal = 0;

    // DEVPKEY_Device_InstanceId {78c34fc8-104a-4aca-9ea4-524d52996e57} pid 256: the device-instance
    // string ("HID\VID_...\...") the interface path belongs to, the input to the devnode lookup.
    private static readonly DevPropKey InstanceIdKey =
        new DevPropKey(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);

    // DEVPKEY_Device_ContainerId {8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c} pid 2: the physical-device GUID.
    private static readonly DevPropKey ContainerIdKey =
        new DevPropKey(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);

    /// <summary>
    /// Resolve the ContainerId of the physical device behind a HID interface path, formatted as a
    /// lowercased "{guid}". Returns null when the path has no resolvable device node or the device
    /// reports no real container (the all-zero GUID), so the caller falls back to the per-interface
    /// device path - the safe direction that never collapses distinct devices.
    /// </summary>
    public static string? Resolve(string deviceInterfacePath) {
        string? instanceId = GetInterfaceInstanceId(deviceInterfacePath);
        if (instanceId is null || instanceId.Length == 0) {
            return null;
        }

        if (CM_Locate_DevNodeW(out uint devInst, instanceId, LocateDevNodeNormal) != CrSuccess) {
            return null;
        }

        byte[]? raw = GetDevNodeProperty(devInst, ContainerIdKey);
        if (raw is null || raw.Length != 16) {
            return null;
        }

        var container = new Guid(raw);
        // A device with no real container reports the all-zero GUID; treat that as "unknown" so such
        // devices fall back to the device-path key instead of all collapsing onto one zero id.
        if (container == Guid.Empty) {
            return null;
        }

        return container.ToString("B").ToLowerInvariant();
    }

    private static string? GetInterfaceInstanceId(string deviceInterfacePath) {
        // A static readonly field cannot be passed by ref, so copy the key into a local first.
        DevPropKey key = InstanceIdKey;

        // First call sizes the buffer: a null buffer makes it report the required byte count in size
        // and return CR_BUFFER_SMALL. Anything else (or a zero size) means there is nothing to read.
        uint size = 0;
        int sizing = CM_Get_Device_Interface_PropertyW(deviceInterfacePath, ref key, out _, null, ref size, 0);
        if ((sizing != CrSuccess && sizing != CrBufferSmall) || size == 0) {
            return null;
        }

        var buffer = new byte[size];
        if (CM_Get_Device_Interface_PropertyW(
                deviceInterfacePath, ref key, out _, buffer, ref size, 0) != CrSuccess) {
            return null;
        }

        return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    private static byte[]? GetDevNodeProperty(uint devInst, DevPropKey key) {
        uint size = 0;
        int sizing = CM_Get_DevNode_PropertyW(devInst, ref key, out _, null, ref size, 0);
        if ((sizing != CrSuccess && sizing != CrBufferSmall) || size == 0) {
            return null;
        }

        var buffer = new byte[size];
        if (CM_Get_DevNode_PropertyW(devInst, ref key, out _, buffer, ref size, 0) != CrSuccess) {
            return null;
        }

        return buffer;
    }

    // Mirrors the native DEVPROPKEY: a property-set GUID plus an id within that set.
    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey {
        public DevPropKey(Guid formatId, uint propertyId) {
            FormatId = formatId;
            PropertyId = propertyId;
        }

        public Guid FormatId;
        public uint PropertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_Device_Interface_PropertyW(
        string pszDeviceInterface, ref DevPropKey propertyKey, out uint propertyType,
        byte[]? propertyBuffer, ref uint propertyBufferSize, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint dnDevInst, ref DevPropKey propertyKey, out uint propertyType,
        byte[]? propertyBuffer, ref uint propertyBufferSize, uint ulFlags);
}
