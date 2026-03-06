using System.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SNMPSimMgr.Devices;
using SNMPSimMgr.Interfaces;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services
{
    /// <summary>
    /// File-based device profile store — reads/writes Data/devices.json.
    /// Matches the original SNMPSimMgr DeviceProfileStore API.
    /// </summary>
    public class DeviceProfileStore : IDeviceStore
    {
        private static readonly string DataRoot = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Data");

        private static readonly string SchemasRoot = Path.Combine(DataRoot, "schemas");

        private static readonly JsonSerializerOptions  JsonOpts = new JsonSerializerOptions() {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public DeviceProfileStore()
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(SchemasRoot);

            // Seed default devices on first run
            var devicesFile = Path.Combine(DataRoot, "devices.json");
            if (!File.Exists(devicesFile))
                SeedDefaultDevices(devicesFile);
        }

        public async Task<List<DeviceProfile>> LoadProfilesAsync()
        {
            var path = Path.Combine(DataRoot, "devices.json");
            if (!File.Exists(path)) return new List<DeviceProfile>();

            var json = await Task.Run(() => File.ReadAllText(path));
            return JsonSerializer.Deserialize<List<DeviceProfile>>(json, JsonOpts)
                ?? new List<DeviceProfile>();
        }

        public async Task SaveProfilesAsync(List<DeviceProfile> profiles)
        {
            var path = Path.Combine(DataRoot, "devices.json");
            var json = JsonSerializer.Serialize(profiles, JsonOpts);
            await Task.Run(() => File.WriteAllText(path, json));
        }

        public async Task<List<SnmpRecord>> LoadWalkDataAsync(DeviceProfile device)
        {
            var folder = DeviceFolder(device);
            var path = Path.Combine(folder, "walk.json");
            if (!File.Exists(path)) return new List<SnmpRecord>();

            var json = await Task.Run(() => File.ReadAllText(path));
            return JsonSerializer.Deserialize<List<SnmpRecord>>(json, JsonOpts)
                ?? new List<SnmpRecord>();
        }

        public async Task SaveWalkDataAsync(DeviceProfile device, List<SnmpRecord> records)
        {
            var folder = DeviceFolder(device);
            var path = Path.Combine(folder, "walk.json");
            var json = JsonSerializer.Serialize(records, JsonOpts);
            await Task.Run(() => File.WriteAllText(path, json));
        }

        public bool HasWalkData(DeviceProfile device)
        {
            var folder = DeviceFolder(device);
            return File.Exists(Path.Combine(folder, "walk.json"));
        }

        private string DeviceFolder(DeviceProfile device)
        {
            var safeName = string.Join("_", device.Name.Split(Path.GetInvalidFileNameChars()));
            var folder = Path.Combine(DataRoot, safeName);
            Directory.CreateDirectory(folder);
            return folder;
        }

        // ── Seed default devices ────────────────────────────────────────

        private void SeedDefaultDevices(string devicesFile)
        {
            var mibsDir = FindMibsDirectory();
            if (mibsDir == null) mibsDir = "";

            var devices = new List<DeviceProfile>();

            // Hardware devices with MIBs
            AddDevice(devices, "COMBINER", "60.60.60.22", mibsDir, "COMBINER");
            AddDevice(devices, "SPLITTER", "60.60.60.21", mibsDir, "SPLITTER");
            AddDevice(devices, "BUC", "60.60.60.40", mibsDir, "BUC");
            AddDevice(devices, "ACU", "60.60.60.41", mibsDir, "ACU");
            AddDevice(devices, "PIRANHA", "60.60.60.20", mibsDir, "PIRANHA");
            AddDevice(devices, "OSA", "60.60.60.42", mibsDir, "OSA");

            //// Super-Device (top-level MIBs + GENERAL)
            //if (!string.IsNullOrEmpty(mibsDir))
            //{
            //    var superMibs = new List<string>();
            //    var superMib = Path.Combine(mibsDir, "SUPER-DEVICE-MIB.txt");
            //    if (File.Exists(superMib)) superMibs.Add(superMib);
            //    foreach (var f in Directory.GetFiles(Path.Combine(mibsDir, "GENERAL")))
            //        superMibs.Add(f);

            //    var superSchemaFile = Path.Combine(SchemasRoot, "Super-Device-Lab.json");
            //    devices.Add(new DeviceProfile
            //    {
            //        Name = "Super-Device-Lab",
            //        IpAddress = "10.0.0.100",
            //        Community = "supertest",
            //        MibFilePaths = superMibs,
            //        SchemaPath = File.Exists(superSchemaFile) ? superSchemaFile : null
            //    });
            //}

            // IDD example device (non-SNMP — defined in SampleIddDevice class)
            // Same pattern as MIB files for SNMP — but defined in C# code instead.
            // Export IDD schema to JSON so it can be loaded like an SNMP schema.
            var iddProfile = SampleIddDevice.CreateProfile();
            var iddSchemaFile = Path.Combine(SchemasRoot, iddProfile.Name + ".json");
            IddPanelBuilderService.ExportSchemaToFile(
                iddProfile.Name, iddProfile.IpAddress, iddProfile.IddFields, iddSchemaFile);
            iddProfile.SchemaPath = iddSchemaFile;
            devices.Add(iddProfile);

            var json = JsonSerializer.Serialize(devices, JsonOpts);
            File.WriteAllText(devicesFile, json);
        }

        private static void AddDevice(List<DeviceProfile> devices, string name, string ip,
            string mibsDir, string subFolder)
        {
            var mibPaths = new List<string>();
            if (!string.IsNullOrEmpty(mibsDir))
            {
                var folder = Path.Combine(mibsDir, subFolder);
                if (Directory.Exists(folder))
                    mibPaths.AddRange(Directory.GetFiles(folder));
            }

            // Check for pre-built schema JSON (exported from MIB Browser)
            var schemaFile = Path.Combine(SchemasRoot, name + ".json");
            devices.Add(new DeviceProfile
            {
                Name = name,
                IpAddress = ip,
                MibFilePaths = mibPaths,
                SchemaPath = File.Exists(schemaFile) ? schemaFile : null
            });
        }

        /// <summary>
        /// Walk up from exe directory to find the MIBs folder.
        /// Handles: bin/Debug/net472 → WPF → SNMPSimMgr-Integration → MIBs
        /// </summary>
        private static string FindMibsDirectory()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                var candidate = Path.Combine(dir, "MIBs");
                if (Directory.Exists(candidate))
                    return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }
    }
}
