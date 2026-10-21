using HidSharp;

namespace GHelper.Peripherals
{
    // button actions arrive on an input-only collection that varies per model, so all are watched
    internal sealed class PeripheralEventListener
    {
        private readonly ushort vendorId;
        private readonly ushort productId;
        private readonly string name;
        private readonly Action<byte[], int> onReport;

        private readonly HashSet<string> paths = new();
        private readonly List<HidStream> streams = new();
        private bool stopped;

        public PeripheralEventListener(ushort vendorId, ushort productId, string name, Action<byte[], int> onReport)
        {
            this.vendorId = vendorId;
            this.productId = productId;
            this.name = name;
            this.onReport = onReport;
        }

        // one reconnecting read loop per input collection; Start is safe to call again
        // after a dongle re-plug to pick up collections that appeared in the meantime
        public void Start(string? commandPath)
        {
            stopped = false;

            foreach (var device in DeviceList.Local.GetHidDevices(vendorId, productId))
            {
                // mi_00 is the pointer collection and would report every mouse movement
                if (device.DevicePath.Contains("mi_00")) continue;
                if (commandPath is not null && device.DevicePath.Contains(commandPath)) continue;

                int size;
                try { size = device.GetMaxInputReportLength(); }
                catch { continue; }
                if (size < 2) continue;

                lock (paths)
                {
                    if (!paths.Add(device.DevicePath)) continue;
                }

                Logger.WriteLine(name + $": event stream {device.DevicePath} (input {size})");
                Task.Run(() => Loop(device, size));
            }
        }

        public void Stop()
        {
            stopped = true;
            lock (paths)
            {
                foreach (var opened in streams)
                {
                    try { opened.Dispose(); } catch { }
                }
                streams.Clear();
                paths.Clear();
            }
        }

        // a sleeping dongle drops its streams: reopen until the interface itself is gone,
        // so the first report after wake is never missed while the app runs
        private void Loop(HidDevice device, int size)
        {
            byte[] buffer = new byte[size];

            while (!stopped)
            {
                HidStream? stream = null;
                try
                {
                    var config = new OpenConfiguration();
                    config.SetOption(OpenOption.Interruptible, true);
                    config.SetOption(OpenOption.Exclusive, false);
                    stream = device.Open(config);
                    stream.ReadTimeout = Timeout.Infinite;

                    lock (paths) { streams.Add(stream); }

                    while (true)
                    {
                        int count = stream.Read(buffer);
                        if (count > 0) try { onReport(buffer, count); } catch { }
                    }
                }
                catch (Exception e)
                {
                    Logger.WriteLine(name + ": event stream closed - " + e.Message);
                }
                finally
                {
                    lock (paths) { streams.Remove(stream); }
                    try { stream?.Dispose(); } catch { }
                }

                Thread.Sleep(500);
                if (stopped) break;
                if (!DeviceList.Local.GetHidDevices(vendorId, productId).Any(x => x.DevicePath == device.DevicePath)) break;
            }

            lock (paths) { paths.Remove(device.DevicePath); }
            Logger.WriteLine(name + ": event stream gone " + device.DevicePath);
        }
    }
}
