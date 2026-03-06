using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services
{
    public class MibStore
    {
        /// <summary>All resolved OID→MibDefinition for the currently loaded device.</summary>
        /// <remarks>Thread-safe: use ConcurrentDictionary to prevent corruption
        /// when the SignalR hub and WPF UI access it concurrently.</remarks>
        public ConcurrentDictionary<string, MibDefinition>  LoadedOids { get; } = new ConcurrentDictionary<string, MibDefinition>();

        /// <summary>Display names of loaded MIB files for UI.</summary>
        public ObservableCollection<string>  LoadedFileNames { get; } = new ObservableCollection<string>();

        public int TotalDefinitions => LoadedOids.Count;

        // Prevents concurrent LoadForDeviceAsync calls from corrupting state
        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Load all MIB files associated with a device profile.
        /// Uses multi-file parsing so cross-MIB dependencies resolve correctly.
        /// Thread-safe: serialized via SemaphoreSlim, uses ConcurrentDictionary.
        /// </summary>
        /// <param name="updateUI">
        /// When true (default), updates the LoadedFileNames ObservableCollection.
        /// Pass false when calling from a background thread (e.g., SignalR hub)
        /// to avoid Dispatcher deadlocks.
        /// </param>
        public async Task LoadForDeviceAsync(DeviceProfile device, bool updateUI = true)
        {
            await _loadLock.WaitAsync();
            try
            {
                LoadedOids.Clear();
                if (updateUI) DispatchUI(() => LoadedFileNames.Clear());

                if (device.MibFilePaths.Count == 0) return;

                // Remove paths that no longer exist on disk
                device.MibFilePaths.RemoveAll(p => !File.Exists(p));
                if (device.MibFilePaths.Count == 0) return;

                // Parse all files together for cross-file dependency resolution
                var results = await Task.Run(() =>
                    MibParserService.ParseMultiple(device.MibFilePaths));

                foreach (var info in results)
                {
                    if (updateUI)
                        DispatchUI(() => LoadedFileNames.Add($"{info.ModuleName} ({info.DefinitionCount})"));
                    foreach (var def in info.Definitions)
                        LoadedOids[def.Oid] = def;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MIB parse error: {ex.Message}");
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private static void DispatchUI(Action action)
        {
            var dispatcher = Application.Current.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(action);
            else
                action();
        }

        /// <summary>
        /// Parse a single MIB file (for preview/validation when user selects a file).
        /// </summary>
        public async Task<MibFileInfo> LoadMibFileAsync(string filePath)
        {
            return await Task.Run(() => MibParserService.ParseFile(filePath));
        }
    }
}
